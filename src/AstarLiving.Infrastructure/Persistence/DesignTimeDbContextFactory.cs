using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AstarLiving.Infrastructure.Persistence;

/// <summary>
/// ใช้โดยเครื่องมือ dotnet ef ตอนสร้างหรือดู migration เท่านั้น
/// ไม่ถูกเรียกตอนโปรแกรมทำงานจริง
///
/// ตั้งค่าเชื่อมต่อผ่านตัวแปรสภาพแวดล้อม ASTARLIVING_DESIGN_DB ได้
/// ค่าเริ่มต้นชี้ไปที่ฐานข้อมูลสำหรับพัฒนาบนเครื่องตัวเอง
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AstarLivingDbContext>
{
    private const string DefaultConnection =
        "Host=127.0.0.1;Port=54329;Database=astarliving;Username=postgres;Password=devpass";

    public AstarLivingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ASTARLIVING_DESIGN_DB") ?? DefaultConnection;

        var dataSource = AstarLivingDataSource.Create(connectionString);

        var options = new DbContextOptionsBuilder<AstarLivingDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        return new AstarLivingDbContext(options);
    }
}
