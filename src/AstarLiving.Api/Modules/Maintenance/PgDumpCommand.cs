using AstarLiving.Api.Configuration;

namespace AstarLiving.Api.Modules.Maintenance;

/// <summary>
/// ประกอบอาร์กิวเมนต์ของ pg_dump
///
/// แยกออกมาเป็นฟังก์ชันล้วนเพื่อให้เขียนเทสต์ได้โดยไม่ต้องมี PostgreSQL
/// และเพื่อตรึงกฎสองข้อที่พลาดแล้วเจ็บ
///   1. ทุกค่าต้องมาจากค่าตั้ง ห้ามฮาร์ดโค้ด
///      (โค้ดสำรองของตัวอัปเดตฮาร์ดโค้ด host/port/db ไว้ ซึ่งไม่ตรงกับค่าเริ่มต้นใน appsettings)
///   2. รหัสผ่านห้ามอยู่ในอาร์กิวเมนต์เด็ดขาด เพราะจะไปโผล่ในรายการโปรเซสให้ใครก็เห็น
///      ต้องส่งผ่านตัวแปรสภาพแวดล้อม PGPASSWORD เท่านั้น
/// </summary>
public static class PgDumpCommand
{
    /// <summary>รูปแบบ custom ของ PostgreSQL บีบอัดในตัวและกู้คืนเลือกตารางได้</summary>
    public const string CustomFormatFlag = "-Fc";

    public static IReadOnlyList<string> BuildArguments(DatabaseOptions database, string outputPath) =>
    [
        "-h", database.Host,
        "-p", database.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-U", database.Username,
        "-d", database.Database,
        CustomFormatFlag,
        "-f", outputPath,
    ];

    /// <summary>ชื่อไฟล์สำรองที่สร้างด้วยมือ ใช้เวลาไทยเพื่อให้ตรงกับที่ผู้ใช้เห็นบนหน้าจอ</summary>
    public static string BuildFileName(DateTimeOffset bangkokNow) =>
        $"astarliving-{bangkokNow:yyyyMMdd-HHmmss}.dump";
}
