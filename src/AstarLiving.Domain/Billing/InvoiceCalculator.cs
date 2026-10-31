namespace AstarLiving.Domain.Billing;

/// <summary>ค่าตั้งต้นจาก gv_settings แถวแรกของโครงการ (ระบบเดิมอ่าน settings[0] เสมอ)</summary>
public readonly record struct BillingRates(
    decimal? MaintenanceFeeRate,
    decimal? FineRatePercent,
    decimal? CollectionFee);

/// <summary>ผลของแถวหนึ่งในหน้า preview "ออกใบแจ้งหนี้"</summary>
public sealed record InvoicePreviewLine
{
    public required decimal Maintenance { get; init; }

    /// <summary>fine_amount — ค่าปรับจากค่าส่วนกลางงวดนี้ เมื่อมีหนี้ค้างใน พ.ศ. เดียวกับงวด</summary>
    public required decimal FineAmount { get; init; }

    /// <summary>
    /// fine_amount_preview_current_year — ค่าเดียวกับ FineAmount (เก็บ field เดิมให้ FE)
    /// </summary>
    public required decimal FineAmountPreviewCurrentYear { get; init; }

    public required decimal CollectionAmount { get; init; }

    public required decimal DebtTotal { get; init; }

    public required decimal Total { get; init; }

    public required decimal TotalPreviewCurrentYear { get; init; }
}

/// <summary>ยอดที่จะบันทึกลง gv_payments ตอนกดออกใบแจ้งหนี้จริง</summary>
public sealed record InvoiceCreateLine
{
    public required decimal Maintenance { get; init; }

    public required decimal Fine { get; init; }

    public required decimal CollectionFee { get; init; }

    public required decimal TotalDue { get; init; }

    /// <summary>ต้องสร้างใบทวงถามชนิด FINE หรือไม่</summary>
    public required bool NeedsFineNotice { get; init; }

    /// <summary>ต้องสร้างใบทวงถามชนิด COLLECTION หรือไม่</summary>
    public required bool NeedsCollectionNotice { get; init; }
}

/// <summary>
/// สูตรคิดยอดใบแจ้งหนี้ — พอร์ตจาก getInvoiceList() และ createInvoices()
/// ใน src/modules/internal/payment/payment.service.ts
///
/// ⚠ ระบบเดิมใช้ "ฐานคำนวณค่าปรับ" ต่างกันระหว่างสองเส้นทางนี้ และเป็นพฤติกรรมที่ตั้งใจคงไว้
///   - Preview : ฐาน = ค่าส่วนกลางของงวดนี้ (maintenance_fee_rate x ownership_ratio)
///   - Create  : ฐาน = amount_due ของ "เดือนก่อนหน้า" ที่ยังไม่ถูกชำระ/ยกเลิก
/// การแก้ให้ตรงกันจะทำให้ยอดที่ออกไม่ตรงกับใบที่ลูกค้าถืออยู่ จึงห้ามรวบให้เหมือนกันเอง
/// </summary>
public static class InvoiceCalculator
{
    /// <summary>
    /// หนึ่งแถวของหน้า preview
    /// </summary>
    /// <param name="rates">ค่าตั้งต้นของโครงการ</param>
    /// <param name="ownershipRatio">อัตราส่วนกรรมสิทธิ์ของบ้านหลังนี้</param>
    /// <param name="priorDebts">งวดค้างก่อนหน้า (status = NOT_PAY เท่านั้น ตามของเดิม)</param>
    /// <param name="period">งวดที่กำลังจะออก</param>
    /// <param name="isSelectedResident">ผู้ใช้ติ๊กเลือกบ้านหลังนี้ให้คิดค่าทวงถามหรือไม่</param>
    /// <param name="isPrepaidPeriod">บ้านหลังนี้ชำระล่วงหน้าในงวดนี้แล้วหรือไม่</param>
    public static InvoicePreviewLine Preview(
        BillingRates rates,
        decimal? ownershipRatio,
        IReadOnlyList<DebtLine> priorDebts,
        Common.MonthYear period,
        bool isSelectedResident,
        bool isPrepaidPeriod)
    {
        var debtTotal = DebtSummary.PdfInvoiceDebtTotal(priorDebts, period.Year, period.Month);

        var maintenanceRate = BillingCalculator.Maintenance(rates.MaintenanceFeeRate, ownershipRatio);
        var maintenance = isPrepaidPeriod ? 0m : maintenanceRate;
        var collection = isPrepaidPeriod
            ? 0m
            : BillingCalculator.CollectionFee(isSelectedResident, rates.CollectionFee);

        var hasDebtCurrentYear = HasChargeableCurrentYearDebt(priorDebts, period.Year);
        var fine = isPrepaidPeriod || !hasDebtCurrentYear
            ? 0m
            : BillingCalculator.Fine(maintenanceRate, rates.FineRatePercent);
        var finePreviewCurrentYear = fine;

        var total = isPrepaidPeriod
            ? debtTotal
            : DebtSummary.PdfInvoiceGrandTotal(maintenance, fine, collection, priorDebts, period.Year, period.Month);

        var totalPreviewCurrentYear = isPrepaidPeriod
            ? debtTotal
            : total - fine + finePreviewCurrentYear;

        return new InvoicePreviewLine
        {
            Maintenance = maintenance,
            FineAmount = fine,
            FineAmountPreviewCurrentYear = finePreviewCurrentYear,
            CollectionAmount = collection,
            DebtTotal = debtTotal,
            Total = total,
            TotalPreviewCurrentYear = totalPreviewCurrentYear,
        };
    }

