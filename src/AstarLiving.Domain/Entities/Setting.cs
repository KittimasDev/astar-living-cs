namespace AstarLiving.Domain.Entities;

/// <summary>
/// ตาราง gv_settings — อัตราค่าบริการของโครงการ
/// หมายเหตุ: ระบบเดิมอ่าน project.settings[0] เสมอ (แถวแรกเท่านั้นที่มีผล)
/// </summary>
public class Setting : BaseEntityAudit
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    /// <summary>ค่าส่วนกลางต่อตารางวา</summary>
    public decimal? MaintenanceFeeRate { get; set; }
    /// <summary>ค่าเบี้ยปรับ (%)</summary>
    public decimal? FineRate { get; set; }
    /// <summary>ค่าทวงถามต่อเดือน (บาท)</summary>
    public decimal? CollectionFee { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public string? InvoicePrefix { get; set; }
    public string? ReceiptPrefix { get; set; }
    /// <summary>HTML จาก TinyMCE — ใช้เป็นหมายเหตุท้ายใบแจ้งหนี้</summary>
    public string? InvoiceNote { get; set; }
    /// <summary>HTML จาก TinyMCE — ใช้เป็นหมายเหตุท้ายใบเสร็จ</summary>
    public string? ReceiptNote { get; set; }
    /// <summary>ส่วนลดรายปี (%)</summary>
    public decimal? DiscountRate { get; set; }
    public bool Active { get; set; } = true;

    public Project Project { get; set; } = null!;
}
