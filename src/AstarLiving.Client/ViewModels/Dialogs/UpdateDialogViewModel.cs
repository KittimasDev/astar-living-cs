using AstarLiving.Client.Common.Dialogs;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Dialogs;

/// <summary>
/// กล่องอัปเดตเวอร์ชัน
///
/// เปิดได้สองทาง
/// 1. ผู้ใช้กดปุ่มตรวจสอบการอัปเดตเอง — ต้องตอบเสมอแม้เป็นเวอร์ชันล่าสุดอยู่แล้ว
///    ไม่อย่างนั้นผู้ใช้จะไม่รู้ว่ากดแล้วเกิดอะไรขึ้น
/// 2. ระบบบังคับ เมื่อเครื่องแม่ตอบ 426 ว่าโปรแกรมเก่าเกินไป — ปิดไม่ได้จนกว่าจะอัปเดต
/// </summary>
public sealed partial class UpdateDialogViewModel : DialogViewModel<bool>
{
    private readonly UpdateService _updates;

    public UpdateDialogViewModel(UpdateService updates, bool forced = false, string? forcedMessage = null)
    {
        _updates = updates;
        IsForced = forced;
        ForcedMessage = forcedMessage;

        _updates.StatusChanged += OnStatusChanged;
    }

    public override double DialogWidth => 460;

    /// <summary>true = เครื่องแม่บังคับให้อัปเดตก่อนใช้งานต่อ ปิดกล่องนี้ไม่ได้</summary>
    public bool IsForced { get; }

    public string? ForcedMessage { get; }

    public bool CanDismiss => !IsForced;

    public string Title => IsForced ? "ต้องอัปเดตก่อนใช้งาน" : "ตรวจสอบการอัปเดต";

    public string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    [ObservableProperty]
    private string _message = "กำลังตรวจสอบการอัปเดต...";

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private bool _showProgress;

    [ObservableProperty]
    private bool _canUpdate;

    [ObservableProperty]
    private bool _canRestart;

    /// <summary>
    /// ค่าเริ่มต้นคือติดตั้งตอนปิดโปรแกรม
    /// คนที่กำลังออกบิลค้างอยู่ไม่ควรถูกบังคับปิดโปรแกรมกลางคัน
    /// </summary>
    [ObservableProperty]
    private bool _applyOnExit = true;

    /// <summary>
    /// ผู้เรียกไม่ได้รอผล จึงต้องไม่ปล่อยให้ข้อผิดพลาดหลุดออกไป
    /// ถ้าหลุด กล่องจะค้างที่ข้อความ "กำลังตรวจสอบ" ตลอดไปโดยผู้ใช้ไม่รู้ว่าเกิดอะไรขึ้น
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            if (!_updates.IsInstalled)
            {
                Message = "โปรแกรมนี้ไม่ได้ติดตั้งผ่านตัวติดตั้ง จึงอัปเดตอัตโนมัติไม่ได้";
                return;
            }

            var hasUpdate = await _updates.CheckAsync();

            Message = _updates.Status switch
            {
                UpdateStatus.UpdateAvailable =>
                    $"มีเวอร์ชันใหม่ {_updates.PendingVersion}\nเวอร์ชันที่ใช้อยู่ {CurrentVersion}",

                UpdateStatus.UpToDate => $"ใช้เวอร์ชันล่าสุดอยู่แล้ว ({CurrentVersion})",

                _ => "ตรวจสอบการอัปเดตไม่ได้ กรุณาตรวจสอบว่าเครื่องแม่ข่ายเปิดอยู่",
            };

            CanUpdate = hasUpdate;
        }
        catch (Exception ex)
        {
            Message = $"ตรวจสอบการอัปเดตไม่ได้\n{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        CanUpdate = false;
        ShowProgress = true;
        Message = "กำลังดาวน์โหลดเวอร์ชันใหม่...";

        var progress = new Progress<int>(value => Progress = value);

        if (!await _updates.DownloadAsync(progress))
        {
            ShowProgress = false;
            Message = "ดาวน์โหลดไม่สำเร็จ กรุณาลองใหม่อีกครั้ง";
            CanUpdate = true;
            return;
        }

        ShowProgress = false;

        if (ApplyOnExit && !IsForced)
        {
            _updates.ApplyOnExit();
            Message = "ดาวน์โหลดเรียบร้อย ระบบจะติดตั้งให้เมื่อปิดโปรแกรม";
            return;
        }

        Message = "ดาวน์โหลดเรียบร้อย กดเปิดโปรแกรมใหม่เพื่อติดตั้ง";
        CanRestart = true;
    }

    [RelayCommand]
    private void Restart() => _updates.ApplyAndRestart();

    [RelayCommand]
    private void Close()
    {
        if (IsForced) return;

        _updates.StatusChanged -= OnStatusChanged;
        base.Close(true);
    }

    private void OnStatusChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Message));
}
