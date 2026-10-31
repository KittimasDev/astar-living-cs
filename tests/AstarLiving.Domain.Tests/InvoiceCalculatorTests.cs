using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Domain.Tests;

/// <summary>
/// ตรึงพฤติกรรมของการคิดยอดใบแจ้งหนี้ โดยเฉพาะจุดที่หน้าพรีวิวกับตอนบันทึกจริงคิดไม่เหมือนกัน
/// ซึ่งผู้ใช้ยืนยันแล้วว่าให้คงไว้ตามระบบเดิม
/// </summary>
public class InvoiceCalculatorTests
{
    /// <summary>ค่าส่วนกลาง 50 บาทต่อตารางวา ค่าปรับ 10% ค่าทวงถาม 200 บาท</summary>
    private static readonly BillingRates Rates = new(MaintenanceFeeRate: 50m, FineRatePercent: 10m, CollectionFee: 200m);

    private static readonly MonthYear Period = new(5, 2568);

    private const decimal OwnershipRatio = 40m;

    /// <summary>ค่าส่วนกลางของงวดนี้ = 50 x 40</summary>
    private const decimal Maintenance = 2000m;

    private static List<DebtLine> OneDebt(int year = 2568, int month = 4, decimal amount = 1500m) =>
        [new DebtLine(year, month, amount, 0m, 0m)];

    [Fact]
    public void Preview_without_debt_has_no_fine()
    {
        var line = InvoiceCalculator.Preview(
            Rates, OwnershipRatio, [], Period, isSelectedResident: false, isPrepaidPeriod: false);

        line.Maintenance.Should().Be(Maintenance);
        line.FineAmount.Should().Be(0m);
        line.CollectionAmount.Should().Be(0m);
        line.Total.Should().Be(Maintenance);
    }

    /// <summary>ค่าทวงถามคิดเฉพาะบ้านที่ถูกติ๊กเลือก</summary>
    [Fact]
    public void Preview_charges_collection_fee_only_for_selected_houses()
    {
        var selected = InvoiceCalculator.Preview(
            Rates, OwnershipRatio, [], Period, isSelectedResident: true, isPrepaidPeriod: false);
        var notSelected = InvoiceCalculator.Preview(
            Rates, OwnershipRatio, [], Period, isSelectedResident: false, isPrepaidPeriod: false);

        selected.CollectionAmount.Should().Be(200m);
        notSelected.CollectionAmount.Should().Be(0m);
    }

    /// <summary>
    /// หน้าพรีวิวคิดค่าปรับจาก "ค่าส่วนกลางของงวดนี้" = 2000 x 10% = 200
    /// </summary>
    [Fact]
    public void Preview_bases_the_fine_on_this_period_maintenance()
    {
        var line = InvoiceCalculator.Preview(
            Rates, OwnershipRatio, OneDebt(), Period, isSelectedResident: false, isPrepaidPeriod: false);

        line.FineAmount.Should().Be(200m);
        line.DebtTotal.Should().Be(1500m);
        line.Total.Should().Be(Maintenance + 200m + 1500m);
    }

    /// <summary>
    /// ตอนบันทึกจริงคิดค่าปรับจาก "ยอดของเดือนก่อนหน้า" = 1500 x 10% = 150
    /// ต่างจากหน้าพรีวิวโดยตั้งใจ
    /// </summary>
    [Fact]
    public void Create_bases_the_fine_on_the_previous_month_amount()
    {
        var line = InvoiceCalculator.Create(
            Rates, OwnershipRatio, OneDebt(), Period,
            isSelectedResident: false, isPrepaidPeriod: false, previousMonthAmountDue: 1500m);

        line.Fine.Should().Be(150m);
        line.TotalDue.Should().Be(Maintenance + 150m + 1500m);
    }

    /// <summary>ยืนยันตรง ๆ ว่าสองเส้นทางให้ค่าปรับไม่เท่ากันเมื่อยอดเดือนก่อนต่างจากค่าส่วนกลางงวดนี้</summary>
    [Fact]
    public void Preview_and_create_disagree_on_the_fine_by_design()
    {
        var preview = InvoiceCalculator.Preview(
            Rates, OwnershipRatio, OneDebt(), Period, isSelectedResident: false, isPrepaidPeriod: false);

        var create = InvoiceCalculator.Create(
            Rates, OwnershipRatio, OneDebt(), Period,
            isSelectedResident: false, isPrepaidPeriod: false, previousMonthAmountDue: 1500m);

        preview.FineAmount.Should().Be(200m);
        create.Fine.Should().Be(150m);
        preview.FineAmount.Should().NotBe(create.Fine);
    }

    /// <summary>ไม่มีงวดเดือนก่อนค้างอยู่ -> ฐานเป็น 0 จึงไม่มีค่าปรับ แม้จะมีหนี้ปีเก่า</summary>
    [Fact]
    public void Create_has_no_fine_when_the_previous_month_row_is_missing()
    {
        var line = InvoiceCalculator.Create(
            Rates, OwnershipRatio, OneDebt(year: 2566, month: 12), Period,
            isSelectedResident: false, isPrepaidPeriod: false, previousMonthAmountDue: null);

        line.Fine.Should().Be(0m);
        line.NeedsFineNotice.Should().BeFalse();
    }

