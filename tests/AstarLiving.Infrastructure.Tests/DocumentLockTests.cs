using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace AstarLiving.Infrastructure.Tests;

/// <summary>
/// ล็อกกันเลขเอกสารชนกันต้องเรียกใช้ได้จริงกับ PostgreSQL
///
/// ที่ต้องมีเทสต์ตัวนี้: PostgreSQL มี pg_advisory_xact_lock สองแบบเท่านั้น
/// คือรับ bigint ตัวเดียว หรือรับ integer สองตัว ไม่มีแบบรับ bigint สองตัว
/// เคยเขียนเป็น bigint สองตัวแล้วพังทั้งเส้นทางออกเอกสาร (ใบเสร็จ ใบแจ้งหนี้ รายรับอื่น)
/// โดยไม่มีเทสต์ไหนจับได้ เพราะเป็นข้อผิดพลาดที่โผล่ตอนรันจริงกับฐานข้อมูลเท่านั้น
/// </summary>
[Collection(LegacyDatabaseCollection.Name)]
public class DocumentLockTests(LegacyDatabaseFixture fixture, ITestOutputHelper output)
{
    private readonly LegacyDatabaseFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task ล็อกออกเลขเอกสารเรียกใช้ได้จริง()
    {
        if (_fixture.SkipReason is not null)
        {
            _output.WriteLine($"ข้ามเทสต์: {_fixture.SkipReason}");
            return;
        }

        await using var db = _fixture.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();

        const int projectId = 1;
        const int dayKey = 691003;

        // คำสั่งเดียวกับที่ PaymentQueries และ RevenueTransactionService ใช้จริง
        // ต้องไม่โยน 42883 (ไม่พบฟังก์ชัน)
        var act = async () => await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({projectId}::int4, {dayKey}::int4)");

        await act.Should().NotThrowAsync();

        await transaction.RollbackAsync();
    }
}
