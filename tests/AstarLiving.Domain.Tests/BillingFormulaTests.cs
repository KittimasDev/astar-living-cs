using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Domain.Tests;

public class BillingFormulaTests
{
    [Fact]
    public void Maintenance_is_rate_times_ownership_ratio() =>
        BillingCalculator.Maintenance(25m, 88.5m).Should().Be(2212.5m);

    [Fact]
    public void Maintenance_treats_null_as_zero() =>
        BillingCalculator.Maintenance(null, 88.5m).Should().Be(0m);

    [Theory]
    [InlineData(1000, 10, 100)]
    [InlineData(0, 10, 0)]      // ยอดฐานไม่เป็นบวก -> ไม่คิดค่าปรับ
    [InlineData(-50, 10, 0)]
    [InlineData(1000, 0, 0)]    // อัตราไม่เป็นบวก -> ไม่คิดค่าปรับ
    public void Fine_follows_legacy_guard_clauses(decimal baseAmount, decimal rate, decimal expected) =>
        BillingCalculator.Fine(baseAmount, rate).Should().Be(expected);

    [Fact]
    public void CollectionFee_only_charged_when_selected()
    {
        BillingCalculator.CollectionFee(true, 200m).Should().Be(200m);
        BillingCalculator.CollectionFee(false, 200m).Should().Be(0m);
    }
}

public class MoneyFormatTests
{
    /// <summary>formatMoney / formatMoneyTh — ทศนิยม 2 ตำแหน่งตายตัว</summary>
    [Theory]
    [InlineData(1234.5, "1,234.50")]
    [InlineData(1234.5678, "1,234.57")]
    [InlineData(0, "0.00")]
    [InlineData(1234567.891, "1,234,567.89")]
    public void Fixed2_always_shows_two_decimals(decimal value, string expected) =>
        MoneyFormat.Fixed2(value).Should().Be(expected);

    /// <summary>
    /// บรรทัดยอดค้างบนใบแจ้งหนี้ใช้ toLocaleString ที่ไม่ได้กำหนด maximumFractionDigits
    /// จึงได้ทศนิยมสูงสุด 3 ตำแหน่ง ไม่ใช่ 2
    /// </summary>
    [Theory]
    [InlineData(1234.5, "1,234.50")]
    [InlineData(1234.5678, "1,234.568")]
    [InlineData(1234.5671, "1,234.567")]
    [InlineData(0, "0.00")]
    public void Min2Max3_keeps_up_to_three_decimals(decimal value, string expected) =>
        MoneyFormat.Min2Max3(value).Should().Be(expected);
}

public class DebtSummaryTests
{
    private static DebtLine Debt(int year, int month, decimal amountDue, decimal fine = 0m, decimal collection = 0m) =>
        new(year, month, amountDue, fine, collection);

    /// <summary>ไม่มีหนี้เลย -> ยังต้องได้ 3 บรรทัดว่าง เพื่อตรึงความสูงของใบแจ้งหนี้</summary>
    [Fact]
    public void Always_returns_three_lines_even_when_empty()
    {
        var result = DebtSummary.ForInvoicePartitioned([], []);

        result.Items.Should().HaveCount(3);
        result.Items.Should().AllBeEquivalentTo(DebtTextLine.Blank);
        result.DebtTotal.Should().Be(0m);
    }

    /// <summary>ค่าเบี้ยปรับ/ค่าทวงถามที่รวมได้ 0 จะถูกซ่อน แต่ค่าส่วนกลางไม่ถูกซ่อน</summary>
    [Fact]
    public void Zero_fine_and_collection_lines_are_blanked_but_maintenance_is_not()
    {
        var result = DebtSummary.ForInvoicePartitioned([Debt(2567, 12, 0m)], []);

        result.Items[0].Text.Should().Be("ค่าส่วนกลางรวม = 0.00 บาท");
        result.Items[1].Should().Be(DebtTextLine.Blank);
        result.Items[2].Should().Be(DebtTextLine.Blank);
    }

