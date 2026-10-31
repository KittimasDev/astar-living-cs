namespace AstarLiving.Domain.Billing;

/// <summary>แถว gv_payments ของงวดที่กำลังพิจารณา — ใช้เฉพาะ 2 ฟิลด์นี้ในการคัดบ้าน</summary>
public readonly record struct PeriodPaymentRow(int ResidentId, bool Advance);

/// <summary>
/// กฎการคัดบ้านเข้ารอบออกใบแจ้งหนี้ — พอร์ตจาก
/// src/modules/internal/payment/util/invoice-inclusion.util.ts
/// </summary>
public static class InvoiceInclusion
{
    /// <summary>
    /// isZeroChargePayment() — ใบแจ้งเพื่อทราบที่ไม่มียอดคิดใหม่
    /// ใช้ตัดออกจากการนับ "หนี้ค้างงวดก่อน"
    /// </summary>
    public static bool IsZeroCharge(decimal? amountDue, decimal? fine, decimal? collectionFee) =>
        (amountDue ?? 0m) + (fine ?? 0m) + (collectionFee ?? 0m) <= 0m;

    /// <summary>getPrepaidResidentIds() — บ้านที่มีแถวชำระล่วงหน้าในงวดนี้</summary>
    public static HashSet<int> GetPrepaidResidentIds(IEnumerable<PeriodPaymentRow> periodPayments) =>
        periodPayments.Where(p => p.Advance).Select(p => p.ResidentId).ToHashSet();

    /// <summary>
    /// getExcludedResidentIdsForInvoice() — บ้านที่ต้องตัดออก
    /// 1) มีใบปกติ (advance = false) ของงวดนี้แล้ว
    /// 2) มีแค่ชำระล่วงหน้า และไม่มีหนี้ค้างงวดก่อน
    /// </summary>
    public static IReadOnlyList<int> GetExcludedResidentIds(
        IEnumerable<PeriodPaymentRow> periodPayments,
        IReadOnlySet<int> residentIdsWithPriorDebt)
    {
        var (hasRegular, hasAdvance) = Split(periodPayments);

        var excluded = new HashSet<int>(hasRegular);
        foreach (var residentId in hasAdvance)
        {
            if (hasRegular.Contains(residentId)) continue;
            if (!residentIdsWithPriorDebt.Contains(residentId)) excluded.Add(residentId);
        }

        return excluded.ToList();
    }

    /// <summary>
    /// getMissingPrepaidDebtResidentIds() — บ้านที่จ่ายล่วงหน้าแล้วยังค้างหนี้เก่า
    /// และยังไม่มีใบปกติของงวดนี้ (เป็นตัวจุดชนวนโหมด supplement)
    /// </summary>
    public static IReadOnlyList<int> GetMissingPrepaidDebtResidentIds(
        IEnumerable<PeriodPaymentRow> periodPayments,
        IReadOnlySet<int> residentIdsWithPriorDebt)
    {
        var (hasRegular, hasAdvance) = Split(periodPayments);

        return hasAdvance
            .Where(id => !hasRegular.Contains(id) && residentIdsWithPriorDebt.Contains(id))
            .ToList();
    }

    private static (HashSet<int> Regular, HashSet<int> Advance) Split(IEnumerable<PeriodPaymentRow> rows)
    {
        var regular = new HashSet<int>();
        var advance = new HashSet<int>();

        foreach (var row in rows)
        {
            if (row.Advance) advance.Add(row.ResidentId);
            else regular.Add(row.ResidentId);
        }

        return (regular, advance);
    }
}
