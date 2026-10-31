using System.Globalization;

namespace AstarLiving.Domain.Billing;

/// <summary>
/// การจัดรูปแบบตัวเงินให้ตรงกับระบบเดิมแบบอักขระต่ออักขระ
/// ระบบเดิมมีอยู่ 2 แบบที่ไม่เหมือนกัน และแบบที่ 2 เป็นกับดักที่มองข้ามง่าย
/// </summary>
public static class MoneyFormat
{
    /// <summary>
    /// ใช้ตัวคั่นหลักพันเป็น "," และจุดทศนิยมเป็น "." เหมือนผลลัพธ์ของ en-US และ th-TH
    /// (ไม่ใช้ CultureInfo("th-TH") ตรง ๆ เพราะบางแพลตฟอร์ม ICU ให้ตัวเลขไทยหรือกลุ่มต่างออกไป)
    /// </summary>
    private static readonly NumberFormatInfo Numeric = new()
    {
        NumberDecimalSeparator = ".",
        NumberGroupSeparator = ",",
        NumberGroupSizes = [3],
    };

    /// <summary>
    /// formatMoney() / formatMoneyTh() เดิม — ทศนิยม 2 ตำแหน่งตายตัว
    /// ใช้ในตาราง PDF ทุกใบและในรายงานสรุปค่าใช้จ่าย
    /// </summary>
    public static string Fixed2(decimal? value) =>
        (value ?? 0m).ToString("#,##0.00", Numeric);

    /// <summary>
    /// toLocaleString('th-TH', { minimumFractionDigits: 2 }) เดิม — ไม่ได้กำหนด maximumFractionDigits
    /// ตามสเปก ECMA-402 ค่า max จะกลายเป็น max(min, 3) = 3 โดยปริยาย
    /// ดังนั้น 1234.5 -> "1,234.50" แต่ 1234.5678 -> "1,234.568"
    /// ใช้เฉพาะในข้อความบรรทัดยอดค้างชำระบนใบแจ้งหนี้ (DebtSummary)
    /// </summary>
    public static string Min2Max3(decimal? value) =>
        (value ?? 0m).ToString("#,##0.00#", Numeric);
}
