using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AstarLiving.Infrastructure.Persistence;

/// <summary>ผลของการเตรียมฐานข้อมูลตอนเปิดโปรแกรม</summary>
public enum SchemaBootstrapOutcome
{
    /// <summary>ฐานข้อมูลว่าง จึงสร้างตารางทั้งหมดใหม่</summary>
    CreatedFresh,

    /// <summary>พบตารางของระบบเดิมอยู่แล้ว จึงผูกเข้าระบบ migration โดยไม่แตะข้อมูล</summary>
    AdoptedExisting,

    /// <summary>มี migration ค้างอยู่และได้ปรับปรุงเรียบร้อย</summary>
    Migrated,

    /// <summary>ทันสมัยอยู่แล้ว ไม่ต้องทำอะไร</summary>
    AlreadyUpToDate,
}

/// <summary>
/// เตรียมฐานข้อมูลให้พร้อมใช้งานตอน API เริ่มทำงาน
///
/// รองรับสามสถานการณ์ด้วยโค้ดเส้นเดียว
/// 1) เครื่องลูกค้าใหม่: ฐานข้อมูลว่างเปล่า -> สร้างตารางทั้งหมดจาก migration
/// 2) ลูกค้าเดิมที่มีข้อมูลอยู่แล้ว: มีตาราง gv_* แต่ไม่มีประวัติ migration
///    -> บันทึกว่าสถานะปัจจุบันเท่ากับ Baseline แล้วไม่แตะข้อมูลเดิม
/// 3) อัปเดตเวอร์ชัน: มี migration ใหม่ค้างอยู่ -> ปรับปรุงให้
///
/// ขั้นตอนนี้ต้องรันที่เครื่องแม่เท่านั้น และต้องจบก่อนเปิดรับคำขอจากเครื่องลูก
/// </summary>
public class SchemaBootstrapper(AstarLivingDbContext db, ILogger<SchemaBootstrapper> logger)
{
    /// <summary>ตารางที่ใช้ตัดสินว่าฐานข้อมูลนี้เป็นของระบบเดิมหรือไม่</summary>
    private const string LegacyProbeTable = "gv_projects";

    private readonly AstarLivingDbContext _db = db;
    private readonly ILogger<SchemaBootstrapper> _logger = logger;

    public async Task<SchemaBootstrapOutcome> EnsureReadyAsync(CancellationToken ct)
    {
        // กันไม่ให้สองโปรเซสทำพร้อมกัน เช่นช่างเปิดโปรแกรมขึ้นมาเองขณะที่ service ทำงานอยู่
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_lock(hashtext('astarliving_schema_migration'))", ct);

        try
        {
            var historyExists = await TableExistsAsync("__EFMigrationsHistory", ct);
            var legacyExists = await TableExistsAsync(LegacyProbeTable, ct);

            if (!historyExists && legacyExists)
            {
                await AdoptLegacySchemaAsync(ct);
                _logger.LogInformation("ผูกฐานข้อมูลเดิมเข้ากับระบบ migration แล้ว โดยไม่แก้ไขข้อมูล");
                return SchemaBootstrapOutcome.AdoptedExisting;
            }

            var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToList();

            if (pending.Count == 0)
            {
                return SchemaBootstrapOutcome.AlreadyUpToDate;
            }

            var isFresh = !legacyExists;
            await _db.Database.MigrateAsync(ct);

            _logger.LogInformation(
                "ปรับปรุงฐานข้อมูลสำเร็จ {Count} รายการ: {Migrations}",
                pending.Count, string.Join(", ", pending));

            return isFresh ? SchemaBootstrapOutcome.CreatedFresh : SchemaBootstrapOutcome.Migrated;
        }
        finally
        {
            await _db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_unlock(hashtext('astarliving_schema_migration'))", ct);
        }
    }

    /// <summary>
    /// บันทึกว่าฐานข้อมูลที่มีอยู่แล้วอยู่ในสถานะเท่ากับ migration แรก
    /// เพื่อให้ migration ถัดไปรันต่อได้โดยไม่พยายามสร้างตารางซ้ำ
    /// </summary>
    private async Task AdoptLegacySchemaAsync(CancellationToken ct)
    {
        var baseline = _db.Database.GetMigrations().FirstOrDefault()
            ?? throw new InvalidOperationException("ไม่พบ migration แรกในแอสเซมบลี");

        await _db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            )
            """, ct);

        await _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ({0}, {1})
            ON CONFLICT ("MigrationId") DO NOTHING
            """.Replace("{0}", $"'{baseline}'").Replace("{1}", "'8.0.10'"), ct);
    }

    /// <summary>
    /// ตรวจว่ามีตารางนี้อยู่หรือไม่
    ///
    /// ใช้ SqlQueryRaw แทนการหยิบ DbConnection มาใช้เอง เพราะการครอบด้วย using
    /// จะไปปิดการเชื่อมต่อที่ DbContext ใช้ร่วมกันอยู่ ทำให้คำสั่งถัดไปพังทั้งหมด
    /// </summary>
    private async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        var qualified = $"public.\"{tableName}\"";

        var result = await _db.Database
            .SqlQueryRaw<bool>("SELECT to_regclass({0}) IS NOT NULL AS \"Value\"", qualified)
            .SingleAsync(ct);

        return result;
    }
}
