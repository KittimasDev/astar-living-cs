using AstarLiving.Domain.Billing;
using AstarLiving.Shared.Contracts;
using ClosedXML.Excel;

namespace AstarLiving.Api.Modules.External;

/// <summary>
/// ไฟล์ Excel ของรายงานค้างชำระแจ้งลูกบ้าน
///
/// แท็บนี้เป็นแท็บเดียวในระบบที่ออกไฟล์ "สองชีต" ต่างจากแท็บอื่นที่ออกชีตเดียว
///
/// ชีต Report — แยกบล็อกตามปี พ.ศ. หัวตารางซ้ำทุกบล็อก
///              ปิดท้ายแต่ละบล็อกด้วยยอดรวมที่เป็นสูตร SUM ไม่ใช่ตัวเลขนิ่ง
/// ชีต Data   — ตารางแบนเก้าคอลัมน์ ไม่มีบล็อก ไม่มีหัวเรื่อง
///              เอาไว้ให้ผู้ใช้เอาไปทำตารางสรุปเองได้
///
/// รูปแบบยกมาจากระบบเดิมตัวต่อตัว รวมถึงจุดที่ดูไม่สม่ำเสมอ เช่น
/// หัวคอลัมน์ในชีต Report ขึ้นบรรทัดใหม่ก่อนหน่วยเงิน แต่ในชีต Data ไม่ขึ้น
/// และหัวข้อ "ค่าปรับ (10%)" ไม่มีคำว่าบาทต่อท้ายทั้งที่ช่องอื่นมี
/// </summary>
public static class ArrearsResidentWorkbook
{
    private const string FontName = "TH Sarabun New";
    private const double BodyFontSize = 10;
    private const string MoneyFormat = "#,##0.00";

    /// <summary>ชีต Report กว้างแปดคอลัมน์ หัวเรื่องรวมเซลล์เต็มความกว้างนี้</summary>
    private const int ReportColumns = 8;

    /// <summary>ช่องตั้งแต่คอลัมน์นี้เป็นต้นไปในชีต Report เป็นจำนวนเงิน</summary>
    private const int ReportMoneyFrom = 5;

    /// <summary>ช่องตั้งแต่คอลัมน์นี้เป็นต้นไปในชีต Data เป็นจำนวนเงิน</summary>
    private const int DataMoneyFrom = 6;

    private static readonly double[] ReportWidths = [8, 12, 10, 22, 18, 16, 16, 18];
    private static readonly double[] DataWidths = [14, 12, 10, 12, 22, 18, 16, 16, 18];

    private static readonly string[] ReportHeaders =
    [
        "ลำดับ", "เดือน", "ปี (พ.ศ.)", "เลขที่ใบแจ้งหนี้",
        "ค่าส่วนกลาง\n(บาท)", "ค่าปรับ (10%)", "ค่าทวงถาม\n(บาท)", "ยอดที่ต้องชำระ\n(บาท)",
    ];

    private static readonly string[] DataHeaders =
    [
        "บ้านเลขที่", "ปี (พ.ศ.)", "เดือนที่", "เดือน", "เลขที่ใบแจ้งหนี้",
        "ค่าส่วนกลาง (บาท)", "ค่าปรับ (10%)", "ค่าทวงถาม (บาท)", "ยอดที่ต้องชำระ (บาท)",
    ];

