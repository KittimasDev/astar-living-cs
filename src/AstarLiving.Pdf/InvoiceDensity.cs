using AstarLiving.Pdf.Models;

namespace AstarLiving.Pdf;

public enum InvoiceDensityPreset
{
    Normal,
    Compact,
    Tight,
}

/// <summary>ขนาดตัวอักษรและระยะห่างของหนึ่งระดับความหนาแน่น</summary>
public sealed record InvoiceDensityStyles
{
    public required float DefaultFontSize { get; init; }
    public required float DefaultLineHeight { get; init; }
    public required float HeaderTitleFs { get; init; }
    public required float HeaderSubtitleFs { get; init; }
    public required float HeaderOuterMarginTop { get; init; }
    public required float HeaderOuterMarginBottom { get; init; }
    public required float HeaderTitleColumnMarginTop { get; init; }
    public required float LogoFitW { get; init; }
    public required float LogoFitH { get; init; }
    public required float DividerVMargin { get; init; }
    public required float CustomerFs { get; init; }
    public required float CustomerMarginBottom { get; init; }
    public required float ItemsCellFs { get; init; }
    public required float ItemsCellLineHeight { get; init; }
    public required float ItemsDebtLabelFs { get; init; }
    public required float ItemsDebtRowFs { get; init; }
    public required float ItemsHeaderFs { get; init; }
    public required float ItemsSummaryFs { get; init; }
    public required float ItemsMarginTop { get; init; }
    public required float FooterNoteTitleFs { get; init; }
    public required float FooterNoteBodyFs { get; init; }
    public required float FooterNotePadding { get; init; }
    public required float FooterSignNameFs { get; init; }
    public required float FooterSignPosFs { get; init; }
    public required float FooterSignDateFs { get; init; }
    public required float FooterLineMarginTop { get; init; }
    public required float FooterSignStackMarginTop { get; init; }
    public required float FooterOuterMarginTop { get; init; }
    public required float FooterOuterMarginBottom { get; init; }
    public required float PaymentFs { get; init; }
}

/// <summary>
/// กลไกปรับความหนาแน่นของใบแจ้งหนี้ — พอร์ตจาก invoice-layout-density.util.ts
///
/// ใบแจ้งหนี้หนึ่งใบต้องพอดีครึ่งหน้ากระดาษ เพราะอีกครึ่งเป็นสำเนา
/// ระบบจึงประเมิน "ภาระความสูง" ของแต่ละใบก่อน แล้วเลือกระดับที่แน่นที่สุดในชุดมาใช้ทั้งเอกสาร
/// </summary>
public static class InvoiceDensity
{
    public static readonly InvoiceDensityStyles Normal = new()
    {
        DefaultFontSize = 14, DefaultLineHeight = 1.1f,
        HeaderTitleFs = 18, HeaderSubtitleFs = 13,
        HeaderOuterMarginTop = 1, HeaderOuterMarginBottom = 1, HeaderTitleColumnMarginTop = 1,
        LogoFitW = 55, LogoFitH = 40,
        DividerVMargin = 1,
        CustomerFs = 14, CustomerMarginBottom = 0,
        ItemsCellFs = 14, ItemsCellLineHeight = 0.8f,
        ItemsDebtLabelFs = 14, ItemsDebtRowFs = 11,
        ItemsHeaderFs = 14, ItemsSummaryFs = 14, ItemsMarginTop = 2,
        FooterNoteTitleFs = 12, FooterNoteBodyFs = 10, FooterNotePadding = 4,
        FooterSignNameFs = 14, FooterSignPosFs = 11, FooterSignDateFs = 12,
        FooterLineMarginTop = 8, FooterSignStackMarginTop = 3,
        FooterOuterMarginTop = 5, FooterOuterMarginBottom = 1,
        PaymentFs = 12,
    };

    public static readonly InvoiceDensityStyles Compact = Normal with
    {
        HeaderTitleFs = 17, HeaderSubtitleFs = 11,
        LogoFitW = 52, LogoFitH = 38,
        ItemsDebtRowFs = 10, ItemsMarginTop = 1,
        FooterNoteTitleFs = 11, FooterNoteBodyFs = 9, FooterNotePadding = 3,
        FooterSignNameFs = 13, FooterSignDateFs = 11,
        FooterLineMarginTop = 6, FooterSignStackMarginTop = 2,
        FooterOuterMarginTop = 3,
        PaymentFs = 11,
    };

