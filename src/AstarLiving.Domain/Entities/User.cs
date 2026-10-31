using AstarLiving.Domain.Enums;

namespace AstarLiving.Domain.Entities;

/// <summary>ตาราง gv_users</summary>
public class User : BaseEntityAudit
{
    public int Id { get; set; }
    /// <summary>เก็บเป็นตัวพิมพ์เล็ก trim แล้วเสมอ (ระบบเดิมทำตอน login และตอนสร้าง)</summary>
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public UserRole Role { get; set; }
    public int? ProjectId { get; set; }
    public bool Active { get; set; } = true;

    public Project? Project { get; set; }
}
