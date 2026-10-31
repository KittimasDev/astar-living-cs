using System.Collections.ObjectModel;
using System.Globalization;
using AstarLiving.Client.Common.Converters;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Client.Tests;

/// <summary>
/// เลขลำดับของแถวในตาราง
///
/// เทสต์ชุดนี้เน้นที่ "ชนิดของค่าที่คืน" เป็นพิเศษ
/// เคยคืนเป็นจำนวนเต็มออกไป ซึ่ง TextBlock.Text รับไม่ได้
/// WPF ทิ้งค่าแล้วปล่อยช่องว่างเปล่าโดยไม่มีอะไรฟ้อง
/// คอลัมน์ "ลำดับ" จึงว่างทั้งเจ็ดหน้าที่ใช้ตัวแปลงนี้ และไม่มีใครสังเกตเห็น
/// </summary>
public class RowNumberConverterTests
{
    private static readonly RowNumberConverter Converter = new();

    [Fact]
    public void ต้องคืนค่าเป็นข้อความเท่านั้น()
    {
        var items = new ObservableCollection<string> { "ก", "ข", "ค" };

        var result = Converter.Convert([items[1], items, 1], typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().BeOfType<string>("TextBlock.Text รับเฉพาะข้อความ ถ้าคืนตัวเลขช่องจะว่าง");
        result.Should().Be("2");
    }

    [Theory]
    [InlineData(1, 0, "1")]
    [InlineData(1, 9, "10")]
    [InlineData(2, 0, "11")]
    [InlineData(3, 4, "25")]
    public void นับต่อเนื่องข้ามหน้า(int page, int indexInPage, string expected)
    {
        // ขนาดหน้าของระบบคือสิบแถว เลขจึงต้องไหลต่อจากหน้าก่อนหน้า
        var items = new ObservableCollection<string>(
            Enumerable.Range(0, 10).Select(i => $"แถว{i}"));

        var result = Converter.Convert(
            [items[indexInPage], items, page], typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }

    [Fact]
    public void ค่าที่ผูกไม่สำเร็จให้คืนข้อความว่าง()
    {
        // WPF ส่ง UnsetValue มาเมื่อผูกข้อมูลไม่สำเร็จ ห้ามโยนข้อผิดพลาด
        var result = Converter.Convert(
            [new object(), System.Windows.DependencyProperty.UnsetValue, 1],
            typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be(string.Empty);
    }
}
