using AstarLiving.Domain.Billing;
using AstarLiving.Pdf.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AstarLiving.Pdf.Documents;

/// <summary>
/// ใบเสร็จรับเงิน — พอร์ตจาก receipt-layout.helper.ts
///
/// ใช้โครงครึ่งหน้าเหมือนใบแจ้งหนี้ แต่ระยะขอบเป็น 20 รอบด้าน และเส้นคั่นกลางเป็นเส้นประ
/// ตารางรายการเติมแถวว่างให้เสมอ เพื่อให้ความสูงคงที่ไม่ว่าจะมีกี่รายการ
///
/// เอกสารนี้ใช้กับใบรับเงินอื่น ๆ ด้วย แต่ไม่ใช่แค่เปลี่ยนชื่อหัวเอกสาร
/// ระบบเดิมแยกสองชนิดนี้ออกจากกัน และต่างกันสามจุดที่เห็นได้บนกระดาษ
///   1. ชื่อเอกสาร — "ใบเสร็จรับเงิน" กับ "ใบรับเงินอื่น ๆ"
///   2. ใบรับเงินอื่น ๆ **ไม่มี**บรรทัด "เดือน / Month:"
///   3. ขนาดตัวอักษรส่วนชำระเงิน — ใบเสร็จ 12 ใบรับเงินอื่น ๆ 14
/// </summary>
public class ReceiptDocument(
    ProjectInfo project,
    IReadOnlyList<ReceiptDocumentModel> receipts,
    SignInfo signInfo,
    byte[] logo,
    string title = ReceiptDocument.ReceiptTitle) : IDocument
{
    /// <summary>ชื่อเอกสารของใบเสร็จค่าส่วนกลาง</summary>
    public const string ReceiptTitle = "ใบเสร็จรับเงิน";

    /// <summary>ชื่อเอกสารของใบรับเงินอื่น ๆ — มีช่องว่างหน้า ๆ ตามของเดิม</summary>
    public const string RevenueTitle = "ใบรับเงินอื่น ๆ";

    /// <summary>true เมื่อเป็นใบรับเงินอื่น ๆ ซึ่งจัดวางต่างจากใบเสร็จปกติสามจุด</summary>
    private readonly bool _isRevenue = title == RevenueTitle;

    private const float PageWidth = 595.28f;
    private const float PageHeight = 841.89f;
    private const float Margin = 20f;
    private const float DividerBandHeight = 10f;

    /// <summary>จำนวนแถวรายการขั้นต่ำที่ตารางต้องมี รวมแถวว่างที่เติมเข้าไป</summary>
    private const int MinimumItemRows = 3;

    /// <summary>แถวว่างที่คั่นระหว่างรายการกับบรรทัดยอดรวม</summary>
    private const int SpacerRows = 3;

    private const string TextColor = "#4D4C4C";
    private const string HeaderFillColor = "#f2f2f2";

    private static readonly float ContentWidth = PageWidth - (Margin * 2);
    private static readonly float ContentHeight = PageHeight - (Margin * 2);
    private static readonly float HalfHeight = (ContentHeight - DividerBandHeight) / 2f;

    private readonly InvoiceDensityStyles _styles = InvoiceDensity.Normal;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        foreach (var receipt in receipts)
        {
            container.Page(page =>
            {
                page.Size(PageWidth, PageHeight, Unit.Point);
                page.Margin(Margin, Unit.Point);

                page.DefaultTextStyle(text => text
                    .FontFamily(PdfEnvironment.FontFamily)
                    .FontSize(_styles.DefaultFontSize)
                    .LineHeight(_styles.DefaultLineHeight)
                    .FontColor(TextColor));

                page.Content().Column(column =>
                {
                    column.Item().Height(HalfHeight, Unit.Point)
                        .Element(c => ComposeHalf(c, receipt, "ต้นฉบับ"));

                    // เส้นประกลางหน้าไว้ให้ฉีกแยกสำเนา
                    column.Item().Height(DividerBandHeight, Unit.Point)
                        .Element(c => DashedDivider.Draw(c, ContentWidth));

                    column.Item().Height(HalfHeight, Unit.Point)
                        .Element(c => ComposeHalf(c, receipt, "สำเนา"));
                });
            });
        }
    }

    private void ComposeHalf(IContainer container, ReceiptDocumentModel receipt, string copyLabel)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ComposeHeader(c, copyLabel));

            column.Item()
                .PaddingVertical(_styles.DividerVMargin, Unit.Point)
                .LineHorizontal(0.6f, Unit.Point).LineColor(Colors.Black);

            column.Item().Element(c => ComposeCustomer(c, receipt));
            column.Item().Element(c => ComposeItems(c, receipt));
            column.Item().Element(c => ComposePaymentInfo(c, receipt));
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
                    .Image(logo).FitArea();

                row.RelativeItem().Column(column =>
                {
                    column.Item().Text($"นิติบุคคลหมู่บ้านจัดสรร {InvoiceDocument.Safe(project.ProjectName)}")
                        .Bold().FontSize(_styles.CustomerFs);

                    column.Item().Text(
                        $"{InvoiceDocument.Safe(project.ProjectAddress)} โทร. {InvoiceDocument.Safe(project.ProjectTel)}");
                });

                row.ConstantItem(140, Unit.Point)
                    .PaddingLeft(14, Unit.Point)
                    .Column(column =>
                    {
                        column.Item().AlignCenter().Text(title)
                            .Bold().FontSize(_styles.HeaderTitleFs);
                        column.Item().AlignCenter().Text($"({copyLabel})")
                            .Bold().FontSize(_styles.HeaderSubtitleFs);
                    });
            });
    }

    private void ComposeCustomer(IContainer container, ReceiptDocumentModel receipt)
    {
        container.Row(row =>
        {
            row.RelativeItem(70).Column(column =>
            {
                Line(column, "ชื่อ / Name:", InvoiceDocument.Safe(receipt.CustomerName), 70);
                Line(column, "บ้านเลขที่:", InvoiceDocument.Safe(receipt.HouseNo), 70);
                Line(column, "ที่อยู่ / Address:", InvoiceDocument.Safe(receipt.Address), 70);
            });

            row.RelativeItem(30).Column(column =>
            {
                Line(column, "เลขที่ / No.:", InvoiceDocument.Safe(receipt.ReceiptNo), 85, alignRight: true);
                Line(column, "วันที่ / Date:", InvoiceDocument.Safe(receipt.Date), 85, alignRight: true);
                // ใบรับเงินอื่น ๆ ไม่มีบรรทัดนี้ ไม่ใช่มีแล้วเว้นว่าง
                if (!_isRevenue)
                {
                    Line(column, "เดือน / Month:", InvoiceDocument.Safe(receipt.Month), 85, alignRight: true);
                }
            });
        });
    }

    private void Line(
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

    private void ComposeItems(IContainer container, ReceiptDocumentModel receipt)
    {
        container.PaddingTop(_styles.ItemsMarginTop, Unit.Point).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                // ['*', 80, 90, 90] ตามระบบเดิม
                // คอลัมน์ที่สองต้องกว้าง 80 ไม่ใช่ 60 ไม่อย่างนั้น
                // ข้อความ "รวมเป็นเงินทั้งสิ้น" จะถูกตัดเป็นสองบรรทัด
                columns.RelativeColumn();
                columns.ConstantColumn(80, Unit.Point);
                columns.ConstantColumn(90, Unit.Point);
                columns.ConstantColumn(90, Unit.Point);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "รายการ", left: true);
                HeaderCell(header.Cell(), "จำนวนหน่วย");
                HeaderCell(header.Cell(), "ราคาต่อหน่วย");
                HeaderCell(header.Cell(), "จำนวนเงิน");
            });

            foreach (var item in receipt.Items)
            {
                var hasValue = (item.Total ?? 0m) != 0m;

                BodyCell(table.Cell(), hasValue ? item.Name ?? " " : " ", left: true);
                BodyCell(table.Cell(), hasValue && item.Unit is not null ? MoneyFormat.Fixed2(item.Unit) : " ");
                BodyCell(table.Cell(), hasValue && item.Price is not null ? MoneyFormat.Fixed2(item.Price) : " ");
                BodyCell(table.Cell(), hasValue ? MoneyFormat.Fixed2(item.Total) : " ");
            }

            // เติมแถวว่างให้ตารางสูงเท่ากันเสมอ ไม่ว่ารายการจะมีกี่บรรทัด
            var padding = Math.Max(0, MinimumItemRows - receipt.Items.Count) + SpacerRows;
            for (var i = 0; i < padding; i++)
            {
                for (var c = 0; c < 4; c++) BodyCell(table.Cell(), " ");
            }

            table.Cell().Text(string.Empty);
            table.Cell().AlignRight().Text("รวมเป็นเงินทั้งสิ้น").Bold();
            table.Cell().Text(string.Empty);
            table.Cell().AlignRight().Text(MoneyFormat.Fixed2(receipt.Total)).Bold();
        });
    }

    /// <summary>
    /// ขนาดตัวอักษรของส่วนชำระเงิน
    /// ระบบเดิมใบเสร็จดึงค่าจาก preset มาได้ 12 ส่วนใบรับเงินอื่น ๆ ไม่ได้ตั้ง จึงเป็น 14
    /// </summary>
    private float PaymentFontSize => _isRevenue ? 14f : _styles.PaymentFs;

    /// <summary>ชำระเงินโดย / วันที่ชำระเงิน — หมายเหตุถูกต่อท้ายวันที่ในวงเล็บ</summary>
    private void ComposePaymentInfo(IContainer container, ReceiptDocumentModel receipt)
    {
        var payment = receipt.Payment;

        var method = string.Join(' ', new[] { payment?.Name, payment?.BankName, payment?.ChequeNo }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.AutoItem().Text("ชำระเงินโดย:").Bold().FontSize(PaymentFontSize);
                row.RelativeItem().PaddingLeft(4, Unit.Point)
                    .Text(InvoiceDocument.Safe(method)).FontSize(PaymentFontSize);
            });

            column.Item().Row(row =>
            {
                row.AutoItem().Text("วันที่ชำระเงิน:").Bold().FontSize(PaymentFontSize);
                row.RelativeItem().PaddingLeft(4, Unit.Point)
                    .Text(InvoiceDocument.Safe(payment?.IssueDate)).FontSize(PaymentFontSize);
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container
            .PaddingTop(_styles.FooterOuterMarginTop, Unit.Point)
            .Row(row =>
            {
                row.ConstantItem(298, Unit.Point)
                    .Border(0.7f).BorderColor("#CFCFCF")
                    .Padding(_styles.FooterNotePadding, Unit.Point)
                    .Column(column =>
                    {
                        column.Item().Text("หมายเหตุ :")
                            .Bold().FontSize(_styles.FooterNoteTitleFs).FontColor("#4B4B4B");

                        // ใบเสร็จใช้หมายเหตุของตัวเอง ถ้าไม่มีจึงถอยไปใช้หมายเหตุกลาง
                        var source = string.IsNullOrWhiteSpace(project.ReceiptNote)
                            ? project.Remark
                            : project.ReceiptNote;

                        foreach (var line in PdfHtml.ToFooterLines(source))
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
                        .AlignCenter().Text(signInfo.Name)
                        .Bold().FontSize(_styles.FooterSignNameFs);

                    column.Item().AlignCenter().Text(signInfo.Position)
                        .FontSize(_styles.FooterSignPosFs);
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
}
