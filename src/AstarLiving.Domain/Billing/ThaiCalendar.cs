using AstarLiving.Domain.Common;

namespace AstarLiving.Domain.Billing;

/// <summary>
/// ปฏิทินไทยของระบบ — พอร์ตจาก src/config/value.ts และ src/common/utils/date/dateTH.utils.ts
/// ทุกค่า "ตอนนี้" อิง Asia/Bangkok และปีที่เก็บลงฐานข้อมูลเป็น พ.ศ. (ค.ศ. + 543)
/// </summary>
public sealed class ThaiCalendar(IClock clock)
{
    public const int BuddhistOffset = 543;

    private readonly IClock _clock = clock;

    /// <summary>getNowMoment() — เวลาปัจจุบันตามโซน Asia/Bangkok (ปี ค.ศ.)</summary>
    public DateTime NowBangkok => _clock.NowBangkok;

    /// <summary>เวลาปัจจุบันแบบ UTC สำหรับคอลัมน์ timestamptz</summary>
    public DateTimeOffset UtcNow => _clock.UtcNow;

    /// <summary>getThaiMoment() — เวลาปัจจุบันที่บวกปีเป็น พ.ศ. แล้ว</summary>
    public DateTime NowThai => NowBangkok.AddYears(BuddhistOffset);

    /// <summary>getCurrentYear() — ปี พ.ศ. ปัจจุบัน</summary>
    public int CurrentYear => NowThai.Year;

    /// <summary>getCurrentMonth() — เดือนปัจจุบัน 1-12</summary>
    public int CurrentMonth => NowThai.Month;

    /// <summary>งวดปัจจุบันตามปฏิทินไทย</summary>
    public MonthYear CurrentPeriod => new(CurrentMonth, CurrentYear);

    /// <summary>
    /// getCurrentDateAddhours() — เวลาไทย + 7 ชั่วโมง
    /// ระบบเดิมใช้ค่านี้เป็น payment_date / notice_date ตอนสร้างเอกสาร และเป็นฐานของ
    /// prefix เลขรันเอกสาร จึงต้องคงไว้ตามเดิม ไม่อย่างนั้นเลขเอกสารจะไม่ต่อจากของเก่า
    /// </summary>
    public DateTime NowThaiPlus7Hours => NowThai.AddHours(7);

    /// <summary>getCurrentDateAddhoursYYMMDD() — prefix เลขรันเอกสาร เช่น 2568-11-19 -> "681119"</summary>
    public string RunningNumberPrefix()
    {
        var m = NowThai;
        return $"{m.Year % 100:00}{m.Month:00}{m.Day:00}";
    }

    /// <summary>getThaiYearShort() — คืนปี พ.ศ. 2 หลักสุดท้าย (รับได้ทั้งปี ค.ศ. และ พ.ศ.)</summary>
    public static string ThaiYearShort(int year)
    {
        if (year == 0) return "";
        var buddhistYear = year < 2400 ? year + BuddhistOffset : year;
        return (buddhistYear % 100).ToString("00");
    }

    /// <summary>formatThaiMonthYear(month, year) — "พ.ย. 68"</summary>
    public static string FormatThaiMonthYear(int month, int year) =>
        $"{ThaiMonths.GetAbbr(month)} {ThaiYearShort(year)}";

    /// <summary>formatMonthThai(month, year) — "พ.ย. 2568" (ใช้ในรายงาน)</summary>
    public static string FormatMonthThai(int month, int year) =>
        $"{ThaiMonths.GetAbbr(month)} {year}";

    /// <summary>getPreviousMonthYear() — เดือนก่อนหน้า วน 1 ไปเป็น 12 ของปีก่อน</summary>
    public static MonthYear PreviousMonth(int month, int year)
    {
        var newMonth = month - 1;
        var newYear = year;
        if (newMonth == 0)
        {
            newMonth = 12;
            newYear -= 1;
        }

        return new MonthYear(newMonth, newYear);
    }

    public static MonthYear PreviousMonth(MonthYear period) => PreviousMonth(period.Month, period.Year);

    /// <summary>getNextMonthYear() — เดือนถัดไป วน 12 ไปเป็น 1 ของปีถัดไป</summary>
    public static MonthYear NextMonth(int month, int year)
    {
        var newMonth = month + 1;
        var newYear = year;
        if (newMonth == 13)
        {
            newMonth = 1;
            newYear += 1;
        }

        return new MonthYear(newMonth, newYear);
    }

    public static MonthYear NextMonth(MonthYear period) => NextMonth(period.Month, period.Year);

    /// <summary>toThaiDate() — "DD/MM/YYYY" โดยบวก 543 ให้เมื่อปียังเป็น ค.ศ.</summary>
    public static string ToThaiDate(DateTime date)
    {
        var year = date.Year < 2500 ? date.Year + BuddhistOffset : date.Year;
        return $"{date.Day:00}/{date.Month:00}/{year}";
    }

    public static string ToThaiDate(DateOnly date) => ToThaiDate(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>toThaiMonthYear() — "MM/YYYY" แบบ พ.ศ.</summary>
    public static string ToThaiMonthYear(DateTime date)
    {
        var year = date.Year < 2500 ? date.Year + BuddhistOffset : date.Year;
        return $"{date.Month:00}/{year}";
    }

    /// <summary>
    /// parseThaiDateRange() — รับ "YYYYMMDD" ที่เป็นปี พ.ศ. แล้วคืนช่วงทั้งวันเป็น ค.ศ.
    /// ใช้กับตัวกรอง created_at BETWEEN ของใบแจ้งหนี้
    /// </summary>
    public static (DateTime Start, DateTime End)? ParseThaiDateRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 8) return null;
        if (!int.TryParse(value[..4], out var year)) return null;
        if (!int.TryParse(value.Substring(4, 2), out var month)) return null;
        if (!int.TryParse(value.Substring(6, 2), out var day)) return null;

        var gregorianYear = year - BuddhistOffset;
        var start = new DateTime(gregorianYear, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        return (start, start.AddDays(1).AddSeconds(-1));
    }

    /// <summary>
    /// parseThaiDate() — ระบบเดิม "ไม่" ลบ 543 ในฟังก์ชันนี้ (ต่างจาก ParseThaiDateRange)
    /// เส้นทางสร้าง PDF ใบแจ้งหนี้แยกปี/เดือนจากสตริงโดยตรงแบบนี้ จึงคงพฤติกรรมไว้ตามเดิม
    /// </summary>
    public static (int Year, int Month, int Day)? ParseThaiDateRaw(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 8) return null;
        if (!int.TryParse(value[..4], out var year)) return null;
        if (!int.TryParse(value.Substring(4, 2), out var month)) return null;
        if (!int.TryParse(value.Substring(6, 2), out var day)) return null;
        return (year, month, day);
    }
}
