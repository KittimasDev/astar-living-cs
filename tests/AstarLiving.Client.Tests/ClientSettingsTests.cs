using AstarLiving.Client.Services;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Client.Tests;

/// <summary>
/// การตีความที่อยู่เครื่องแม่ข่ายที่ช่างกรอกหน้างาน
/// คนหน้างานมักกรอกแค่เลขไอพี ไม่ใช่ URL เต็ม ระบบจึงต้องเติมให้เอง
/// </summary>
public class ClientSettingsTests
{
    [Theory]
    [InlineData("192.168.1.10", "http://192.168.1.10:5080")]
    [InlineData("192.168.1.10:8080", "http://192.168.1.10:8080")]
    [InlineData("http://192.168.1.10:8080", "http://192.168.1.10:8080")]
    [InlineData("https://server.local:5443", "https://server.local:5443")]
    [InlineData("  192.168.1.10  ", "http://192.168.1.10:5080")]
    [InlineData("http://192.168.1.10:8080/", "http://192.168.1.10:8080")]
    // Uri ปรับชื่อเครื่องเป็นตัวพิมพ์เล็กให้เอง ซึ่งไม่มีผลเพราะ DNS ไม่แยกตัวพิมพ์
    [InlineData("SERVER-PC", "http://server-pc:5080")]
    public void เติมรูปแบบที่อยู่เครื่องแม่ข่ายให้ครบ(string input, string expected) =>
        ClientSettings.NormalizeServerUrl(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("ftp://192.168.1.10")]
    public void ปฏิเสธที่อยู่ที่ใช้ไม่ได้(string? input) =>
        ClientSettings.NormalizeServerUrl(input).Should().BeNull();

    [Fact]
    public void ตัดเส้นทางและพารามิเตอร์ที่เกินมาออก()
    {
        // HttpClient.BaseAddress ต้องเป็นแค่ชื่อเครื่องกับพอร์ต
        // ถ้าปล่อยเส้นทางติดมาด้วย ทุก endpoint จะถูกต่อท้ายผิดที่
        ClientSettings.NormalizeServerUrl("http://192.168.1.10:8080/api/v1?x=1")
            .Should().Be("http://192.168.1.10:8080");
    }
}
