using AstarLiving.Shared.Contracts;

namespace AstarLiving.Client.Services;

/// <summary>
/// ผู้ใช้ที่กำลังเข้าสู่ระบบอยู่ และสถานะที่ทุกหน้าจอใช้ร่วมกัน
///
/// เป็น singleton ตัวเดียวทั้งโปรแกรม จึงเป็นที่เก็บ "ประตูต่ออายุโทเคน" ด้วย
/// ถ้าให้ตัว handler ถือ semaphore เอง HttpClientFactory จะทิ้งและสร้างใหม่ทุก 2 นาที
/// ทำให้มีหลายประตูพร้อมกันและ refresh ซ้อนกันได้
/// </summary>
public sealed class AuthSession
{
    private CurrentUserResponse? _user;

    /// <summary>ให้มีการต่ออายุโทเคนได้ทีละรายเท่านั้นตลอดอายุโปรแกรม</summary>
    internal SemaphoreSlim RefreshGate { get; } = new(1, 1);

    public CurrentUserResponse? User
    {
        get => _user;
        private set
        {
            _user = value;
            UserChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsAuthenticated => _user is not null;

    public string Role => _user?.Role ?? string.Empty;

    public int? ProjectId => _user?.ProjectId;

    /// <summary>ชื่อสิทธิ์ภาษาไทยที่แสดงใต้ชื่อผู้ใช้บนหัวหน้าจอ — ตรงกับ Header.tsx ของเว็บเดิม</summary>
    public string RoleDisplayName => _user?.Role switch
    {
        "SUPERADMIN" => "Astar",
        "ADMIN" => "ผู้จัดการโครงการ",
        "MANAGER" => "หัวหน้านิติ",
        _ => "นิติ",
    };

    public string DisplayName
    {
        get
        {
            if (_user is null) return "-";
            if (!string.IsNullOrWhiteSpace(_user.FullName)) return _user.FullName;
            return string.IsNullOrWhiteSpace(_user.Username) ? "-" : _user.Username;
        }
    }

    public event EventHandler? UserChanged;

    /// <summary>ยิงเมื่อโทเคนหมดอายุและต่ออายุไม่ได้ หน้าจอหลักจะพากลับไปหน้าเข้าสู่ระบบ</summary>
    public event EventHandler? SignedOut;

    /// <summary>
    /// ยิงเมื่อเครื่องแม่ตอบว่าโปรแกรมเวอร์ชันนี้คุยด้วยไม่ได้แล้ว (426)
    /// หน้าจอหลักจะขึ้นกล่องบังคับอัปเดตที่ปิดไม่ได้
    /// </summary>
    public event EventHandler<string>? UpgradeRequired;

    public void SetUser(CurrentUserResponse user) => User = user;

    public void Clear() => User = null;

    /// <summary>เรียกจาก <see cref="AuthTokenHandler"/> เมื่อยืนยันแล้วว่าหมดสิทธิ์จริง</summary>
    internal void NotifySignedOut()
    {
        User = null;
        SignedOut?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>เรียกจาก <see cref="ApiClient"/> เมื่อได้ 426 กลับมาจากเครื่องแม่</summary>
    internal void NotifyUpgradeRequired(string message) => UpgradeRequired?.Invoke(this, message);
}
