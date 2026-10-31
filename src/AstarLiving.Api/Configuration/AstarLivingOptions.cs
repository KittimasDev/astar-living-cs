using System.ComponentModel.DataAnnotations;

namespace AstarLiving.Api.Configuration;

/// <summary>
/// ค่าตั้งของเครื่องแม่ อ่านจาก appsettings.json แล้วทับด้วย machine.json ของเครื่องนั้น
/// ไฟล์ machine.json อยู่นอกโฟลเดอร์ติดตั้ง จึงไม่ถูกลบตอนอัปเดต
/// </summary>
public class AstarLivingOptions
{
    public const string SectionName = "AstarLiving";

    [Required]
    public DatabaseOptions Database { get; set; } = new();

    [Required]
    public JwtOptions Jwt { get; set; } = new();

    public SiteOptions Site { get; set; } = new();

    public StorageOptions Storage { get; set; } = new();

    public BackupOptions Backup { get; set; } = new();
}

/// <summary>
/// ค่าตั้งของการสำรองฐานข้อมูลด้วยมือจากหน้าจอ
///
/// แยกจากการสำรองอัตโนมัติของตัวอัปเดต ทั้งโฟลเดอร์และอายุไฟล์
/// เพื่อไม่ให้การกวาดไฟล์ของฝั่งหนึ่งไปลบไฟล์ของอีกฝั่ง
/// </summary>
public class BackupOptions
{
    /// <summary>
    /// ที่อยู่ของ pg_dump.exe ค่าเริ่มต้นตรงกับที่ตัวติดตั้งวางไว้
    /// ไซต์ที่ชี้ไปฐานข้อมูลภายนอกหรือเครื่องนักพัฒนาจะไม่มีไฟล์นี้ ต้องสั่งทับเอง
    /// </summary>
    public string PgDumpPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "AstarLiving", "pgsql", "bin", "pg_dump.exe");

    /// <summary>โฟลเดอร์พักไฟล์ที่สำรองด้วยมือ แยกจาก before-*.dump ของตัวอัปเดต</summary>
    public string WorkDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AstarLiving", "backups", "manual");

    /// <summary>โฟลเดอร์ที่ตัวอัปเดตเก็บไฟล์สำรองอัตโนมัติไว้ ใช้หาเวลาสำรองล่าสุด</summary>
    public string BackupRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AstarLiving", "backups");

    /// <summary>เวลาสูงสุดที่ยอมให้ pg_dump ทำงาน เกินแล้วฆ่าทิ้งทั้งต้นไม้โปรเซส</summary>
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>อายุของ token ดาวน์โหลด สั้นเพราะไฟล์มีข้อมูลส่วนบุคคลทั้งหมู่บ้าน</summary>
    public TimeSpan TokenTtl { get; set; } = TimeSpan.FromMinutes(15);
}

public class DatabaseOptions
{
    /// <summary>ใส่ connection string เต็มได้ตรงนี้ ถ้าใส่แล้วจะไม่สนค่าแยกด้านล่าง</summary>
    public string? ConnectionString { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 54329;

    public string Database { get; set; } = "gvbilling";

    public string Username { get; set; } = "asliving_app";

    /// <summary>
    /// บนเครื่องลูกค้าจริงรหัสผ่านจะถูกอ่านจาก db.secret ที่ป้องกันด้วย DPAPI
    /// ช่องนี้มีไว้สำหรับเครื่องนักพัฒนาเท่านั้น
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>ไฟล์ DPAPI ที่เก็บรหัสผ่านฐานข้อมูลบนเครื่องแม่</summary>
    public string? SecretFilePath { get; set; }
}

public class JwtOptions
{
    /// <summary>
    /// ระบบเดิมเซ็น access กับ refresh ด้วยกุญแจเดียวกัน ทำให้ refresh token ใช้แทน access token ได้
    /// ระบบใหม่แยกกุญแจกัน ตามข้อ 3 ของตารางบั๊กในแผน
    ///
    /// บนเครื่องลูกค้าค่านี้มาจากไฟล์ที่ป้องกันด้วย DPAPI ซึ่งตัวติดตั้งสุ่มให้ตอนติดตั้ง
    /// ช่องนี้จึงว่างได้ตอนอ่านไฟล์ตั้งค่า แต่ต้องมีค่าแล้วเมื่อโปรแกรมเริ่มทำงานจริง
    /// </summary>
    public string AccessSecret { get; set; } = string.Empty;

    public string RefreshSecret { get; set; } = string.Empty;

    /// <summary>ไฟล์ DPAPI ที่เก็บกุญแจของ access token</summary>
    public string? AccessSecretFilePath { get; set; }

    /// <summary>ไฟล์ DPAPI ที่เก็บกุญแจของ refresh token</summary>
    public string? RefreshSecretFilePath { get; set; }

    /// <summary>อายุ access token — ค่าเดิมของระบบคือ 1 ชั่วโมง</summary>
    public TimeSpan AccessExpires { get; set; } = TimeSpan.FromHours(1);

    /// <summary>อายุ refresh token — ค่าเดิมของระบบคือ 1 วัน</summary>
    public TimeSpan RefreshExpires { get; set; } = TimeSpan.FromDays(1);

    public string Issuer { get; set; } = "astar-living";

    public string Audience { get; set; } = "astar-living";
}

public class SiteOptions
{
    /// <summary>ชื่อไซต์ที่แสดงตอน client ค้นหาเครื่องแม่ใน LAN</summary>
    public string Name { get; set; } = "A star Living";
}

public class StorageOptions
{
    /// <summary>
    /// โฟลเดอร์เก็บไฟล์ที่ผู้ใช้อัปโหลด (โลโก้โครงการ)
    /// ต้องอยู่นอกโฟลเดอร์ติดตั้ง ไม่อย่างนั้นจะหายทุกครั้งที่อัปเดต
    /// </summary>
    public string UploadRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AstarLiving", "uploads");

    /// <summary>URL ฐานที่ใช้ประกอบลิงก์รูป — ระบบเดิมเสิร์ฟที่ /api/images</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
