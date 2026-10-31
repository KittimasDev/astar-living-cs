namespace AstarLiving.Domain.Entities;

/// <summary>ตาราง gv_payment_methods (CASH / TRANSFER / CHEQUE ...)</summary>
public class PaymentMethod : BaseEntityAudit
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Active { get; set; } = true;

    public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
}
