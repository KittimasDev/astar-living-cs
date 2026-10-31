using System.ComponentModel.DataAnnotations;

namespace AstarLiving.Shared.Contracts;

/// <summary>หนึ่งบรรทัดในใบเสร็จรายรับอื่น ๆ (เก็บเป็น jsonb)</summary>
public class RevenueItemContract
{
    public int ItemNo { get; set; }

    [Required(ErrorMessage = "กรุณากรอกรายละเอียด")]
    public string Description { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>ยอดของบรรทัดนี้ — ระบบเดิมไม่คำนวณซ้ำจาก quantity x unitPrice</summary>
    public decimal Total { get; set; }
}

public class RevenuePaymentDetailContract
{
    public List<RevenueItemContract> Items { get; set; } = [];

    /// <summary>ยอดรวม — ถูกคำนวณใหม่ฝั่งเซิร์ฟเวอร์เสมอ ค่าที่ส่งมาถูกทับ</summary>
    public decimal? GrandTotal { get; set; }
}

/// <summary>gv_receipts_revenues — ใบเสร็จรายรับอื่น ๆ</summary>
public class RevenueTransactionResponse
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string ReceiptNo { get; init; } = string.Empty;
    public DateOnly IssueDate { get; init; }
    public int ResidentId { get; init; }
    public RevenuePaymentDetailContract? PaymentDetail { get; init; }
    public decimal TotalAmount { get; init; }
    public int PaymentMethodId { get; init; }
    public string? BankName { get; init; }
    public string? ChequeNo { get; init; }
    public string? Note { get; init; }
    public DateOnly PaymentDate { get; init; }
    public bool Active { get; init; }
    public string? RejectedReason { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public ResidentBrief? Resident { get; init; }
}

public class CreateRevenueTransactionRequest
{
    [Required] public int ProjectId { get; set; }

    /// <summary>ปล่อยว่างให้ระบบออกเลขให้ ถ้าส่งมาแล้วซ้ำจะถูกปฏิเสธ</summary>
    public string? ReceiptNo { get; set; }

    [Required] public DateOnly IssueDate { get; set; }
    [Required] public int ResidentId { get; set; }
    [Required] public RevenuePaymentDetailContract PaymentDetail { get; set; } = new();

    /// <summary>ค่าที่ส่งมาจะถูกแทนด้วยผลรวมของรายการเสมอ</summary>
    public decimal? TotalAmount { get; set; }

    [Required] public int PaymentMethodId { get; set; }
    public string? BankName { get; set; }
    public string? ChequeNo { get; set; }
    public string? Note { get; set; }
    [Required] public DateOnly PaymentDate { get; set; }
}

public class RejectRevenueTransactionRequest
{
    public string? RejectedReason { get; set; }
}

public class DeleteRevenueResult
{
    public string Message { get; init; } = string.Empty;
}
