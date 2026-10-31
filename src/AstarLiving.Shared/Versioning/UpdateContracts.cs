using System.Text.Json.Serialization;

namespace AstarLiving.Shared.Versioning;

/// <summary>
/// นโยบายการอัปเดตที่เครื่องแม่ประกาศให้เครื่องลูก
///
/// เครื่องลูกจะไม่ติดตั้งเวอร์ชันที่สูงกว่า <see cref="AllowedMaxVersion"/> เด็ดขาด
/// ค่านี้เท่ากับเวอร์ชันของเครื่องแม่เอง เครื่องลูกจึงไม่มีทางแซงเครื่องแม่ได้
/// ซึ่งเป็นเงื่อนไขที่ทำให้ไม่ต้องรองรับกรณี client ใหม่คุยกับ API เก่า
/// </summary>
public class UpdatePolicyResponse
{
    [JsonPropertyName("allowedMaxVersion")]
    public string AllowedMaxVersion { get; init; } = string.Empty;

    /// <summary>ที่อยู่ feed ที่เครื่องลูกใช้ดึงอัปเดต ชี้มาที่เครื่องแม่เสมอ</summary>
    [JsonPropertyName("feedUrl")]
    public string FeedUrl { get; init; } = string.Empty;

    /// <summary>false = เครื่องแม่ยังไม่พร้อมปล่อยให้เครื่องลูกอัปเดต</summary>
    [JsonPropertyName("updatesAvailable")]
    public bool UpdatesAvailable { get; init; }

    /// <summary>เวอร์ชันของเครื่องแม่ ใช้แสดงให้ผู้ใช้เห็นเวลาแจ้งปัญหา</summary>
    [JsonPropertyName("serverVersion")]
    public string ServerVersion { get; init; } = string.Empty;
}

/// <summary>สถานะการอัปเดตที่แสดงบนแถบท้ายหน้าจอ</summary>
public enum UpdateStatus
{
    /// <summary>ยังไม่ได้ตรวจ</summary>
    Unknown,

    /// <summary>กำลังตรวจกับเครื่องแม่</summary>
    Checking,

    /// <summary>เป็นเวอร์ชันล่าสุดแล้ว</summary>
    UpToDate,

    /// <summary>มีเวอร์ชันใหม่ให้ติดตั้ง</summary>
    UpdateAvailable,

    /// <summary>กำลังดาวน์โหลด</summary>
    Downloading,

    /// <summary>ดาวน์โหลดเสร็จ รอรีสตาร์ทเพื่อติดตั้ง</summary>
    ReadyToApply,

    /// <summary>ตรวจไม่ได้ เช่น ติดต่อเครื่องแม่ไม่ได้</summary>
    Unavailable,
}
