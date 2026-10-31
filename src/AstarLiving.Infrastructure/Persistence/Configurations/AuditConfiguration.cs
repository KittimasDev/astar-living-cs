using AstarLiving.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstarLiving.Infrastructure.Persistence.Configurations;

/// <summary>
/// คอลัมน์ audit 6 ตัวที่ทุกตารางมีเหมือนกัน และ query filter ของ soft delete
/// ทุก Configuration ต้องเรียก ApplyAudit() เพื่อไม่ให้หลุด
/// </summary>
internal static class AuditConfiguration
{
    public static EntityTypeBuilder<T> ApplyAudit<T>(this EntityTypeBuilder<T> builder)
        where T : BaseEntityAudit
    {
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
        builder.Property(e => e.DeletedBy).HasColumnName("deleted_by");

        // soft delete แบบเดียวกับ @DeleteDateColumn ของ TypeORM
        // ทุก query จะเติม "deleted_at IS NULL" ให้อัตโนมัติ เหมือนพฤติกรรมเดิม
        builder.HasQueryFilter(e => e.DeletedAt == null);

        return builder;
    }

    /// <summary>numeric ที่ไม่ได้กำหนด precision/scale — ตรงกับ schema เดิมทุกคอลัมน์เงิน</summary>
    public const string Numeric = "numeric";
}
