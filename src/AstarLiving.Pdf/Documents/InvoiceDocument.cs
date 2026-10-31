using AstarLiving.Domain.Billing;
using AstarLiving.Pdf.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AstarLiving.Pdf.Documents;

/// <summary>
/// ใบแจ้งหนี้ — พอร์ตจาก invoice-layout.helper.ts และ sections/ ทั้งหมด
///
/// หนึ่งหน้ากระดาษมีใบแจ้งหนี้ใบเดียว แต่พิมพ์สองครั้ง ครึ่งบนเป็นต้นฉบับ ครึ่งล่างเป็นสำเนา
/// คั่นด้วยเส้นสีเทากลางหน้า ความสูงของแต่ละครึ่งถูกตรึงไว้ จึงต้องมีกลไกปรับความหนาแน่น
/// (ดู InvoiceDensity) เพื่อให้เนื้อหาไม่ล้นออกนอกครึ่งของตัวเอง
/// </summary>
public class InvoiceDocument : IDocument
{
    /// <summary>A4 ในหน่วย point</summary>
    private const float PageWidth = 595.28f;
    private const float PageHeight = 841.89f;

    private const float MarginLeft = 18f;
    private const float MarginTop = 9f;
    private const float MarginRight = 18f;
    private const float MarginBottom = 16f;

    /// <summary>แถบกลางที่เส้นคั่นอยู่ ความสูงคงที่เพื่อให้เส้นอยู่กึ่งกลางหน้าพอดี</summary>
    private const float DividerBandHeight = 10f;

    private const string TextColor = "#4D4C4C";
    private const string HeaderFillColor = "#f2f2f2";

    private static readonly float ContentWidth = PageWidth - MarginLeft - MarginRight;
    private static readonly float ContentHeight = PageHeight - MarginTop - MarginBottom;
    private static readonly float HalfHeight = (ContentHeight - DividerBandHeight) / 2f;

    private readonly ProjectInfo _project;
    private readonly IReadOnlyList<InvoiceDocumentModel> _invoices;
    private readonly SignInfo _signInfo;
    private readonly byte[] _logo;
    private readonly InvoiceDensityStyles _styles;
    private readonly bool _isAdvance;

    public InvoiceDocument(
        ProjectInfo project,
        IReadOnlyList<InvoiceDocumentModel> invoices,
        SignInfo signInfo,
        byte[] logo,
        bool isAdvance = false)
    {
        _project = project;
        _invoices = invoices;
        _signInfo = signInfo;
        _logo = logo;
        _isAdvance = isAdvance;

        // ทุกใบในชุดเดียวกันใช้ระดับความหนาแน่นเดียวกัน โดยยึดใบที่แน่นที่สุด
        var presets = invoices.Select(inv => InvoiceDensity.Resolve(
            isAdvance
                ? InvoiceDensity.EstimateAdvanceBurden(inv, project)
                : InvoiceDensity.EstimateInvoiceBurden(inv, project),
            HalfHeight));

        _styles = InvoiceDensity.GetStyles(InvoiceDensity.PickTightest(presets));
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        foreach (var invoice in _invoices)
        {
            container.Page(page =>
            {
                page.Size(PageWidth, PageHeight, Unit.Point);
                page.MarginLeft(MarginLeft, Unit.Point);
                page.MarginTop(MarginTop, Unit.Point);
                page.MarginRight(MarginRight, Unit.Point);
                page.MarginBottom(MarginBottom, Unit.Point);

                page.DefaultTextStyle(text => text
                    .FontFamily(PdfEnvironment.FontFamily)
                    .FontSize(_styles.DefaultFontSize)
                    .LineHeight(_styles.DefaultLineHeight)
                    .FontColor(TextColor));

                page.Content().Column(column =>
                {
                    column.Item().Height(HalfHeight, Unit.Point).Element(c => ComposeHalf(c, invoice, "ต้นฉบับ"));
                    column.Item().Height(DividerBandHeight, Unit.Point).AlignMiddle()
                        .LineHorizontal(1f, Unit.Point).LineColor("#d3d3d3");
                    column.Item().Height(HalfHeight, Unit.Point).Element(c => ComposeHalf(c, invoice, "สำเนา"));
                });
            });
        }
    }