    /// <summary>
    /// ยอดที่จะบันทึกจริงตอนกดออกใบแจ้งหนี้
    /// </summary>
    /// <param name="previousMonthAmountDue">
    /// amount_due ของงวดเดือนก่อนหน้าที่ยัง NOT IN (PAID, CANCELLED)
    /// ระบบเดิมหยิบแถวแรกที่เจอ และใช้ 0 เมื่อไม่มีแถวเลย
    /// </param>
    public static InvoiceCreateLine Create(
        BillingRates rates,
        decimal? ownershipRatio,
        IReadOnlyList<DebtLine> priorDebts,
        Common.MonthYear period,
        bool isSelectedResident,
        bool isPrepaidPeriod,
        decimal? previousMonthAmountDue)
    {
        var maintenanceRate = BillingCalculator.Maintenance(rates.MaintenanceFeeRate, ownershipRatio);
        var maintenance = isPrepaidPeriod ? 0m : maintenanceRate;

        var hasDebtCurrentYear = HasChargeableCurrentYearDebt(priorDebts, period.Year);
        var fine = isPrepaidPeriod || !hasDebtCurrentYear
            ? 0m
            : BillingCalculator.Fine(previousMonthAmountDue ?? 0m, rates.FineRatePercent);

        var collection = isPrepaidPeriod
            ? 0m
            : BillingCalculator.CollectionFee(isSelectedResident, rates.CollectionFee);

        var total = isPrepaidPeriod
            ? 0m
            : DebtSummary.PdfInvoiceGrandTotal(maintenance, fine, collection, priorDebts, period.Year, period.Month);

        return new InvoiceCreateLine
        {
            Maintenance = maintenance,
            Fine = fine,
            CollectionFee = collection,
            TotalDue = total,
            NeedsFineNotice = hasDebtCurrentYear && fine > 0m,
            NeedsCollectionNotice = !isPrepaidPeriod && isSelectedResident && collection > 0m,
        };
    }

    /// <summary>มีหนี้ค้างในปีงวดที่มียอดจริง — ไม่นับใบแจ้งเพื่อทราบที่ยอด 0</summary>
    private static bool HasChargeableCurrentYearDebt(IReadOnlyList<DebtLine> priorDebts, int year) =>
        priorDebts.Any(d =>
            d.Year == year && !InvoiceInclusion.IsZeroCharge(d.AmountDue, d.Fine, d.CollectionFee));
}
