using System.Text.Json.Serialization;

namespace AstarLiving.Shared.Contracts;

/// <summary>จำนวนแถวของตารางหนึ่ง</summary>
public class TableRowCountResponse
{
    /// <summary>ชื่อที่แสดงให้ผู้ใช้เห็น ไม่ใช่ชื่อตารางในฐานข้อมูล</summary>
    public string Label { get; set; } = string.Empty;

    public long Rows { get; set; }
}

/// <summary>
/// สถานะระบบที่แสดงบนหน้าสำรองฐานข้อมูล
///
/// ชื่อฟิลด์ที่มีตัวย่อติดกันกำกับ JsonPropertyName ไว้เอง
/// เพราะกฎแปลงชื่อเป็น snake_case อัตโนมัติให้ผลที่เดาไม่ถูกกับคำพวกนี้
/// และถ้าสองฝั่งสะกดไม่ตรงกันจะได้ค่าว่างแบบเงียบ ๆ ไม่มีข้อผิดพลาดให้เห็น
/// </summary>
public class SystemStatusResponse
{
    public string ProjectName { get; set; } = string.Empty;

    public long DatabaseSizeBytes { get; set; }

    /// <summary>true = จำนวนแถวเป็นค่าประมาณ ต้องติดป้ายบอกผู้ใช้</summary>
    public bool RowCountsAreEstimated { get; set; }

    public List<TableRowCountResponse> RowCounts { get; set; } = [];

    /// <summary>เวอร์ชันของบริการเบื้องหลัง</summary>
    public string ServerVersion { get; set; } = string.Empty;

    /// <summary>รุ่นของโครงสร้างฐานข้อมูลที่ปรับปรุงล่าสุด</summary>
    public string SchemaVersion { get; set; } = string.Empty;

    [JsonPropertyName("postgres_version")]
    public string PostgresVersion { get; set; } = string.Empty;

    /// <summary>ว่างเมื่อหาเครื่องมือสำรองไม่เจอ หน้าจอใช้ค่านี้เตือนล่วงหน้า</summary>
    [JsonPropertyName("pg_dump_version")]
    public string PgDumpVersion { get; set; } = string.Empty;

    /// <summary>null = ยังไม่เคยสำรองเลย ต่างจากอ่านไม่ได้</summary>
    public DateTimeOffset? LastBackupAt { get; set; }

    /// <summary>พื้นที่ว่างของไดรฟ์ที่ใช้พักไฟล์สำรอง</summary>
    public long AvailableDiskBytes { get; set; }
}

/// <summary>ผลของการสั่งสำรอง ยังไม่ใช่ตัวไฟล์ ต้องเอา token ไปดาวน์โหลดต่อ</summary>
public class BackupCreatedResponse
{
    public string Token { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
