namespace AstarLiving.Domain.Entities;

/// <summary>ตาราง gv_activity_logs — บันทึกการทำงานของระบบ</summary>
public class ActivityLog : BaseEntityAudit
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? ProjectId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TargetTable { get; set; }
    public int? TargetId { get; set; }
    public DateTime Timestamp { get; set; }
}
