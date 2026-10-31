using System.Globalization;
using AstarLiving.Domain.Entities;

namespace AstarLiving.Domain.Billing;

/// <summary>
/// ข้อความคอลัมน์ "รายละเอียด" ของรายงานรายรับอื่น ๆ
///
/// ระบบเดิมประกอบข้อความนี้ที่เบราว์เซอร์จากรายการย่อยในใบเสร็จ
/// ระบบใหม่ย้ายมาทำที่เครื่องแม่ เพราะหน้าจอรับผลลัพธ์เป็น JSON ดิบ
/// และต้องส่งข้อความเดียวกันนี้ต่อไปยังไฟล์ Excel ด้วย
///
/// รูปแบบยกมาจากของเดิมทุกตัวอักษร รวมถึงจุดที่ดูไม่สม่ำเสมอ
/// คือราคาต่อหน่วยมีเครื่องหมายคั่นหลักพัน แต่ยอดรวมของรายการไม่มี
/// </summary>
public static class RevenueDetailText
{
    /// <summary>ราคาต่อหน่วย — คั่นหลักพัน ทศนิยมมีเท่าที่จำเป็น สูงสุดสามตำแหน่ง</summary>
    private const string UnitPriceFormat = "#,##0.###";

    /// <summary>จำนวนและยอดรวมของรายการ — ไม่คั่นหลักพัน ทศนิยมมีเท่าที่จำเป็น</summary>
    private const string PlainFormat = "0.############################";

    public static string Build(RevenuePaymentDetail? detail)
    {
        if (detail is null || detail.Items.Count == 0) return "-";

        return string.Join(", ", detail.Items.Select(Describe));
    }

    private static string Describe(RevenuePaymentItem item)
    {
        // ของเดิมเว้นช่องนี้ว่างได้ จึงนับเป็นหนึ่งหน่วยเหมือนที่ตอนพิมพ์ใบเสร็จทำ
        var quantity = item.Quantity ?? 1m;

        // ของเดิมคูณใหม่ตรงนี้ ไม่ได้ใช้ยอดรวมที่เก็บไว้ในฐานข้อมูล
        // สองค่านี้ต่างกันได้ถ้ามีคนแก้ยอดรวมมือ จึงต้องคูณใหม่ให้ตรงกับของเดิม
        //
        // ของเดิมคำนวณด้วยเลขทศนิยมลอยตัวของจาวาสคริปต์ ยอดจึงมีเศษแปลก ๆ ได้
        // เช่น 0.30000000000000004 ระบบใหม่ใช้ decimal เศษพวกนี้จึงหายไป
        // เป็นความต่างที่ตั้งใจ บันทึกไว้แล้วในเอกสารเคสที่ต้องตรวจสอบ ข้อ ข1
        var lineTotal = quantity * item.UnitPrice;

        var quantityText = quantity.ToString(PlainFormat, CultureInfo.InvariantCulture);
        var unitPriceText = item.UnitPrice.ToString(UnitPriceFormat, CultureInfo.InvariantCulture);
        var lineTotalText = lineTotal.ToString(PlainFormat, CultureInfo.InvariantCulture);

        return $"{item.Description} ({quantityText} หน่วย {unitPriceText} บาท รวม {lineTotalText} บาท)";
    }
}
