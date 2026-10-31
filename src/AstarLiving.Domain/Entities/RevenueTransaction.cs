namespace AstarLiving.Domain.Entities;

/// <summary>ตาราง gv_receipts_revenues — ใบเสร็จรายรับอื่น ๆ (นอกเหนือค่าส่วนกลาง)</summary>
public class RevenueTransaction : BaseEntityAudit
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    /// <summary>unique index uq_gv_receipts_revenues_receipt_no</summary>
    public string ReceiptNo { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public int ResidentId { get; set; }
    /// <summary>คอลัมน์ jsonb — { items: [...], grandTotal }</summary>
    public RevenuePaymentDetail PaymentDetail { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public int PaymentMethodId { get; set; }
    public string? BankName { get; set; }
    public string? ChequeNo { get; set; }
    public string? Note { get; set; }
    public DateOnly PaymentDate { get; set; }
    public bool Active { get; set; } = true;
    public string? RejectedReason { get; set; }

    public Project Project { get; set; } = null!;
    public Resident Resident { get; set; } = null!;
    public PaymentMethod PaymentMethod { get; set; } = null!;
}

public class RevenuePaymentDetail
{
    public List<RevenuePaymentItem> Items { get; set; } = new();
    public decimal? GrandTotal { get; set; }
}

public class RevenuePaymentItem
{
    public int ItemNo { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>ระบบเดิมไม่คำนวณซ้ำจาก quantity × unitPrice — ใช้ค่าที่ client ส่งมา</summary>
    public decimal Total { get; set; }
}
