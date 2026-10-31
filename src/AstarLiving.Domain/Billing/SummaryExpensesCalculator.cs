using AstarLiving.Domain.Entities;

namespace AstarLiving.Domain.Billing;

/// <summary>ยอดของงวดหนึ่งหลังเฉลี่ยตามสัดส่วนที่ชำระแล้ว</summary>
public readonly record struct ProratedAmounts(decimal CommonFee, decimal Fine, decimal CollectionFee)
{
    public decimal Total => CommonFee + Fine + CollectionFee;
}

/// <summary>หนึ่งแถวของรายงานสรุปค่าใช้จ่าย แยกตามบ้าน</summary>
public sealed class SummaryExpensesHouseRow
{
    public required int ResidentId { get; init; }
    public required string HouseNo { get; init; }
    public required string FullName { get; init; }
    public decimal CommonFee { get; set; }
    public decimal Fine { get; set; }
    public decimal CollectionFee { get; set; }
    public int PaymentCount { get; set; }
    public decimal Total => CommonFee + Fine + CollectionFee;
}

public sealed class SummaryExpensesGrandTotal
{
    public decimal CommonFee { get; set; }
    public decimal Fine { get; set; }
    public decimal CollectionFee { get; set; }
    public int HouseCount { get; set; }
    public int PaymentCount { get; set; }
    public decimal Total => CommonFee + Fine + CollectionFee;
}

public sealed record SummaryExpensesAggregate(
    IReadOnlyList<SummaryExpensesHouseRow> ByHouse,
    SummaryExpensesGrandTotal Total);

/// <summary>
/// สรุปค่าใช้จ่ายค้างชำระ — พอร์ตจาก
/// src/modules/external/summary-expenses/utils/compute-summary-expenses.util.ts
/// </summary>
public static class SummaryExpensesCalculator
{
    /// <summary>
    /// paymentRowAmounts() — เฉลี่ยยอดตามสัดส่วนเมื่อชำระมาบางส่วน
    /// gross = ค่าส่วนกลาง + ค่าปรับ + ค่าทวงถาม
    /// net   = total_due (ยอดที่ยังค้างจริง)
    /// เมื่อ net น้อยกว่า gross ให้ลดทุกองค์ประกอบลงตามอัตราส่วน net/gross
    /// </summary>
    public static ProratedAmounts RowAmounts(decimal? amountDue, decimal? fine, decimal? collectionFee, decimal? totalDue)
    {
        var commonFee = amountDue ?? 0m;
        var fineAmount = fine ?? 0m;
        var collection = collectionFee ?? 0m;

        var gross = commonFee + fineAmount + collection;
        var net = totalDue ?? gross;

        if (gross > 0m && net < gross)
        {
            var ratio = net / gross;
            commonFee *= ratio;
            fineAmount *= ratio;
            collection *= ratio;
        }

        return new ProratedAmounts(commonFee, fineAmount, collection);
    }

    public static ProratedAmounts RowAmounts(Payment payment) =>
        RowAmounts(payment.AmountDue, payment.Fine, payment.CollectionFee, payment.TotalDue);

    /// <summary>computeSummaryExpenses() — รวมยอดต่อบ้านและยอดรวมทั้งโครงการ</summary>
    public static SummaryExpensesAggregate Compute(IReadOnlyList<Payment> payments)
    {
        var byHouse = new Dictionary<int, SummaryExpensesHouseRow>();
        var grand = new SummaryExpensesGrandTotal();

        foreach (var payment in payments)
        {
            var amounts = RowAmounts(payment);

            if (!byHouse.TryGetValue(payment.ResidentId, out var row))
            {
                row = new SummaryExpensesHouseRow
                {
                    ResidentId = payment.ResidentId,
                    HouseNo = payment.Resident?.HouseNo ?? string.Empty,
                    FullName = payment.Resident?.FullName ?? string.Empty,
                };
                byHouse[payment.ResidentId] = row;
            }

            row.CommonFee += amounts.CommonFee;
            row.Fine += amounts.Fine;
            row.CollectionFee += amounts.CollectionFee;
            row.PaymentCount += 1;

            grand.CommonFee += amounts.CommonFee;
            grand.Fine += amounts.Fine;
            grand.CollectionFee += amounts.CollectionFee;
            grand.PaymentCount += 1;
        }

        grand.HouseCount = byHouse.Count;
        return new SummaryExpensesAggregate(byHouse.Values.ToList(), grand);
    }

    /// <summary>fineColumnLabel() — หัวคอลัมน์ค่าปรับที่มีอัตราต่อท้าย</summary>
    public static string FineColumnLabel(decimal? fineRate) =>
        fineRate is null ? "ค่าปรับ" : $"ค่าปรับ ({fineRate.Value.Normalize()}%)";

    /// <summary>buildPeriodLabel() — ป้ายช่วงเวลาบนหัวรายงาน</summary>
    public static string BuildPeriodLabel(string? year, string? month)
    {
        if (!string.IsNullOrWhiteSpace(year) && !string.IsNullOrWhiteSpace(month))
            return $"พ.ศ. {year} เดือน {month}";
        if (!string.IsNullOrWhiteSpace(year))
            return $"พ.ศ. {year}";
        return "ทั้งหมด";
    }

    /// <summary>ตัดศูนย์ท้ายทิ้งให้เหมือน Number(x) ของ JavaScript เช่น 10.00 -> 10</summary>
    private static decimal Normalize(this decimal value) => value / 1.000000000000000000000000000000000m;
}
