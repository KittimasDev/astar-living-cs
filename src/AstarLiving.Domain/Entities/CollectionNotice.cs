using AstarLiving.Domain.Enums;

namespace AstarLiving.Domain.Entities;

/// <summary>
/// ตาราง gv_collection_notices — ใบทวงถาม/ค่าเบี้ยปรับ
/// หนึ่งงวด (payment_id) มีได้ทั้งแถว FINE และ COLLECTION
/// </summary>
public class CollectionNotice : BaseEntityAudit
{
    public int Id { get; set; }
    public int ResidentId { get; set; }
    public int? PaymentId { get; set; }
    public NoticeType NoticesType { get; set; }
    /// <summary>ปี พ.ศ.</summary>
    public int Year { get; set; }
    public short Month { get; set; }
    public DateOnly NoticeDate { get; set; }
    public decimal? FeeAmount { get; set; }
    public NoticeStatus Status { get; set; }
    public int? ReceiptId { get; set; }
    public string? Note { get; set; }
    /// <summary>เวลาที่กด "ลดหนี้" (ยกเลิกใบทวงถาม)</summary>
    public DateTimeOffset? DeductedAt { get; set; }
    public int? DeductedBy { get; set; }
    public string? DeductedReason { get; set; }
    public bool Active { get; set; } = true;

    public Resident Resident { get; set; } = null!;
    public Payment? Payment { get; set; }
    public Receipt? Receipt { get; set; }
}
