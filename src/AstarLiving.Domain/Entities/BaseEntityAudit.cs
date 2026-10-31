namespace AstarLiving.Domain.Entities;

/// <summary>
/// คอลัมน์ audit ที่ทุกตารางในระบบเดิมสืบทอดมาจาก BaseEntityAudit ของ TypeORM
/// (created_at / created_by / updated_at / updated_by / deleted_at / deleted_by)
/// deleted_at ทำหน้าที่เป็น soft-delete marker เหมือน @DeleteDateColumn
/// </summary>
public abstract class BaseEntityAudit
{
    public DateTimeOffset? CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedBy { get; set; }
}
