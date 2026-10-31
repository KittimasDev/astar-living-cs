using System.Text.Json.Serialization;

namespace AstarLiving.Shared.Versioning;

/// <summary>
/// สัญญาระหว่าง client กับ API
///
/// ContractVersion เปลี่ยนเมื่อรูปแบบการคุยกันเปลี่ยนเท่านั้น ไม่ใช่ทุกครั้งที่ปล่อยเวอร์ชันใหม่
/// ส่วน ProductVersion มาจาก Directory.Build.props ซึ่งเป็นเวอร์ชันของทั้งชุด
/// </summary>
public static class ProductContract
{
    /// <summary>รุ่นของสัญญาที่โค้ดชุดนี้พูดได้</summary>
    public const int Current = 1;

    /// <summary>รุ่นต่ำสุดของ client ที่ API ยังยอมให้เรียก (ต่ำกว่านี้ตอบ 426)</summary>
    public const int MinimumClient = 1;

    /// <summary>รุ่นต่ำสุดของ API ที่ client ยอมคุยด้วย</summary>
    public const int MinimumApi = 1;

    /// <summary>ส่วนหัวที่ client แนบไปทุก request</summary>
    public const string ContractHeader = "X-AL-Contract";

    public const string ClientVersionHeader = "X-AL-Client";
}

/// <summary>สถานะการปรับปรุงฐานข้อมูลของเครื่องแม่</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationState
{
    /// <summary>พร้อมใช้งาน</summary>
    Ready,

    /// <summary>กำลังปรับปรุงฐานข้อมูล client ต้องรอ</summary>
    Migrating,

    /// <summary>ปรับปรุงไม่สำเร็จ ห้ามใช้งานต่อ</summary>
    Failed,

    /// <summary>ยังไม่ได้ผูกฐานข้อมูลเดิมเข้ากับระบบ migration</summary>
    AwaitingBaseline,
}

/// <summary>
/// สิ่งที่ GET /api/v1/handshake ตอบกลับ — ต้องตอบได้แม้ระหว่าง migrate และไม่ต้อง auth
/// </summary>
public class HandshakeResponse
{
    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; init; } = string.Empty;

    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; init; }

    [JsonPropertyName("minClientContractVersion")]
    public int MinClientContractVersion { get; init; }

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("migrationState")]
    public MigrationState MigrationState { get; init; }

    /// <summary>เวอร์ชันสูงสุดที่เครื่องลูกได้รับอนุญาตให้ติดตั้ง</summary>
    [JsonPropertyName("allowedMaxClientVersion")]
    public string AllowedMaxClientVersion { get; init; } = string.Empty;

    [JsonPropertyName("serverTimeUtc")]
    public DateTimeOffset ServerTimeUtc { get; init; }

    [JsonPropertyName("siteName")]
    public string SiteName { get; init; } = string.Empty;
}
