using AstarLiving.Domain.Common;

namespace AstarLiving.Domain.Billing;

/// <summary>ผลการหางวดที่จะออกใบแจ้งหนี้ — ตรงกับ InvoicePeriodResult ของเดิม</summary>
public sealed record InvoicePeriodResult
{
    public required int ProjectId { get; init; }

    /// <summary>งวดล่าสุดที่เคยออกใบแจ้งหนี้ปกติ (advance = false) — null เมื่อยังไม่เคยออกเลย</summary>
    public MonthYear? Latest { get; init; }

    /// <summary>ปี พ.ศ. ของงวดที่จะออก</summary>
    public required int Year { get; init; }

    /// <summary>เดือนของงวดที่จะออก</summary>
    public required int Month { get; init; }

    public int Min { get; init; }

    public int Max { get; init; }

    public string MonthTh { get; init; } = string.Empty;

    /// <summary>false = ออกใบแจ้งหนี้ไม่ได้ (มีใบของงวดอนาคตอยู่แล้ว)</summary>
    public required bool IsPayment { get; init; }

    /// <summary>ปีของงวดถัดไป — มีค่าเฉพาะตอน IsPayment = false เพื่อใช้ในข้อความ error</summary>
    public int? NextYear { get; init; }

    public string? NextMonthTh { get; init; }

    /// <summary>true = โหมดเติมใบย้อนหลังให้บ้านที่ขาด ไม่ใช่การเลื่อนไปงวดถัดไป</summary>
    public bool IsSupplement { get; init; }

    /// <summary>ข้อความ error เดิมเมื่อพยายามออกใบแจ้งหนี้ซ้ำ</summary>
    public string DuplicateIssueMessage =>
        $"ไม่สามารถออกใบแจ้งหนี้ซ้ำได้ ระบบตรวจพบว่ามีการออกใบแจ้งหนี้เดือน {MonthTh} {Year} และ {NextMonthTh} {NextYear}";
}

/// <summary>
/// หางวดที่จะออกใบแจ้งหนี้ — พอร์ตจาก src/common/utils/invoice-period.util.ts
/// เป็นฟังก์ชันบริสุทธิ์ ฝั่ง Infrastructure เป็นคนหา "งวดล่าสุด" จากฐานข้อมูลมาป้อน
/// </summary>
public static class InvoicePeriodResolver
{
    /// <summary>
    /// resolveInvoicePeriod()
    /// </summary>
    /// <param name="projectId">โครงการ</param>
    /// <param name="latest">
    /// งวดล่าสุดที่มี gv_payments แถวที่ advance = false ของโครงการนี้
    /// (เรียง year DESC, month DESC เอาแถวแรก) — null เมื่อยังไม่เคยออกใบเลย
    /// </param>
    /// <param name="now">งวดปัจจุบันตามปฏิทินไทย (ปี พ.ศ.)</param>
    public static InvoicePeriodResult Resolve(int projectId, MonthYear? latest, MonthYear now)
    {
        if (latest is null)
        {
            // ยังไม่เคยออกใบแจ้งหนี้เลย -> ออกงวดปัจจุบันได้
            return new InvoicePeriodResult
            {
                ProjectId = projectId,
                Latest = null,
                Year = now.Year,
                Month = now.Month,
                Min = now.Month,
                Max = now.Month,
                MonthTh = ThaiMonths.GetFull(now.Month),
                IsPayment = true,
                IsSupplement = false,
            };
        }

        var last = latest.Value;
        var targetYear = now.Year;
        var targetMonth = now.Month;

        // งวดล่าสุดอยู่ก่อนงวดปัจจุบัน -> ออกงวดปัจจุบัน (ค่าตั้งต้นอยู่แล้ว)

        // งวดล่าสุดคืองวดปัจจุบันพอดี -> เลื่อนไปงวดถัดไป
        if (last.Year == now.Year && last.Month == now.Month)
        {
            var next = ThaiCalendar.NextMonth(now.Month, now.Year);
            targetMonth = next.Month;
            targetYear = next.Year;
        }

        // งวดล่าสุดล้ำหน้างวดปัจจุบัน -> ออกไม่ได้
        if (last > now)
        {
            var next = ThaiCalendar.NextMonth(now.Month, now.Year);
            return new InvoicePeriodResult
            {
                ProjectId = projectId,
                Latest = last,
                Year = targetYear,
                Month = targetMonth,
                Min = targetMonth,
                Max = targetMonth,
                MonthTh = ThaiMonths.GetFull(targetMonth),
                NextYear = next.Year,
                NextMonthTh = ThaiMonths.GetFull(next.Month),
                IsPayment = false,
                IsSupplement = false,
            };
        }

        return new InvoicePeriodResult
        {
            ProjectId = projectId,
            Latest = last,
            Year = targetYear,
            Month = targetMonth,
            Min = targetMonth,
            Max = targetMonth,
            MonthTh = ThaiMonths.GetFull(targetMonth),
            IsPayment = true,
            IsSupplement = false,
        };
    }

    /// <summary>
    /// applySupplementPeriod()
    /// เมื่อออกงวดปัจจุบันไปแล้ว แต่ยังมีบ้านที่จ่ายล่วงหน้าและค้างหนี้เก่าโดยไม่มีใบปกติ
    /// ให้ตรึงงวดกลับไปที่งวดล่าสุดเพื่อ "เติมใบที่ขาด" แทนการเลื่อนไปเดือนถัดไป
    /// </summary>
    public static InvoicePeriodResult ApplySupplement(
        InvoicePeriodResult period, MonthYear now, bool hasMissingPrepaidDebt)
    {
        if (!hasMissingPrepaidDebt || period.Latest is null)
        {
            return period with { IsSupplement = false };
        }

        var latest = period.Latest.Value;
        var latestIsCurrentOrFuture = latest.Year > now.Year
            || (latest.Year == now.Year && latest.Month >= now.Month);

        if (!latestIsCurrentOrFuture)
        {
            return period with { IsSupplement = false };
        }

        return period with
        {
            Year = latest.Year,
            Month = latest.Month,
            Min = latest.Month,
            Max = latest.Month,
            MonthTh = ThaiMonths.GetFull(latest.Month),
            IsPayment = true,
            IsSupplement = true,
        };
    }
}
