namespace AstarLiving.Domain.Entities;

/// <summary>ตาราง gv_receipts — ใบเสร็จรับเงินค่าส่วนกลาง</summary>
public class Receipt : BaseEntityAudit
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    /// <summary>เลขที่ใบเสร็จ รูปแบบ YYMMDD(พ.ศ.) + 5 หลัก</summary>
    public string? ReceiptNo { get; set; }
    public DateOnly IssueDate { get; set; }
    public int ResidentId { get; set; }
    /// <summary>
    /// คอลัมน์ json ชื่อ payment_ids แต่ความจริงเก็บ "ชื่อเดือนภาษาอังกฤษ" เช่น ["JAN","FEB"]
    /// ไม่ใช่รหัสของ gv_payments — ตั้งชื่อผิดมาตั้งแต่ระบบเดิม
    /// </summary>
    public List<string>? MonthCodes { get; set; }
    public decimal TotalAmount { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? BankName { get; set; }
    public string? ChequeNo { get; set; }
    public string? Note { get; set; }
    /// <summary>เหตุผลที่ยกเลิกใบเสร็จ (active = false)</summary>
    public string? RejectedReason { get; set; }
    public DateOnly PaymentDate { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>true = ใบเสร็จนี้ใช้ส่วนลดรายปี (สร้างงวดล่วงหน้าเดือน 2-12 ให้อัตโนมัติ)</summary>
    public bool HaveDiscount { get; set; }
    public decimal? DiscountRate { get; set; }

    public Project Project { get; set; } = null!;
    public Resident Resident { get; set; } = null!;
    public PaymentMethod? PaymentMethod { get; set; }
    public User? CreatedUser { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<CollectionNotice> Collections { get; set; } = new List<CollectionNotice>();
}
