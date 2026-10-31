using AstarLiving.Domain.Entities;

namespace AstarLiving.Domain.Billing;

/// <summary>หนึ่งงวดหนี้ค้างที่ใช้คำนวณ — ตัดมาจาก gv_payments เฉพาะฟิลด์ที่สูตรต้องใช้</summary>
public readonly record struct DebtLine(int Year, int Month, decimal AmountDue, decimal Fine, decimal CollectionFee)
{
    public static DebtLine From(Payment p) =>
        new(p.Year, p.Month, p.AmountDue, p.Fine ?? 0m, p.CollectionFee ?? 0m);

    public decimal Total => AmountDue + Fine + CollectionFee;
}

/// <summary>หนึ่งบรรทัดในบล็อก "ยอดค้างชำระ" บนใบแจ้งหนี้ (ว่างได้ เพื่อตรึง layout ให้เป็น 3 บรรทัดเสมอ)</summary>
public readonly record struct DebtTextLine(string Text)
{
    /// <summary>NBSP หนึ่งตัว — ตรงกับ DEBT_BLANK_LINE ของเดิม</summary>
    public static readonly DebtTextLine Blank = new(" ");
}

public sealed record DebtSummaryResult(IReadOnlyList<DebtTextLine> Items, decimal DebtTotal);

/// <summary>
/// สรุปยอดค้างชำระ — พอร์ตจาก src/common/utils/date/dateTH.utils.ts
/// จุดสำคัญ: บล็อกยอดค้างบนใบแจ้งหนี้มี 3 บรรทัดเสมอ (ค่าส่วนกลาง / ค่าเบี้ยปรับ / ค่าทวงถาม)
/// บรรทัดที่ไม่มีข้อมูลจะเป็น NBSP ไม่ใช่ถูกตัดทิ้ง มิฉะนั้นความสูงของใบแจ้งหนี้จะเลื่อน
/// </summary>
public static class DebtSummary
{
    /// <summary>
    /// buildText() ภายในของเดิม
    /// - ไม่มีรายการเลย -> null (กลายเป็นบรรทัดว่าง)
    /// - hideWhenZero และยอดรวมเป็น 0 -> null
    /// </summary>
    private static string? BuildText(string label, IReadOnlyCollection<decimal> amounts, bool hideWhenZero = false)
    {
        if (amounts.Count == 0) return null;

        var total = amounts.Sum();
        if (hideWhenZero && total == 0m) return null;

        return $"{label}รวม = {MoneyFormat.Min2Max3(total)} บาท";
    }

    private static DebtTextLine ToLine(string? text) =>
        text is null ? DebtTextLine.Blank : new DebtTextLine(text);

    /// <summary>
    /// getDebtSummaryForInvoicePartitioned() — รูปแบบที่ใบแจ้งหนี้ PDF ใช้จริง
    /// - บรรทัดค่าส่วนกลาง: amount_due ของทั้งหนี้ปีเก่าและหนี้เดือนก่อนในปีเดียวกัน
    /// - บรรทัดเบี้ยปรับ/ทวงถาม: รวมจากทั้งสองกอง และซ่อนเมื่อยอดเป็น 0
    /// - debtTotal: ผลรวม (amount_due + fine + collection_fee) ของทั้งสองกอง
    /// </summary>
    public static DebtSummaryResult ForInvoicePartitioned(
        IReadOnlyList<DebtLine> olderYear,
        IReadOnlyList<DebtLine> sameYearPrior)
    {
        var both = olderYear.Concat(sameYearPrior).ToList();

        var items = new List<DebtTextLine>
        {
            ToLine(BuildText("ค่าส่วนกลาง", both.Select(d => d.AmountDue).ToList())),
            ToLine(BuildText("ค่าเบี้ยปรับ", both.Select(d => d.Fine).ToList(), hideWhenZero: true)),
            ToLine(BuildText("ค่าทวงถาม", both.Select(d => d.CollectionFee).ToList(), hideWhenZero: true)),
        };

        return new DebtSummaryResult(items, both.Sum(d => d.Total));
    }