    /// <summary>ครึ่งหน้าหนึ่งชุด — ต้นฉบับและสำเนาใช้โครงเดียวกัน ต่างแค่ป้ายกำกับ</summary>
    private void ComposeHalf(IContainer container, InvoiceDocumentModel invoice, string copyLabel)
    {
        container.PaddingTop(5, Unit.Point).Column(column =>
        {
            column.Item().Element(c => ComposeHeader(c, copyLabel));

            column.Item()
                .PaddingVertical(_styles.DividerVMargin, Unit.Point)
                .LineHorizontal(0.6f, Unit.Point).LineColor(Colors.Black);

            column.Item().Element(c => ComposeCustomer(c, invoice));
            column.Item().Element(c => ComposeItems(c, invoice));

            if (_isAdvance)
            {
                column.Item().Element(c => ComposePaymentInfo(c, invoice));
            }
            else
            {
                column.Item().Element(c => ComposeSummary(c, invoice));
            }

            column.Item().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container, string copyLabel)
    {
        container
            .PaddingTop(_styles.HeaderOuterMarginTop, Unit.Point)
            .PaddingBottom(_styles.HeaderOuterMarginBottom, Unit.Point)
            .Row(row =>
            {
                row.ConstantItem(_styles.LogoFitW, Unit.Point)
                    .Height(_styles.LogoFitH, Unit.Point)
                    .AlignLeft().AlignMiddle()
                    .Image(_logo).FitArea();

                row.RelativeItem().Column(column =>
                {
                    column.Item().Text($"นิติบุคคลหมู่บ้านจัดสรร {Safe(_project.ProjectName)}")
                        .Bold().FontSize(_styles.CustomerFs);

                    column.Item().Text(
                        $"{Safe(_project.ProjectAddress)} โทร. {Safe(_project.ProjectTel)}");
                });

                row.ConstantItem(140, Unit.Point)
                    .PaddingTop(_styles.HeaderTitleColumnMarginTop, Unit.Point)
                    .PaddingLeft(14, Unit.Point)
                    .Column(column =>
                    {
                        column.Item().AlignCenter().Text(_isAdvance ? "ใบแจ้งหนี้" : "ใบแจ้งหนี้")
                            .Bold().FontSize(_styles.HeaderTitleFs);
                        column.Item().AlignCenter().Text($"({copyLabel})")
                            .Bold().FontSize(_styles.HeaderSubtitleFs);
                    });
            });
    }

    private void ComposeCustomer(IContainer container, InvoiceDocumentModel invoice)
    {
        container.PaddingBottom(_styles.CustomerMarginBottom, Unit.Point).Row(row =>
        {
            row.RelativeItem(70).Column(column =>
            {
                CustomerLine(column, "ชื่อ / Name:", Safe(invoice.CustomerName), 70);
                CustomerLine(column, "บ้านเลขที่:", Safe(invoice.HouseNo), 70);
                CustomerLine(column, "ที่อยู่ / Address:", Safe(invoice.Address), 70);
            });

            row.RelativeItem(30).Column(column =>
            {
                CustomerLine(column, "เลขที่ / No.:", Safe(invoice.InvoiceNo), 85, alignRight: true);
                CustomerLine(column, "วันที่ / Date:", Safe(invoice.Date), 85, alignRight: true);
                CustomerLine(column, "เดือน / Month:", Safe(invoice.Month), 85, alignRight: true);
            });
        });
    }

    private void CustomerLine(
        ColumnDescriptor column, string label, string value, float labelWidth, bool alignRight = false)
    {
        column.Item().Row(row =>
        {
            row.ConstantItem(labelWidth, Unit.Point).Text(label).Bold().FontSize(_styles.CustomerFs);

            var cell = row.RelativeItem();
            if (alignRight) cell = cell.AlignRight();

            cell.Text(value).FontSize(_styles.CustomerFs);
        });
    }

    /// <summary>
    /// ตารางรายการ
    ///
    /// แถวที่ไม่มีชื่อหรือยอดเป็นศูนย์ถูกพิมพ์เป็นแถวว่าง ไม่ใช่ถูกตัดทิ้ง
    /// และบล็อกยอดค้างมี 3 บรรทัดเสมอ ทั้งสองอย่างทำให้ความสูงของตารางคงที่
    /// ซึ่งจำเป็นเพราะครึ่งหน้าถูกตรึงความสูงไว้
    /// </summary>
    private void ComposeItems(IContainer container, InvoiceDocumentModel invoice)
    {
        container.PaddingTop(_styles.ItemsMarginTop, Unit.Point).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(60, Unit.Point);
                columns.ConstantColumn(80, Unit.Point);
                columns.ConstantColumn(90, Unit.Point);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "รายการ", left: true);
                HeaderCell(header.Cell(), "จำนวนหน่วย");
                HeaderCell(header.Cell(), "ราคาต่อหน่วย");
                HeaderCell(header.Cell(), "จำนวนเงิน");
            });

