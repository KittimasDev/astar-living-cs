namespace AstarLiving.Domain.Billing;

/// <summary>พอร์ตตรงจาก src/common/utils/date/month.utils.ts</summary>
public static class ThaiMonths
{
    /// <summary>index 1-12 (index 0 เป็นสตริงว่างเหมือนของเดิม)</summary>
    public static readonly string[] Abbr =
    [
        "", "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
        "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค.",
    ];

    public static readonly string[] Full =
    [
        "", "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
        "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม",
    ];

    public static readonly string[] EnAbbr =
    [
        "", "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
        "JUL", "AUG", "SEP", "OCT", "NOV", "DEC",
    ];

    public static readonly string[] EnFull =
    [
        "", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ];

    public static string GetAbbr(int month) => month is >= 1 and <= 12 ? Abbr[month] : "";

    public static string GetFull(int month) => month is >= 1 and <= 12 ? Full[month] : "";

    public static string GetEnAbbr(int month) => month is >= 1 and <= 12 ? EnAbbr[month] : "";

    public static string GetEnFull(int month) => month is >= 1 and <= 12 ? EnFull[month] : "";

    /// <summary>MONTH_MAP เดิม: "JAN" -> 1 (ไม่สนตัวพิมพ์ เพราะ client เดิมส่ง "Jan" มาก็มี)</summary>
    public static int? FromEnAbbr(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var key = code.Trim().ToUpperInvariant();
        var index = Array.IndexOf(EnAbbr, key);
        return index >= 1 ? index : null;
    }

    /// <summary>mapMonthNumbersToEN เดิม</summary>
    public static List<string> ToEnAbbrList(IEnumerable<int> months) =>
        months.Select(GetEnAbbr).ToList();
}
