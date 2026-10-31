namespace AstarLiving.Domain.Entities;

/// <summary>
/// ตาราง gv_notice_template — เทมเพลตใบทวงถาม
/// ช่องข้อความเก็บเป็น HTML (จาก TinyMCE) และรองรับ token {resident_name} / {amount}
/// </summary>
public class NoticeTemplate : BaseEntityAudit
{
    public int Id { get; set; }
    public string TopicName { get; set; } = string.Empty;
    public string? IntroductionDetail { get; set; }
    public string? AdditionalDetail { get; set; }
    public string? ClosureText { get; set; }
    public string ClosingWord { get; set; } = string.Empty;
    public string? Remark { get; set; }
    public int ProjectId { get; set; }
    public bool Active { get; set; } = true;

    public Project Project { get; set; } = null!;
}