            foreach (var item in invoice.Items)
            {
                if (item.IsBlank)
                {
                    for (var i = 0; i < 4; i++) BodyCell(table.Cell(), " ");
                    continue;
                }

                BodyCell(table.Cell(), item.Name ?? " ", left: true);
                BodyCell(table.Cell(), item.Unit is null ? " " : MoneyFormat.Fixed2(item.Unit));
                BodyCell(table.Cell(), item.Price is null ? " " : MoneyFormat.Fixed2(item.Price));
                BodyCell(table.Cell(), item.Total is null ? " " : MoneyFormat.Fixed2(item.Total));
            }

            // ป้ายยอดค้างชำระ แสดงเฉพาะตอนมียอดจริง แต่แถวยังถูกจองไว้เสมอ
            if (invoice.HasDebtRows)
            {
                table.Cell().ColumnSpan(4).Text("ยอดค้างชำระ:")
                    .Bold().Underline().FontSize(_styles.ItemsDebtLabelFs);
            }
            else
            {
                table.Cell().ColumnSpan(4).Text(" ").Bold();
            }

            // บล็อกยอดค้างมี 3 บรรทัดเสมอ เติมบรรทัดว่างให้ครบถ้าข้อมูลมาไม่ครบ
            var debtLines = invoice.DebtItems.Take(3).ToList();
            while (debtLines.Count < 3) debtLines.Add(" ");

