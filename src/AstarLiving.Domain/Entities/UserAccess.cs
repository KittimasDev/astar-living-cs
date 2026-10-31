using AstarLiving.Domain.Enums;

namespace AstarLiving.Domain.Entities;

/// <summary>ตาราง gv_user_access — ระบบเดิมเป็น scaffold ยังไม่ได้ใช้จริง แต่ตารางมีอยู่</summary>
public class UserAccess : BaseEntityAudit
{
    public int Id { get; set; }
    public UserRole Role { get; set; }
    public bool Active { get; set; } = true;
}
