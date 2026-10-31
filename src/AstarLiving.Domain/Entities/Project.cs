namespace AstarLiving.Domain.Entities;

/// <summary>ตาราง gv_projects — โครงการ/นิติบุคคล</summary>
public class Project : BaseEntityAudit
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactFaxPhone { get; set; }
    /// <summary>path สัมพัทธ์ เช่น uploads/project/3/2568_01_01_09-00-00.png</summary>
    public string? ProjectLogo { get; set; }
    public bool Active { get; set; } = true;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Resident> Residents { get; set; } = new List<Resident>();
    public ICollection<Setting> Settings { get; set; } = new List<Setting>();
    public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
    public ICollection<NoticeTemplate> NoticeTemplates { get; set; } = new List<NoticeTemplate>();
}
