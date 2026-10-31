using AstarLiving.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace AstarLiving.Infrastructure.Tests;

/// <summary>
/// เชื่อมต่อฐานข้อมูลเดิมเพื่อพิสูจน์ว่า EF mapping อ่านข้อมูลจริงได้
///
/// อ่านค่าเชื่อมต่อตามลำดับ:
/// 1) ตัวแปรสภาพแวดล้อม ASTARLIVING_TEST_DB (connection string เต็ม)
/// 2) ไฟล์ .env ของระบบเดิม ผ่าน ASTARLIVING_LEGACY_ENV
/// 3) ../../astar-living-be/.env ที่อยู่ข้าง ๆ repo นี้
///
/// ถ้าต่อไม่ได้ เทสต์จะถูกข้าม ไม่ทำให้ build แดงบนเครื่องที่ไม่มีฐานข้อมูล
/// </summary>
public sealed class LegacyDatabaseFixture : IDisposable
{
    public NpgsqlDataSource? DataSource { get; }

    public string? SkipReason { get; }

    public LegacyDatabaseFixture()
    {
        try
        {
            var connectionString = ResolveConnectionString();
            if (connectionString is null)
            {
                SkipReason = "ไม่พบค่าเชื่อมต่อฐานข้อมูลเดิม (ตั้ง ASTARLIVING_TEST_DB หรือวางไฟล์ .env ของระบบเดิมไว้)";
                return;
            }

            var dataSource = AstarLivingDataSource.Create(connectionString);
            using (var probe = dataSource.OpenConnection())
            {
                using var cmd = new NpgsqlCommand("SELECT 1", probe);
                cmd.ExecuteScalar();
            }

            DataSource = dataSource;
        }
        catch (Exception ex)
        {
            SkipReason = $"เชื่อมต่อฐานข้อมูลเดิมไม่ได้: {ex.Message}";
        }
    }

    public AstarLivingDbContext CreateContext() =>
        new(AstarLivingDataSource.CreateOptions(DataSource!));

    public void Dispose() => DataSource?.Dispose();

    private static string? ResolveConnectionString()
    {
        var direct = Environment.GetEnvironmentVariable("ASTARLIVING_TEST_DB");
        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        var envFile = Environment.GetEnvironmentVariable("ASTARLIVING_LEGACY_ENV") ?? FindLegacyEnvFile();
        if (envFile is null || !File.Exists(envFile)) return null;

        var values = File.ReadAllLines(envFile)
            .Where(line => line.Contains('=') && !line.TrimStart().StartsWith('#'))
            .ToDictionary(
                line => line[..line.IndexOf('=')].Trim(),
                line => line[(line.IndexOf('=') + 1)..].Trim().Trim('"'));

        if (!values.TryGetValue("DB_HOST", out var host)) return null;

        return AstarLivingDataSource.BuildConnectionString(
            host,
            int.Parse(values["DB_PORT"]),
            values["DB_DATABASE"],
            values["DB_USERNAME"],
            values["DB_PASSWORD"]);
    }

    /// <summary>ไต่ขึ้นจากโฟลเดอร์ทดสอบไปหา astar-living-be/.env ที่วางคู่กันอยู่</summary>
    private static string? FindLegacyEnvFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "astar-living-be", ".env");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}

[CollectionDefinition(Name)]
public class LegacyDatabaseCollection : ICollectionFixture<LegacyDatabaseFixture>
{
    public const string Name = "legacy-database";
}
