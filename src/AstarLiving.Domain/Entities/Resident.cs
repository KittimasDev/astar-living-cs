namespace AstarLiving.Domain.Entities;

/// <summary>ตาราง gv_residents — ลูกบ้าน/ผู้อยู่อาศัย</summary>
public class Resident : BaseEntityAudit
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string HouseNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    /// <summary>อัตราส่วนกรรมสิทธิ์ (ตารางวา) — ตัวคูณค่าส่วนกลาง</summary>
    public decimal? OwnershipRatio { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool Active { get; set; } = true;

    public Project Project { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
}