    public static readonly InvoiceDensityStyles Tight = Normal with
    {
        DefaultLineHeight = 1.05f,
        HeaderTitleFs = 15, HeaderSubtitleFs = 10,
        LogoFitW = 48, LogoFitH = 35,
        DividerVMargin = 0.5f,
        CustomerFs = 10,
        ItemsCellFs = 13, ItemsDebtRowFs = 9, ItemsMarginTop = 0,
        FooterNoteTitleFs = 10, FooterNoteBodyFs = 8, FooterNotePadding = 2,
        FooterSignNameFs = 12, FooterSignPosFs = 10, FooterSignDateFs = 10,
        FooterLineMarginTop = 5, FooterSignStackMarginTop = 2,
        FooterOuterMarginTop = 2,
        PaymentFs = 10,
    };

    public static InvoiceDensityStyles GetStyles(InvoiceDensityPreset preset) => preset switch
    {
        InvoiceDensityPreset.Compact => Compact,
        InvoiceDensityPreset.Tight => Tight,
        _ => Normal,
    };

    /// <summary>pickTightestPreset() — เอกสารหนึ่งชุดใช้ระดับเดียวกันทั้งหมด</summary>
    public static InvoiceDensityPreset PickTightest(IEnumerable<InvoiceDensityPreset> presets)
    {
        var tightest = InvoiceDensityPreset.Normal;
        foreach (var preset in presets)
        {
            if (preset > tightest) tightest = preset;
        }

        return tightest;
    }

    /// <summary>
    /// estimateInvoiceHalfBurdenUnits() — ประเมินความสูงที่ใบหนึ่งใบต้องใช้ (หน่วยเป็น pt)
    ///
    /// ค่าคงที่ทั้งหมดมาจากระบบเดิม: หัวกระดาษ 50 + เส้นคั่น 5 + ข้อมูลลูกบ้าน 42
    /// + ท้ายกระดาษ 70 + ระยะขอบ 8 จากนั้นบวกความสูงของตารางและกล่องหมายเหตุ
    /// </summary>
    public static float EstimateInvoiceBurden(InvoiceDocumentModel invoice, ProjectInfo project)
    {
        var itemsLength = invoice.Items.Count;
        var remarkLines = EstimateRemarkLines(project.Remark);

        var pt = 50f + 5f + 42f + 70f + 8f;

        // แถวที่มีเสมอ: หัวตาราง + ป้ายยอดค้าง + 3 บรรทัดยอดค้าง = 5
        pt += (itemsLength + 5) * 14f;

        // บล็อกสรุปยอด 3 บรรทัด
        pt += 30f;

        pt += remarkLines * 9f;
        pt += AddressDensityBonus(invoice);

        return pt;
    }

    /// <summary>
    /// estimateAdvanceHalfBurdenUnits() — ใบแจ้งหนี้ล่วงหน้าไม่มีบล็อกยอดค้าง
    /// แต่มีบล็อกข้อมูลการชำระเงินแทน
    /// </summary>
    public static float EstimateAdvanceBurden(InvoiceDocumentModel invoice, ProjectInfo project)
    {
        var itemsLength = invoice.Items.Count;
        var remarkLines = EstimateRemarkLines(project.Remark);

        var pt = 50f + 5f + 42f + 70f + 8f;

        pt += (itemsLength + 4) * 13f;
        pt += 26f;
        pt += remarkLines * 9f;
        pt += AddressDensityBonus(invoice);

        return pt;
    }

    /// <summary>
    /// resolveInvoiceDensityPreset() — เทียบภาระกับพื้นที่ครึ่งหน้าที่มี
    /// งบขั้นต่ำ 160 pt กันกรณีหน้ากระดาษเล็กผิดปกติ
    /// </summary>
    public static InvoiceDensityPreset Resolve(float burdenUnits, float halfHeightPt)
    {
        var budget = Math.Max(160f, halfHeightPt - 6f);
        var ratio = burdenUnits / budget;

        if (ratio <= 1.0f) return InvoiceDensityPreset.Normal;
        if (ratio <= 1.1f) return InvoiceDensityPreset.Compact;
        return InvoiceDensityPreset.Tight;
    }

    /// <summary>ประเมินจำนวนบรรทัดของหมายเหตุ สูงสุด 3 บรรทัด</summary>
    private static int EstimateRemarkLines(string? remarkHtml)
    {
        var remark = PdfHtml.CleanToText(remarkHtml);
        if (remark.Length == 0) return 0;

        return Math.Min(3, Math.Max(1, (int)Math.Ceiling(remark.Length / 72.0)));
    }

    /// <summary>ชื่อและที่อยู่ที่ยาวมีโอกาสตัดบรรทัด จึงบวกความสูงเผื่อไว้ไม่เกิน 20 pt</summary>
    private static float AddressDensityBonus(InvoiceDocumentModel invoice)
    {
        var density = (invoice.Address.Length + invoice.CustomerName.Length) * 0.06f;
        return Math.Min(20f, density);
    }
}
