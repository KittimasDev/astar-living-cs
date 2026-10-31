using AstarLiving.Domain.Billing;
using AstarLiving.Pdf.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AstarLiving.Pdf.Documents;

/// <summary>
/// รายงานสรุปรายการค้างชำระ — พอร์ตจาก summary-expenses-*-pdf-layout.helper.ts
///
/// ระบบเดิมมีสองแบบที่จัดวางคนละอย่าง ไม่ใช่แบบเดียวกันที่กรองข้อมูลต่างกัน
///   ทั้งโครงการ — ตารางแบนตารางเดียว 7 คอลัมน์ หนึ่งแถวต่อหนึ่งบ้าน
///   รายบ้าน     — แยกบล็อกตามปี ตารางละ 8 คอลัมน์ แล้วปิดท้ายด้วยตารางสรุปยอด
///
/// ขอบกระดาษและระยะในตารางก็ต่างกัน จึงต้องแยกเส้นทางตั้งแต่ตอนตั้งค่าหน้า
/// </summary>
public class SummaryExpensesDocument(
    ProjectInfo project,
    SummaryExpensesDocumentModel model,
    byte[] logo) : IDocument
{
    private const string TextColor = "#333333";
    private const string HeaderFill = "#D9EAF7";
    private const string FooterFill = "#E8E8E8";
    private const string LineColor = "#CCCCCC";

    private bool IsProjectWide => model.Scope == SummaryExpenseScope.Project;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);

            // ขอบกระดาษของสองแบบไม่เท่ากัน รายงานทั้งโครงการใช้ขอบแคบกว่าเพราะตารางกว้าง
            if (IsProjectWide)
            {
                page.MarginLeft(20, Unit.Point);
                page.MarginTop(28, Unit.Point);
                page.MarginRight(20, Unit.Point);
                page.MarginBottom(48, Unit.Point);
            }
            else
            {
                page.MarginLeft(40, Unit.Point);
                page.MarginTop(40, Unit.Point);
                page.MarginRight(40, Unit.Point);
                page.MarginBottom(60, Unit.Point);
            }

            page.DefaultTextStyle(text => text
                .FontFamily(PdfEnvironment.FontFamily)
                .FontSize(14)
                .FontColor(TextColor));

            page.Header().Element(ComposeTitle);
            page.Content().Element(IsProjectWide ? ComposeProjectTable : ComposeResidentSections);
            page.Footer().Element(ComposePageFooter);
        });
    }

    /// <summary>สามถึงสี่บรรทัดกึ่งกลาง ตามแบบของระบบเดิม</summary>
    private void ComposeTitle(IContainer container)
    {
        container.PaddingBottom(IsProjectWide ? 0 : 16, Unit.Point).Column(column =>
        {
            column.Item().AlignCenter().PaddingBottom(4, Unit.Point)
                .Text(InvoiceDocument.Safe(project.ProjectName)).Bold().FontSize(18);

            column.Item().AlignCenter().PaddingBottom(4, Unit.Point)
                .Text(IsProjectWide
                    ? "รายงานสรุปรายการค้างชำระลูกบ้านทั้งหมด"
                    : "รายงานสรุปรายการค้างชำระลูกบ้านต่อหลัง")
                .Bold().FontSize(16);

            if (!IsProjectWide)
            {
                var house = model.Groups.FirstOrDefault()?.HouseNo ?? string.Empty;

                column.Item().AlignCenter().PaddingBottom(4, Unit.Point)
                    .Text($"บ้านเลขที่ {house} ปี:{InvoiceDocument.Safe(model.PeriodLabel)}");
            }

            column.Item().AlignCenter().Text($"ณ วันที่ {ThaiLongDate(DateTime.Now)}");
        });
    }

    // ─── รายงานทั้งโครงการ ───────────────────────────────────────────────────

    /// <summary>ตารางเดียว หนึ่งแถวต่อหนึ่งบ้าน ปิดท้ายด้วยแถวยอดรวม</summary>
    private void ComposeProjectTable(IContainer container)
    {
        container.PaddingTop(16, Unit.Point).Table(table =>
        {
            // [22, 46, '*', 58, 58, 58, 64] ตามระบบเดิม
            // ยกเว้นคอลัมน์ลำดับที่ขยายจาก 22 เป็น 30
            // เพราะ QuestPDF วัดความกว้างตัวอักษรไทยกว้างกว่า pdfmake เล็กน้อย
            // ที่ 22 หัวคอลัมน์ "ลำดับ" จะถูกตัดเป็นสองบรรทัดซึ่งดูเหมือนข้อผิดพลาด
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30, Unit.Point);
                columns.ConstantColumn(46, Unit.Point);
                columns.RelativeColumn();
                columns.ConstantColumn(58, Unit.Point);
                columns.ConstantColumn(58, Unit.Point);
                columns.ConstantColumn(58, Unit.Point);
                columns.ConstantColumn(64, Unit.Point);
            });

            table.Header(header =>
            {
                Head(header.Cell(), "ลำดับ", Align.Center);
                Head(header.Cell(), "บ้านเลขที่", Align.Left);
                Head(header.Cell(), "ชื่อ-นามสกุล", Align.Left);
                Head(header.Cell(), "ค่าส่วนกลาง (บาท)", Align.Right);
                Head(header.Cell(), model.FineColumnLabel, Align.Right);
                Head(header.Cell(), "ค่าทวงถาม (บาท)", Align.Right);
                Head(header.Cell(), "รวมทั้งหมด (บาท)", Align.Right);
            });

            for (var i = 0; i < model.Groups.Count; i++)
            {
                var group = model.Groups[i];
                var line = group.Lines.FirstOrDefault() ?? new SummaryExpenseLine();

                Body(table.Cell(), (i + 1).ToString(), Align.Center);
                Body(table.Cell(), Dash(group.HouseNo), Align.Left);
                Body(table.Cell(), Dash(group.FullName), Align.Left);
                Body(table.Cell(), MoneyFormat.Fixed2(line.CommonFee), Align.Right);
                Body(table.Cell(), MoneyFormat.Fixed2(line.Fine), Align.Right);
                Body(table.Cell(), MoneyFormat.Fixed2(line.CollectionFee), Align.Right);
                Body(table.Cell(), MoneyFormat.Fixed2(group.Total), Align.Right);
            }

            table.Cell().ColumnSpan(3).Background(FooterFill).BorderTop(0.5f).BorderColor(LineColor)
                .PaddingHorizontal(2, Unit.Point).PaddingVertical(3, Unit.Point)
                .Text("ยอดรวมค้างชำระทั้งหมด").Bold();

            Total(table.Cell(), model.TotalCommonFee);
            Total(table.Cell(), model.TotalFine);
            Total(table.Cell(), model.TotalCollectionFee);
            Total(table.Cell(), model.GrandTotal);
        });
    }

    // ─── รายงานรายบ้าน ───────────────────────────────────────────────────────

    /// <summary>บล็อกตามปี แล้วปิดท้ายด้วยตารางสรุปยอดแยกตามชนิด</summary>
    private void ComposeResidentSections(IContainer container)
    {
        var group = model.Groups.FirstOrDefault();

        container.Column(column =>
        {
            foreach (var year in (group?.Lines ?? []).GroupBy(l => l.Year).OrderBy(g => g.Key))
            {
                column.Item().PaddingTop(8, Unit.Point).PaddingBottom(4, Unit.Point)
                    .Column(section =>
                    {
                        section.Item().PaddingBottom(6, Unit.Point)
                            .Text($"รายการค้างชำระ: พ.ศ. {year.Key}").Bold();

                        section.Item().Element(c => ComposeYearTable(c, year.ToList(), group!.HouseNo, year.Key));
                    });
            }

            column.Item().PaddingTop(12, Unit.Point).Element(ComposeGrandSummary);
        });
    }

    private void ComposeYearTable(IContainer container, List<SummaryExpenseLine> lines, string houseNo, int year)
    {
        container.Table(table =>
        {
            // [28, 42, 48, 78, '*', '*', '*', '*'] ตามระบบเดิม
            // คอลัมน์ลำดับขยายจาก 28 เป็น 34 ด้วยเหตุผลเดียวกับตารางของทั้งโครงการ
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(34, Unit.Point);
                columns.ConstantColumn(42, Unit.Point);
                columns.ConstantColumn(48, Unit.Point);
                columns.ConstantColumn(78, Unit.Point);
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            table.Header(header =>
            {
                Head(header.Cell(), "ลำดับ", Align.Center, padding: 4);
                Head(header.Cell(), "เดือน", Align.Center, padding: 4);
                Head(header.Cell(), "ปี (พ.ศ.)", Align.Center, padding: 4);
                Head(header.Cell(), "เลขที่ใบแจ้งหนี้", Align.Center, padding: 4);
                Head(header.Cell(), "ค่าส่วนกลาง (บาท)", Align.Right, padding: 4);
                Head(header.Cell(), model.FineColumnLabel, Align.Right, padding: 4);
                Head(header.Cell(), "ค่าทวงถาม (บาท)", Align.Right, padding: 4);
                Head(header.Cell(), "ยอดที่ต้องชำระ (บาท)", Align.Right, padding: 4);
            });

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                Body(table.Cell(), (i + 1).ToString(), Align.Center, padding: 4);
                Body(table.Cell(), line.MonthLabel, Align.Center, padding: 4);
                Body(table.Cell(), line.Year.ToString(), Align.Center, padding: 4);
                Body(table.Cell(), Dash(line.InvoiceNo), Align.Center, padding: 4);
                Body(table.Cell(), MoneyFormat.Fixed2(line.CommonFee), Align.Right, padding: 4);
                Body(table.Cell(), MoneyFormat.Fixed2(line.Fine), Align.Right, padding: 4);
                Body(table.Cell(), MoneyFormat.Fixed2(line.CollectionFee), Align.Right, padding: 4);
                Body(table.Cell(), MoneyFormat.Fixed2(line.Total), Align.Right, padding: 4);
            }

            table.Cell().ColumnSpan(4).BorderTop(0.5f).BorderColor(LineColor)
                .PaddingHorizontal(4, Unit.Point).PaddingVertical(3, Unit.Point)
                .Text($"ยอดรวมค้างชำระ: บ้านเลขที่ {houseNo} พ.ศ. {year}").Bold();

            Sum(table.Cell(), lines.Sum(l => l.CommonFee));
            Sum(table.Cell(), lines.Sum(l => l.Fine));
            Sum(table.Cell(), lines.Sum(l => l.CollectionFee));
            Sum(table.Cell(), lines.Sum(l => l.Total));
        });
    }

    private void ComposeGrandSummary(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(6, Unit.Point).Text("สรุปรายการค้างชำระ").Bold();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(120, Unit.Point);
                });

                table.Header(header =>
                {
                    Head(header.Cell(), "รายการ", Align.Left, padding: 4);
                    Head(header.Cell(), "จำนวนเงิน(บาท)", Align.Right, padding: 4);
                });

                SummaryRow(table, "ยอดรวมค่าส่วนกลาง", model.TotalCommonFee);
                SummaryRow(table, "ยอดรวมค่าเบี้ยปรับ", model.TotalFine);
                SummaryRow(table, "ยอดรวมค่าทวงถาม", model.TotalCollectionFee);

                table.Cell().Background(HeaderFill).BorderTop(0.3f).BorderColor(LineColor)
                    .PaddingHorizontal(4, Unit.Point).PaddingVertical(3, Unit.Point)
                    .Text("ยอดรวมค้างชำระทั้งหมด").Bold();

                table.Cell().Background(HeaderFill).BorderTop(0.3f).BorderColor(LineColor)
                    .PaddingHorizontal(4, Unit.Point).PaddingVertical(3, Unit.Point)
                    .AlignRight().Text(MoneyFormat.Fixed2(model.GrandTotal)).Bold();
            });
        });
    }

    private static void SummaryRow(TableDescriptor table, string label, decimal value)
    {
        Body(table.Cell(), label, Align.Left, padding: 4);
        Body(table.Cell(), MoneyFormat.Fixed2(value), Align.Right, padding: 4);
    }

    /// <summary>เลขหน้าและวันเวลาที่พิมพ์ ชิดขวาทุกหน้า</summary>
    private void ComposePageFooter(IContainer container)
    {
        container.PaddingTop(10, Unit.Point).Column(column =>
        {
            column.Item().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(12));
                text.Span("หน้า ");
                text.CurrentPageNumber();
                text.Span("/");
                text.TotalPages();
            });

            column.Item().AlignRight()
                .Text($"วันที่พิมพ์รายงาน {ThaiLongDate(DateTime.Now)} " +
                      $"เวลา {DateTime.Now:HH.mm} น.")
                .FontSize(12);
        });
    }

    // ─── ตัวช่วยวาดเซลล์ ─────────────────────────────────────────────────────

    private enum Align { Left, Center, Right }

    private static IContainer Aligned(IContainer cell, Align align) => align switch
    {
        Align.Right => cell.AlignRight(),
        Align.Center => cell.AlignCenter(),
        _ => cell.AlignLeft(),
    };

    private static void Head(IContainer cell, string text, Align align, float padding = 2)
    {
        var container = cell.Background(HeaderFill)
            .BorderTop(0.5f).BorderBottom(0.3f).BorderColor(LineColor)
            .PaddingHorizontal(padding, Unit.Point).PaddingVertical(3, Unit.Point);

        Aligned(container, align).Text(text).Bold().FontSize(13);
    }

    private static void Body(IContainer cell, string text, Align align, float padding = 2)
    {
        var container = cell.BorderBottom(0.3f).BorderColor(LineColor)
            .PaddingHorizontal(padding, Unit.Point).PaddingVertical(3, Unit.Point);

        Aligned(container, align).Text(text);
    }

    private static void Total(IContainer cell, decimal value) =>
        cell.Background(FooterFill).BorderTop(0.5f).BorderColor(LineColor)
            .PaddingHorizontal(2, Unit.Point).PaddingVertical(3, Unit.Point)
            .AlignRight().Text(MoneyFormat.Fixed2(value)).Bold();

    private static void Sum(IContainer cell, decimal value) =>
        cell.BorderTop(0.5f).BorderColor(LineColor)
            .PaddingHorizontal(4, Unit.Point).PaddingVertical(3, Unit.Point)
            .AlignRight().Text(MoneyFormat.Fixed2(value)).Bold();

    /// <summary>เช่น "3 ตุลาคม 2569" — วัน เดือนเต็ม ปี พ.ศ. ตามที่ระบบเดิมพิมพ์</summary>
    private static string ThaiLongDate(DateTime date) =>
        $"{date.Day} {ThaiMonths.GetFull(date.Month)} {date.Year + 543}";

    private static string Dash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;
}
