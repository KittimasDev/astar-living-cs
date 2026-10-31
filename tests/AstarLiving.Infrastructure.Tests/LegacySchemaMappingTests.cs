using AstarLiving.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace AstarLiving.Infrastructure.Tests;

/// <summary>
/// พิสูจน์ว่า EF mapping อ่านตาราง gv_* ของระบบเดิมได้ครบทุกตาราง
/// ถ้าคอลัมน์หรือชนิดข้อมูลไหน map ผิด เทสต์กลุ่มนี้จะพังทันที
/// </summary>
[Collection(LegacyDatabaseCollection.Name)]
public class LegacySchemaMappingTests(LegacyDatabaseFixture fixture, ITestOutputHelper output)
{
    private readonly LegacyDatabaseFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    private bool Unavailable()
    {
        if (_fixture.SkipReason is null) return false;
        _output.WriteLine($"ข้ามเทสต์: {_fixture.SkipReason}");
        return true;
    }

    [Fact]
    public async Task Every_table_is_readable()
    {
        if (Unavailable()) return;
        await using var db = _fixture.CreateContext();

        var counts = new Dictionary<string, int>
        {
            ["gv_projects"] = await db.Projects.CountAsync(),
            ["gv_users"] = await db.Users.CountAsync(),
            ["gv_residents"] = await db.Residents.CountAsync(),
            ["gv_settings"] = await db.Settings.CountAsync(),
            ["gv_payment_methods"] = await db.PaymentMethods.CountAsync(),
            ["gv_notice_template"] = await db.NoticeTemplates.CountAsync(),
            ["gv_payments"] = await db.Payments.CountAsync(),
            ["gv_receipts"] = await db.Receipts.CountAsync(),
            ["gv_collection_notices"] = await db.CollectionNotices.CountAsync(),
            ["gv_receipts_revenues"] = await db.RevenueTransactions.CountAsync(),
            ["gv_activity_logs"] = await db.ActivityLogs.CountAsync(),
        };

        foreach (var (table, count) in counts) _output.WriteLine($"{table,-24} {count,6}");

        counts["gv_payments"].Should().BeGreaterThan(0, "ฐานข้อมูลอ้างอิงต้องมีข้อมูลงวดค่าส่วนกลางจริง");
    }

    /// <summary>enum ของ PostgreSQL ต้องอ่านเป็น enum ของ C# ได้ ไม่ใช่พังตอน deserialize</summary>
    [Fact]
    public async Task Postgres_enums_map_to_clr_enums()
    {
        if (Unavailable()) return;
        await using var db = _fixture.CreateContext();

        var statuses = await db.Payments.Select(p => p.Status).Distinct().ToListAsync();
        var roles = await db.Users.Select(u => u.Role).Distinct().ToListAsync();

        _output.WriteLine($"PaymentStatus ที่พบ: {string.Join(", ", statuses)}");
        _output.WriteLine($"UserRole ที่พบ: {string.Join(", ", roles)}");

        statuses.Should().OnlyContain(s => Enum.IsDefined(s));
        roles.Should().OnlyContain(r => Enum.IsDefined(r));
    }

    /// <summary>
    /// payment_ids เป็นคอลัมน์ json ที่เก็บชื่อเดือนอังกฤษ ไม่ใช่รหัสงวด
    /// ถ้า map ผิดจะ deserialize ไม่ผ่านหรือได้ค่าว่างทั้งหมด
    /// </summary>
    [Fact]
    public async Task Receipt_month_codes_deserialize_as_english_month_names()
    {
        if (Unavailable()) return;
        await using var db = _fixture.CreateContext();

        var sample = await db.Receipts
            .Where(r => r.MonthCodes != null)
            .OrderByDescending(r => r.Id)
            .Select(r => new { r.Id, r.ReceiptNo, r.MonthCodes })
            .FirstOrDefaultAsync();

        if (sample is null)
        {
            _output.WriteLine("ไม่พบใบเสร็จที่มี payment_ids ในฐานข้อมูลนี้");
            return;
        }

        _output.WriteLine($"ใบเสร็จ {sample.ReceiptNo}: [{string.Join(", ", sample.MonthCodes!)}]");
        sample.MonthCodes.Should().NotBeEmpty();
    }

    /// <summary>payment_detail เป็น jsonb ที่คีย์ภายในเป็น camelCase ตามที่ frontend เดิมส่งมา</summary>
    [Fact]
    public async Task Revenue_payment_detail_deserializes_with_camel_case_keys()
    {
        if (Unavailable()) return;
        await using var db = _fixture.CreateContext();

        var sample = await db.RevenueTransactions.OrderByDescending(t => t.Id).FirstOrDefaultAsync();
        if (sample is null)
        {
            _output.WriteLine("ไม่พบรายการรายรับอื่นในฐานข้อมูลนี้");
            return;
        }

        _output.WriteLine($"ใบเสร็จรายรับอื่น {sample.ReceiptNo}: {sample.PaymentDetail.Items.Count} รายการ, รวม {sample.TotalAmount}");
        sample.PaymentDetail.Items.Should().NotBeEmpty("payment_detail ต้อง deserialize ได้จริง ไม่ใช่ได้ object ว่าง");
        sample.PaymentDetail.Items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Description));
    }

    /// <summary>ความสัมพันธ์ที่ใช้บ่อยที่สุดในการออกบิล ต้อง Include ได้โดยไม่ต้องเขียน SQL เอง</summary>
    [Fact]
    public async Task Resident_graph_used_by_billing_loads()
    {
        if (Unavailable()) return;
        await using var db = _fixture.CreateContext();

        var resident = await db.Residents
            .Include(r => r.Project).ThenInclude(p => p.Settings)
            .Where(r => r.Active)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync();

        if (resident is null)
        {
            _output.WriteLine("ไม่พบลูกบ้านที่ใช้งานอยู่");
            return;
        }

        var settings = resident.Project.Settings.FirstOrDefault();
        _output.WriteLine(
            $"บ้าน {resident.HouseNo} / {resident.FullName} / อัตราส่วน {resident.OwnershipRatio} " +
            $"/ โครงการ {resident.Project.Name} / ค่าส่วนกลางต่อตารางวา {settings?.MaintenanceFeeRate}");

        resident.Project.Should().NotBeNull();
    }

    /// <summary>
    /// soft delete ต้องถูกกรองอัตโนมัติเหมือน @DeleteDateColumn ของ TypeORM
    /// </summary>
    [Fact]
    public async Task Soft_deleted_rows_are_filtered_out()
    {
        if (Unavailable()) return;
        await using var db = _fixture.CreateContext();

        var visible = await db.Payments.CountAsync();
        var all = await db.Payments.IgnoreQueryFilters().CountAsync();
        var deleted = await db.Payments.IgnoreQueryFilters().CountAsync(p => p.DeletedAt != null);

        _output.WriteLine($"gv_payments: เห็น {visible} จากทั้งหมด {all} (ถูกลบแบบ soft {deleted})");
        visible.Should().Be(all - deleted);
    }
}
