using System.Globalization;

namespace AstarLiving.Client.Common;

/// <summary>
/// รูปแบบตัวเลขและวันที่แบบไทยที่ใช้ทั้งโปรแกรม
///
/// ปีในระบบนี้เป็น พ.ศ. ทั้งหมด ทั้งในฐานข้อมูลและบนหน้าจอ
/// ตัวแปลงตรงนี้จึงเป็นที่เดียวที่รู้เรื่องการบวกลบ 543 — ห้ามเขียนกระจายในหน้าจอ
/// </summary>
public static class ThaiFormat
{
    /// <summary>ส่วนต่างระหว่าง พ.ศ. กับ ค.ศ.</summary>
    public const int BuddhistOffset = 543;

    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("th-TH");

    /// <summary>ชื่อเดือนแบบย่อ ตรงกับ TH_MONTHS ของระบบเดิม</summary>
    public static readonly string[] ShortMonths =
    [
        "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
        "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค.",
    ];

    /// <summary>ชื่อเดือนเต็ม ตรงกับ THAI_MONTHS_FULL ของระบบเดิม</summary>
    public static readonly string[] FullMonths =
    [
        "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
        "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม",
    ];

    /// <summary>เงินแบบมีคั่นหลักพัน ไม่มีทศนิยม — ตรงกับ formatNumber ของเว็บเดิม</summary>
    public static string Money(decimal? value) =>
        value is null ? "-" : value.Value.ToString("#,##0", Culture);

    /// <summary>เงินแบบมีทศนิยมสองตำแหน่ง ใช้ในตารางที่ต้องกระทบยอด</summary>
    public static string Money2(decimal? value) =>
        value is null ? "-" : value.Value.ToString("#,##0.00", Culture);

    public static string Integer(int? value) =>
        value is null ? "-" : value.Value.ToString("#,##0", Culture);

    /// <summary>เปอร์เซ็นต์ทศนิยมสองตำแหน่ง</summary>
    public static string Percent(decimal? value) =>
        value is null ? "-" : value.Value.ToString("0.##", Culture);

    /// <summary>ปี ค.ศ. เป็น พ.ศ.</summary>
    public static int ToBuddhistYear(int gregorianYear) => gregorianYear + BuddhistOffset;

    public static int ToGregorianYear(int buddhistYear) => buddhistYear - BuddhistOffset;

    /// <summary>ปี พ.ศ. ของตอนนี้ตามเวลาไทย</summary>
    public static int CurrentBuddhistYear => ToBuddhistYear(NowBangkok.Year);

    /// <summary>เวลาปัจจุบันที่กรุงเทพ ใช้เป็นฐานของค่าเริ่มต้นในตัวกรองทุกหน้า</summary>
    public static DateTime NowBangkok
    {
        get
        {
            try
            {
                return TimeZoneInfo.ConvertTime(
                    DateTimeOffset.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")).DateTime;
            }
            catch (TimeZoneNotFoundException)
            {
                // เครื่องที่ตัดฐานข้อมูลโซนเวลาออก ให้บวกเองเพื่อไม่ให้โปรแกรมล้ม
                return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime;
            }
        }
    }

    /// <summary>"พ.ย. 68" — ตรงกับ formatThaiMonthYear ของระบบเดิม (รับปี พ.ศ.)</summary>
    public static string MonthYear(int month, int buddhistYear)
    {
        if (month is < 1 or > 12) return string.Empty;
        return $"{ShortMonths[month - 1]} {buddhistYear % 100:00}";
    }

    /// <summary>"พฤศจิกายน 2568"</summary>
    public static string MonthYearFull(int month, int buddhistYear)
    {
        if (month is < 1 or > 12) return string.Empty;
        return $"{FullMonths[month - 1]} {buddhistYear}";
    }

    /// <summary>"19 พ.ย. 2568" จากวันที่ ค.ศ.</summary>
    public static string ShortDate(DateTime? value)
    {
        if (value is null) return "-";
        var d = value.Value;
        return $"{d.Day} {ShortMonths[d.Month - 1]} {ToBuddhistYear(d.Year)}";
    }

    public static string ShortDate(DateOnly? value) =>
        value is null ? "-" : ShortDate(value.Value.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// วันที่ของเอกสารที่เก็บในฐานข้อมูลเป็น พ.ศ. อยู่แล้ว (เช่น payment_date = 2569-09-17)
    /// จึงต้องแสดงปีตามที่เก็บ ห้ามบวก 543 ซ้ำ
    /// </summary>
    public static string DocumentDate(DateOnly? value)
    {
        if (value is null) return "-";
        var d = value.Value;
        return $"{d.Day} {ShortMonths[d.Month - 1]} {d.Year}";
    }

    /// <summary>"19 พ.ย. 2568 14:35" สำหรับ created_at ซึ่งเป็น ค.ศ. จริง</summary>
    public static string DateTimeText(DateTimeOffset? value)
    {
        if (value is null) return "-";

        var local = value.Value.ToOffset(TimeSpan.FromHours(7));
        return $"{local.Day} {ShortMonths[local.Month - 1]} {ToBuddhistYear(local.Year)} {local:HH:mm}";
    }
}
