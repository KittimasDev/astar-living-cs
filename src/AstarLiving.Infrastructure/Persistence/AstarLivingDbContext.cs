using AstarLiving.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Infrastructure.Persistence;

/// <summary>
/// DbContext ที่ map ตาราง gv_* ของระบบเดิมแบบตรงตัว
/// ห้ามให้ EF ตั้งชื่ออะไรเอง ทุกตาราง/คอลัมน์/คีย์ระบุชื่อไว้ชัดเจนในคลาส Configuration
/// </summary>
public class AstarLivingDbContext(DbContextOptions<AstarLivingDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<NoticeTemplate> NoticeTemplates => Set<NoticeTemplate>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<CollectionNotice> CollectionNotices => Set<CollectionNotice>();
    public DbSet<RevenueTransaction> RevenueTransactions => Set<RevenueTransaction>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ชื่อ enum ใน PostgreSQL ที่ TypeORM สร้างไว้ ต้องประกาศให้ EF รู้จัก
        // (ฝั่ง NpgsqlDataSource ต้อง MapEnum ด้วยชื่อเดียวกัน ดู DataSourceFactory)
        modelBuilder.HasPostgresEnum<Domain.Enums.UserRole>(
            name: NpgsqlEnums.UserRoleType, nameTranslator: NpgsqlEnums.Translator);
        modelBuilder.HasPostgresEnum<Domain.Enums.PaymentStatus>(
            name: NpgsqlEnums.PaymentStatusType, nameTranslator: NpgsqlEnums.Translator);
        modelBuilder.HasPostgresEnum<Domain.Enums.NoticeType>(
            name: NpgsqlEnums.NoticeTypeType, nameTranslator: NpgsqlEnums.Translator);
        modelBuilder.HasPostgresEnum<Domain.Enums.NoticeStatus>(
            name: NpgsqlEnums.NoticeStatusType, nameTranslator: NpgsqlEnums.Translator);

        // gv_user_access จงใจไม่ถูก map: ตารางนี้เป็น scaffold ที่ระบบเดิมไม่เคยใช้ (0 แถว)
        // และคอลัมน์ role ของมันเป็น enum คนละชนิด (gv_user_access_role_enum) ที่ชน UserRole
        // ตารางยังคงอยู่ในฐานข้อมูลตามเดิม เพียงแต่ EF ไม่รู้จัก

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AstarLivingDbContext).Assembly);
    }
}
