using AstarLiving.Api.Modules.External;
using AstarLiving.Shared.Contracts;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Api.Tests;

/// <summary>
/// ไฟล์ Excel ของรายงานค้างชำระแจ้งลูกบ้าน
///
/// แท็บนี้เป็นแท็บเดียวที่ออกไฟล์สองชีต แท็บอื่นออกชีตเดียวทั้งหมด
/// เทสต์ชุดนี้ล็อกโครงที่ระบบเดิมใช้ไว้ เพราะถ้าเผลอทำให้เหลือชีตเดียว
/// หรือเปลี่ยนยอดรวมรายปีจากสูตรเป็นตัวเลขนิ่ง จะไม่มีอะไรฟ้อง
/// ไฟล์ยังเปิดได้ปกติและตัวเลขยังถูก แต่ผู้ใช้จะแก้ตัวเลขแล้วยอดรวมไม่ขยับตาม
/// </summary>
public class ArrearsResidentWorkbookTests
{
    private static XLWorkbook Build()
    {
        var data = new SummaryExpensesByResidentResponse
        {
            ResidentId = 1,
            HouseNo = "99/1",
            FullName = "สมชาย ใจดี",
            ByYear =
            [
                new SummaryExpensesYearGroup
                {
                    Year = 2569,
                    MonthCount = 2,
                    CommonFee = 4000m,
                    Fine = 400m,
                    CollectionFee = 200m,
                    Total = 4600m,
                    Months =
                    [
                        Month(9, 2569, "69090500001", 2000m, 200m, 100m, 2300m),
                        Month(10, 2569, "69100500041", 2000m, 200m, 100m, 2300m),
                    ],
                },
                new SummaryExpensesYearGroup
                {
                    Year = 2568,
                    MonthCount = 1,
                    CommonFee = 1800m,
                    Fine = 0m,
                    CollectionFee = 0m,
                    Total = 1800m,
                    Months = [Month(12, 2568, "68120500007", 1800m, 0m, 0m, 1800m)],
                },
            ],
            Total = new SummaryExpensesResidentTotal
            {
                CommonFee = 5800m,
                Fine = 400m,
                CollectionFee = 200m,
                Total = 6400m,
                PaymentCount = 3,
                YearCount = 2,
            },
        };

        var bytes = ArrearsResidentWorkbook.Build(
            data, "หมู่บ้านตัวอย่าง", "ข้อมูล ณ วันที่ 04/10/2569");

        return new XLWorkbook(new MemoryStream(bytes));
    }

    private static SummaryExpensesMonthRow Month(
        short month, int year, string invNo, decimal common, decimal fine, decimal collection, decimal total) =>
        new()
        {
            Month = month,
            Year = year,
            InvNo = invNo,
            CommonFee = common,
            Fine = fine,
            CollectionFee = collection,
            Total = total,
        };

    [Fact]
    public void ต้องมีสองชีตชื่อ_Report_กับ_Data()
    {
        using var workbook = Build();

        workbook.Worksheets.Select(w => w.Name).Should().Equal("Report", "Data");
    }

    [Fact]
    public void ยอดรวมรายปีต้องเป็นสูตรไม่ใช่ตัวเลขนิ่ง()
    {
        using var workbook = Build();
        var sheet = workbook.Worksheet("Report");

        var formulas = sheet.CellsUsed(c => c.HasFormula)
            .Select(c => c.FormulaA1)
            .ToList();

        // สองปี ปีละสี่ช่องเงิน
        formulas.Should().HaveCount(8);
        formulas.Should().OnlyContain(f => f.StartsWith("SUM("));
    }

    [Fact]
    public void บล็อกรายปีต้องมีหัวตารางซ้ำทุกปี()
    {
        using var workbook = Build();
        var sheet = workbook.Worksheet("Report");

        var headings = sheet.CellsUsed(c => c.GetString().StartsWith("รายการค้างชำระ: พ.ศ."))
            .Select(c => c.GetString())
            .ToList();

        headings.Should().Equal("รายการค้างชำระ: พ.ศ. 2569", "รายการค้างชำระ: พ.ศ. 2568");

        var repeated = sheet.CellsUsed(c => c.GetString() == "ลำดับ").Count();
        repeated.Should().Be(2, "หัวตารางต้องซ้ำทุกบล็อกปี");
    }

    [Fact]
    public void เลขลำดับเริ่มใหม่ทุกปี()
    {
        using var workbook = Build();
        var sheet = workbook.Worksheet("Report");

        // ปี 2569 มีสองงวด ปี 2568 มีงวดเดียว ลำดับจึงเป็น 1,2 แล้ว 1
        var sequence = sheet.Column(1).CellsUsed()
            .Where(c => c.DataType == XLDataType.Number)
            .Select(c => c.GetValue<int>())
            .ToList();

        sequence.Should().Equal(1, 2, 1);
    }

    [Fact]
    public void ชีต_Data_เป็นตารางแบนเก้าคอลัมน์ไม่มีหัวเรื่อง()
    {
        using var workbook = Build();
        var sheet = workbook.Worksheet("Data");

        sheet.Cell(1, 1).GetString().Should().Be("บ้านเลขที่");
        sheet.Cell(1, 9).GetString().Should().Be("ยอดที่ต้องชำระ (บาท)");

        // หัวตารางหนึ่งแถว บวกสามงวดจากทั้งสองปี
        sheet.LastRowUsed()!.RowNumber().Should().Be(4);

        sheet.CellsUsed(c => c.HasFormula).Should().BeEmpty("ชีตนี้มีไว้ให้เอาไปทำตารางสรุปต่อ");
    }

    [Fact]
    public void ยอดเงินต้องเก็บเป็นตัวเลขไม่ใช่ข้อความ()
    {
        using var workbook = Build();
        var sheet = workbook.Worksheet("Data");

        // ถ้าเก็บเป็นข้อความ สูตร SUM ในชีต Report จะได้ศูนย์
        sheet.Cell(2, 6).DataType.Should().Be(XLDataType.Number);
        sheet.Cell(2, 9).GetValue<decimal>().Should().Be(2300m);
    }

    [Fact]
    public void บล็อกสรุปท้ายไฟล์ต้องมียอดรวมสี่บรรทัด()
    {
        using var workbook = Build();
        var sheet = workbook.Worksheet("Report");

        var labels = new[]
        {
            "ยอดรวมค่าส่วนกลาง", "ยอดรวมค่าเบี้ยปรับ", "ยอดรวมค่าทวงถาม", "ยอดรวมค้างชำระทั้งหมด",
        };

        foreach (var label in labels)
        {
            var cell = sheet.CellsUsed(c => c.GetString() == label).SingleOrDefault();
            cell.Should().NotBeNull($"ต้องมีบรรทัด {label}");
        }

        var grandTotal = sheet.CellsUsed(c => c.GetString() == "ยอดรวมค้างชำระทั้งหมด").Single();
        sheet.Cell(grandTotal.Address.RowNumber, 8).GetValue<decimal>().Should().Be(6400m);
    }
}
