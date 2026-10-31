using System.Security;
using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels;

/// <summary>
/// หน้าเข้าสู่ระบบ — พอร์ตจาก LoginForm.tsx
///
/// กฎการตรวจข้อมูลยกมาตามเดิม: ชื่อผู้ใช้อย่างน้อย 3 ตัว รหัสผ่านอย่างน้อย 4 ตัว
/// ข้อความผิดพลาดแยกเป็นรายช่อง ส่วนข้อผิดพลาดจากเครื่องแม่ข่ายแสดงเป็นแถบแดงใต้ฟอร์ม
/// </summary>
public sealed partial class LoginViewModel(
    AuthApi auth, ClientSettings settings, ServerReadiness readiness) : ObservableObjectBase
{
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private SecureString? _password;

    [ObservableProperty]
    private string? _usernameError;

    [ObservableProperty]
    private string? _passwordError;

    /// <summary>
    /// ข้อความระหว่างรอเครื่องแม่ข่าย ว่างเมื่อพร้อมใช้งานแล้ว
    ///
    /// มีไว้เพื่อกรณีเดียว คือผู้ใช้เปิดเครื่องแล้วกดโปรแกรมทันทีก่อนบริการจะเริ่มเสร็จ
    /// โปรแกรมจะรอให้เอง ผู้ใช้ไม่ต้องกดอะไรซ้ำ
    /// </summary>
    [ObservableProperty]
    private string? _serverStatusMessage;

    /// <summary>true เมื่อยังรอเครื่องแม่ข่ายอยู่ ใช้ปิดปุ่มเข้าสู่ระบบไว้ก่อน</summary>
    [ObservableProperty]
    private bool _isWaitingForServer;

    /// <summary>true เมื่อฐานข้อมูลมีปัญหาที่รอแล้วไม่หาย ต้องให้คนเข้าไปดู</summary>
    [ObservableProperty]
    private bool _serverNeedsAttention;

    /// <summary>ข้อความใต้แถบความคืบหน้า บอกว่ากำลังรออะไรอยู่</summary>
    public string BusyMessage => ServerStatusMessage ?? "กำลังเข้าสู่ระบบ กรุณารอสักครู่...";

    /// <summary>แสดงแถบความคืบหน้าทั้งตอนเข้าสู่ระบบและตอนรอเครื่องแม่ข่าย</summary>
    public bool ShowBusyIndicator => IsBusy || IsWaitingForServer;

    partial void OnServerStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(BusyMessage));
    }

    partial void OnIsWaitingForServerChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBusyIndicator));
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(IsBusy)) OnPropertyChanged(nameof(ShowBusyIndicator));
    }

    /// <summary>ที่อยู่เครื่องแม่ข่ายที่กำลังใช้ แสดงไว้ให้ช่างตรวจได้ตอนติดตั้งหน้างาน</summary>
    public string ServerUrl => settings.ServerUrl;

    public bool IsServerUrlUnset => settings.IsUsingFallback;

    /// <summary>ยิงเมื่อเข้าสู่ระบบสำเร็จ ให้หน้าต่างเปลี่ยนไปหน้าหลัก</summary>
    public event EventHandler? SignedIn;

    /// <summary>ยิงเมื่อผู้ใช้ขอตั้งค่าที่อยู่เครื่องแม่ข่าย</summary>
    public event EventHandler? ServerSettingsRequested;

    partial void OnUsernameChanged(string value) => UsernameError = null;

    partial void OnPasswordChanged(SecureString? value) => PasswordError = null;

    /// <summary>
    /// เรียกตอนหน้าเข้าสู่ระบบปรากฏ รอให้เครื่องแม่ข่ายพร้อมก่อนเปิดให้กรอก
    ///
    /// ไม่โยนข้อผิดพลาดออกไป เพราะผู้ใช้ไม่ได้สั่งให้ทำ
    /// ถ้าพร้อมอยู่แล้วจะไม่มีอะไรแสดงบนหน้าจอเลย
    /// </summary>
    public async Task WaitForServerAsync(CancellationToken ct = default)
    {
        var first = await readiness.ProbeAsync(ct);
        if (first.State == ServerState.Ready) return;

        IsWaitingForServer = true;
        ServerStatusMessage = first.Message;

        var progress = new Progress<ServerStatus>(status => ServerStatusMessage = status.Message);
        var final = await readiness.WaitAsync(progress, timeout: null, ct);

        IsWaitingForServer = false;
        ServerNeedsAttention = final.State == ServerState.Failed;

        // รอจนหมดเวลาแล้วยังไม่ขึ้น ปล่อยให้ผู้ใช้ลองกดเองได้ แต่คงข้อความบอกสถานะไว้
        ServerStatusMessage = final.State == ServerState.Ready ? null : final.Message;
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (!Validate()) return;

        var ok = await RunAsync(async () =>
        {
            try
            {
                await auth.LoginAsync(Username, Password!);
            }
            catch (ApiException ex) when (ex.IsConnectivity)
            {
                // เครื่องแม่อาจกำลังเริ่มทำงานอยู่ รอให้พร้อมแล้วลองใหม่ให้เอง
                // ถ้ายังไม่ขึ้นจริง ๆ ข้อผิดพลาดเดิมจะถูกโยนออกไปแสดงตามปกติ
                IsWaitingForServer = true;
                ServerStatusMessage = ex.Message;

                var progress = new Progress<ServerStatus>(s => ServerStatusMessage = s.Message);
                var status = await readiness.WaitAsync(progress, TimeSpan.FromSeconds(60));

                IsWaitingForServer = false;
                ServerStatusMessage = null;

                if (status.State != ServerState.Ready) throw;

                await auth.LoginAsync(Username, Password!);
            }
        });

        if (!ok) return;

        // ล้างรหัสผ่านทันทีที่ใช้เสร็จ ไม่ให้ค้างอยู่ในหน่วยความจำหลังเข้าสู่ระบบแล้ว
        Password?.Dispose();
        Password = null;

        SignedIn?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenServerSettings() => ServerSettingsRequested?.Invoke(this, EventArgs.Empty);

    private bool Validate()
    {
        ClearError();
        UsernameError = null;
        PasswordError = null;

        if (string.IsNullOrWhiteSpace(Username))
        {
            UsernameError = "กรุณากรอกชื่อผู้ใช้";
        }
        else if (Username.Trim().Length < 3)
        {
            UsernameError = "ชื่อผู้ใช้ต้องมีอย่างน้อย 3 ตัวอักษร";
        }

        if (Password is null || Password.Length == 0)
        {
            PasswordError = "กรุณากรอกรหัสผ่าน";
        }
        else if (Password.Length < 4)
        {
            PasswordError = "รหัสผ่านต้องมีอย่างน้อย 4 ตัวอักษร";
        }

        return UsernameError is null && PasswordError is null;
    }
}
