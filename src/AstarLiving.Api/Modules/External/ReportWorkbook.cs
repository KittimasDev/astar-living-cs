using System.Globalization;
using AstarLiving.Shared.Contracts;
using ClosedXML.Excel;

namespace AstarLiving.Api.Modules.External;

/// <summary>
/// บันทึกตารางรายงานเป็นไฟล์ Excel
///
/// ระบบเดิมสร้างไฟล์นี้ที่เบราว์เซอร์ด้วย exceljs ระบบใหม่ย้ายมาสร้างที่เครื่องแม่
/// ด้วยเหตุผลเดียวกับที่ย้าย PDF มา คือใช้ตรรกะชุดเดียวกับเอกสารอื่น
/// และไม่ต้องฝังเครื่องมือสร้างไฟล์ไว้ในโปรแกรมหน้าจอ
///
/// ค่าทุกตัวในนี้ยกมาจากของเดิม รวมถึงสิ่งที่ "ไม่ได้ทำ" ด้วย
/// ของเดิมไม่มีเส้นขอบ ไม่ตรึงหัวตาราง ไม่ใส่ตัวกรอง และปิดเส้นตารางพื้นหลัง
/// ถ้าใส่เพิ่มเข้าไปไฟล์จะหน้าตาไม่เหมือนที่ลูกค้าเคยได้
/// </summary>
public static class ReportWorkbook
{
    private const string FontName = "TH Sarabun New";
    private const double BodyFontSize = 10;
    private const string HeaderFill = "FFD9EAF7";
    private const string SummaryFill = "FFF8FAFC";
    private const string MoneyFormat = "#,##0.00";

    /// <summary>หัวเรื่องกินความกว้างอย่างน้อย 8 คอลัมน์เสมอ แม้ตารางจะแคบกว่านั้น</summary>
    private const int MinimumTitleColumns = 8;

    public static byte[] Build(ReportPrintRequest request, string projectName, string subtitle, string asOfLine)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");

        var columns = request.Columns;
        var titleWidth = Math.Max(columns.Count, MinimumTitleColumns);

        ApplyBaseStyle(sheet, titleWidth);
        ApplyColumnWidths(sheet, columns);

        WriteTitle(sheet, 1, titleWidth, projectName, size: 14, bold: true, height: 20);
        WriteTitle(sheet, 2, titleWidth, request.ReportLabel, size: 14, bold: true, height: 18);
        WriteTitle(sheet, 3, titleWidth, subtitle, size: 11, bold: false, height: null);
        WriteTitle(sheet, 4, titleWidth, asOfLine, size: 11, bold: false, height: null);

        // แถว 5 เว้นว่างไว้ หัวตารางจึงเริ่มที่แถว 6 เหมือนของเดิม
        const int headerRow = 6;

        WriteHeader(sheet, headerRow, columns);

        var row = headerRow + 1;

        foreach (var values in request.Rows)
        {
            WriteBodyRow(sheet, row, columns, values);
            row++;
        }

        if (request.Summary.Count > 0) WriteSummaryBlock(sheet, row + 1, columns.Count, request.Summary);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ApplyBaseStyle(IXLWorksheet sheet, int columnCount)
    {
        sheet.Style.Font.FontName = FontName;
        sheet.Style.Font.FontSize = BodyFontSize;
        sheet.RowHeight = 18;

        // ของเดิมปิดเส้นตารางพื้นหลังแล้วใช้สีพื้นหัวตารางแทนเส้นขอบ
        sheet.ShowGridLines = false;

        for (var i = 1; i <= columnCount; i++)
        {
            sheet.Column(i).Style.Font.FontName = FontName;
            sheet.Column(i).Style.Font.FontSize = BodyFontSize;
            sheet.Column(i).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
    }

    /// <summary>
    /// ความกว้างคอลัมน์ตามกฎของเดิม
    /// ลำดับ 8 · คอลัมน์ข้อความที่ชิดซ้าย 28 · คอลัมน์เงิน 18 · ที่เหลือ 18
    /// </summary>
    private static void ApplyColumnWidths(IXLWorksheet sheet, List<ReportPrintColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            var width = column.Header == "ลำดับ" ? 8
                : column.Alignment == ReportPrintAlignment.Left && column.Kind == ReportPrintValueKind.Text ? 28
                : 18;

            sheet.Column(i + 1).Width = width;
        }
    }

    private static void WriteTitle(
        IXLWorksheet sheet, int row, int columnCount, string text, double size, bool bold, double? height)
    {
        var cell = sheet.Cell(row, 1);

        cell.Value = text;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = size;
        cell.Style.Font.Bold = bold;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        if (columnCount > 1) sheet.Range(row, 1, row, columnCount).Merge();
        if (height is not null) sheet.Row(row).Height = height.Value;
    }