    [Fact]
    public void Partitions_debts_by_year_and_month_of_the_invoice_period()
    {
        List<DebtLine> debts =
        [
            Debt(2567, 11, 1000m, 100m, 200m), // ปีก่อน -> นับ
            Debt(2568, 3, 500m, 50m, 0m),      // ปีเดียวกัน เดือนก่อน -> นับ
            Debt(2568, 5, 900m, 90m, 0m),      // งวดเดียวกัน -> ไม่นับ
            Debt(2568, 7, 700m, 70m, 0m),      // งวดหลัง -> ไม่นับ
        ];

        var total = DebtSummary.PdfInvoiceDebtTotal(debts, invoiceYear: 2568, invoiceMonth: 5);

        total.Should().Be(1000m + 100m + 200m + 500m + 50m);
    }

    [Fact]
    public void GrandTotal_is_current_period_plus_prior_debt()
    {
        List<DebtLine> debts = [Debt(2567, 12, 1000m, 0m, 0m)];

        var grand = DebtSummary.PdfInvoiceGrandTotal(
            amountDue: 2000m, fine: 100m, collectionFee: 200m,
            debts, invoiceYear: 2568, invoiceMonth: 1);

        grand.Should().Be(2000m + 100m + 200m + 1000m);
    }
}

public class InvoicePeriodResolverTests
{
    private static readonly MonthYear Now = new(5, 2568);

    [Fact]
    public void No_invoice_ever_issued_targets_current_period()
    {
        var period = InvoicePeriodResolver.Resolve(1, latest: null, Now);

        period.IsPayment.Should().BeTrue();
        period.Month.Should().Be(5);
        period.Year.Should().Be(2568);
        period.MonthTh.Should().Be("พฤษภาคม");
    }

    [Fact]
    public void Latest_before_now_targets_current_period()
    {
        var period = InvoicePeriodResolver.Resolve(1, new MonthYear(3, 2568), Now);

        period.IsPayment.Should().BeTrue();
        period.Month.Should().Be(5);
    }

    [Fact]
    public void Latest_equal_to_now_rolls_forward_one_month()
    {
        var period = InvoicePeriodResolver.Resolve(1, new MonthYear(5, 2568), Now);

        period.IsPayment.Should().BeTrue();
        period.Month.Should().Be(6);
        period.Year.Should().Be(2568);
    }

    [Fact]
    public void Latest_equal_to_december_rolls_into_next_year()
    {
        var period = InvoicePeriodResolver.Resolve(1, new MonthYear(12, 2568), new MonthYear(12, 2568));

        period.Month.Should().Be(1);
        period.Year.Should().Be(2569);
    }

    [Fact]
    public void Latest_ahead_of_now_blocks_issuing_with_legacy_message()
    {
        var period = InvoicePeriodResolver.Resolve(1, new MonthYear(7, 2568), Now);

        period.IsPayment.Should().BeFalse();
        period.NextMonthTh.Should().Be("มิถุนายน");
        period.NextYear.Should().Be(2568);
        period.DuplicateIssueMessage.Should()
            .Be("ไม่สามารถออกใบแจ้งหนี้ซ้ำได้ ระบบตรวจพบว่ามีการออกใบแจ้งหนี้เดือน พฤษภาคม 2568 และ มิถุนายน 2568");
    }

    /// <summary>
    /// ออกงวดปัจจุบันไปแล้ว (period เลื่อนเป็นเดือน 6) แต่ยังมีบ้านจ่ายล่วงหน้าที่ค้างหนี้
    /// -> ตรึงกลับไปงวด 5 เพื่อเติมใบที่ขาด
    /// </summary>
    [Fact]
    public void Supplement_pins_period_back_to_latest_issued_month()
    {
        var period = InvoicePeriodResolver.Resolve(1, new MonthYear(5, 2568), Now);
        var supplemented = InvoicePeriodResolver.ApplySupplement(period, Now, hasMissingPrepaidDebt: true);

        supplemented.IsSupplement.Should().BeTrue();
        supplemented.IsPayment.Should().BeTrue();
        supplemented.Month.Should().Be(5);
    }

    [Fact]
    public void Supplement_does_nothing_when_latest_is_in_the_past()
    {
        var period = InvoicePeriodResolver.Resolve(1, new MonthYear(3, 2568), Now);
        var supplemented = InvoicePeriodResolver.ApplySupplement(period, Now, hasMissingPrepaidDebt: true);

        supplemented.IsSupplement.Should().BeFalse();
        supplemented.Month.Should().Be(5);
    }
}