    public static byte[] Build(
        SummaryExpensesByResidentResponse data, string projectName, string asOfLine)
    {
        using var workbook = new XLWorkbook();

        var report = workbook.Worksheets.Add("Report");
        var flat = workbook.Worksheets.Add("Data");

        ApplyBaseStyle(report, ReportColumns, ReportWidths);
        ApplyBaseStyle(flat, DataHeaders.Length, DataWidths);

        WriteTitle(report, projectName, data, asOfLine);
        WriteHeaderRow(flat, 1, DataHeaders);

        // แถว 5 เว้นว่าง บล็อกแรกจึงเริ่มที่แถว 6
        var reportRow = 6;
        var flatRow = 2;

        foreach (var year in data.ByYear)
        {
            WriteYearBlock(report, flat, data, year, ref reportRow, ref flatRow);
        }

        WriteGrandSummary(report, data.Total, ref reportRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ApplyBaseStyle(IXLWorksheet sheet, int columnCount, double[] widths)
    {
        sheet.Style.Font.FontName = FontName;
        sheet.Style.Font.FontSize = BodyFontSize;
        sheet.RowHeight = 18;

        // ของเดิมปิดเส้นตารางพื้นหลังทุกชีต
        sheet.ShowGridLines = false;

        for (var i = 1; i <= columnCount; i++)
        {
            sheet.Column(i).Style.Font.FontName = FontName;
            sheet.Column(i).Style.Font.FontSize = BodyFontSize;
            sheet.Column(i).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Column(i).Width = widths[i - 1];
        }
    }

    private static void WriteTitle(
        IXLWorksheet sheet, string projectName, SummaryExpensesByResidentResponse data, string asOfLine)
    {
        var houseNo = string.IsNullOrWhiteSpace(data.HouseNo) ? "-" : data.HouseNo;

        // ระบบเดิมเขียน "ปี:ทั้งหมด" ตายตัว ไม่ได้สะท้อนตัวกรองปีที่ผู้ใช้เลือก
        var lines = new (string Text, double Size, bool Bold, double? Height)[]
        {
            (projectName, 14, true, 20),
            ("รายงานสรุปรายการค้างชำระลูกบ้านต่อหลัง", 14, true, 18),
            ($"บ้านเลขที่ {houseNo} {data.FullName} ปี:ทั้งหมด".Replace("  ", " "), 11, false, null),
            (asOfLine, 11, false, null),
        };

        for (var i = 0; i < lines.Length; i++)
        {
            var (text, size, bold, height) = lines[i];
            var cell = sheet.Cell(i + 1, 1);

            cell.Value = text;
            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = size;
            cell.Style.Font.Bold = bold;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            sheet.Range(i + 1, 1, i + 1, ReportColumns).Merge();

            if (height is not null) sheet.Row(i + 1).Height = height.Value;
        }
    }

    private static void WriteYearBlock(
        IXLWorksheet report,
        IXLWorksheet flat,
        SummaryExpensesByResidentResponse data,
        SummaryExpensesYearGroup year,
        ref int reportRow,
        ref int flatRow)
    {
        var heading = report.Cell(reportRow, 1);

        heading.Value = $"รายการค้างชำระ: พ.ศ. {year.Year}";
        heading.Style.Font.FontName = FontName;
        heading.Style.Font.FontSize = BodyFontSize;
        heading.Style.Font.Bold = true;
        heading.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        report.Range(reportRow, 1, reportRow, ReportColumns).Merge();
        reportRow++;

        WriteHeaderRow(report, reportRow, ReportHeaders);
        reportRow++;

        var firstDataRow = reportRow;

        // เลขลำดับเริ่มใหม่ทุกปี ไม่ได้นับต่อเนื่องทั้งไฟล์
        var index = 1;

        foreach (var month in year.Months)
        {
            var monthName = ThaiMonths.GetAbbr(month.Month);

            WriteBodyRow(report, reportRow,
            [
                index, monthName, month.Year, month.InvNo,
                month.CommonFee, month.Fine, month.CollectionFee, month.Total,
            ], ReportMoneyFrom);

            WriteBodyRow(flat, flatRow,
            [
                data.HouseNo, month.Year, (int)month.Month, monthName, month.InvNo,
                month.CommonFee, month.Fine, month.CollectionFee, month.Total,
            ], DataMoneyFrom);

            reportRow++;
            flatRow++;
            index++;
        }

        WriteYearSummary(report, reportRow, data.HouseNo, year, firstDataRow, reportRow - 1);
        reportRow++;

        // เว้นบรรทัดคั่นระหว่างปี
        reportRow++;
    }

    /// <summary>
    /// ยอดรวมของปี เขียนเป็นสูตร SUM ไม่ใช่ตัวเลขนิ่ง
    /// ระบบเดิมทำแบบนี้เพื่อให้ผู้ใช้แก้ตัวเลขในตารางแล้วยอดรวมขยับตาม
    /// </summary>
    private static void WriteYearSummary(
        IXLWorksheet sheet, int row, string houseNo, SummaryExpensesYearGroup year,
        int firstDataRow, int lastDataRow)
    {
        var house = string.IsNullOrWhiteSpace(houseNo) ? "-" : houseNo;

        sheet.Cell(row, 1).Value = $"ยอดรวมค้างชำระ: บ้านเลขที่ {house} พ.ศ. {year.Year}";
        sheet.Range(row, 1, row, 4).Merge();

        var hasRows = lastDataRow >= firstDataRow;
        var letters = new[] { "E", "F", "G", "H" };
        var fallbacks = new[] { year.CommonFee, year.Fine, year.CollectionFee, year.Total };

        for (var i = 0; i < letters.Length; i++)
        {
            var cell = sheet.Cell(row, ReportMoneyFrom + i);

            // ปีที่ไม่มีงวดเลยจะอ้างช่วงว่างไม่ได้ ใส่ยอดที่เครื่องแม่คิดมาแทน
            if (hasRows) cell.FormulaA1 = $"SUM({letters[i]}{firstDataRow}:{letters[i]}{lastDataRow})";
            else cell.Value = fallbacks[i];
        }

        for (var column = 1; column <= ReportColumns; column++)
        {
            var cell = sheet.Cell(row, column);
            var isMoney = column >= ReportMoneyFrom;

            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = BodyFontSize;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(unchecked((int)0xFFF8FAFC));
            cell.Style.Alignment.Horizontal = isMoney
                ? XLAlignmentHorizontalValues.Right
                : XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;

            if (isMoney) cell.Style.NumberFormat.Format = MoneyFormat;
        }
    }

    /// <summary>
    /// บล็อกสรุปท้ายชีต Report
    ///
    /// ระบบเดิมเคยเขียนบล็อกนี้เป็นสูตรที่อ้างชีต Data แต่คอมเมนต์ทิ้งไว้
    /// แล้วหันมาใช้ตัวเลขที่เครื่องแม่คิดมาแทน ระบบใหม่ทำตามของที่เปิดใช้จริง
    /// ไม่ใช่ตามโค้ดที่ถูกคอมเมนต์ไว้
    /// </summary>
    private static void WriteGrandSummary(
        IXLWorksheet sheet, SummaryExpensesResidentTotal total, ref int row)
    {
        var heading = sheet.Cell(row, 1);

        heading.Value = "สรุปรายการค้างชำระ";
        heading.Style.Font.FontName = FontName;
        heading.Style.Font.FontSize = BodyFontSize;
        heading.Style.Font.Bold = true;
        heading.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        heading.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Range(row, 1, row, ReportColumns).Merge();
        row++;

        WriteHeaderRow(sheet, row, ["รายการ", "", "", "", "", "", "", "จำนวนเงิน (บาท)"]);
        row++;

        var lines = new (string Label, decimal Amount)[]
        {
            ("ยอดรวมค่าส่วนกลาง", total.CommonFee),
            ("ยอดรวมค่าเบี้ยปรับ", total.Fine),
            ("ยอดรวมค่าทวงถาม", total.CollectionFee),
            ("ยอดรวมค้างชำระทั้งหมด", total.Total),
        };

        foreach (var (text, amount) in lines)
        {
            var label = sheet.Cell(row, 1);

            label.Value = text;
            label.Style.Font.FontName = FontName;
            label.Style.Font.FontSize = BodyFontSize;
            label.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            label.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Range(row, 1, row, ReportColumns - 1).Merge();

            var value = sheet.Cell(row, ReportColumns);

            value.Value = amount;
            value.Style.NumberFormat.Format = MoneyFormat;
            value.Style.Font.FontName = FontName;
            value.Style.Font.FontSize = BodyFontSize;
            value.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            value.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            row++;
        }
    }

    private static void WriteHeaderRow(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        sheet.Row(row).Height = 34;

        for (var i = 0; i < headers.Count; i++)
        {
            var cell = sheet.Cell(row, i + 1);

            cell.Value = headers[i];
            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = BodyFontSize;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(unchecked((int)0xFFD9EAF7));
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }
    }

    /// <summary>
    /// เขียนหนึ่งแถว ช่องตั้งแต่ <paramref name="moneyFromColumn"/> เป็นต้นไปเป็นจำนวนเงิน
    /// ยอดเงินต้องเขียนเป็นตัวเลขจริง ไม่ใช่ข้อความ ไม่อย่างนั้นสูตร SUM จะได้ศูนย์
    /// </summary>
    private static void WriteBodyRow(
        IXLWorksheet sheet, int row, IReadOnlyList<object> values, int moneyFromColumn)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            var isMoney = i + 1 >= moneyFromColumn;

            switch (values[i])
            {
                case decimal money:
                    cell.Value = money;
                    break;

                case int number:
                    cell.Value = number;
                    break;

                default:
                    cell.Value = values[i]?.ToString() ?? string.Empty;
                    break;
            }

            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = BodyFontSize;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Horizontal = isMoney
                ? XLAlignmentHorizontalValues.Right
                : XLAlignmentHorizontalValues.Left;

            if (isMoney) cell.Style.NumberFormat.Format = MoneyFormat;
        }
    }
}
