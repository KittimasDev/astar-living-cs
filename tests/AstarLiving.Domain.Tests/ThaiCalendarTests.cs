using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Domain.Tests;

public class ThaiCalendarTests
{
    /// <summary>2025-11-19 เวลาไทย -> ปี พ.ศ. 2568 -> prefix "681119"</summary>
    [Fact]
    public void RunningNumberPrefix_uses_buddhist_year()
    {
        var calendar = new ThaiCalendar(new FixedClock(new DateTime(2025, 11, 19, 10, 0, 0)));

        calendar.CurrentYear.Should().Be(2568);
        calendar.CurrentMonth.Should().Be(11);
        calendar.RunningNumberPrefix().Should().Be("681119");
    }

    /// <summary>
    /// ระบบเดิมบวก 7 ชั่วโมงก่อนใช้เป็นวันที่เอกสาร ทำให้ช่วง 17:00 เป็นต้นไปกลายเป็นวันถัดไป
    /// พฤติกรรมนี้ต้องคงไว้ ไม่งั้นเลขรันเอกสารจะไม่ต่อจากของเดิม
    /// </summary>
    [Fact]
    public void Plus7Hours_can_roll_over_to_next_day()
    {
        var calendar = new ThaiCalendar(new FixedClock(new DateTime(2025, 11, 19, 18, 30, 0)));

        calendar.NowThaiPlus7Hours.Day.Should().Be(20);
        calendar.NowThaiPlus7Hours.Year.Should().Be(2568);
    }

    [Theory]
    [InlineData(2568, "68")]
    [InlineData(2025, "68")] // ปี ค.ศ. ถูกบวก 543 ให้ก่อน
    [InlineData(2569, "69")]
    public void ThaiYearShort_accepts_both_calendars(int year, string expected) =>
        ThaiCalendar.ThaiYearShort(year).Should().Be(expected);

    [Fact]
    public void FormatThaiMonthYear_matches_legacy_format() =>
        ThaiCalendar.FormatThaiMonthYear(11, 2568).Should().Be("พ.ย. 68");

    [Fact]
    public void PreviousMonth_wraps_january_to_december() =>
        ThaiCalendar.PreviousMonth(1, 2568).Should().Be(new MonthYear(12, 2567));

    [Fact]
    public void NextMonth_wraps_december_to_january() =>
        ThaiCalendar.NextMonth(12, 2568).Should().Be(new MonthYear(1, 2569));

    [Fact]
    public void ToThaiDate_adds_543_only_when_year_is_gregorian()
    {
        ThaiCalendar.ToThaiDate(new DateTime(2025, 1, 5)).Should().Be("05/01/2568");
        ThaiCalendar.ToThaiDate(new DateTime(2568, 1, 5)).Should().Be("05/01/2568");
    }

    /// <summary>ParseThaiDateRange ลบ 543 แต่ ParseThaiDateRaw ไม่ลบ — ความไม่สอดคล้องของเดิมที่คงไว้</summary>
    [Fact]
    public void Two_date_parsers_disagree_on_purpose()
    {
        ThaiCalendar.ParseThaiDateRange("25681119")!.Value.Start.Year.Should().Be(2025);
        ThaiCalendar.ParseThaiDateRaw("25681119")!.Value.Year.Should().Be(2568);
    }
}
