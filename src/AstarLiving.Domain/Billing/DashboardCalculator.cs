using AstarLiving.Domain.Enums;

namespace AstarLiving.Domain.Billing;

/// <summary>งวดหนึ่งเท่าที่แดชบอร์ดต้องใช้</summary>
public readonly record struct DashboardPayment(
    int ResidentId,
    string HouseNo,
    string FullName,
    short Month,
    decimal AmountDue,
    decimal Fine,
    decimal CollectionFee,
    PaymentStatus Status);

public sealed record DashboardSummary(
    int TotalResident,
    decimal TotalBilled,
    decimal TotalPaid,
    decimal TotalUnpaid,
    decimal PaymentRate);

/// <summary>หนึ่งจุดบนกราฟ — หนึ่งเดือนให้สามจุด (ออกบิล / ชำระแล้ว / ค้าง)</summary>
public sealed record DashboardSeriesPoint(string Label, string Key, decimal Value);

public sealed record DashboardOutstanding(
    int ResidentId,
    string HouseNo,
    string FullName,
    int Count,
    decimal AmountDue,
    decimal Fine,
    decimal CollectionFee,
    decimal TotalDue,
    string Status);

/// <summary>
/// การคำนวณของหน้าแดชบอร์ด — พอร์ตจาก src/modules/external/dashboard/utils/
/// แยกออกมาเป็นฟังก์ชันบริสุทธิ์เพื่อให้ทดสอบได้โดยไม่ต้องมีฐานข้อมูล
/// </summary>
public static class DashboardCalculator
{
    /// <summary>ข้อความสถานะคงที่ที่ระบบเดิมใส่ให้ทุกแถวในตารางค้างชำระ</summary>
    public const string OutstandingStatusText = "ยังไม่ชำระเงิน";

    /// <summary>
    /// computeSummary() — ยอดทั้งหมดคิดจาก amount_due เท่านั้น ไม่รวมค่าปรับและค่าทวงถาม
    /// อัตราการชำระปัดทศนิยม 2 ตำแหน่ง และเป็น 0 เมื่อยังไม่มีการออกบิล
    /// </summary>
    public static DashboardSummary ComputeSummary(
        IReadOnlyList<DashboardPayment> payments, int totalResident)
    {
        var billed = payments.Sum(p => p.AmountDue);
        var paid = payments.Where(p => p.Status == PaymentStatus.PAID).Sum(p => p.AmountDue);
        var unpaid = payments.Where(p => p.Status == PaymentStatus.NOT_PAY).Sum(p => p.AmountDue);

        var rate = billed > 0m
            ? Math.Round(paid / billed * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new DashboardSummary(totalResident, billed, paid, unpaid, rate);
    }

    /// <summary>
    /// computeMonthlyTrend() — เรียงตามเดือน แล้วแผ่ออกเป็นสามจุดต่อเดือน
    /// สังเกตว่างวดที่ชำระบางส่วนจะถูกนับใน billed แต่ไม่ถูกนับทั้งใน paid และ unpaid
    /// </summary>
    public static IReadOnlyList<DashboardSeriesPoint> ComputeMonthlyTrend(
        IReadOnlyList<DashboardPayment> payments)
    {
        return payments
            .GroupBy(p => p.Month)
            .OrderBy(g => g.Key)
            .SelectMany(g =>
            {
                var label = ThaiMonths.GetAbbr(g.Key);
                var billed = g.Sum(p => p.AmountDue);
                var paid = g.Where(p => p.Status == PaymentStatus.PAID).Sum(p => p.AmountDue);
                var unpaid = g.Where(p => p.Status == PaymentStatus.NOT_PAY).Sum(p => p.AmountDue);

                return new[]
                {
                    new DashboardSeriesPoint(label, "billed", billed),
                    new DashboardSeriesPoint(label, "paid", paid),
                    new DashboardSeriesPoint(label, "unpaid", unpaid),
                };
            })
            .ToList();
    }

    /// <summary>
    /// computeOutstandings() — รวมยอดค้างต่อบ้าน
    /// ยอดรวมของแถวนี้รวมค่าปรับและค่าทวงถามด้วย ต่างจาก summary ด้านบน
    /// </summary>
    public static IReadOnlyList<DashboardOutstanding> ComputeOutstandings(
        IReadOnlyList<DashboardPayment> unpaidPayments)
    {
        return unpaidPayments
            .GroupBy(p => p.ResidentId)
            .Select(g =>
            {
                var first = g.First();
                var amountDue = g.Sum(p => p.AmountDue);
                var fine = g.Sum(p => p.Fine);
                var collectionFee = g.Sum(p => p.CollectionFee);

                return new DashboardOutstanding(
                    g.Key,
                    first.HouseNo,
                    first.FullName,
                    g.Count(),
                    amountDue,
                    fine,
                    collectionFee,
                    amountDue + fine + collectionFee,
                    OutstandingStatusText);
            })
            .ToList();
    }
}
