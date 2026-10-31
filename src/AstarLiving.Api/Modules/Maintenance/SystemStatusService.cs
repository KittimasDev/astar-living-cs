using System.Diagnostics;
using AstarLiving.Api.Configuration;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AstarLiving.Api.Modules.Maintenance;

/// <summary>
/// สถานะระบบที่แสดงบนหน้าสำรองฐานข้อมูล
///
/// หน้านี้มีไว้ให้ "ตรวจสอบ" ตัวเลขจึงต้องเชื่อถือได้ และต้องบอกให้ชัดเมื่อเป็นค่าประมาณ
/// </summary>
public sealed class SystemStatusService(
    AstarLivingDbContext db,
    NpgsqlDataSource dataSource,
    IOptions<AstarLivingOptions> options,
    ILogger<SystemStatusService> logger)
{
    /// <summary>
    /// ตารางที่แสดงจำนวนแถว ชื่อตารางเป็นค่าคงที่ในโค้ด ห้ามรับจากคำขอ
    /// เพราะชื่อตารางใส่เป็นพารามิเตอร์ของคำสั่งไม่ได้ ถ้ารับมาจะเปิดช่องให้แทรกคำสั่ง
    /// </summary>
    private static readonly (string Table, string Label)[] CountedTables =
    [
        ("gv_residents", "ลูกบ้าน"),
        ("gv_payments", "งวดค่าส่วนกลาง"),
        ("gv_receipts", "ใบเสร็จ"),
        ("gv_collection_notices", "ใบทวงถาม"),
        ("gv_receipts_revenues", "รายรับอื่น ๆ"),
        ("gv_users", "ผู้ใช้งาน"),
    ];

    private readonly AstarLivingOptions _options = options.Value;

    public async Task<SystemStatusResponse> GetAsync(int? projectId, CancellationToken ct)
    {
        var response = new SystemStatusResponse
        {
            ServerVersion = typeof(SystemStatusService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            ProjectName = await ReadProjectNameAsync(projectId, ct),
            SchemaVersion = await ReadSchemaVersionAsync(ct),
            LastBackupAt = ReadLastBackupAt(),
            AvailableDiskBytes = ReadAvailableDisk(),
            PgDumpVersion = await ReadPgDumpVersionAsync(ct),
        };

        await using var connection = await dataSource.OpenConnectionAsync(ct);

        response.PostgresVersion = connection.PostgreSqlVersion.ToString();
        response.DatabaseSizeBytes = await ReadDatabaseSizeAsync(connection, ct);

        var (counts, estimated) = await ReadRowCountsAsync(connection, ct);
        response.RowCounts = counts;
        response.RowCountsAreEstimated = estimated;

        return response;
    }

    private async Task<string> ReadProjectNameAsync(int? projectId, CancellationToken ct)
    {
        if (projectId is null) return string.Empty;

        return await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;
    }

    private async Task<string> ReadSchemaVersionAsync(CancellationToken ct)
    {
        try
        {
            var applied = await db.Database.GetAppliedMigrationsAsync(ct);
            return applied.LastOrDefault() ?? "ยังไม่ได้ปรับปรุง";
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            logger.LogWarning(ex, "อ่านรุ่นโครงสร้างฐานข้อมูลไม่ได้");
            return "อ่านไม่ได้";
        }
    }

    private static async Task<long> ReadDatabaseSizeAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_database_size(current_database())";
        command.CommandTimeout = 10;

        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// นับแถวจริงก่อน ถ้าช้าเกินจึงถอยไปใช้ค่าประมาณ
    ///
    /// ตารางบันทึกการทำงานโตทุกครั้งที่มีการแก้ข้อมูล จึงเป็นหลักล้านแถวได้
    /// และ PostgreSQL ไม่มีทางนับแถวโดยไม่อ่านทั้งตาราง การเผื่อทางถอยจึงจำเป็น
    /// </summary>
    private async Task<(List<TableRowCountResponse> Counts, bool Estimated)> ReadRowCountsAsync(
        NpgsqlConnection connection, CancellationToken ct)
    {
        try
        {
            var selects = CountedTables.Select(t => $"(SELECT COUNT(*) FROM {t.Table})");

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT " + string.Join(", ", selects);
            command.CommandTimeout = 10;

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return ([], false);

            var counts = CountedTables
                .Select((t, i) => new TableRowCountResponse { Label = t.Label, Rows = reader.GetInt64(i) })
                .ToList();

            return (counts, false);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            logger.LogWarning(ex, "นับจำนวนแถวจริงไม่ทัน จึงใช้ค่าประมาณแทน");
            return (await ReadEstimatedRowCountsAsync(connection, ct), true);
        }
    }

    private static async Task<List<TableRowCountResponse>> ReadEstimatedRowCountsAsync(
        NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT relname, n_live_tup FROM pg_stat_user_tables WHERE relname = ANY(@tables)";
        command.CommandTimeout = 10;
        command.Parameters.AddWithValue("tables", CountedTables.Select(t => t.Table).ToArray());

        var byTable = new Dictionary<string, long>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) byTable[reader.GetString(0)] = reader.GetInt64(1);

        return CountedTables
            .Select(t => new TableRowCountResponse
            {
                Label = t.Label,
                Rows = byTable.GetValueOrDefault(t.Table),
            })
            .ToList();
    }

    /// <summary>
    /// เวลาสำรองล่าสุด ครอบคลุมทั้งที่ตัวอัปเดตสำรองให้อัตโนมัติก่อนอัปเดตเวอร์ชัน
    /// (ดูจากไฟล์ที่ยังอยู่) และที่ผู้ใช้สำรองเอง (ดูจากที่จดไว้ เพราะไฟล์ถูกลบหลังดาวน์โหลด)
    /// </summary>
    private DateTimeOffset? ReadLastBackupAt()
    {
        try
        {
            var root = _options.Backup.BackupRoot;
            if (!Directory.Exists(root)) return null;

            var newestFile = Directory
                .EnumerateFiles(root, "*.dump", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty()
                .Max();

            var fromFile = newestFile == default
                ? (DateTimeOffset?)null
                : new DateTimeOffset(newestFile, TimeSpan.Zero);

            // ไฟล์ที่สำรองด้วยมือถูกลบหลังดาวน์โหลด จึงต้องดูจากที่จดไว้ด้วย
            var fromMarker = BackupMarker.Read(root);

            if (fromFile is null) return fromMarker;
            if (fromMarker is null) return fromFile;

            return fromFile > fromMarker ? fromFile : fromMarker;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "อ่านเวลาสำรองล่าสุดไม่ได้");
            return null;
        }
    }

    private long ReadAvailableDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_options.Backup.WorkDirectory));
            return string.IsNullOrWhiteSpace(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "อ่านพื้นที่ว่างไม่ได้");
            return 0;
        }
    }

    /// <summary>
    /// เวอร์ชันของเครื่องมือสำรอง ต้องไม่เก่ากว่าเวอร์ชันฐานข้อมูล
    /// ไม่อย่างนั้นสำรองไม่ได้เลย และนี่คือข้อมูลแรกที่ช่างต้องการตอนแก้ปัญหาหน้างาน
    /// </summary>
    private async Task<string> ReadPgDumpVersionAsync(CancellationToken ct)
    {
        var path = _options.Backup.PgDumpPath;
        if (!File.Exists(path)) return string.Empty;

        try
        {
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process is null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return output.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "อ่านเวอร์ชันเครื่องมือสำรองไม่ได้");
            return string.Empty;
        }
    }
}
