using System.ComponentModel.DataAnnotations;
using AstarLiving.Domain.Enums;

namespace AstarLiving.Shared.Contracts;

/// <summary>gv_collection_notices หนึ่งใบ</summary>
public class CollectionNoticeResponse
{
    public int Id { get; init; }
    public int ResidentId { get; init; }
    public int? PaymentId { get; init; }
    /// <summary>FINE = ค่าเบี้ยปรับ · COLLECTION = ค่าทวงถาม</summary>
    public NoticeType NoticesType { get; init; }
    /// <summary>ปี พ.ศ.</summary>
    public int Year { get; init; }
    public short Month { get; init; }
    public DateOnly NoticeDate { get; init; }
    public decimal? FeeAmount { get; init; }
    public NoticeStatus Status { get; init; }
    public int? ReceiptId { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset? DeductedAt { get; init; }
    public int? DeductedBy { get; init; }
    public string? DeductedReason { get; init; }
    public bool Active { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public ResidentBrief? Resident { get; init; }
}

/// <summary>
/// แถวในหน้าลดหนี้ — หนึ่งแถวต่อหนึ่งงวด ไม่ใช่ต่อหนึ่งใบทวงถาม
/// ยอดค่าเบี้ยปรับและค่าทวงถามถูกรวมมาจากทุกใบที่ผูกกับงวดเดียวกัน
/// </summary>
public class ReduceNoticeResponse : CollectionNoticeResponse
{
    /// <summary>ยอดค่าเบี้ยปรับรวมของงวดนี้ (null เมื่อใบนี้ไม่ได้ผูกกับงวด)</summary>
    public decimal? FeeAmountFine { get; init; }

    /// <summary>ยอดค่าทวงถามรวมของงวดนี้</summary>
    public decimal? FeeAmountCollection { get; init; }

    /// <summary>ผลรวมของสองยอดข้างบน</summary>
    public decimal? FeeAmountTotal { get; init; }

    /// <summary>ชื่อผู้ที่กดลดหนี้</summary>
    public string? DeductedName { get; init; }

    public PaymentResponse? Payment { get; init; }
}

/// <summary>ผลการตรวจก่อนลดหนี้</summary>
public class ReduceCheckResponse
{
    /// <summary>ข้อความเตือน หรือ null เมื่อไม่มีอะไรต้องเตือน</summary>
    public string? Message { get; init; }

    /// <summary>true = งวดเดือนก่อนหน้ายังค้างชำระอยู่</summary>
    public bool PreviousMonthHasOutstanding { get; init; }

    public ReduceNoticeResponse? CollectionNotice { get; init; }

    /// <summary>งวดที่ใบทวงถามนี้ผูกอยู่</summary>
    public PaymentResponse? NoticePayment { get; init; }

    public PreviousMonthInfo? PreviousMonth { get; init; }

    public decimal? FeeAmountFine { get; init; }
    public decimal? FeeAmountCollection { get; init; }
    public decimal? FeeAmountTotal { get; init; }
}

public class PreviousMonthInfo
{
    public int Year { get; init; }
    public int Month { get; init; }
    public PaymentResponse? Payment { get; init; }
}

/// <summary>PATCH /v1/reduce/:id</summary>
public class CancelReduceRequest
{
    [MaxLength(20000)]
    public string? DeductedReason { get; set; }
}

/// <summary>ผลของการลดหนี้</summary>
public class CancelReduceResponse
{
    public ReduceNoticeResponse? CollectionNotice { get; init; }
    public PaymentResponse? Payment { get; init; }
}

public class CreateCollectionRequest
{
    [Required] public int ResidentId { get; set; }
    public int? PaymentId { get; set; }
    [Required] public NoticeType NoticesType { get; set; }

    /// <summary>ปี พ.ศ.</summary>
    [Required] [Range(2500, 3000, ErrorMessage = "กรุณาระบุปีเป็น พ.ศ.")]
    public int Year { get; set; }

    [Required] [Range(1, 12)] public short Month { get; set; }
    [Required] public DateOnly NoticeDate { get; set; }
    public decimal? FeeAmount { get; set; }
    [Required] public NoticeStatus Status { get; set; }
    public int? ReceiptId { get; set; }
    public string? Note { get; set; }
}

public class UpdateCollectionRequest
{
    public NoticeType? NoticesType { get; set; }
    public int? Year { get; set; }
    public short? Month { get; set; }
    public DateOnly? NoticeDate { get; set; }
    public decimal? FeeAmount { get; set; }
    public NoticeStatus? Status { get; set; }
    public int? ReceiptId { get; set; }
    public string? Note { get; set; }
    public bool? Active { get; set; }
}

/// <summary>POST /v1/collections/print — พิมพ์ใบทวงถามหลายใบพร้อมกัน</summary>
public class PrintCollectionRequest
{
    [Required] public List<int> CollectionIds { get; set; } = [];
}
