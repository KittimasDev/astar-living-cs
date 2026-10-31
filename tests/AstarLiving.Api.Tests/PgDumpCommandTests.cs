using AstarLiving.Api.Configuration;
using AstarLiving.Api.Modules.Maintenance;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Api.Tests;

/// <summary>
/// อาร์กิวเมนต์ที่ส่งให้ pg_dump
///
/// ชุดนี้คุมกฎสองข้อที่พลาดแล้วเจ็บและมองไม่เห็นตอนทดสอบบนเครื่องนักพัฒนา
/// เพราะเครื่องนักพัฒนาบังเอิญใช้ค่าเดียวกับที่ฮาร์ดโค้ดไว้พอดี
/// </summary>
public class PgDumpCommandTests
{
    private static DatabaseOptions Options() => new()
    {
        Host = "10.0.0.5",
        Port = 5433,
        Database = "lukkhacustomer",
        Username = "someone_else",
        Password = "s3cr3t-must-not-leak",
    };

    [Fact]
    public void ใช้ค่าจากการตั้งค่า_ไม่ใช่ค่าที่ฮาร์ดโค้ดไว้()
    {
        var args = PgDumpCommand.BuildArguments(Options(), @"C:\tmp\x.dump");

        args.Should().ContainInOrder("-h", "10.0.0.5");
        args.Should().ContainInOrder("-p", "5433");
        args.Should().ContainInOrder("-U", "someone_else");
        args.Should().ContainInOrder("-d", "lukkhacustomer");

        // ค่าที่โค้ดสำรองของตัวอัปเดตฮาร์ดโค้ดไว้ ต้องไม่หลุดมาที่นี่
        args.Should().NotContain("127.0.0.1");
        args.Should().NotContain("54329");
        args.Should().NotContain("asliving_app");
        args.Should().NotContain("astarliving");
    }

    /// <summary>
    /// รหัสผ่านต้องไปทางตัวแปรสภาพแวดล้อมเท่านั้น
    /// ถ้าหลุดมาอยู่ในอาร์กิวเมนต์ ใครก็ตามที่เปิดรายการโปรเซสจะเห็นรหัสผ่านฐานข้อมูล
    /// </summary>
    [Fact]
    public void รหัสผ่านต้องไม่อยู่ในอาร์กิวเมนต์เด็ดขาด()
    {
        var args = PgDumpCommand.BuildArguments(Options(), @"C:\tmp\x.dump");

        string.Join(" ", args).Should().NotContain("s3cr3t-must-not-leak");
    }

    [Fact]
    public void ใช้รูปแบบ_custom_ที่กู้คืนเลือกตารางได้()
    {
        var args = PgDumpCommand.BuildArguments(Options(), @"C:\tmp\x.dump");

        args.Should().Contain("-Fc");
        args.Should().ContainInOrder("-f", @"C:\tmp\x.dump");
    }

    [Fact]
    public void ชื่อไฟล์มีวันเวลาและลงท้ายด้วย_dump()
    {
        var name = PgDumpCommand.BuildFileName(
            new DateTimeOffset(2569, 10, 4, 14, 15, 0, TimeSpan.FromHours(7)));

        name.Should().Be("astarliving-25691004-141500.dump");
    }
}
