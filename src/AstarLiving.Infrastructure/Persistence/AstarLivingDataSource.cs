using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AstarLiving.Infrastructure.Persistence;

/// <summary>
/// สร้าง NpgsqlDataSource ที่ผูก enum ไว้แล้ว และต่อเข้ากับ DbContext
/// ใช้ทั้งจาก API และจากเครื่องมือทดสอบ/สคริปต์
/// </summary>
public static class AstarLivingDataSource
{
    /// <summary>
    /// ค่าตั้งต้นของการเชื่อมต่อที่เหมาะกับการใช้งานหน้างาน
    /// Timeout สั้นพอให้รู้เร็วว่าเครื่องแม่ล่ม แต่ Command Timeout ยาวพอสำหรับรายงานย้อนหลัง
    /// </summary>
    public static NpgsqlDataSource Create(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapAstarLivingEnums();
        return builder.Build();
    }

    public static string BuildConnectionString(
        string host, int port, string database, string username, string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            Timeout = 15,
            CommandTimeout = 120,
            KeepAlive = 30,
            TcpKeepAlive = true,
            MaxPoolSize = 30,
        }.ConnectionString;

    /// <summary>สร้าง options สำหรับ DbContext จาก data source ที่ผูก enum แล้ว</summary>
    public static DbContextOptions<AstarLivingDbContext> CreateOptions(NpgsqlDataSource dataSource) =>
        new DbContextOptionsBuilder<AstarLivingDbContext>()
            .UseNpgsql(dataSource)
            .Options;
}