            foreach (var line in debtLines)
            {
                var text = string.IsNullOrWhiteSpace(line) ? " " : line;
                table.Cell().ColumnSpan(4).PaddingHorizontal(1, Unit.Point)
                    .Text(text).FontSize(_styles.ItemsDebtRowFs);
            }
        });
    }

    private void ComposeSummary(IContainer container, InvoiceDocumentModel invoice)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(80, Unit.Point);
                columns.ConstantColumn(80, Unit.Point);
                columns.ConstantColumn(90, Unit.Point);
            });

            SummaryRow(table, "รวมเงิน", invoice.Subtotal);
            SummaryRow(table, "ยอดค้างชำระ", invoice.Debt);
            SummaryRow(table, "รวมเป็นเงินทั้งสิ้น", invoice.Total);
        });
    }

    private void SummaryRow(TableDescriptor table, string label, decimal value)
    {
        var fontSize = Math.Max(8f, _styles.ItemsSummaryFs);

        table.Cell().Text(string.Empty);
        table.Cell().AlignRight().Text(label).Bold().FontSize(fontSize).LineHeight(0.85f);
        table.Cell().Text(string.Empty);
        table.Cell().AlignRight().Text(MoneyFormat.Fixed2(value)).Bold().FontSize(fontSize).LineHeight(0.85f);
    }

    /// <summary>บล็อกข้อมูลการชำระเงิน ใช้เฉพาะใบแจ้งหนี้ล่วงหน้าแทนบล็อกสรุปยอด</summary>
    private void ComposePaymentInfo(IContainer container, InvoiceDocumentModel invoice)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"วิธีการชำระเงิน: {Safe(invoice.Payment?.Name)}")
                    .FontSize(_styles.PaymentFs);
                row.RelativeItem().AlignRight()
                    .Text($"วันที่ชำระ: {Safe(invoice.Payment?.IssueDate)}")
                    .FontSize(_styles.PaymentFs);
            });

            if (!string.IsNullOrWhiteSpace(invoice.Payment?.BankName)
                || !string.IsNullOrWhiteSpace(invoice.Payment?.ChequeNo))
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"ธนาคาร: {Safe(invoice.Payment?.BankName)}")
                        .FontSize(_styles.PaymentFs);
                    row.RelativeItem().AlignRight()
                        .Text($"เลขที่เช็ค: {Safe(invoice.Payment?.ChequeNo)}")
                        .FontSize(_styles.PaymentFs);
                });
            }

            column.Item().PaddingTop(2, Unit.Point).Row(row =>
            {
                row.RelativeItem();
                row.ConstantItem(90, Unit.Point).AlignRight()
                    .Text(MoneyFormat.Fixed2(invoice.Total))
                    .Bold().FontSize(_styles.ItemsSummaryFs);
            });
        });
    }

    /// <summary>ท้ายกระดาษ: กล่องหมายเหตุทางซ้าย และช่องลงนามทางขวา</summary>
    private void ComposeFooter(IContainer container)
    {
        container
            .PaddingTop(_styles.FooterOuterMarginTop, Unit.Point)
            .PaddingBottom(_styles.FooterOuterMarginBottom, Unit.Point)
            .Row(row =>
            {
                row.ConstantItem(298, Unit.Point)
                    .Border(0.7f).BorderColor("#CFCFCF")
                    .Padding(_styles.FooterNotePadding, Unit.Point)
                    .Column(column =>
                    {
                        column.Item().Text("หมายเหตุ :")
                            .Bold().FontSize(_styles.FooterNoteTitleFs).FontColor("#4B4B4B");

                        var lines = PdfHtml.ToFooterLines(_project.Remark);
                        foreach (var line in lines)
                        {
                            column.Item().Text(line.Length == 0 ? " " : line)
                                .FontSize(_styles.FooterNoteBodyFs).FontColor("#6e6e6e");
                        }
                    });

                row.RelativeItem().Column(column =>
                {
                    column.Item()
                        .PaddingTop(_styles.FooterLineMarginTop, Unit.Point)
                        .AlignCenter().Width(140, Unit.Point)
                        .LineHorizontal(1f, Unit.Point);

                    column.Item()
                        .PaddingTop(_styles.FooterSignStackMarginTop, Unit.Point)
                        .AlignCenter().Text(_signInfo.Name)
                        .Bold().FontSize(_styles.FooterSignNameFs);

                    column.Item().AlignCenter().Text(_signInfo.Position)
                        .FontSize(_styles.FooterSignPosFs);

                    column.Item().PaddingTop(3, Unit.Point).PaddingLeft(35, Unit.Point).Row(dateRow =>
                    {
                        dateRow.ConstantItem(30, Unit.Point).AlignRight()
                            .Text("วันที่").FontSize(_styles.FooterSignDateFs);

                        dateRow.ConstantItem(100, Unit.Point).AlignBottom()
                            .PaddingBottom(2, Unit.Point)
                            .LineHorizontal(0.8f, Unit.Point).LineColor(Colors.Black);
                    });
                });
            });
    }

    private void HeaderCell(IContainer cell, string text, bool left = false)
    {
        var container = cell.Background(HeaderFillColor).PaddingHorizontal(1, Unit.Point);
        var aligned = left ? container : container.AlignRight();
        aligned.Text(text).Bold().FontSize(_styles.ItemsHeaderFs);
    }

    private void BodyCell(IContainer cell, string text, bool left = false)
    {
        var container = cell.PaddingHorizontal(1, Unit.Point);
        var aligned = left ? container : container.AlignRight();
        aligned.Text(text).FontSize(_styles.ItemsCellFs).LineHeight(_styles.ItemsCellLineHeight);
    }

    /// <summary>safe() ของเดิม — กันค่า null ไม่ให้กลายเป็นคำว่า undefined บนกระดาษ</summary>
    internal static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value;
}