    /// <summary>
    /// ค่าปรับนับเฉพาะเมื่อมีหนี้ค้างในปี พ.ศ. เดียวกับงวด — หนี้ปีเก่าอย่างเดียวได้ศูนย์ทั้ง fine_amount
    /// และ fine_amount_preview_current_year
    /// </summary>
    [Fact]
    public void Preview_current_year_fine_ignores_debts_from_earlier_years()
    {
        var line = InvoiceCalculator.Preview(
            Rates, OwnershipRatio, OneDebt(year: 2566, month: 12), Period,
            isSelectedResident: false, isPrepaidPeriod: false);

        line.FineAmount.Should().Be(0m);
        line.FineAmountPreviewCurrentYear.Should().Be(0m);
        line.TotalPreviewCurrentYear.Should().Be(line.Total);
        line.DebtTotal.Should().Be(1500m);
    }

    /// <summary>ตอนบันทึกจริงก็ไม่คิดค่าปรับจากหนี้ปีก่อน แม้มียอดเดือนก่อนเป็นฐาน</summary>
    [Fact]
    public void Create_has_no_fine_when_only_earlier_year_debts_exist()
    {
        var line = InvoiceCalculator.Create(
            Rates, OwnershipRatio, OneDebt(year: 2566, month: 12), Period,
            isSelectedResident: false, isPrepaidPeriod: false, previousMonthAmountDue: 1500m);

        line.Fine.Should().Be(0m);
        line.NeedsFineNotice.Should().BeFalse();
    }

    /// <summary>
    /// ใบปีงวดที่ยอด 0 (ใบแจ้งเพื่อทราบ) ไม่ถือเป็นหนี้ปีงวด — เคสบ้าน 556/26
    /// </summary>
    [Fact]
    public void Preview_ignores_zero_charge_row_in_the_invoice_year()
    {
        List<DebtLine> debts =
        [
            new DebtLine(2566, 12, 1995m, 0m, 0m),
            new DebtLine(2568, 4, 0m, 0m, 0m),
        ];

        var line = InvoiceCalculator.Preview(
            Rates, OwnershipRatio, debts, Period, isSelectedResident: false, isPrepaidPeriod: false);

        line.FineAmount.Should().Be(0m);
        line.FineAmountPreviewCurrentYear.Should().Be(0m);
    }

    [Fact]
    public void Create_ignores_zero_charge_row_in_the_invoice_year()
    {
        List<DebtLine> debts =
        [
            new DebtLine(2566, 12, 1995m, 0m, 0m),
            new DebtLine(2568, 4, 0m, 0m, 0m),
        ];

        var line = InvoiceCalculator.Create(
            Rates, OwnershipRatio, debts, Period,
            isSelectedResident: false, isPrepaidPeriod: false, previousMonthAmountDue: 1500m);

        line.Fine.Should().Be(0m);
        line.NeedsFineNotice.Should().BeFalse();
    }

    /// <summary>
    /// บ้านที่ชำระล่วงหน้าไว้แล้วจะไม่ถูกคิดอะไรใหม่ในงวดนี้
    /// พรีวิวแสดงยอดเท่ากับหนี้ค้าง ส่วนตอนบันทึกจริงลงยอดรวมเป็นศูนย์ (ใบแจ้งเพื่อทราบ)
    /// </summary>
    [Fact]
    public void Prepaid_house_is_charged_nothing_new()
    {
        var preview = InvoiceCalculator.Preview(
            Rates, OwnershipRatio, OneDebt(), Period, isSelectedResident: true, isPrepaidPeriod: true);

        var create = InvoiceCalculator.Create(
            Rates, OwnershipRatio, OneDebt(), Period,
            isSelectedResident: true, isPrepaidPeriod: true, previousMonthAmountDue: 1500m);

        preview.Maintenance.Should().Be(0m);
        preview.FineAmount.Should().Be(0m);
        preview.CollectionAmount.Should().Be(0m);
        preview.Total.Should().Be(1500m, "พรีวิวแสดงยอดค้างที่ยังเหลืออยู่");

        create.Maintenance.Should().Be(0m);
        create.TotalDue.Should().Be(0m, "แถวที่บันทึกเป็นใบแจ้งเพื่อทราบ ไม่มียอดคิดใหม่");
        create.NeedsCollectionNotice.Should().BeFalse();
    }

    /// <summary>เงื่อนไขการสร้างใบทวงถามทั้งสองชนิด</summary>
    [Fact]
    public void Notices_are_requested_only_when_their_amounts_are_positive()
    {
        var withBoth = InvoiceCalculator.Create(
            Rates, OwnershipRatio, OneDebt(), Period,
            isSelectedResident: true, isPrepaidPeriod: false, previousMonthAmountDue: 1500m);

        withBoth.NeedsFineNotice.Should().BeTrue();
        withBoth.NeedsCollectionNotice.Should().BeTrue();

        var noDebt = InvoiceCalculator.Create(
            Rates, OwnershipRatio, [], Period,
            isSelectedResident: true, isPrepaidPeriod: false, previousMonthAmountDue: 1500m);

        noDebt.NeedsFineNotice.Should().BeFalse("ไม่มีหนี้ค้างจึงไม่มีค่าปรับ");
        noDebt.NeedsCollectionNotice.Should().BeTrue();
    }
}
