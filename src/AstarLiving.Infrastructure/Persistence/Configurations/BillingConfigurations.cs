using System.Text.Json;
using AstarLiving.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AstarLiving.Infrastructure.Persistence.Configurations;

/// <summary>gv_users</summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("gv_users");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
        builder.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
        builder.Property(e => e.FullName).HasColumnName("full_name").HasMaxLength(255).IsRequired();
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(50);
        builder.Property(e => e.Role).HasColumnName("role").HasColumnType("gv_users_role_enum").IsRequired();
        builder.Property(e => e.ProjectId).HasColumnName("project_id");
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);

        builder.HasIndex(e => e.Username).IsUnique();

        builder.HasOne(e => e.Project)
            .WithMany(p => p.Users)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ApplyAudit();
    }
}

/// <summary>gv_residents</summary>
public class ResidentConfiguration : IEntityTypeConfiguration<Resident>
{
    public void Configure(EntityTypeBuilder<Resident> builder)
    {
        builder.ToTable("gv_residents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(e => e.HouseNo).HasColumnName("house_no").HasMaxLength(50).IsRequired();
        builder.Property(e => e.FullName).HasColumnName("full_name").HasMaxLength(255).IsRequired();
        builder.Property(e => e.OwnershipRatio).HasColumnName("ownership_ratio").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(50);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(e => e.Address).HasColumnName("address").HasColumnType("text");
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);

        builder.HasOne(e => e.Project)
            .WithMany(p => p.Residents)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ApplyAudit();
    }
}

/// <summary>gv_payments — หนึ่งแถวคือหนึ่งงวดค่าส่วนกลาง</summary>
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("gv_payments");
        builder.HasKey(e => e.Id).HasName("PK_491bdc3fe4ae243631727806072");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ResidentId).HasColumnName("resident_id").IsRequired();
        builder.Property(e => e.InvNo).HasColumnName("inv_no").HasMaxLength(50);
        builder.Property(e => e.Year).HasColumnName("year").IsRequired();
        builder.Property(e => e.Month).HasColumnName("month").HasColumnType("smallint").IsRequired();
        builder.Property(e => e.AmountDue).HasColumnName("amount_due").HasColumnType(AuditConfiguration.Numeric).IsRequired();
        builder.Property(e => e.PaidAmount).HasColumnName("paid_amount").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.Fine).HasColumnName("fine").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.CollectionFee).HasColumnName("collection_fee").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.TotalDue).HasColumnName("total_due").HasColumnType(AuditConfiguration.Numeric).IsRequired();
        builder.Property(e => e.PaymentDate).HasColumnName("payment_date").HasColumnType("date");
        builder.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id");
        builder.Property(e => e.Status).HasColumnName("status").HasColumnType("gv_payments_status_enum").IsRequired();
        builder.Property(e => e.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(e => e.CollectionNoticeId).HasColumnName("collection_notice_id");
        builder.Property(e => e.HasCollectionNotice).HasColumnName("has_collection_notice").HasDefaultValue(false);
        builder.Property(e => e.CollectionPaid).HasColumnName("collection_paid").HasDefaultValue(false);
        builder.Property(e => e.Advance).HasColumnName("advance").HasDefaultValue(false);
        builder.Property(e => e.ReceiptsId).HasColumnName("receipts_id");
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);

        builder.Ignore(e => e.IsZeroCharge);

        builder.HasOne(e => e.Resident)
            .WithMany(r => r.Payments)
            .HasForeignKey(e => e.ResidentId)
            .HasConstraintName("FK_6e12b601a48c4fe9cbfaab2a40e")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.Receipt)
            .WithMany(r => r.Payments)
            .HasForeignKey(e => e.ReceiptsId)
            .HasConstraintName("FK_e5806facf7832f022d4da9700e9")
            .OnDelete(DeleteBehavior.SetNull);

        // ระบบเดิมใช้คอลัมน์ audit created_by เป็น FK ไปยัง gv_users ด้วย (ใช้ในรายงาน REPORT_INVOICE)
        builder.HasOne(e => e.CreatedUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_f2ec69212baf11990423f0b69ca")
            .OnDelete(DeleteBehavior.NoAction);

        builder.ApplyAudit();
    }
}