    private static void WriteHeader(IXLWorksheet sheet, int row, List<ReportPrintColumn> columns)
    {
        sheet.Row(row).Height = 34;

        for (var i = 0; i < columns.Count; i++)
        {
            var cell = sheet.Cell(row, i + 1);

            cell.Value = NormalizeHeader(columns[i].Header);
            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = BodyFontSize;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(unchecked((int)0xFFD9EAF7));
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }
    }

    private static void WriteBodyRow(
        IXLWorksheet sheet, int row, List<ReportPrintColumn> columns, List<string> values)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            var column = columns[i];
            var text = i < values.Count ? values[i] : string.Empty;

            // ยอดเงินต้องเป็นตัวเลขจริง ไม่ใช่ข้อความ
            // ไม่อย่างนั้นผู้ใช้จะรวมยอดหรือเรียงลำดับใน Excel ไม่ได้
            // ซึ่งเป็นเหตุผลหลักที่คนขอไฟล์ Excel แทน PDF
            if (column.Kind != ReportPrintValueKind.Text && TryParseNumber(text, out var number))
            {
                cell.Value = number;
                cell.Style.NumberFormat.Format = MoneyFormat;
            }
            else
            {
                cell.Value = text;
            }

            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = BodyFontSize;
            cell.Style.Alignment.Horizontal = ToExcel(column.Alignment);
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }
    }

    /// <summary>
    /// บล็อกสรุปท้ายชีต ใช้กับรายงานที่เครื่องแม่ส่งยอดรวมมาให้
    /// ป้ายกำกับรวมเซลล์ถึงคอลัมน์รองสุดท้าย ตัวเลขอยู่คอลัมน์สุดท้าย
    /// </summary>
    private static void WriteSummaryBlock(
        IXLWorksheet sheet, int startRow, int columnCount, List<ReportSummaryLine> summary)
    {
        if (columnCount < 2) columnCount = 2;

        var row = startRow;

        var heading = sheet.Cell(row, 1);
        heading.Value = "สรุปรายการค้างชำระ";
        heading.Style.Font.FontName = FontName;
        heading.Style.Font.FontSize = BodyFontSize;
        heading.Style.Font.Bold = true;
        heading.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Range(row, 1, row, columnCount).Merge();
        row++;

        var headerCell = sheet.Cell(row, 1);
        headerCell.Value = "รายการ";
        sheet.Range(row, 1, row, columnCount - 1).Merge();

        var amountHeader = sheet.Cell(row, columnCount);
        amountHeader.Value = "จำนวนเงิน (บาท)";

        sheet.Row(row).Height = 34;

        foreach (var cell in new[] { headerCell, amountHeader })
        {
            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = BodyFontSize;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(unchecked((int)0xFFD9EAF7));
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }

        row++;

        foreach (var line in summary)
        {
            var label = sheet.Cell(row, 1);
            label.Value = line.Label;
            label.Style.Font.FontName = FontName;
            label.Style.Font.FontSize = BodyFontSize;
            label.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            label.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Range(row, 1, row, columnCount - 1).Merge();

            var value = sheet.Cell(row, columnCount);
            value.Value = line.Amount;
            value.Style.NumberFormat.Format = MoneyFormat;
            value.Style.Font.FontName = FontName;
            value.Style.Font.FontSize = BodyFontSize;
            value.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            value.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            if (line.Emphasise)
            {
                label.Style.Font.Bold = true;
                value.Style.Font.Bold = true;
                label.Style.Fill.BackgroundColor = XLColor.FromArgb(unchecked((int)0xFFF8FAFC));
                value.Style.Fill.BackgroundColor = XLColor.FromArgb(unchecked((int)0xFFF8FAFC));
            }

            row++;
        }
    }

    /// <summary>
    /// ของเดิมตัดคำว่า "เบี้ย" ออกและขึ้นบรรทัดใหม่ก่อนหน่วยเงิน
    /// เพื่อให้หัวคอลัมน์ไม่กว้างเกินไป ลำดับการแทนที่สำคัญ
    /// </summary>
    internal static string NormalizeHeader(string header)
    {
        var result = header.Replace("ค่าเบี้ยปรับ", "ค่าปรับ");

        foreach (var unit in new[]
                 {
                     "ค่าส่วนกลาง (บาท)", "ค่าทวงถาม (บาท)", "ยอดที่ต้องชำระ (บาท)",
                     "ยอดชำระ (บาท)", "ยอดรวมทั้งหมด (บาท)", "ชำระแล้ว (บาท)", "ค้างชำระ (บาท)",
                 })
        {
            var split = unit.Replace(" (บาท)", "\n(บาท)");
            result = result.Replace(unit, split);
        }

        return result;
    }

    private static bool TryParseNumber(string text, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text) || text == "-") return false;

        return double.TryParse(
            text, NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }

    private static XLAlignmentHorizontalValues ToExcel(ReportPrintAlignment alignment) => alignment switch
    {
        ReportPrintAlignment.Right => XLAlignmentHorizontalValues.Right,
        ReportPrintAlignment.Center => XLAlignmentHorizontalValues.Center,
        _ => XLAlignmentHorizontalValues.Left,
    };
}
