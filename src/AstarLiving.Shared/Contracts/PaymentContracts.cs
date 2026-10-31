using System.ComponentModel.DataAnnotations;
using AstarLiving.Domain.Enums;

namespace AstarLiving.Shared.Contracts;

/// <summary>ลูกบ้านแบบย่อที่ติดมากับรายการงวด (SELECT_FIELDS_RESIDENT_PAYMENT ของเดิม)</summary>
public class ResidentBrief
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Address { get; init; }
}

/// <summary>
/// gv_payments หนึ่งแถว
///
/// หมายเหตุสำคัญ: total_due ในเส้นทางรายการ "ถูกคำนวณใหม่ตอนอ่าน" ไม่ใช่ค่าที่เก็บไว้ในตาราง
/// เพราะยอดค้างเปลี่ยนไปเรื่อย ๆ เมื่อลูกบ้านชำระงวดเก่า
/// </summary>
public record PaymentResponse
{
    public int Id { get; init; }
    public int? ReceiptsId { get; init; }
    public int ResidentId { get; init; }
    public string? InvNo { get; init; }
    /// <summary>ปี พ.ศ.</summary>
    public int Year { get; init; }
    public short Month { get; init; }
    public decimal AmountDue { get; init; }
    public decimal? PaidAmount { get; init; }
    public decimal? Fine { get; init; }
    public decimal? CollectionFee { get; init; }
    /// <summary>ยอดรวมที่ต้องชำระ รวมยอดค้างจากงวดก่อนหน้า</summary>
    public decimal TotalDue { get; init; }
    public DateOnly? PaymentDate { get; init; }
    public int? PaymentMethodId { get; init; }
    public PaymentStatus Status { get; init; }
    public string? Note { get; init; }
    public bool Active { get; init; }
    public int? CollectionNoticeId { get; init; }
    public bool HasCollectionNotice { get; init; }
    public bool CollectionPaid { get; init; }
    public bool Advance { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public ResidentBrief? Resident { get; init; }
}

public class CreatePaymentRequest
{
    [Required] public int ResidentId { get; set; }
    [Required] public int Year { get; set; }
    [Required] [Range(1, 12)] public short Month { get; set; }
    [Required] public decimal AmountDue { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? Fine { get; set; }
    public decimal? CollectionFee { get; set; }
    [Required] public decimal TotalDue { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public int? PaymentMethodId { get; set; }
    [Required] public PaymentStatus Status { get; set; }
    public string? Note { get; set; }
    public int? CollectionNoticeId { get; set; }
    public bool? HasCollectionNotice { get; set; }
    public bool? CollectionPaid { get; set; }
}

public class UpdatePaymentRequest
{
    public decimal? AmountDue { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? Fine { get; set; }
    public decimal? CollectionFee { get; set; }
    public decimal? TotalDue { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public int? PaymentMethodId { get; set; }
    public PaymentStatus? Status { get; set; }
    public string? Note { get; set; }
    public bool? Active { get; set; }
}

/// <summary>body ของการออกใบแจ้งหนี้: รายชื่อบ้านที่ถูกติ๊กให้คิดค่าทวงถาม</summary>
public class CreateInvoicesRequest
{
    public List<int>? ResidentIds { get; set; }
}

/// <summary>หนึ่งแถวในหน้าพรีวิวก่อนกดออกใบแจ้งหนี้</summary>
public class InvoicePreviewRow
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public decimal? OwnershipRatio { get; init; }
    public decimal? MaintenanceFeeRate { get; init; }
    /// <summary>ค่าส่วนกลางของงวดนี้</summary>
    public decimal Maintenance { get; init; }
    public decimal FineAmount { get; init; }
    /// <summary>ค่าปรับที่นับเฉพาะเมื่อมีหนี้ค้างในปี พ.ศ. เดียวกับงวด (ใช้แสดงผลเท่านั้น)</summary>
    public decimal FineAmountPreviewCurrentYear { get; init; }
    public decimal CollectionAmount { get; init; }
    public decimal DebtTotal { get; init; }
    public decimal Total { get; init; }
    public decimal TotalPreviewCurrentYear { get; init; }
}

/// <summary>ผลของการกดออกใบแจ้งหนี้</summary>
public class CreateInvoicesResult
{
    public int Month { get; init; }
    public int Year { get; init; }
    public List<int> PaymentIds { get; init; } = [];
    /// <summary>true = เป็นการเติมใบย้อนหลังให้บ้านที่ขาด ไม่ใช่การออกงวดใหม่</summary>
    public bool IsSupplement { get; init; }
}

/// <summary>งวดที่ระบบจะออกใบแจ้งหนี้ให้ครั้งถัดไป</summary>
public class InvoicePeriodResponse
{
    public int ProjectId { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
    public string MonthTh { get; init; } = string.Empty;
    public bool IsPayment { get; init; }
    public int? NextYear { get; init; }
    public string? NextMonthTh { get; init; }
    public bool IsSupplement { get; init; }
}

// ─── ใบแจ้งหนี้ล่วงหน้า ──────────────────────────────────────────────────────

public class AdvanceInvoicePreviewRequest
{
    public int? ResidentId { get; set; }
    public int? Year { get; set; }
    public List<int>? Months { get; set; }
}

public class AdvanceInvoicePreviewResponse
{
    public ResidentBrief? Resident { get; init; }
    /// <summary>ข้อความช่วงเดือน เช่น "ค่าส่วนกลางเดือน ม.ค. 69 - ธ.ค. 69"</summary>
    public string Label { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
    public decimal Total { get; init; }
}

/// <summary>ปีที่เริ่มออกใบแจ้งหนี้ล่วงหน้าได้</summary>
public class AdvanceInvoiceYearResponse
{
    public int Min { get; init; }
}

/// <summary>เดือนที่ยังออกใบแจ้งหนี้ล่วงหน้าได้ (12 แถวเสมอ)</summary>
public class AdvanceInvoiceMonthOption
{
    public int Month { get; init; }
    public string MonthTh { get; init; } = string.Empty;
    /// <summary>false = เดือนนี้มีงวดอยู่แล้ว เลือกไม่ได้</summary>
    public bool Active { get; init; }
}

public class AdvanceInvoiceCreateRequest
{
    [Required] public int ResidentId { get; set; }
    [Required] public int Year { get; set; }
    [Required] [MinLength(1, ErrorMessage = "กรุณาเลือกอย่างน้อย 1 เดือน")]
    public List<int> Months { get; set; } = [];
    public decimal? TotalAmount { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? BankName { get; set; }
    public string? ChequeNo { get; set; }
    public string? Note { get; set; }
    [Required] public DateOnly IssueDate { get; set; }
}

public class AdvanceInvoiceCreateResult
{
    public string? InvNo { get; init; }
    public List<PaymentResponse> Items { get; init; } = [];
}

/// <summary>แถวในหน้ารายการทวงถาม (งวดเดือนก่อนหน้าที่ยังไม่ชำระ)</summary>
public class ReminderRow
{
    public int Id { get; init; }
    public int ResidentId { get; init; }
    public string? InvNo { get; init; }
    public int Year { get; init; }
    public short Month { get; init; }
    public decimal AmountDue { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string HouseNo { get; init; } = string.Empty;
    public string? Address { get; init; }
    /// <summary>ยอดค้างรวมทุกงวดของบ้านหลังนี้</summary>
    public decimal DebtTotal { get; init; }
}

/// <summary>ผลของการลบใบแจ้งหนี้ทั้งงวด</summary>
public class DeleteByMonthResult
{
    public int Deleted { get; init; }
}
