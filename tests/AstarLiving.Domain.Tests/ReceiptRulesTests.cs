using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Domain.Tests;

/// <summary>กฎแปลงรายการที่ติ๊กเลือกในหน้าออกใบเสร็จเป็นสถานะของงวด</summary>
public class ReceiptSelectionRulesTests
{
    private static SelectedReceiptItem Maintenance(bool selected, int id = 101) =>
        new(ReceiptItemType.MAINTENANCE, selected, id);

    private static SelectedReceiptItem Fine(bool selected, int id = 201) =>
        new(ReceiptItemType.FINE, selected, id);

    private static SelectedReceiptItem Collection(bool selected, int id = 301) =>
        new(ReceiptItemType.COLLECTION, selected, id);

    [Fact]
    public void Selecting_everything_marks_the_period_paid()
    {
        var outcome = ReceiptSelectionRules.Resolve([Maintenance(true), Fine(true), Collection(true)]);

        outcome.Status.Should().Be(PaymentStatus.PAID);
        outcome.MaintenancePaymentId.Should().Be(101);
        outcome.NoticeIds.Should().BeEquivalentTo([201, 301]);
        outcome.CollectionPaid.Should().BeTrue();
    }

    [Fact]
    public void Selecting_some_items_marks_the_period_partially_paid()
    {
        var outcome = ReceiptSelectionRules.Resolve([Maintenance(true), Fine(false)]);

        outcome.Status.Should().Be(PaymentStatus.PARTIAL_PAY);
        outcome.NoticeIds.Should().BeEmpty();
        outcome.CollectionPaid.Should().BeFalse();
    }

    [Fact]
    public void Selecting_nothing_leaves_the_period_unpaid()
    {
        var outcome = ReceiptSelectionRules.Resolve([Maintenance(false), Fine(false)]);

        outcome.Status.Should().Be(PaymentStatus.NOT_PAY);
        outcome.MaintenancePaymentId.Should().BeNull();
    }

    /// <summary>เลือกเฉพาะค่าทวงถามโดยไม่จ่ายค่าส่วนกลาง เป็นเคสที่เกิดจริงในหน้าจอ</summary>
    [Fact]
    public void Paying_only_the_notice_still_records_which_notice_was_paid()
    {
        var outcome = ReceiptSelectionRules.Resolve([Maintenance(false), Collection(true, 999)]);

        outcome.Status.Should().Be(PaymentStatus.PARTIAL_PAY);
        outcome.MaintenancePaymentId.Should().BeNull();
        outcome.NoticeIds.Should().BeEquivalentTo([999]);
    }
}

/// <summary>บรรทัดรายการในใบเสร็จ — ช่วงเดือนของค่าปรับ/ค่าทวงถามใช้เดือนก่อนหน้างวดเสมอ</summary>
public class ReceiptItemBuilderTests
{
    private static Payment MakePayment(int id, short month, int year, decimal amountDue,
        PaymentStatus status = PaymentStatus.PARTIAL_PAY) => new()
        {
            Id = id,
            Month = month,
            Year = year,
            AmountDue = amountDue,
            Status = status,
        };

    private static CollectionNotice MakeNotice(NoticeType type, decimal fee) => new()
    {
        NoticesType = type,
        FeeAmount = fee,
        Status = NoticeStatus.PENDING,
    };

    [Fact]
    public void Single_period_shows_one_month_in_the_label()
    {
        var item = ReceiptItemBuilder.BuildSummaryItem([MakePayment(1, 5, 2568, 2000m)]);

        item!.Name.Should().Be("ค่าส่วนกลาง (พ.ค. 68)");
        item.Unit.Should().Be(1);
        item.Price.Should().Be(2000m);
        item.Total.Should().Be(2000m);
    }

    [Fact]
    public void Multiple_periods_show_a_range_and_sum_the_amounts()
    {
        var item = ReceiptItemBuilder.BuildSummaryItem([
            MakePayment(1, 3, 2568, 2000m),
            MakePayment(2, 4, 2568, 2000m),
            MakePayment(3, 5, 2568, 2100m),
        ]);

        item!.Name.Should().Be("ค่าส่วนกลาง (มี.ค. 68 - พ.ค. 68)");
        item.Unit.Should().Be(3);
        item.Price.Should().Be(2000m, "ราคาต่อหน่วยยึดจากงวดแรก");
        item.Total.Should().Be(6100m);
    }

    /// <summary>งวดที่ยังไม่ชำระเลยไม่ถูกนับเป็นรายการในใบเสร็จ</summary>
    [Fact]
    public void Unpaid_periods_are_excluded()
    {
        var item = ReceiptItemBuilder.BuildSummaryItem([
            MakePayment(1, 5, 2568, 2000m, PaymentStatus.NOT_PAY),
        ]);

        item.Should().BeNull();
    }

    /// <summary>
    /// ค่าเบี้ยปรับของงวดเดือน 5 ต้องแสดงเป็นเดือน 4 เพราะเป็นค่าปรับของการค้างเดือนก่อน
    /// </summary>
    [Fact]
    public void Charge_items_are_labelled_with_the_previous_month()
    {
        var groups = new List<ReceiptPaymentGroup>
        {
            new(MakePayment(1, 5, 2568, 2000m), [MakeNotice(NoticeType.FINE, 200m)], []),
        };

        var item = ReceiptItemBuilder.BuildChargeItem(groups, NoticeType.FINE);

        item!.Name.Should().Be("ค่าเบี้ยปรับ (เม.ย. 68)");
        item.Unit.Should().Be(1);
        item.Total.Should().Be(200m);
    }

    /// <summary>ยอดรวมของค่าปรับคิดจาก จำนวนใบ x ราคาใบแรก ตามระบบเดิม</summary>
    [Fact]
    public void Charge_total_is_count_times_the_first_price()
    {
        var groups = new List<ReceiptPaymentGroup>
        {
            new(MakePayment(1, 4, 2568, 2000m), [MakeNotice(NoticeType.FINE, 200m)], []),
            new(MakePayment(2, 5, 2568, 2000m), [MakeNotice(NoticeType.FINE, 500m)], []),
        };

        var item = ReceiptItemBuilder.BuildChargeItem(groups, NoticeType.FINE);

        item!.Unit.Should().Be(2);
        item.Price.Should().Be(200m);
        item.Total.Should().Be(400m, "ระบบเดิมถือว่าทุกใบราคาเท่ากัน ไม่ได้บวกทีละใบ");
    }

    /// <summary>แถวส่วนลดถูกต่อท้ายรายการ แต่ถูกลบออกจากยอดรวม</summary>
    [Fact]
    public void Discount_is_appended_as_a_row_and_subtracted_from_the_total()
    {
        var groups = new List<ReceiptPaymentGroup>
        {
            new(MakePayment(1, 1, 2569, 2000m), [], []),
        };

        var result = ReceiptItemBuilder.Build(groups, haveDiscount: true, discountRate: 5.00m, discount: 100m);

        result.Items.Should().HaveCount(2);
        result.Items[^1].Name.Should().Be("ส่วนลด 5.00 %");
        result.Items[^1].Total.Should().Be(100m);
        result.Total.Should().Be(1900m);
    }

    [Fact]
    public void Advance_invoice_label_covers_the_whole_selected_range()
    {
        ReceiptItemBuilder.BuildThaiMonthRange([1, 2, 3, 12], 2569)
            .Should().Be("ค่าส่วนกลางเดือน ม.ค. 69 - ธ.ค. 69");
    }
}
