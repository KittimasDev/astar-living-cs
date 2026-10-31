using AstarLiving.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstarLiving.Infrastructure.Persistence.Configurations;

/// <summary>gv_projects</summary>
public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("gv_projects");
        builder.HasKey(e => e.Id).HasName("PK_0b11139334c1e8df3dd446cb20a");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(e => e.Address).HasColumnName("address").HasColumnType("text");
        builder.Property(e => e.ContactName).HasColumnName("contact_name").HasMaxLength(255);
        builder.Property(e => e.ContactPhone).HasColumnName("contact_phone").HasMaxLength(50);
        builder.Property(e => e.ContactFaxPhone).HasColumnName("contact_fax_phone").HasMaxLength(50);
        builder.Property(e => e.ProjectLogo).HasColumnName("project_logo").HasColumnType("text");
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);

        builder.ApplyAudit();
    }
}

/// <summary>gv_settings — อัตราค่าบริการ ระบบเดิมอ่านเฉพาะแถวแรกของโครงการ</summary>
public class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("gv_settings");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(e => e.MaintenanceFeeRate).HasColumnName("maintenance_fee_rate").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.FineRate).HasColumnName("fine_rate").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.CollectionFee).HasColumnName("collection_fee").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.DiscountRate).HasColumnName("discount_rate").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.EffectiveDate).HasColumnName("effective_date").HasColumnType("date");
        builder.Property(e => e.InvoicePrefix).HasColumnName("invoice_prefix").HasMaxLength(10);
        builder.Property(e => e.ReceiptPrefix).HasColumnName("receipt_prefix").HasMaxLength(10);
        builder.Property(e => e.InvoiceNote).HasColumnName("invoice_note").HasColumnType("text");
        builder.Property(e => e.ReceiptNote).HasColumnName("receipt_note").HasColumnType("text");
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);

        builder.HasOne(e => e.Project)
            .WithMany(p => p.Settings)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ApplyAudit();
    }
}

/// <summary>gv_payment_methods</summary>
public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("gv_payment_methods");
        builder.HasKey(e => e.Id).HasName("PK_d15a6475d9f1be58a50939e4614");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);

        builder.HasIndex(e => e.Code).IsUnique().HasDatabaseName("UQ_c8a214ddba9e88113ce624c5d79");

        builder.ApplyAudit();
    }
}

/// <summary>gv_notice_template — ข้อความเก็บเป็น HTML และมี token {resident_name} / {amount}</summary>
public class NoticeTemplateConfiguration : IEntityTypeConfiguration<NoticeTemplate>
{
    public void Configure(EntityTypeBuilder<NoticeTemplate> builder)
    {
        builder.ToTable("gv_notice_template");
        builder.HasKey(e => e.Id).HasName("PK_949711edd0d5144c891180b423f");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TopicName).HasColumnName("topic_name").HasMaxLength(50).IsRequired();
        builder.Property(e => e.IntroductionDetail).HasColumnName("introduction_detail").HasColumnType("text");
        builder.Property(e => e.AdditionalDetail).HasColumnName("additional_detail").HasColumnType("text");
        builder.Property(e => e.ClosureText).HasColumnName("closure_text").HasColumnType("text");
        builder.Property(e => e.ClosingWord).HasColumnName("closing_word").HasMaxLength(255).IsRequired();
        builder.Property(e => e.Remark).HasColumnName("remark").HasColumnType("text");
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);

        builder.HasOne(e => e.Project)
            .WithMany(p => p.NoticeTemplates)
            .HasForeignKey(e => e.ProjectId)
            .HasConstraintName("FK_ac0706a08a505b9e18a2c3aa87f")
            .OnDelete(DeleteBehavior.SetNull);

        builder.ApplyAudit();
    }
}

/// <summary>gv_activity_logs</summary>
public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("gv_activity_logs");
        builder.HasKey(e => e.Id).HasName("PK_5aabfbe1395ec63ea9e2cb1f95a");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.ProjectId).HasColumnName("project_id");
        builder.Property(e => e.Action).HasColumnName("action").HasMaxLength(255).IsRequired();
        builder.Property(e => e.TargetTable).HasColumnName("target_table").HasMaxLength(100);
        builder.Property(e => e.TargetId).HasColumnName("target_id");
        builder.Property(e => e.Timestamp).HasColumnName("timestamp").HasColumnType("timestamp").IsRequired();

        builder.ApplyAudit();
    }
}
