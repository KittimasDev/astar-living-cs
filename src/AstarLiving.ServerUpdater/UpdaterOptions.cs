namespace AstarLiving.ServerUpdater;

/// <summary>
/// ค่าตั้งของบริการอัปเดตบนเครื่องแม่
/// อ่านจาก machine.json ที่อยู่นอกโฟลเดอร์ติดตั้ง จึงรอดจากการอัปเดตทุกครั้ง
/// </summary>
public sealed class UpdaterOptions
{
    public const string SectionName = "AstarLiving:Updater";

    /// <summary>ที่อยู่ของ manifest บนเซิร์ฟเวอร์ปล่อยเวอร์ชัน ว่างไว้ = ปิดการอัปเดตอัตโนมัติ</summary>
    public string? FeedUrl { get; set; }

    /// <summary>ช่องทางการปล่อย stable หรือ pilot</summary>
    public string Channel { get; set; } = "stable";

    /// <summary>กุญแจสาธารณะที่ใช้ตรวจลายเซ็นของ manifest</summary>
    public string? ReleasePublicKey { get; set; }

    /// <summary>ความถี่ในการตรวจ</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>ชื่อบริการ Windows ของ API ที่ต้องหยุดและเริ่มใหม่ตอนอัปเดต</summary>
    public string ApiServiceName { get; set; } = "AstarLiving API";

    /// <summary>โฟลเดอร์ติดตั้งของทั้งชุด</summary>
    public string InstallRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AstarLiving");

    /// <summary>ที่อยู่ที่ใช้ตรวจว่า API กลับมาพร้อมใช้งานแล้ว</summary>
    public string HealthUrl { get; set; } = "http://127.0.0.1:5080/api/health/ready";

    /// <summary>เวลาที่ยอมรอให้ API กลับมาพร้อมหลังอัปเดต เกินกว่านี้ถือว่าล้มเหลว</summary>
    public TimeSpan HealthTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>จำนวนเวอร์ชันเก่าที่เก็บไว้เผื่อย้อนกลับ</summary>
    public int KeepVersions { get; set; } = 2;

    // ── ที่อยู่ที่คำนวณจากค่าด้านบน ─────────────────────────────────────────

    public string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AstarLiving");

    /// <summary>โฟลเดอร์ที่ API เสิร์ฟให้เครื่องลูกดึงอัปเดต</summary>
    public string FeedRoot => Path.Combine(DataRoot, "updates");

    public string BackupRoot => Path.Combine(DataRoot, "backups");

    /// <summary>ที่พักไฟล์ระหว่างดาวน์โหลด ล้างได้เสมอ</summary>
    public string StagingRoot => Path.Combine(DataRoot, "staging");

    /// <summary>โฟลเดอร์ที่เก็บ API แต่ละเวอร์ชัน</summary>
    public string ApiVersionsRoot => Path.Combine(InstallRoot, "api", "versions");

    /// <summary>ทางลัดที่ชี้ไปเวอร์ชันที่ใช้งานอยู่ การย้อนกลับคือการชี้ทางลัดนี้กลับ</summary>
    public string ApiCurrentPath => Path.Combine(InstallRoot, "api", "current");

    public string PgDumpPath => Path.Combine(InstallRoot, "pgsql", "bin", "pg_dump.exe");

    public string ConfigRoot => Path.Combine(DataRoot, "config");
}
