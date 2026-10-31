using System.Globalization;
using System.IO;
using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>แถวหนึ่งในตารางสถานะระบบ</summary>
public sealed record StatusRow(string Label, string Value);

/// <summary>
/// หน้าสำรองฐานข้อมูล — เห็นได้เฉพาะผู้ดูแลระบบสูงสุด
///
/// ก่อนมีหน้านี้ ระบบสำรองฐานข้อมูลให้เฉพาะตอนอัปเดตเวอร์ชันและตอนถอนการติดตั้ง
/// ไฟล์ที่ได้ก็ค้างอยู่บนเครื่องแม่ ดึงออกมาไม่ได้ ถ้าเครื่องแม่พังก็หายไปพร้อมกัน
/// </summary>
public sealed partial class DatabaseBackupPageViewModel(
    BackupApi backups, DialogService dialogs) : PageViewModel
{
    private CancellationTokenSource? _running;

    public override string Title => "สำรองฐานข้อมูล";

    public System.Collections.ObjectModel.ObservableCollection<StatusRow> StatusRows { get; } = [];

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string? _rowCountNote;

    /// <summary>ข้อความบอกว่ากำลังทำอะไรอยู่ ว่างเมื่อไม่ได้ทำอะไร</summary>
    [ObservableProperty]
    private string? _progressMessage;

    [ObservableProperty]
    private bool _isWorking;

    /// <summary>เตือนล่วงหน้าเมื่อเครื่องแม่ยังไม่พร้อมสำรอง จะได้ไม่ต้องกดแล้วค่อยรู้</summary>
    [ObservableProperty]
    private string? _blockingWarning;

    public bool CanBackup => !IsWorking && BlockingWarning is null;

    partial void OnIsWorkingChanged(bool value) => OnPropertyChanged(nameof(CanBackup));

    partial void OnBlockingWarningChanged(string? value) => OnPropertyChanged(nameof(CanBackup));

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            var status = await backups.GetStatusAsync();
            Apply(status);
        });
    }

    private void Apply(SystemStatusResponse status)
    {
        ProjectName = status.ProjectName;

        BlockingWarning = string.IsNullOrWhiteSpace(status.PgDumpVersion)
            ? "ไม่พบเครื่องมือสำรองฐานข้อมูลบนเครื่องแม่ข่าย กรุณาติดต่อผู้ดูแลระบบ"
            : null;

        RowCountNote = status.RowCountsAreEstimated
            ? "จำนวนแถวเป็นค่าโดยประมาณ เพราะตารางใหญ่เกินกว่าจะนับทันที"
            : null;

        StatusRows.Clear();

        StatusRows.Add(new StatusRow("ขนาดฐานข้อมูล", FormatBytes(status.DatabaseSizeBytes)));
        StatusRows.Add(new StatusRow("พื้นที่ว่างบนเครื่องแม่ข่าย", FormatBytes(status.AvailableDiskBytes)));

        foreach (var row in status.RowCounts)
        {
            StatusRows.Add(new StatusRow(row.Label, row.Rows.ToString("N0", ThaiCulture)));
        }

        StatusRows.Add(new StatusRow("สำรองล่าสุด", FormatLastBackup(status.LastBackupAt)));
        StatusRows.Add(new StatusRow("เวอร์ชันบริการเบื้องหลัง", status.ServerVersion));
        StatusRows.Add(new StatusRow("รุ่นโครงสร้างฐานข้อมูล", status.SchemaVersion));
        StatusRows.Add(new StatusRow("เวอร์ชัน PostgreSQL", status.PostgresVersion));

        StatusRows.Add(new StatusRow(
            "เครื่องมือสำรอง",
            string.IsNullOrWhiteSpace(status.PgDumpVersion) ? "ไม่พบ" : status.PgDumpVersion));
    }

    /// <summary>
    /// สำรองแล้วดาวน์โหลด
    ///
    /// ถามที่เก็บก่อนสั่งสำรอง ไม่ใช่หลัง เพราะบัตรผ่านมีอายุสั้น
    /// ถ้าถามทีหลังแล้วผู้ใช้เลือกช้า บัตรจะหมดอายุก่อนได้ใช้
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBackup))]
    private async Task BackupAsync()
    {
        var confirmed = await dialogs.ConfirmAsync(
            "สำรองฐานข้อมูล",
            "ไฟล์ที่ได้มีข้อมูลส่วนบุคคลของลูกบ้านทั้งโครงการ กรุณาเก็บไว้ในที่ปลอดภัย\n" +
            "ระหว่างสำรองระบบยังใช้งานได้ตามปกติ แต่อาจช้าลงบ้าง",
            confirmText: "สำรองข้อมูล");

        if (!confirmed) return;

        var target = AskWhereToSave();
        if (target is null) return;

        _running = new CancellationTokenSource();

        try
        {
            IsWorking = true;
            ClearError();

            ProgressMessage = "กำลังสำรองฐานข้อมูล อาจใช้เวลาหลายนาที...";
            var created = await backups.CreateAsync(_running.Token);

            ProgressMessage = $"กำลังดาวน์โหลด {FormatBytes(created.SizeBytes)}...";

            var progress = new Progress<long>(written =>
                ProgressMessage = $"กำลังดาวน์โหลด {FormatBytes(written)} จาก {FormatBytes(created.SizeBytes)}");

            var size = await backups.DownloadToFileAsync(
                created.Token, target, progress, _running.Token);

            ProgressMessage = null;

            await dialogs.SuccessAsync(
                $"สำรองข้อมูลเรียบร้อย\n{target}\nขนาด {FormatBytes(size)}");

            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            ProgressMessage = null;
            await dialogs.ErrorAsync("ยกเลิกการสำรองข้อมูลแล้ว");
        }
        catch (ApiException ex)
        {
            ProgressMessage = null;
            await dialogs.ErrorAsync("สำรองข้อมูลไม่สำเร็จ", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProgressMessage = null;
            await dialogs.ErrorAsync("บันทึกไฟล์ไม่สำเร็จ", ex.Message);
        }
        finally
        {
            IsWorking = false;
            _running?.Dispose();
            _running = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _running?.Cancel();

    /// <summary>
    /// ตั้งต้นที่โฟลเดอร์เอกสารของผู้ใช้ ไม่ใช่โฟลเดอร์ล่าสุดที่เคยใช้
    /// เพราะโฟลเดอร์ที่ซิงก์ขึ้นคลาวด์จะทำให้ข้อมูลลูกบ้านออกนอกองค์กรโดยไม่ตั้งใจ
    /// </summary>
    private static string? AskWhereToSave()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "บันทึกไฟล์สำรองฐานข้อมูล",
            Filter = "ไฟล์สำรองฐานข้อมูล (*.dump)|*.dump",
            DefaultExt = ".dump",
            FileName = $"astarliving-{DateTime.Now:yyyyMMdd-HHmmss}.dump",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static readonly CultureInfo ThaiCulture = CultureInfo.GetCultureInfo("th-TH");

    private static string FormatBytes(long bytes)
    {
        string[] units = ["ไบต์", "กิโลไบต์", "เมกะไบต์", "กิกะไบต์", "เทระไบต์"];

        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes:N0} {units[0]}"
            : $"{value.ToString("N1", ThaiCulture)} {units[unit]}";
    }

    private static string FormatLastBackup(DateTimeOffset? at) =>
        at is null
            ? "ยังไม่เคยสำรอง"
            : at.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm น.", ThaiCulture);
}
