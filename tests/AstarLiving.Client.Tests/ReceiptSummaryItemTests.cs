using System.Text.Json;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Client.Tests;

/// <summary>
/// ตัวเลขในใบสรุปยอดที่ต้องชำระ
///
/// ช่อง count กับ unit_price ประกาศเป็น object เพราะแถวส่วนลดส่งสตริงว่างมา
/// พอผ่านตัวแปลง JSON ค่าที่ได้จึงเป็น JsonElement ไม่ใช่ decimal
/// การใส่รูปแบบตัวเลขที่ XAML จึงไม่มีผล หน้าจอเคยแสดง "2200" กับ "220.0"
/// ขณะที่ช่องข้าง ๆ แสดง "6,600.00" ซึ่งไม่ตรงกับระบบเดิมที่บังคับสองตำแหน่งเสมอ
/// </summary>
public class ReceiptSummaryItemTests
{
    private static ReceiptSummaryItem FromJson(string json) =>
        JsonSerializer.Deserialize<ReceiptSummaryItem>(json, AstarJson.Options)!;

    [Theory]
    [InlineData("2200", "2,200.00")]
    [InlineData("220.0", "220.00")]
    [InlineData("200", "200.00")]
    [InlineData("1234567.5", "1,234,567.50")]
    public void ราคาต่อหน่วยต้องมีทศนิยมสองตำแหน่งและคั่นหลักพัน(string raw, string expected)
    {
        var item = FromJson($$"""{"name":"ค่าส่วนกลาง","count":3,"unit_price":{{raw}},"total":6600}""");

        item.UnitPriceText.Should().Be(expected);
    }

    [Fact]
    public void แถวส่วนลดส่งสตริงว่างมาให้คงความว่างไว้()
    {
        // ระบบเดิมเว้นสองช่องนี้ว่างในแถวส่วนลด ห้ามกลายเป็น 0.00
        var item = FromJson("""{"name":"ส่วนลด","count":"","unit_price":"","total":-500}""");

        item.UnitPriceText.Should().BeEmpty();
        item.CountText.Should().BeEmpty();
    }

    [Fact]
    public void จำนวนหน่วยที่เป็นตัวเลขให้แสดงเป็นจำนวนเต็ม()
    {
        var item = FromJson("""{"name":"ค่าทวงถาม","count":2,"unit_price":200,"total":400}""");

        item.CountText.Should().Be("2");
    }
}