/// <summary>gv_receipts — ใบเสร็จค่าส่วนกลาง</summary>
public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    /// <summary>
    /// คอลัมน์ json ชื่อ payment_ids เก็บ array ชื่อเดือนอังกฤษ เช่น ["JAN","FEB"]
    /// ไม่ใช่รหัสของ gv_payments ตามที่ชื่อชวนให้เข้าใจ
    /// </summary>
    private static readonly ValueComparer<List<string>?> MonthCodesComparer = new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        v => v == null ? 0 : v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v == null ? null : v.ToList());

    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("gv_receipts");
        builder.HasKey(e => e.Id).HasName("PK_59faac42efbb23216c4e855208a");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(e => e.ReceiptNo).HasColumnName("receipt_no").HasMaxLength(50);
        builder.Property(e => e.IssueDate).HasColumnName("issue_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.ResidentId).HasColumnName("resident_id").IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType(AuditConfiguration.Numeric).IsRequired();
        builder.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id");
        builder.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(100);
        builder.Property(e => e.ChequeNo).HasColumnName("cheque_no").HasMaxLength(100);
        builder.Property(e => e.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(e => e.RejectedReason).HasColumnName("rejected_reason").HasColumnType("text");
        builder.Property(e => e.PaymentDate).HasColumnName("payment_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(e => e.HaveDiscount).HasColumnName("have_discount").HasDefaultValue(false);
        builder.Property(e => e.DiscountRate).HasColumnName("discount_rate").HasColumnType(AuditConfiguration.Numeric);

        builder.Property(e => e.MonthCodes)
            .HasColumnName("payment_ids")
            .HasColumnType("json")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v) ? null : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null),
                MonthCodesComparer);

        builder.HasOne(e => e.Project)
            .WithMany(p => p.Receipts)
            .HasForeignKey(e => e.ProjectId)
            .HasConstraintName("FK_7c0dd3529ae2256401560f9e2ac")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Resident)
            .WithMany(r => r.Receipts)
            .HasForeignKey(e => e.ResidentId)
            .HasConstraintName("FK_08721186aebed5c9224ca2995ba")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.PaymentMethod)
            .WithMany(m => m.Receipts)
            .HasForeignKey(e => e.PaymentMethodId)
            .HasConstraintName("FK_7311768c5bd78488d838330b485")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.CreatedUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_3e5e24db0102026afdcef027f71")
            .OnDelete(DeleteBehavior.NoAction);

        builder.ApplyAudit();
    }
}

/// <summary>gv_collection_notices — ใบทวงถาม / ค่าเบี้ยปรับ</summary>
public class CollectionNoticeConfiguration : IEntityTypeConfiguration<CollectionNotice>
{
    public void Configure(EntityTypeBuilder<CollectionNotice> builder)
    {
        builder.ToTable("gv_collection_notices");
        builder.HasKey(e => e.Id).HasName("PK_335e4f143a5db6456fbd885d0f1");

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ResidentId).HasColumnName("resident_id").IsRequired();
        builder.Property(e => e.PaymentId).HasColumnName("payment_id");
        builder.Property(e => e.NoticesType).HasColumnName("notices_type")
            .HasColumnType("gv_collection_notices_notices_type_enum").IsRequired();
        builder.Property(e => e.Year).HasColumnName("year").IsRequired();
        builder.Property(e => e.Month).HasColumnName("month").HasColumnType("smallint").IsRequired();
        builder.Property(e => e.NoticeDate).HasColumnName("notice_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.FeeAmount).HasColumnName("fee_amount").HasColumnType(AuditConfiguration.Numeric);
        builder.Property(e => e.Status).HasColumnName("status")
            .HasColumnType("gv_collection_notices_status_enum").IsRequired();
        builder.Property(e => e.ReceiptId).HasColumnName("receipt_id");
        builder.Property(e => e.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(e => e.DeductedAt).HasColumnName("deducted_at").HasColumnType("timestamptz");
        builder.Property(e => e.DeductedBy).HasColumnName("deducted_by");
        builder.Property(e => e.DeductedReason).HasColumnName("deducted_reason").HasColumnType("text");
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);

        builder.HasOne(e => e.Resident)
            .WithMany()
            .HasForeignKey(e => e.ResidentId)
            .HasConstraintName("FK_b2176c4fe016bb1828816df12de")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.Payment)
            .WithMany(p => p.Collections)
            .HasForeignKey(e => e.PaymentId)
            .HasConstraintName("FK_3bb3c9b9b4d6b3fafa48b1fda81")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Receipt)
            .WithMany(r => r.Collections)
            .HasForeignKey(e => e.ReceiptId)
            .HasConstraintName("FK_a098d82b900cec4c8244b68e28a")
            .OnDelete(DeleteBehavior.SetNull);

        builder.ApplyAudit();
    }
}

