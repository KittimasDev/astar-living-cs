using AstarLiving.Client.Common;
using AstarLiving.Client.ViewModels.Pages;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Client.Tests;

/// <summary>
/// เมนูที่แต่ละสิทธิ์มองเห็น
///
/// การซ่อนเมนูไม่ใช่กำแพงความปลอดภัย ฝั่งเครื่องแม่บังคับสิทธิ์จริงอยู่แล้ว
/// แต่ความผิดพลาดที่แพงที่สุดคือ "คนที่ไม่ควรเห็น กลับเห็น" เพราะมันชวนให้กด
/// ชุดนี้จึงคุมฝั่งที่ต้องไม่เห็นเป็นหลัก
/// </summary>
public class AppMenuTests
{
    private static IEnumerable<Type?> AllPages(string role) =>
        AppMenu.Build(role).SelectMany(Flatten).Select(n => n.PageType);

    private static IEnumerable<NavigationNode> Flatten(NavigationNode node)
    {
        yield return node;

        foreach (var child in node.Children ?? [])
        {
            foreach (var nested in Flatten(child)) yield return nested;
        }
    }

    [Fact]
    public void ผู้ดูแลสูงสุดเห็นเมนูสำรองฐานข้อมูล() =>
        AllPages(AppMenu.SuperAdmin).Should().Contain(typeof(DatabaseBackupPageViewModel));

    [Theory]
    [InlineData(AppMenu.Admin)]
    [InlineData(AppMenu.Manager)]
    [InlineData(AppMenu.User)]
    public void สิทธิ์อื่นต้องไม่เห็นเมนูสำรองฐานข้อมูล(string role) =>
        AllPages(role).Should().NotContain(typeof(DatabaseBackupPageViewModel));

    /// <summary>คนที่สั่งสำรองต้องตรวจสอบบันทึกการทำงานของตัวเองได้</summary>
    [Fact]
    public void ผู้ดูแลสูงสุดเห็นเมนูบันทึกการทำงาน() =>
        AllPages(AppMenu.SuperAdmin).Should().Contain(typeof(SystemLogsPageViewModel));

    [Fact]
    public void ผู้ใช้ทั่วไปยังไม่เห็นบันทึกการทำงานเหมือนเดิม() =>
        AllPages(AppMenu.User).Should().NotContain(typeof(SystemLogsPageViewModel));

    /// <summary>หน้าแรกของผู้ดูแลสูงสุดต้องไม่เปลี่ยนเพราะการเพิ่มเมนู</summary>
    [Fact]
    public void หน้าแรกของผู้ดูแลสูงสุดยังเป็นหน้าตั้งค่าโครงการ() =>
        AppMenu.LandingPage(AppMenu.SuperAdmin).Should().Be<ProjectSettingsPageViewModel>();

    [Fact]
    public void ผู้ดูแลสูงสุดยังไม่เห็นเมนูงานประจำวัน()
    {
        var pages = AllPages(AppMenu.SuperAdmin).ToList();

        pages.Should().NotContain(typeof(DashboardPageViewModel));
        pages.Should().NotContain(typeof(ReceiptPageViewModel));
        pages.Should().NotContain(typeof(InvoicePageViewModel));
    }
}
