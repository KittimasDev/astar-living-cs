using System.Text.Json;
using AstarLiving.Shared.Security;
using Microsoft.Extensions.Logging;

namespace AstarLiving.Infrastructure.Persistence;

/// <summary>
/// อ่านว่าเครื่องนี้ควรสร้างข้อมูลตั้งต้นแบบไหน
///
/// บัญชีผู้ดูแลมาจากไฟล์ที่ตัวติดตั้งเขียนไว้ ไม่ได้ฝังในโค้ด
/// และไฟล์นั้นถูกลบทิ้งทันทีที่ใช้เสร็จ รหัสผ่านจึงไม่ค้างบนดิสก์ของลูกค้า
/// </summary>
public static class SeedPlanLoader
{
    private const string FileName = "admin-bootstrap.secret";

    /// <summary>
    /// ต้องไม่สนตัวพิมพ์ใหญ่เล็ก เพราะฝั่งที่เขียนไฟล์ใช้ชื่อคีย์ตัวพิมพ์เล็ก
    /// ถ้าปล่อยค่าเริ่มต้นของ System.Text.Json ไว้ จะอ่านได้เป็นค่าว่างแบบเงียบ ๆ
    /// แล้วไปพังตอนสร้างบัญชีแทน ซึ่งหาสาเหตุยากกว่ามาก
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// ประกอบแผนจากค่าตั้งและไฟล์ที่ตัวติดตั้งเขียนไว้
    /// ไม่โยนข้อผิดพลาดเมื่อไฟล์เสียหาย เพราะผู้เรียกจะรายงานสาเหตุที่ชัดกว่าเอง
    /// </summary>
    public static SeedPlan Load(string? configDir, bool includeDemoData, ILogger logger)
    {
        if (includeDemoData) return SeedPlan.Demo;

        if (string.IsNullOrWhiteSpace(configDir)) return new SeedPlan();

        var path = Path.Combine(configDir, FileName);
        if (!File.Exists(path)) return new SeedPlan();

        try
        {
            var bootstrap = JsonSerializer.Deserialize<AdminBootstrap>(MachineSecret.Read(path), JsonOptions);

            if (bootstrap is null || string.IsNullOrWhiteSpace(bootstrap.Username))
            {
                logger.LogWarning("ไฟล์บัญชีผู้ดูแลที่ {Path} อ่านได้แต่ไม่มีข้อมูล", path);
                return new SeedPlan();
            }

            return new SeedPlan
            {
                IncludeDemoData = false,
                AdminUsername = bootstrap.Username,
                AdminPassword = bootstrap.Password,
                ProjectName = string.IsNullOrWhiteSpace(bootstrap.Project)
                    ? "โครงการของฉัน"
                    : bootstrap.Project,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "อ่านไฟล์บัญชีผู้ดูแลที่ {Path} ไม่สำเร็จ", path);
            return new SeedPlan();
        }
    }

    /// <summary>
    /// ลบไฟล์บัญชีผู้ดูแลทิ้งหลังสร้างบัญชีเรียบร้อย
    ///
    /// ลบไม่สำเร็จไม่ใช่เหตุให้ระบบไม่ทำงาน แต่ต้องบันทึกไว้ให้ชัด
    /// เพราะแปลว่ารหัสผ่านยังค้างอยู่บนดิสก์ ซึ่งต้องมีคนไปจัดการ
    /// </summary>
    public static void Consume(string? configDir, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(configDir)) return;

        var path = Path.Combine(configDir, FileName);
        if (!File.Exists(path)) return;

        try
        {
            File.Delete(path);
            logger.LogInformation("ลบไฟล์บัญชีผู้ดูแลตั้งต้นแล้ว");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ลบไฟล์บัญชีผู้ดูแลที่ {Path} ไม่สำเร็จ ต้องลบด้วยมือ", path);
        }
    }

    private sealed record AdminBootstrap
    {
        public string Username { get; init; } = string.Empty;

        public string Password { get; init; } = string.Empty;

        public string? Project { get; init; }
    }
}
