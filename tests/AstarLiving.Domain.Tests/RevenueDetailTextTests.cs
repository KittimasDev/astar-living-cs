using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Domain.Tests;

/// <summary>
/// ข้อความคอลัมน์ "รายละเอียด" ของรายงานรายรับอื่น ๆ
///
/// ค่าที่คาดหวังในแต่ละข้อยกมาจากผลลัพธ์ของระบบเดิมโดยตรง
/// ซึ่งประกอบข้อความนี้ที่เบราว์เซอร์ด้วยสูตร
/// `${description} (${quantity} หน่วย ${unitPrice.toLocaleString("th-TH")} บาท รวม ${quantity * unitPrice} บาท)`
/// </summary>
public class RevenueDetailTextTests
{
    [Fact]
    public void ไม่มีรายการย่อยให้แสดงขีดกลาง()
    {
        RevenueDetailText.Build(new RevenuePaymentDetail()).Should().Be("-");
        RevenueDetailText.Build(null).Should().Be("-");
    }

    [Fact]
    public void ราคาต่อหน่วยคั่นหลักพันแต่ยอดรวมของรายการไม่คั่น()
    {
        // ความไม่สม่ำเสมอนี้เป็นของระบบเดิม เพราะราคาต่อหน่วยผ่าน toLocaleString
        // ส่วนยอดรวมถูกแทรกเป็นตัวเลขดิบ
        var detail = Detail(Item("ค่าน้ำประปา", quantity: 3m, unitPrice: 1500m));

        RevenueDetailText.Build(detail)
            .Should().Be("ค่าน้ำประปา (3 หน่วย 1,500 บาท รวม 4500 บาท)");
    }

    [Fact]
    public void หลายรายการคั่นด้วยจุลภาคและเว้นวรรค()
    {
        var detail = Detail(
            Item("ค่าน้ำประปา", 2m, 100m),
            Item("ค่าส่วนกลางสนาม", 1m, 250m));

        RevenueDetailText.Build(detail)
            .Should().Be("ค่าน้ำประปา (2 หน่วย 100 บาท รวม 200 บาท), ค่าส่วนกลางสนาม (1 หน่วย 250 บาท รวม 250 บาท)");
    }

    [Fact]
    public void ทศนิยมมีเท่าที่จำเป็นไม่เติมศูนย์ต่อท้าย()
    {
        var detail = Detail(Item("ค่าไฟส่วนกลาง", 2.5m, 1200.5m));

        RevenueDetailText.Build(detail)
            .Should().Be("ค่าไฟส่วนกลาง (2.5 หน่วย 1,200.5 บาท รวม 3001.25 บาท)");
    }

    [Fact]
    public void ไม่ระบุจำนวนให้นับเป็นหนึ่งหน่วย()
    {
        // ตรงกับที่ตอนพิมพ์ใบเสร็จทำอยู่แล้ว ระบบเดิมจะแสดงคำว่า undefined ตรงนี้
        var detail = Detail(Item("ค่าปรับจอดรถ", quantity: null, unitPrice: 300m));

        RevenueDetailText.Build(detail)
            .Should().Be("ค่าปรับจอดรถ (1 หน่วย 300 บาท รวม 300 บาท)");
    }

    [Fact]
    public void ยอดรวมคิดใหม่จากจำนวนคูณราคาไม่ใช่ค่าที่เก็บไว้()
    {
        // ระบบเดิมก็คูณใหม่ ถ้าค่าที่เก็บไว้เพี้ยนรายงานจะไม่สะท้อนความเพี้ยนนั้น
        var item = Item("ค่าซ่อมประตู", 2m, 500m);
        item.Total = 9999m;

        RevenueDetailText.Build(Detail(item))
            .Should().Be("ค่าซ่อมประตู (2 หน่วย 500 บาท รวม 1000 บาท)");
    }

    private static RevenuePaymentDetail Detail(params RevenuePaymentItem[] items) =>
        new() { Items = [.. items] };

    private static RevenuePaymentItem Item(string description, decimal? quantity, decimal unitPrice) =>
        new()
        {
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Total = (quantity ?? 1m) * unitPrice,
        };
}