    /// <summary>getDebtSummaryForInvoice() — รูปแบบที่ไม่แบ่งกอง (เส้นทางเก่าที่ยังมีผู้เรียกอยู่)</summary>
    public static DebtSummaryResult ForInvoice(IReadOnlyList<DebtLine> debts)
    {
        var items = new List<DebtTextLine>
        {
            ToLine(BuildText("ค่าส่วนกลาง", debts.Select(d => d.AmountDue).ToList())),
            ToLine(BuildText("ค่าเบี้ยปรับ", debts.Select(d => d.Fine).ToList(), hideWhenZero: true)),
            ToLine(BuildText("ค่าทวงถาม", debts.Select(d => d.CollectionFee).ToList(), hideWhenZero: true)),
        };

        return new DebtSummaryResult(items, debts.Sum(d => d.Total));
    }

    /// <summary>
    /// getPdfInvoiceDebtTotal() — แบ่งหนี้เป็น 2 กองตามงวดของใบแจ้งหนี้ แล้วรวมยอด
    /// กองที่ 1: ปีก่อนหน้างวดนี้ · กองที่ 2: ปีเดียวกันแต่เดือนก่อนหน้า (เรียงตามเดือน)
    /// </summary>
    public static decimal PdfInvoiceDebtTotal(IReadOnlyList<DebtLine> debts, int invoiceYear, int invoiceMonth)
    {
        var olderYear = debts.Where(d => d.Year < invoiceYear).ToList();
        var sameYearPrior = debts
            .Where(d => d.Year == invoiceYear && d.Month < invoiceMonth)
            .OrderBy(d => d.Month)
            .ToList();

        return ForInvoicePartitioned(olderYear, sameYearPrior).DebtTotal;
    }

    /// <summary>getPdfInvoiceDebtSummary() — เหมือนข้างบนแต่คืนข้อความ 3 บรรทัดมาด้วย</summary>
    public static DebtSummaryResult PdfInvoiceDebtSummary(
        IReadOnlyList<DebtLine> debts, int invoiceYear, int invoiceMonth)
    {
        var olderYear = debts.Where(d => d.Year < invoiceYear).ToList();
        var sameYearPrior = debts
            .Where(d => d.Year == invoiceYear && d.Month < invoiceMonth)
            .OrderBy(d => d.Month)
            .ToList();

        return ForInvoicePartitioned(olderYear, sameYearPrior);
    }

    /// <summary>
    /// getPdfInvoiceGrandTotal() — รวมเป็นเงินทั้งสิ้นของใบแจ้งหนี้
    /// = (ค่าส่วนกลาง + ค่าเบี้ยปรับ + ค่าทวงถาม ของงวดนี้) + ยอดค้างจากงวดก่อน
    /// </summary>
    public static decimal PdfInvoiceGrandTotal(
        decimal? amountDue, decimal? fine, decimal? collectionFee,
        IReadOnlyList<DebtLine> debts, int invoiceYear, int invoiceMonth)
    {
        var subtotal = (amountDue ?? 0m) + (fine ?? 0m) + (collectionFee ?? 0m);
        return subtotal + PdfInvoiceDebtTotal(debts, invoiceYear, invoiceMonth);
    }

    /// <summary>
    /// sumDebtItems() — ยอดค้างรวมที่นับใบทวงถามแยกต่างหาก (ใช้ในเส้นทางที่ไม่ใช่ PDF ใบแจ้งหนี้)
    /// </summary>
    public static decimal SumDebtItems(
        IReadOnlyList<DebtLine> debts,
        IReadOnlyList<decimal> fineFees,
        IReadOnlyList<decimal> collectionFees) =>
        debts.Sum(d => d.Total) + fineFees.Sum() + collectionFees.Sum();

    /// <summary>
    /// buildDebtSummaryTexts() — สรุป 3 บรรทัดจาก payments + notices แยกชุด
    /// ต่างจาก ForInvoice ตรงที่ค่าเบี้ยปรับ/ค่าทวงถามมาจากตาราง gv_collection_notices
    /// </summary>
    public static IReadOnlyList<DebtTextLine> BuildDebtSummaryTexts(
        IReadOnlyList<DebtLine> debts,
        IReadOnlyList<decimal> fineFees,
        IReadOnlyList<decimal> collectionFees) =>
    [
        ToLine(BuildText("ค่าส่วนกลาง", debts.Select(d => d.AmountDue).ToList())),
        ToLine(BuildText("ค่าเบี้ยปรับ", fineFees, hideWhenZero: true)),
        ToLine(BuildText("ค่าทวงถาม", collectionFees, hideWhenZero: true)),
    ];
}
