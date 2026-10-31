using AstarLiving.Domain.Billing;
using AstarLiving.Pdf.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AstarLiving.Pdf.Documents;

/// <summary>
/// ใบทวงถาม — พอร์ตจาก collection-layout.helper.ts
///
/// เนื้อความมาจากเทมเพลตที่ผู้ใช้แก้ได้เองในหน้าตั้งค่า (เก็บเป็น HTML)
/// โดยผ่านการแทนที่ token {resident_name} และ {amount} มาแล้วจากชั้นบริการ
///
/// ใช้โครงครึ่งหน้าเหมือนเอกสารอื่น คั่นด้วยเส้นประสีดำ
/// </summary>
public class CollectionDocument(
    ProjectInfo project,
    IReadOnlyList<CollectionDocumentModel> notices,
    SignInfo signInfo,
    byte[] logo) : IDocument
{
    private const float PageWidth = 595.28f;
    private const float PageHeight = 841.89f;
    private const float Margin = 20f;
    private const float DividerBandHeight = 10f;

    private const string TextColor = "#4D4C4C";

    private static readonly float ContentWidth = PageWidth - (Margin * 2);
    private static readonly float ContentHeight = PageHeight - (Margin * 2);
    private static readonly float HalfHeight = (ContentHeight - DividerBandHeight) / 2f;

    private readonly InvoiceDensityStyles _styles = InvoiceDensity.Normal;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        foreach (var notice in notices)
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
                        .Element(c => ComposeHalf(c, notice, "ต้นฉบับ"));

                    // เส้นประกลางหน้าไว้ให้ฉีกแยกสำเนา
                    column.Item().Height(DividerBandHeight, Unit.Point)
                        .Element(c => DashedDivider.Draw(c, ContentWidth));

                    column.Item().Height(HalfHeight, Unit.Point)
                        .Element(c => ComposeHalf(c, notice, "สำเนา"));
                });
            });
        }
    }

    private void ComposeHalf(IContainer container, CollectionDocumentModel notice, string copyLabel)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ComposeHeader(c, copyLabel, notice));

            column.Item()
                .PaddingVertical(_styles.DividerVMargin, Unit.Point)
                .LineHorizontal(0.6f, Unit.Point).LineColor(Colors.Black);

            column.Item().Element(c => ComposeAddressee(c, notice));
            column.Item().Element(c => ComposeBody(c, notice));
            column.Item().Element(c => ComposeItems(c, notice));
            column.Item().Element(c => ComposeClosing(c, notice));
        });
    }

    private void ComposeHeader(IContainer container, string copyLabel, CollectionDocumentModel notice)
    {
        container.Row(row =>
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

            row.ConstantItem(140, Unit.Point).PaddingLeft(14, Unit.Point).Column(column =>
            {
                column.Item().AlignCenter().Text("ใบทวงถาม")
                    .Bold().FontSize(_styles.HeaderTitleFs);
                column.Item().AlignCenter().Text($"({copyLabel})")
                    .Bold().FontSize(_styles.HeaderSubtitleFs);
                column.Item().AlignCenter().Text($"หน้า {InvoiceDocument.Safe(notice.Page)}")
                    .FontSize(_styles.FooterSignDateFs);
            });
        });
    }

    private void ComposeAddressee(IContainer container, CollectionDocumentModel notice)
    {
        container.PaddingTop(2, Unit.Point).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"เรื่อง  {InvoiceDocument.Safe(notice.Subject)}").Bold();
                row.AutoItem().Text($"วันที่  {InvoiceDocument.Safe(notice.Date)}");
            });

            column.Item().Text($"เรียน  {InvoiceDocument.Safe(notice.CustomerName)}  บ้านเลขที่ {InvoiceDocument.Safe(notice.HouseNo)}");
        });
    }

    /// <summary>ย่อหน้าเปิดและย่อหน้าเสริมจากเทมเพลต</summary>
    private void ComposeBody(IContainer container, CollectionDocumentModel notice)
    {
        container.PaddingTop(3, Unit.Point).Column(column =>
        {
            foreach (var line in PdfHtml.ToLines(notice.IntroductionDetail))
            {
                column.Item().Text(line.Length == 0 ? " " : line);
            }

            foreach (var line in PdfHtml.ToLines(notice.AdditionalDetail))
            {
                column.Item().Text(line.Length == 0 ? " " : line);
            }
        });
    }

    private void ComposeItems(IContainer container, CollectionDocumentModel notice)
    {
        container.PaddingTop(3, Unit.Point).Table(table =>
        {
            // ['auto', 110, '*', 90, 90] ตามระบบเดิม
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(58, Unit.Point);
                columns.ConstantColumn(110, Unit.Point);
                columns.RelativeColumn();
                columns.ConstantColumn(90, Unit.Point);
                columns.ConstantColumn(90, Unit.Point);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "วันที่", Align.Center);
                HeaderCell(header.Cell(), "เลขที่ใบแจ้งหนี้", Align.Center);
                HeaderCell(header.Cell(), "รายการ", Align.Left);
                HeaderCell(header.Cell(), "จำนวนเงิน", Align.Right);
                HeaderCell(header.Cell(), "คงเหลือ", Align.Right);
            });

            // วันที่และเลขที่ใบแจ้งหนี้แสดงเฉพาะแถวแรกของใบ ที่เหลือเว้นว่าง
            // เพื่อให้อ่านเป็นกลุ่มเดียวกันเหมือนระบบเดิม
            for (var i = 0; i < notice.Items.Count; i++)
            {
                var item = notice.Items[i];
                var first = i == 0;

                BodyCell(table.Cell(), first ? item.Date : string.Empty, Align.Center);
                BodyCell(table.Cell(), first ? item.InvoiceNo : string.Empty, Align.Center);
                BodyCell(table.Cell(), item.Name, Align.Left);
                BodyCell(table.Cell(), MoneyFormat.Fixed2(item.Amount), Align.Right);
                BodyCell(table.Cell(), MoneyFormat.Fixed2(item.Remain), Align.Right);
            }

            // แถวคั่นก่อนบรรทัดรวม ระบบเดิมเว้นระยะไว้ชัดเจน
            table.Cell().ColumnSpan(5).PaddingTop(16, Unit.Point).PaddingBottom(6, Unit.Point)
                .Text(string.Empty);

            table.Cell().Text(string.Empty);
            table.Cell().Text(string.Empty);
            table.Cell().AlignRight().Text("รวมเงินทั้งสิ้น").Bold();
            table.Cell().Text(string.Empty);
            table.Cell().AlignRight().Text(MoneyFormat.Fixed2(notice.Total)).Bold();
        });
    }

    private enum Align { Left, Center, Right }

    private static IContainer Aligned(IContainer cell, Align align) => align switch
    {
        Align.Right => cell.AlignRight(),
        Align.Center => cell.AlignCenter(),
        _ => cell.AlignLeft(),
    };

    private static void HeaderCell(IContainer cell, string text, Align align) =>
        Aligned(cell.Background("#f2f2f2").PaddingHorizontal(4, Unit.Point)
                    .PaddingVertical(2, Unit.Point), align)
            .Text(text).Bold();

    private static void BodyCell(IContainer cell, string text, Align align) =>
        Aligned(cell.PaddingHorizontal(4, Unit.Point).PaddingVertical(2, Unit.Point), align)
            .Text(text);

    /// <summary>ข้อความปิดท้าย คำลงท้าย และผู้ลงนาม</summary>
    private void ComposeClosing(IContainer container, CollectionDocumentModel notice)
    {
        container.PaddingTop(4, Unit.Point).Column(column =>
        {
            foreach (var line in PdfHtml.ToLines(notice.ClosureText))
            {
                column.Item().Text(line.Length == 0 ? " " : line);
            }

            column.Item().PaddingTop(4, Unit.Point).AlignRight()
                .Text(InvoiceDocument.Safe(notice.ClosingWord));

            column.Item().PaddingTop(2, Unit.Point).AlignRight()
                .Text(signInfo.Name).Bold().FontSize(_styles.FooterSignNameFs);

            column.Item().AlignRight()
                .Text(signInfo.Position).FontSize(_styles.FooterSignPosFs);

            if (!string.IsNullOrWhiteSpace(notice.TemplateRemark))
            {
                column.Item().PaddingTop(3, Unit.Point).Column(remark =>
                {
                    foreach (var line in PdfHtml.ToFooterLines(notice.TemplateRemark))
                    {
                        remark.Item().Text(line.Length == 0 ? " " : line)
                            .FontSize(_styles.FooterNoteBodyFs).FontColor("#6e6e6e");
                    }
                });
            }
        });
    }
}
