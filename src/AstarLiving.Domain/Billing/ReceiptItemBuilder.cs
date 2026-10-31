using System.Globalization;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;

namespace AstarLiving.Domain.Billing;

/// <summary>หนึ่งบรรทัดรายการในใบเสร็จ</summary>
public sealed record ReceiptItem(string Name, decimal Unit, decimal Price, decimal Total);

/// <summary>
/// หนึ่งงวดพร้อมใบทวงถามที่ผูกอยู่ — ตรงกับรูป { payment, fine[], collection[] } ของเดิม
/// </summary>
public sealed record ReceiptPaymentGroup(
    Payment Payment,
    IReadOnlyList<CollectionNotice> Fines,
    IReadOnlyList<CollectionNotice> Collections);

public sealed record ReceiptItemsResult(IReadOnlyList<ReceiptItem> Items, decimal Total);

/// <summary>
/// สร้างรายการในใบเสร็จ — พอร์ตจาก buildReceiptItems ใน dateTH.utils.ts
/// จุดที่ต้องระวัง: ช่วงเดือนของ "ค่าเบี้ยปรับ/ค่าทวงถาม" ใช้ "เดือนก่อนหน้า" ของงวดนั้นเสมอ
/// ไม่ใช่เดือนของงวดเอง และราคาต่อหน่วยยึดจากใบแรกโดยถือว่าทุกใบราคาเท่ากัน
/// </summary>
public static class ReceiptItemBuilder
{
    /// <summary>
    /// buildSummaryItem() — รวมค่าส่วนกลางทุกงวดที่ "ไม่ใช่ NOT_PAY" เป็นบรรทัดเดียว
    /// เรียงตาม id เพื่อหาเดือนเริ่ม-สิ้นสุดของช่วง (ไม่ใช่เรียงตามงวด)
    /// </summary>
    public static ReceiptItem? BuildSummaryItem(IReadOnlyList<Payment> payments)
    {
        if (payments.Count == 0) return null;

        var sorted = payments
            .Where(p => p.Status != PaymentStatus.NOT_PAY)
            .OrderBy(p => p.Id)
            .ToList();

        if (sorted.Count == 0) return null;

        var first = sorted[0];
        var last = sorted[^1];
        var rangeText = FormatRange(first.Month, first.Year, last.Month, last.Year);

        return new ReceiptItem(
            Name: $"ค่าส่วนกลาง {rangeText}",
            Unit: sorted.Count,
            Price: first.AmountDue,
            Total: sorted.Sum(p => p.AmountDue));
    }

    /// <summary>
    /// buildChargeItem() — รวมค่าเบี้ยปรับหรือค่าทวงถามเป็นบรรทัดเดียว
    /// ยอดรวม = จำนวนใบ x ราคาใบแรก (ระบบเดิมถือว่าทุกใบราคาเท่ากัน ไม่ได้บวกทีละใบ)
    /// </summary>
    public static ReceiptItem? BuildChargeItem(IReadOnlyList<ReceiptPaymentGroup> groups, NoticeType type)
    {
        var notices = groups.SelectMany(g => Select(g, type)).ToList();
        if (notices.Count == 0) return null;

        var months = groups
            .Where(g => Select(g, type).Count > 0)
            .Select(g => ThaiCalendar.PreviousMonth(g.Payment.Month, g.Payment.Year))
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();

        var first = months[0];
        var last = months[^1];
        var rangeText = FormatRange(first.Month, first.Year, last.Month, last.Year);

        var unit = notices.Count;
        var price = notices[0].FeeAmount ?? 0m;
        var label = type == NoticeType.FINE ? "ค่าเบี้ยปรับ" : "ค่าทวงถาม";

        return new ReceiptItem($"{label} {rangeText}", unit, price, unit * price);
    }

    /// <summary>buildDiscount() — แถวส่วนลด จำนวนหน่วยและราคาต่อหน่วยเป็น 0 ตามของเดิม</summary>
    public static ReceiptItem BuildDiscount(decimal? discountRate, decimal? discount) =>
        new(
            Name: $"ส่วนลด {FormatRate(discountRate)} %",
            Unit: 0m,
            Price: 0m,
            Total: Round2(discount ?? 0m));

    /// <summary>
    /// buildReceiptItems() — ฟังก์ชันหลัก
    /// แถวส่วนลดถูก "ต่อท้ายรายการ" แต่ถูก "ลบออก" จากยอดรวม
    /// </summary>
    public static ReceiptItemsResult Build(
        IReadOnlyList<ReceiptPaymentGroup> groups,
        bool haveDiscount,
        decimal? discountRate,
        decimal? discount)
    {
        var items = new List<ReceiptItem>();

        var summary = BuildSummaryItem(groups.Select(g => g.Payment).ToList());
        if (summary is not null) items.Add(summary);

        var fine = BuildChargeItem(groups, NoticeType.FINE);
        if (fine is not null) items.Add(fine);

        var collection = BuildChargeItem(groups, NoticeType.COLLECTION);
        if (collection is not null) items.Add(collection);

        var total = items.Sum(i => i.Total);

        if (haveDiscount)
        {
            var discountItem = BuildDiscount(discountRate, discount);
            items.Add(discountItem);
            total -= discountItem.Total;
        }

        return new ReceiptItemsResult(items, total);
    }

    /// <summary>buildThaiMonthRange() — หัวข้อของใบแจ้งหนี้ล่วงหน้า เช่น "ค่าส่วนกลางเดือน ม.ค. 69 - ธ.ค. 69"</summary>
    public static string BuildThaiMonthRange(IReadOnlyList<int> months, int year)
    {
        var sorted = months.OrderBy(m => m).ToList();
        var yearShort = (year % 100).ToString("00");
        return $"ค่าส่วนกลางเดือน {ThaiMonths.GetAbbr(sorted[0])} {yearShort} - {ThaiMonths.GetAbbr(sorted[^1])} {yearShort}";
    }

    private static IReadOnlyList<CollectionNotice> Select(ReceiptPaymentGroup group, NoticeType type) =>
        type == NoticeType.FINE ? group.Fines : group.Collections;

    /// <summary>งวดเดียวแสดง "(ม.ค. 68)" หลายงวดแสดง "(ม.ค. 68 - มี.ค. 68)"</summary>
    private static string FormatRange(int startMonth, int startYear, int endMonth, int endYear)
    {
        var startText = ThaiCalendar.FormatThaiMonthYear(startMonth, startYear);
        if (startMonth == endMonth && startYear == endYear) return $"({startText})";
        return $"({startText} - {ThaiCalendar.FormatThaiMonthYear(endMonth, endYear)})";
    }

    /// <summary>ตรงกับ Number(x.toFixed(2)) ของ JavaScript ซึ่งปัดครึ่งขึ้นออกจากศูนย์</summary>
    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// ระบบเดิมแทรกค่า discount_rate ลงข้อความตรง ๆ ซึ่งเป็นสตริงจากฐานข้อมูล เช่น "5.00"
    /// จึงคงสเกลตามที่เก็บไว้ ไม่ตัดศูนย์ท้าย
    /// </summary>
    private static string FormatRate(decimal? rate) =>
        (rate ?? 0m).ToString(CultureInfo.InvariantCulture);
}
