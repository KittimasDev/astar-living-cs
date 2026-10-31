using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using AstarLiving.Client.Common.Converters;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Client.Tests;

/// <summary>
/// การแสดงรูปโลโก้โครงการ
///
/// จุดที่เคยพลาด: เรียก Freeze() กับรูปที่โหลดจากเครื่องแม่ซึ่งยังดาวน์โหลดไม่เสร็จ
/// มันโยนข้อผิดพลาดแล้วถูกกลืน ผลคือกรอบรูปว่างเปล่าโดยไม่มีอะไรบอกสาเหตุ
/// </summary>
public class ImageSourceConverterTests
{
    private readonly ImageSourceConverter _converter = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ไม่มีที่อยู่รูปต้องได้ค่าว่าง(string? value) =>
        Convert(value).Should().BeNull();

    [Fact]
    public void ที่อยู่ที่ไม่ใช่URLต้องได้ค่าว่าง_ไม่ใช่ข้อผิดพลาด() =>
        Convert("ไม่ใช่ที่อยู่").Should().BeNull();

    [Fact]
    public void ไฟล์ที่ไม่มีอยู่ต้องได้ค่าว่าง_ไม่ใช่ข้อผิดพลาด() =>
        Convert(Path.Combine(Path.GetTempPath(), "ไม่มีไฟล์นี้.png")).Should().BeNull();

    [Fact]
    public void อ่านไฟล์รูปในเครื่องได้()
    {
        var path = WriteTempPng(width: 24, height: 16);

        try
        {
            var image = Convert(path).Should().BeOfType<BitmapImage>().Subject;

            image.PixelWidth.Should().Be(24);
            image.PixelHeight.Should().Be(16);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// ต้องไม่ล็อกไฟล์ไว้ ไม่อย่างนั้นผู้ใช้จะลบหรือแก้ไฟล์ที่เพิ่งเลือกไม่ได้จนกว่าจะปิดโปรแกรม
    /// </summary>
    [Fact]
    public void อ่านแล้วต้องไม่ล็อกไฟล์ไว้()
    {
        var path = WriteTempPng(width: 8, height: 8);

        Convert(path).Should().NotBeNull();

        var act = () => File.Delete(path);

        act.Should().NotThrow();
    }

    /// <summary>
    /// รูปจากเครื่องแม่โหลดแบบไม่รอ ตัวแปลงต้องคืนรูปที่ WPF จะเติมให้ทีหลัง
    /// ห้ามตรึงรูปตรงนี้ และห้ามโยนข้อผิดพลาดแม้ที่อยู่นั้นจะติดต่อไม่ได้
    /// </summary>
    [Fact]
    public void ที่อยู่บนเครือข่ายต้องไม่โยนข้อผิดพลาดและต้องไม่ถูกตรึง()
    {
        object? result = null;
        var act = () => result = Convert("http://127.0.0.1:1/api/images/uploads/project/1/logo.png");

        act.Should().NotThrow();

        result.Should().BeOfType<BitmapImage>();
        result.As<BitmapImage>().IsFrozen.Should().BeFalse();
    }

    private object? Convert(string? value) =>
        _converter.Convert(value, typeof(object), null, CultureInfo.InvariantCulture);

    private static string WriteTempPng(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"astarliving-{Guid.NewGuid():N}.png");

        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)200);

        var source = BitmapSource.Create(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = File.Create(path);
        encoder.Save(stream);

        return path;
    }
}
