using AstarLiving.Pdf.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AstarLiving.Pdf.Documents;

/// <summary>
/// ตารางรายงานสำหรับพิมพ์ — พอร์ตจากส่วน export PDF ใน ReportContent.tsx
///
/// ระบบเดิมสร้างไฟล์นี้ที่ฝั่งเบราว์เซอร์ด้วย jsPDF + jspdf-autotable
/// ไม่ได้สร้างที่เซิร์ฟเวอร์เหมือนใบแจ้งหนี้ ระบบใหม่ย้ายมาสร้างที่เซิร์ฟเวอร์ทั้งหมด
/// เพื่อให้ใช้ฟอนต์ชุดเดียวกันและไม่ต้องฝังเครื่องมือสร้าง PDF ไว้ในโปรแกรมหน้าจอ
///
/// ค่าทุกตัวยกมาจากของเดิมตรง ๆ: ขอบกระดาษ สีหัวตาราง สีแถวสลับ ขนาดตัวอักษร
/// และตำแหน่งข้อความท้ายกระดาษ เพื่อให้พิมพ์ออกมาแล้วหน้าตาเหมือนเดิม
/// </summary>
public sealed class ReportDocument(ReportDocumentModel model) : IDocument
{
    // ─── ค่าจาก autoTable ของเดิม ──────────────────────────────────────────
    private const float BodyFontSize = 12;
    private const float TitleFontSize = 16;
    private const float SubtitleFontSize = 14;
    private const float FooterFontSize = 12;

    /// <summary>
    /// margin ของเดิม: { top: 100, left: 10, right: 10, bottom: 60 }
    ///
    /// ของเดิมวางหัวกระดาษด้วยพิกัดตายตัวที่ y = 40, 60, 80 แล้วให้ตารางเริ่มที่ 100
    /// ส่วน QuestPDF วางส่วนหัวไว้ "ใต้" ขอบบน ถ้าตั้งขอบบนเป็น 100 ตรง ๆ
    /// หัวกระดาษจะไปเริ่มที่ 100 แล้วตารางถูกดันลงไปอีก หน้าหนึ่งจะใส่ข้อมูลได้น้อยลง
    ///
    /// จึงตั้งขอบบนไว้ที่ 35 เพื่อให้หัวกระดาษอยู่ช่วง 35-100 และตารางเริ่มที่ราว 100
    /// ซึ่งตรงกับของเดิม
    /// </summary>
    private const float MarginX = 10;
    private const float MarginTop = 35;
    private const float MarginBottom = 60;

    /// <summary>headStyles.cellPadding { top: 8, bottom: 8 }</summary>
    private const float HeadCellPaddingY = 8;

    private const float BodyCellPaddingY = 4;
    private const float CellPaddingX = 4;

    private static readonly Color HeadFill = Color.FromRGB(240, 249, 255);
    private static readonly Color HeadText = Color.FromRGB(0, 84, 165);
    private static readonly Color BodyText = Color.FromRGB(116, 116, 116);
    private static readonly Color LineColor = Color.FromRGB(220, 220, 220);
    private static readonly Color AlternateRowFill = Color.FromRGB(245, 247, 249);
    private static readonly Color FooterText = Color.FromRGB(100, 100, 100);

    private readonly ReportDocumentModel _model = model;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginLeft(MarginX);
            page.MarginRight(MarginX);
            page.MarginTop(MarginTop);
            page.MarginBottom(MarginBottom);
            page.DefaultTextStyle(x => x.FontFamily(PdfEnvironment.FontFamily).FontSize(BodyFontSize));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeTable);
            page.Footer().Element(ComposeFooter);
        });
    }

    /// <summary>
    /// สามบรรทัดกึ่งกลาง: ชื่อโครงการ / ชื่อรายงานพร้อมปี / วันที่ออกรายงาน
    ///
    /// ของเดิมวางด้วยพิกัดตายตัวที่ y = 40, 60, 80 ซึ่งอยู่เหนือขอบบนของตาราง (100)
    /// ตรงนี้ใช้ส่วนหัวของหน้าแทน ผลลัพธ์ที่ได้อยู่ในตำแหน่งเดียวกัน
    /// </summary>
    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(10).Column(column =>
        {
            column.Item().AlignCenter().Text(_model.ProjectName)
                .FontSize(TitleFontSize).SemiBold();

            column.Item().AlignCenter().Text(_model.ReportTitle)
                .FontSize(SubtitleFontSize);

            column.Item().AlignCenter().Text(_model.AsOfDate)
                .FontSize(SubtitleFontSize);
        });
    }

    private void ComposeTable(IContainer container)
    {
        if (_model.Columns.Count == 0)
        {
            container.Text("ไม่มีข้อมูล").FontSize(BodyFontSize).FontColor(BodyText);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var column in _model.Columns)
                {
                    if (column.RelativeWidth > 0) columns.RelativeColumn(column.RelativeWidth);
                    else columns.RelativeColumn();
                }
            });

            table.Header(header =>
            {
                foreach (var column in _model.Columns)
                {
                    header.Cell()
                        .Background(HeadFill)
                        .BorderBottom(1).BorderColor(LineColor)
                        .PaddingVertical(HeadCellPaddingY)
                        .PaddingHorizontal(CellPaddingX)
                        .AlignedTo(column.Alignment)
                        .Text(column.Header)
                        .FontSize(BodyFontSize)
                        .FontColor(HeadText);
                }
            });

            for (var rowIndex = 0; rowIndex < _model.Rows.Count; rowIndex++)
            {
                var row = _model.Rows[rowIndex];

                // แถวคู่ลงสีอ่อน ตรงกับ alternateRowStyles ของเดิม
                var background = rowIndex % 2 == 1 ? AlternateRowFill : Colors.White;

                for (var i = 0; i < _model.Columns.Count; i++)
                {
                    var column = _model.Columns[i];
                    var value = i < row.Count ? row[i] : string.Empty;

                    table.Cell()
                        .Background(background)
                        .BorderBottom(1).BorderColor(LineColor)
                        .PaddingVertical(BodyCellPaddingY)
                        .PaddingHorizontal(CellPaddingX)
                        .AlignedTo(column.Alignment)
                        .Text(value)
                        .FontSize(BodyFontSize)
                        .FontColor(BodyText);
                }
            }
        });
    }

    /// <summary>
    /// ท้ายกระดาษชิดขวาสองบรรทัด: เลขหน้า แล้ววันที่พิมพ์
    /// ของเดิมวางเลขหน้าไว้บรรทัดบน และวันที่พิมพ์ไว้บรรทัดล่าง
    /// </summary>
    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(10).Column(column =>
        {
            column.Item().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(FooterFontSize).FontColor(FooterText));
                text.Span("หน้า ");
                text.CurrentPageNumber();
                text.Span("/");
                text.TotalPages();
            });

            column.Item().AlignRight()
                .Text($"วันที่พิมพ์รายงาน {_model.PrintedAt}")
                .FontSize(FooterFontSize).FontColor(FooterText);
        });
    }
}

internal static class ReportAlignmentExtensions
{
    public static IContainer AlignedTo(this IContainer container, ReportColumnAlignment alignment) =>
        alignment switch
        {
            ReportColumnAlignment.Right => container.AlignRight(),
            ReportColumnAlignment.Center => container.AlignCenter(),
            _ => container.AlignLeft(),
        };
}