/// <summary>gv_receipts_revenues — ใบเสร็จรายรับอื่น ๆ</summary>
public class RevenueTransactionConfiguration : IEntityTypeConfiguration<RevenueTransaction>
{
    public void Configure(EntityTypeBuilder<RevenueTransaction> builder)
    {
        builder.ToTable("gv_receipts_revenues");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(e => e.ReceiptNo).HasColumnName("receipt_no").HasMaxLength(50).IsRequired();
        builder.Property(e => e.IssueDate).HasColumnName("issue_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.ResidentId).HasColumnName("resident_id").IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(15,2)").IsRequired();
        builder.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id").IsRequired();
        builder.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(100);
        builder.Property(e => e.ChequeNo).HasColumnName("cheque_no").HasMaxLength(100);
        builder.Property(e => e.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(e => e.PaymentDate).HasColumnName("payment_date").HasColumnType("date").IsRequired();
        builder.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(e => e.RejectedReason).HasColumnName("rejected_reason").HasColumnType("text");

        // jsonb: { items: [{ itemNo, description, quantity, unitPrice, total }], grandTotal }
        // ชื่อคีย์ใน JSON เป็น camelCase ตามที่ frontend เดิมส่งมา จึงต้องระบุ naming policy ให้ตรง
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        builder.Property(e => e.PaymentDetail)
            .HasColumnName("payment_detail")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<RevenuePaymentDetail>(v, jsonOptions) ?? new RevenuePaymentDetail(),
                new ValueComparer<RevenuePaymentDetail>(
                    (a, b) => JsonSerializer.Serialize(a, jsonOptions) == JsonSerializer.Serialize(b, jsonOptions),
                    v => JsonSerializer.Serialize(v, jsonOptions).GetHashCode(),
                    v => JsonSerializer.Deserialize<RevenuePaymentDetail>(JsonSerializer.Serialize(v, jsonOptions), jsonOptions)!));

        builder.HasIndex(e => e.ReceiptNo).IsUnique().HasDatabaseName("uq_gv_receipts_revenues_receipt_no");

        builder.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.Resident).WithMany().HasForeignKey(e => e.ResidentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.PaymentMethod).WithMany().HasForeignKey(e => e.PaymentMethodId).OnDelete(DeleteBehavior.NoAction);

        builder.ApplyAudit();
        ApplyTimestampWithoutTimeZoneAudit(builder);
    }

    /// <summary>
    /// ตารางนี้ถูกสร้างด้วย SQL เขียนมือ (sql/create_gv_transactions.sql) ไม่ใช่ TypeORM
    /// คอลัมน์ audit จึงเป็น "timestamp without time zone" ต่างจากทุกตารางอื่นที่เป็น timestamptz
    ///
    /// Npgsql 8 จะโยน InvalidCastException ถ้าอ่านคอลัมน์ชนิดนี้เป็น DateTimeOffset ตรง ๆ
    /// จึงต้องแปลงเอง โดยถือว่าค่าที่เก็บไว้เป็นเวลา UTC (เซิร์ฟเวอร์ตั้ง TimeZone = Etc/UTC
    /// และไดรเวอร์ pg ของ Node เขียนค่าเป็น instant แบบ UTC ลงไป)
    /// </summary>
    private static void ApplyTimestampWithoutTimeZoneAudit(EntityTypeBuilder<RevenueTransaction> builder)
    {
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp").HasConversion(UtcConverter);
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp").HasConversion(UtcConverter);
        builder.Property(e => e.DeletedAt).HasColumnType("timestamp").HasConversion(UtcConverter);
    }

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset?, DateTime?> UtcConverter =
        new(
            // Npgsql ต้องการ Kind = Unspecified สำหรับ timestamp ที่ไม่มีโซนเวลา
            // ถ้าส่ง Kind = Utc เข้าไปจะถูกปฏิเสธทันที
            v => v == null ? null : DateTime.SpecifyKind(v.Value.UtcDateTime, DateTimeKind.Unspecified),
            v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
}
