using System.Text.Json;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AstarLiving.Infrastructure.Tests;

/// <summary>
/// การตัดสินใจว่าเครื่องที่เพิ่งติดตั้งจะได้ข้อมูลแบบไหน
///
/// ความผิดพลาดที่ยอมรับไม่ได้คือข้อมูลปลอมหลุดไปอยู่ในฐานข้อมูลของลูกค้าจริง
/// ชุดนี้จึงคุมไว้ว่าโหมดสาธิตต้องถูกเปิดอย่างตั้งใจเท่านั้น
/// </summary>
public class SeedPlanTests : IDisposable
{
    private readonly string _configDir = Path.Combine(
        Path.GetTempPath(), "astarliving-seed-" + Guid.NewGuid().ToString("N"));

    public SeedPlanTests() => Directory.CreateDirectory(_configDir);

    public void Dispose()
    {
        if (Directory.Exists(_configDir)) Directory.Delete(_configDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ไม่มีไฟล์บัญชีผู้ดูแลต้องไม่ได้ข้อมูลสาธิต()
    {
        var plan = SeedPlanLoader.Load(_configDir, includeDemoData: false, NullLogger.Instance);

        plan.IncludeDemoData.Should().BeFalse();
        plan.HasAdminAccount.Should().BeFalse();
    }

    [Fact]
    public void ไม่ได้ระบุโฟลเดอร์ตั้งค่าก็ต้องไม่ได้ข้อมูลสาธิต()
    {
        var plan = SeedPlanLoader.Load(null, includeDemoData: false, NullLogger.Instance);

        plan.IncludeDemoData.Should().BeFalse();
    }

    /// <summary>
    /// ไฟล์ถูกเขียนด้วยชื่อคีย์ตัวพิมพ์เล็ก แต่คลาสฝั่งอ่านใช้ตัวพิมพ์ใหญ่
    /// ถ้าอ่านแบบสนตัวพิมพ์ จะได้ค่าว่างเงียบ ๆ แล้วไปพังตอนสร้างบัญชีแทน
    /// </summary>
    [Fact]
    public void อ่านไฟล์บัญชีผู้ดูแลที่ตัวติดตั้งเขียนไว้ได้()
    {
        WriteBootstrap(new { username = "siteadmin", password = "Ch4ngeMe!2569", project = "นิติบุคคลทดสอบ" });

        var plan = SeedPlanLoader.Load(_configDir, includeDemoData: false, NullLogger.Instance);

        plan.HasAdminAccount.Should().BeTrue();
        plan.AdminUsername.Should().Be("siteadmin");
        plan.AdminPassword.Should().Be("Ch4ngeMe!2569");
        plan.ProjectName.Should().Be("นิติบุคคลทดสอบ");
        plan.IncludeDemoData.Should().BeFalse();
    }

    [Fact]
    public void ไม่ได้ระบุชื่อโครงการให้ใช้ชื่อตั้งต้น()
    {
        WriteBootstrap(new { username = "admin2", password = "LongEnough1" });

        var plan = SeedPlanLoader.Load(_configDir, includeDemoData: false, NullLogger.Instance);

        plan.ProjectName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ไฟล์เสียหายต้องไม่ทำให้ระบบพัง_แต่ต้องไม่ได้บัญชีด้วย()
    {
        File.WriteAllText(Path.Combine(_configDir, "admin-bootstrap.secret"), "ไม่ใช่ข้อมูลที่ถูกต้อง");

        var plan = SeedPlanLoader.Load(_configDir, includeDemoData: false, NullLogger.Instance);

        plan.HasAdminAccount.Should().BeFalse();
    }

    [Fact]
    public void เปิดโหมดสาธิตแล้วต้องได้ข้อมูลปลอมครบ()
    {
        var plan = SeedPlanLoader.Load(_configDir, includeDemoData: true, NullLogger.Instance);

        plan.IncludeDemoData.Should().BeTrue();
    }

    [Fact]
    public void ใช้ไฟล์บัญชีแล้วต้องถูกลบทิ้ง_ไม่ให้รหัสผ่านค้างบนดิสก์()
    {
        var path = Path.Combine(_configDir, "admin-bootstrap.secret");
        WriteBootstrap(new { username = "siteadmin", password = "LongEnough1" });

        SeedPlanLoader.Consume(_configDir, NullLogger.Instance);

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void ลบไฟล์ที่ไม่มีอยู่ต้องไม่พัง()
    {
        var act = () => SeedPlanLoader.Consume(_configDir, NullLogger.Instance);

        act.Should().NotThrow();
    }

    private void WriteBootstrap(object payload) =>
        MachineSecret.Write(
            Path.Combine(_configDir, "admin-bootstrap.secret"),
            JsonSerializer.Serialize(payload));
}
