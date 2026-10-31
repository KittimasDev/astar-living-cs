using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Domain.Tests;

/// <summary>การคำนวณของหน้าแดชบอร์ด</summary>
public class DashboardCalculatorTests
{
    private static DashboardPayment Payment(
        short month, decimal amountDue, PaymentStatus status,
        int residentId = 1, decimal fine = 0m, decimal collectionFee = 0m) =>
        new(residentId, $"556/{residentId}", $"ลูกบ้าน {residentId}", month, amountDue, fine, collectionFee, status);

    [Fact]
    public void Summary_counts_only_maintenance_amounts()
    {
        var payments = new List<DashboardPayment>
        {
            Payment(1, 1000m, PaymentStatus.PAID, fine: 500m),
            Payment(2, 1000m, PaymentStatus.NOT_PAY, collectionFee: 200m),
        };

        var summary = DashboardCalculator.ComputeSummary(payments, totalResident: 83);

        summary.TotalResident.Should().Be(83);
        summary.TotalBilled.Should().Be(2000m, "ค่าปรับและค่าทวงถามไม่ถูกนับในตัวเลขสรุป");
        summary.TotalPaid.Should().Be(1000m);
        summary.TotalUnpaid.Should().Be(1000m);
        summary.PaymentRate.Should().Be(50m);
    }

    /// <summary>งวดที่ชำระบางส่วนถูกนับในยอดออกบิล แต่ไม่ถูกนับทั้งในยอดชำระแล้วและยอดค้าง</summary>
    [Fact]
    public void Partially_paid_periods_fall_outside_both_paid_and_unpaid()
    {
        var payments = new List<DashboardPayment>
        {
            Payment(1, 1000m, PaymentStatus.PARTIAL_PAY),
        };

        var summary = DashboardCalculator.ComputeSummary(payments, totalResident: 1);

        summary.TotalBilled.Should().Be(1000m);
        summary.TotalPaid.Should().Be(0m);
        summary.TotalUnpaid.Should().Be(0m);
    }

    [Fact]
    public void Payment_rate_is_zero_when_nothing_was_billed()
    {
        var summary = DashboardCalculator.ComputeSummary([], totalResident: 10);

        summary.PaymentRate.Should().Be(0m);
    }

    [Fact]
    public void Payment_rate_is_rounded_to_two_decimals()
    {
        var payments = new List<DashboardPayment>
        {
            Payment(1, 1000m, PaymentStatus.PAID),
            Payment(2, 2000m, PaymentStatus.NOT_PAY),
        };

        var summary = DashboardCalculator.ComputeSummary(payments, totalResident: 2);

        summary.PaymentRate.Should().Be(33.33m);
    }

    /// <summary>หนึ่งเดือนให้สามจุดเสมอ และเรียงตามเดือน</summary>
    [Fact]
    public void Monthly_trend_emits_three_points_per_month_in_order()
    {
        var payments = new List<DashboardPayment>
        {
            Payment(3, 500m, PaymentStatus.PAID),
            Payment(1, 1000m, PaymentStatus.PAID),
            Payment(1, 2000m, PaymentStatus.NOT_PAY),
        };

        var trend = DashboardCalculator.ComputeMonthlyTrend(payments);

        trend.Should().HaveCount(6);
        trend[0].Label.Should().Be("ม.ค.");
        trend[0].Key.Should().Be("billed");
        trend[0].Value.Should().Be(3000m);
        trend[1].Key.Should().Be("paid");
        trend[1].Value.Should().Be(1000m);
        trend[2].Key.Should().Be("unpaid");
        trend[2].Value.Should().Be(2000m);
        trend[3].Label.Should().Be("มี.ค.");
    }

    /// <summary>ตารางค้างชำระรวมค่าปรับและค่าทวงถามเข้าไปด้วย ต่างจากตัวเลขสรุปด้านบน</summary>
    [Fact]
    public void Outstandings_group_by_house_and_include_every_charge()
    {
        var payments = new List<DashboardPayment>
        {
            Payment(1, 1000m, PaymentStatus.NOT_PAY, residentId: 7, fine: 100m, collectionFee: 200m),
            Payment(2, 1000m, PaymentStatus.NOT_PAY, residentId: 7),
            Payment(1, 500m, PaymentStatus.NOT_PAY, residentId: 9),
        };

        var rows = DashboardCalculator.ComputeOutstandings(payments);

        rows.Should().HaveCount(2);

        var house7 = rows.Single(r => r.ResidentId == 7);
        house7.Count.Should().Be(2);
        house7.AmountDue.Should().Be(2000m);
        house7.Fine.Should().Be(100m);
        house7.CollectionFee.Should().Be(200m);
        house7.TotalDue.Should().Be(2300m);
        house7.HouseNo.Should().Be("556/7");
        house7.Status.Should().Be("ยังไม่ชำระเงิน");
    }
}
