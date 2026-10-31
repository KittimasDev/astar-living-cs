using AstarLiving.Domain.Enums;

namespace AstarLiving.Domain.Entities;

/// <summary>
/// ตาราง gv_payments — หนึ่งแถวคือค่าส่วนกลางหนึ่งงวด (ใบแจ้งหนี้หนึ่งรายการ) ของลูกบ้านหนึ่งราย
/// คีย์ตามธรรมชาติคือ (resident_id, year, month) แต่ schema เดิมไม่ได้บังคับ unique ไว้
/// </summary>
public class Payment : BaseEntityAudit
{
    public int Id { get; set; }
    public int ResidentId { get; set; }
    /// <summary>เลขที่ใบแจ้งหนี้ รูปแบบ YYMMDD(พ.ศ.) + 5 หลัก</summary>
    public string? InvNo { get; set; }
    /// <summary>ปี พ.ศ.</summary>
    public int Year { get; set; }
    /// <summary>เดือน 1-12</summary>
    public short Month { get; set; }
    /// <summary>ค่าส่วนกลางของงวดนี้</summary>
    public decimal AmountDue { get; set; }
    public decimal? PaidAmount { get; set; }
    /// <summary>ค่าเบี้ยปรับ</summary>
    public decimal? Fine { get; set; }
    /// <summary>ค่าทวงถาม</summary>
    public decimal? CollectionFee { get; set; }
    /// <summary>ยอดรวมที่ต้องชำระ (รวมหนี้ค้างตามสูตร GrandTotal)</summary>
    public decimal TotalDue { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public int? PaymentMethodId { get; set; }
    public PaymentStatus Status { get; set; }
    public string? Note { get; set; }
    public int? CollectionNoticeId { get; set; }
    public bool HasCollectionNotice { get; set; }
    public bool CollectionPaid { get; set; }
    /// <summary>true = งวดที่เกิดจากการชำระล่วงหน้า (ไม่นับในการหางวดบิลล่าสุด)</summary>
    public bool Advance { get; set; }
    public int? ReceiptsId { get; set; }
    public bool Active { get; set; } = true;

    public Resident Resident { get; set; } = null!;
    public Receipt? Receipt { get; set; }
    /// <summary>ระบบเดิม map created_by เป็น FK ไปยัง gv_users ด้วย (ใช้ในรายงาน REPORT_INVOICE)</summary>
    public User? CreatedUser { get; set; }
    public ICollection<CollectionNotice> Collections { get; set; } = new List<CollectionNotice>();

    /// <summary>งวดที่ไม่มียอดอะไรเลย — ระบบเดิมใช้ตัดออกจากการนับหนี้ค้าง</summary>
    public bool IsZeroCharge =>
        (AmountDue + (Fine ?? 0m) + (CollectionFee ?? 0m)) <= 0m;
}
