using System.IO;
using AstarLiving.Client.Services;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Client.Tests;

/// <summary>
/// กฎข้อเดียวที่ทำให้การอัปเดตอัตโนมัติไม่ทำข้อมูลลูกค้าหาย
///
/// ตัวอัปเดต Velopack แทนที่โฟลเดอร์ติดตั้งทั้งก้อนทุกครั้ง
/// อะไรที่โปรแกรมเขียนลงใต้โฟลเดอร์ตัวเองจะหายไปเงียบ ๆ ทุกการอัปเดต
/// โดยที่ไม่มี error ให้เห็นเลย กว่าจะรู้ก็ตอนลูกค้าโทรมาบอกว่าต้องล็อกอินใหม่ทุกครั้ง
///
/// เทสต์ชุดนี้จึงตรึงไว้ว่าทุกที่อยู่ที่โปรแกรมเขียนต้องอยู่นอกโฟลเดอร์ติดตั้ง
/// </summary>
public class AppPathsTests
{
    public static TheoryData<string> WritablePaths() =>
    [
        AppPaths.UserRoot,
        AppPaths.UserSettings,
        AppPaths.SessionFile,
        AppPaths.LogDirectory,
        AppPaths.DocumentCache,
        AppPaths.MachineRoot,
        AppPaths.MachineConfigDirectory,
        AppPaths.MachineClientConfig,
    ];

    [Theory]
    [MemberData(nameof(WritablePaths))]
    public void ที่อยู่ที่เขียนไฟล์ต้องไม่อยู่ใต้โฟลเดอร์ติดตั้ง(string path)
    {
        var installRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(AppContext.BaseDirectory));

        var target = Path.GetFullPath(path);

        target.Should().NotStartWith(
            installRoot + Path.DirectorySeparatorChar,
            "ตัวอัปเดตแทนที่โฟลเดอร์ติดตั้งทั้งก้อน ไฟล์ที่เขียนไว้ตรงนั้นจะหายทุกครั้งที่อัปเดต");
    }

    [Theory]
    [MemberData(nameof(WritablePaths))]
    public void ที่อยู่ที่เขียนไฟล์ต้องเป็นเส้นทางเต็ม(string path)
    {
        Path.IsPathFullyQualified(path).Should().BeTrue(
            "เส้นทางสัมพัทธ์จะอ้างอิงโฟลเดอร์ปัจจุบันของโปรเซส ซึ่งเปลี่ยนได้ตามวิธีเปิดโปรแกรม");
    }

    [Fact]
    public void ไฟล์เซสชันกับค่าตั้งของผู้ใช้ต้องอยู่คนละไฟล์กัน()
    {
        // ถ้าเผลอรวมเป็นไฟล์เดียว การล้างเซสชันจะลบที่อยู่เครื่องแม่ข่ายไปด้วย
        AppPaths.SessionFile.Should().NotBe(AppPaths.UserSettings);
    }
}