public class InvoiceInclusionTests
{
    [Fact]
    public void Resident_with_regular_invoice_is_excluded()
    {
        PeriodPaymentRow[] rows = [new(10, Advance: false)];

        InvoiceInclusion.GetExcludedResidentIds(rows, new HashSet<int>())
            .Should().BeEquivalentTo([10]);
    }

    [Fact]
    public void Prepaid_resident_without_prior_debt_is_excluded()
    {
        PeriodPaymentRow[] rows = [new(11, Advance: true)];

        InvoiceInclusion.GetExcludedResidentIds(rows, new HashSet<int>())
            .Should().BeEquivalentTo([11]);
    }

    [Fact]
    public void Prepaid_resident_with_prior_debt_is_kept_and_flagged_as_missing()
    {
        PeriodPaymentRow[] rows = [new(12, Advance: true)];
        var withDebt = new HashSet<int> { 12 };

        InvoiceInclusion.GetExcludedResidentIds(rows, withDebt).Should().BeEmpty();
        InvoiceInclusion.GetMissingPrepaidDebtResidentIds(rows, withDebt).Should().BeEquivalentTo([12]);
    }

    /// <summary>มีทั้งใบปกติและใบล่วงหน้า -> ใบปกติชนะ ถูกตัดออกและไม่นับเป็นบ้านที่ขาด</summary>
    [Fact]
    public void Regular_invoice_wins_over_advance()
    {
        PeriodPaymentRow[] rows = [new(13, Advance: true), new(13, Advance: false)];
        var withDebt = new HashSet<int> { 13 };

        InvoiceInclusion.GetExcludedResidentIds(rows, withDebt).Should().BeEquivalentTo([13]);
        InvoiceInclusion.GetMissingPrepaidDebtResidentIds(rows, withDebt).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(100, 0, 0, false)]
    [InlineData(-100, 50, 50, true)]
    public void IsZeroCharge_matches_legacy(decimal amountDue, decimal fine, decimal collection, bool expected) =>
        InvoiceInclusion.IsZeroCharge(amountDue, fine, collection).Should().Be(expected);
}

public class RunningNumberTests
{
    [Fact]
    public void Starts_at_one_when_no_document_today() =>
        RunningNumber.Next("681119", null).Should().Be("68111900001");

    [Fact]
    public void Continues_from_last_five_digits() =>
        RunningNumber.Next("681119", "68111900042").Should().Be("68111900043");

    [Fact]
    public void Document_number_is_eleven_characters() =>
        RunningNumber.Next("681119", "68111900042").Should().HaveLength(11);
}

public class SummaryExpensesCalculatorTests
{
    /// <summary>ยังไม่ได้ชำระอะไรเลย (total_due = gross) -> ไม่เฉลี่ย</summary>
    [Fact]
    public void No_proration_when_nothing_paid()
    {
        var amounts = SummaryExpensesCalculator.RowAmounts(1000m, 100m, 200m, 1300m);

        amounts.CommonFee.Should().Be(1000m);
        amounts.Fine.Should().Be(100m);
        amounts.CollectionFee.Should().Be(200m);
    }

    /// <summary>ค้างอยู่ครึ่งเดียว -> ทุกองค์ประกอบลดลงครึ่งหนึ่ง</summary>
    [Fact]
    public void Prorates_every_component_by_net_over_gross()
    {
        var amounts = SummaryExpensesCalculator.RowAmounts(1000m, 100m, 200m, 650m);

        amounts.CommonFee.Should().Be(500m);
        amounts.Fine.Should().Be(50m);
        amounts.CollectionFee.Should().Be(100m);
        amounts.Total.Should().Be(650m);
    }

    /// <summary>total_due มากกว่า gross -> ไม่แตะยอด (ของเดิมเฉลี่ยเฉพาะขาลง)</summary>
    [Fact]
    public void Does_not_scale_up_when_net_exceeds_gross()
    {
        var amounts = SummaryExpensesCalculator.RowAmounts(1000m, 0m, 0m, 5000m);

        amounts.CommonFee.Should().Be(1000m);
    }
}
