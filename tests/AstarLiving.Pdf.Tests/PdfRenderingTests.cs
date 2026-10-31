using System.Text;
using AstarLiving.Pdf;
using AstarLiving.Pdf.Documents;
using AstarLiving.Pdf.Models;
using FluentAssertions;
using QuestPDF.Fluent;
using Xunit;
using Xunit.Abstractions;

namespace AstarLiving.Pdf.Tests;

/// <summary>
/// ทดสอบการสร้าง PDF จริง
///
/// คุณค่าหลักของเทสต์ชุดนี้คือ QuestPDF จะโยน exception เมื่อเนื้อหาล้นกรอบที่ตรึงความสูงไว้
/// การ render ผ่านจึงเป็นการพิสูจน์ว่าเนื้อหาพอดีครึ่งหน้ากระดาษจริง ซึ่งเป็นหัวใจของ
/// กลไกปรับความหนาแน่น (InvoiceDensity)
///
/// ตัวอย่างไฟล์ถูกเขียนลงโฟลเดอร์ชั่วคราวเพื่อให้เปิดดูด้วยตาได้ด้วย
/// </summary>
public class PdfRenderingTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static readonly ProjectInfo Project = new()
    {
        ProjectName = "การ์เด้นวิว",
        ProjectAddress = "ซอยลาดพร้าว 112 ถนนลาดพร้าว แขวงพลับพลา เขตวังทองหลาง กรุงเทพมหานคร 10310",
        ProjectTel = "0-2935-2772",
        Remark = "<div>กรุณาชำระภายในวันที่ 10 ของทุกเดือน</div><div>ชำระผ่านบัญชีธนาคารกสิกรไทย เลขที่ 123-4-56789-0</div>",
        ReceiptNote = "<div>ใบเสร็จนี้จะสมบูรณ์เมื่อเรียกเก็บเงินตามเช็คได้แล้ว</div>",
    };

    private static readonly SignInfo Signer = new()
    {
        Name = "นางสาว วชิราภรณ์ หอมจันทร์",
        Position = "ผู้จัดการนิติบุคคล",
    };

    private static readonly byte[] Logo;

    /// <summary>ลงทะเบียนฟอนต์และสัญญาอนุญาตครั้งเดียวก่อนเทสต์ตัวแรกทำงาน</summary>
    static PdfRenderingTests()
    {
        PdfEnvironment.Initialize();
        Logo = new LogoLoader().Load(null);
    }

    private static string OutputDirectory
    {
        get
        {
            var dir = Path.Combine(Path.GetTempPath(), "astar-living-pdf-samples");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private void Save(string fileName, byte[] bytes)
    {
        var path = Path.Combine(OutputDirectory, fileName);
        File.WriteAllBytes(path, bytes);
        _output.WriteLine($"{fileName}  {bytes.Length:N0} ไบต์  ->  {path}");
    }

    private static void ShouldBeValidPdf(byte[] bytes)
    {
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    /// <summary>นับจำนวนหน้าแบบหยาบจากจำนวนอ็อบเจกต์ชนิด Page ในไฟล์</summary>
    private static int CountPages(byte[] bytes)
    {
        var content = Encoding.Latin1.GetString(bytes);
        var count = 0;
        var index = 0;

        while ((index = content.IndexOf("/Type /Page", index, StringComparison.Ordinal)) >= 0)
        {
            // ข้าม /Type /Pages ซึ่งเป็นโหนดราก ไม่ใช่หน้ากระดาษ
            if (index + 11 >= content.Length || content[index + 11] != 's') count++;
            index += 11;
        }

        return count;
    }

    private static InvoiceDocumentModel MakeInvoice(int itemCount = 3, bool withDebt = true) => new()
    {
        InvoiceNo = "69091700013",
        CustomerName = "นาย สมชาย ใจดีมีเมตตากรุณา",
        HouseNo = "556/32",
        Address = "556/32 ซอยลาดพร้าว 112 แขวงพลับพลา เขตวังทองหลาง กรุงเทพมหานคร 10310",
        Date = "17/09/2569",
        Month = "9/2569",
        Items = Enumerable.Range(1, itemCount).Select(i => new DocumentItem
        {
            Name = i == 1 ? "ค่าส่วนกลาง (ก.ย. 69)" : $"รายการทดสอบที่ {i}",
            Unit = 40m,
            Price = 50m,
            Total = 2000m,
        }).ToList(),
        DebtItems = withDebt
            ?
            [
                "ค่าส่วนกลางรวม = 7,545.00 บาท",
                "ค่าเบี้ยปรับรวม = 754.50 บาท",
                " ",
            ]
            : [" ", " ", " "],
        Subtotal = 2000m * itemCount,
        Debt = withDebt ? 8299.50m : 0m,
        Total = (2000m * itemCount) + (withDebt ? 8299.50m : 0m),
    };

    [Fact]
    public void Invoice_renders_one_page_per_invoice()
    {
        var invoices = new[] { MakeInvoice(), MakeInvoice(itemCount: 2, withDebt: false) };

        var bytes = new InvoiceDocument(Project, invoices, Signer, Logo).GeneratePdf();

        ShouldBeValidPdf(bytes);
        CountPages(bytes).Should().Be(2, "หนึ่งใบแจ้งหนี้ต่อหนึ่งหน้า โดยในหน้ามีต้นฉบับและสำเนา");
        Save("invoice.pdf", bytes);
    }

    /// <summary>
    /// ใบแจ้งหนี้ที่มีรายการเยอะต้องยังพิมพ์ได้ กลไกปรับความหนาแน่นต้องบีบให้พอดีครึ่งหน้า
    /// ถ้าล้นกรอบ QuestPDF จะโยน exception และเทสต์นี้จะพัง
    /// </summary>
    [Fact]
    public void Invoice_with_many_items_still_fits_half_a_page()
    {
        var invoices = new[] { MakeInvoice(itemCount: 12) };

        var act = () => new InvoiceDocument(Project, invoices, Signer, Logo).GeneratePdf();

        var bytes = act.Should().NotThrow("เนื้อหาต้องถูกบีบให้พอดีครึ่งหน้า").Subject;
        ShouldBeValidPdf(bytes);
        Save("invoice-many-items.pdf", bytes);
    }

    [Fact]
    public void Receipt_renders()
    {
        var receipts = new[]
        {
            new ReceiptDocumentModel
            {
                ReceiptNo = "69070300003",
                CustomerName = "นางสาว ปรียานุช ทองสุกใส",
                HouseNo = "556/8",
                Address = "556/8 ซอยลาดพร้าว 112 เขตวังทองหลาง กรุงเทพมหานคร",
                Date = "03/07/2569",
                Month = "07/2569",
                Items =
                [
                    new DocumentItem { Name = "ค่าส่วนกลาง (ม.ค. 69 - มี.ค. 69)", Unit = 3m, Price = 2515m, Total = 7545m },
                    new DocumentItem { Name = "ค่าเบี้ยปรับ (ธ.ค. 68)", Unit = 1m, Price = 251.50m, Total = 251.50m },
                ],
                Total = 7796.50m,
                Payment = new PaymentInfo
                {
                    Name = "โอนเงิน",
                    IssueDate = "03/07/2569",
                    BankName = "กสิกรไทย",
                    ChequeNo = null,
                },
            },
        };

        var bytes = new ReceiptDocument(Project, receipts, Signer, Logo).GeneratePdf();

        ShouldBeValidPdf(bytes);
        CountPages(bytes).Should().Be(1);
        Save("receipt.pdf", bytes);
    }

    /// <summary>ใบเสร็จรายรับอื่นใช้เอกสารเดียวกัน ต่างแค่ชื่อหัวเรื่อง</summary>
    [Fact]
    public void Revenue_receipt_uses_the_same_layout_with_a_different_title()
    {
        var receipts = new[]
        {
            new ReceiptDocumentModel
            {
                ReceiptNo = "69070300007",
                CustomerName = "นาย อนันต์ พูนผล",
                HouseNo = "556/15",
                Date = "03/07/2569",
                Items = [new DocumentItem { Name = "ค่าทำบัตรจอดรถ", Unit = 2m, Price = 100m, Total = 200m }],
                Total = 200m,
                Payment = new PaymentInfo { Name = "เงินสด", IssueDate = "03/07/2569" },
            },
        };

        var bytes = new ReceiptDocument(Project, receipts, Signer, Logo, "ใบเสร็จรับเงิน (รายรับอื่น)").GeneratePdf();

        ShouldBeValidPdf(bytes);
        Save("revenue-receipt.pdf", bytes);
    }

    [Fact]
    public void Collection_notice_renders_one_page_per_notice()
    {
        var notices = Enumerable.Range(1, 3).Select(i => new CollectionDocumentModel
        {
            CollectionId = i,
            Date = "02/07/2569",
            CustomerName = $"นาย ทดสอบ รายที่ {i}",
            HouseNo = $"556/{i}",
            Page = $"{i}/3",
            Total = 200m,
            Items = [new CollectionLine { Name = "ค่าทวงถาม (มิ.ย. 69)", Amount = 200m, Remain = 200m }],
            IntroductionDetail = "<div>ตามที่ท่านได้ค้างชำระค่าส่วนกลาง นิติบุคคลขอแจ้งให้ทราบ</div>",
            AdditionalDetail = "<div>กรุณาติดต่อชำระภายใน 7 วันนับจากวันที่ได้รับหนังสือฉบับนี้</div>",
            ClosureText = "<div>จึงเรียนมาเพื่อโปรดดำเนินการ</div>",
            ClosingWord = "ขอแสดงความนับถือ",
            TemplateRemark = "<div>หากชำระแล้วโปรดละเว้นหนังสือฉบับนี้</div>",
        }).ToList();

        var bytes = new CollectionDocument(Project, notices, Signer, Logo).GeneratePdf();

        ShouldBeValidPdf(bytes);
        CountPages(bytes).Should().Be(3);
        Save("collection-notice.pdf", bytes);
    }

    [Fact]
    public void Summary_expenses_renders_and_spans_multiple_pages_when_needed()
    {
        var groups = Enumerable.Range(1, 25).Select(i => new SummaryExpenseGroup
        {
            HouseNo = $"556/{i}",
            FullName = $"นาย ลูกบ้าน หลังที่ {i}",
            Lines =
            [
                new SummaryExpenseLine { Period = "ม.ค. 69", CommonFee = 2000m, Fine = 200m, CollectionFee = 200m, Total = 2400m },
                new SummaryExpenseLine { Period = "ก.พ. 69", CommonFee = 2000m, Fine = 0m, CollectionFee = 0m, Total = 2000m },
            ],
            Total = 4400m,
        }).ToList();

        var model = new SummaryExpensesDocumentModel
        {
            PeriodLabel = "พ.ศ. 2569",
            FineColumnLabel = "ค่าปรับ (10%)",
            Groups = groups,
            GrandTotal = groups.Sum(g => g.Total),
        };

        var bytes = new SummaryExpensesDocument(Project, model, Logo).GeneratePdf();

        ShouldBeValidPdf(bytes);
        CountPages(bytes).Should().BeGreaterThan(1, "ข้อมูล 25 บ้านย่อมเกินหนึ่งหน้า");
        Save("summary-expenses.pdf", bytes);
    }
}

/// <summary>กลไกปรับความหนาแน่นของใบแจ้งหนี้</summary>
public class InvoiceDensityTests
{
    private static readonly ProjectInfo Project = new()
    {
        ProjectName = "การ์เด้นวิว",
        ProjectAddress = "ซอยลาดพร้าว 112",
        ProjectTel = "0-2935-2772",
    };

    /// <summary>ความสูงครึ่งหน้าจริงของ A4 หลังหักระยะขอบของใบแจ้งหนี้</summary>
    private const float HalfHeight = (841.89f - 9f - 16f - 10f) / 2f;

    private static InvoiceDocumentModel WithItems(int count) => new()
    {
        CustomerName = "นาย ทดสอบ",
        Address = "ที่อยู่ทดสอบ",
        Items = Enumerable.Range(1, count)
            .Select(_ => new DocumentItem { Name = "รายการ", Total = 100m })
            .ToList(),
        DebtItems = [" ", " ", " "],
    };

    [Fact]
    public void Few_items_stay_at_normal_density()
    {
        var burden = InvoiceDensity.EstimateInvoiceBurden(WithItems(3), Project);

        InvoiceDensity.Resolve(burden, HalfHeight).Should().Be(InvoiceDensityPreset.Normal);
    }

    [Fact]
    public void Many_items_force_the_tight_preset()
    {
        var burden = InvoiceDensity.EstimateInvoiceBurden(WithItems(20), Project);

        InvoiceDensity.Resolve(burden, HalfHeight).Should().Be(InvoiceDensityPreset.Tight);
    }

    /// <summary>เอกสารหนึ่งชุดต้องใช้ระดับเดียวกันทั้งหมด โดยยึดใบที่แน่นที่สุด</summary>
    [Fact]
    public void Document_adopts_the_tightest_preset_in_the_batch()
    {
        InvoiceDensity.PickTightest(
            [InvoiceDensityPreset.Normal, InvoiceDensityPreset.Tight, InvoiceDensityPreset.Compact])
            .Should().Be(InvoiceDensityPreset.Tight);
    }

    [Fact]
    public void Budget_never_drops_below_the_floor()
    {
        // แม้พื้นที่จะเล็กผิดปกติ สูตรยังต้องใช้งบขั้นต่ำ 160 pt ไม่หารด้วยค่าติดลบ
        var preset = InvoiceDensity.Resolve(burdenUnits: 100f, halfHeightPt: 10f);

        preset.Should().Be(InvoiceDensityPreset.Normal);
    }
}

/// <summary>การแปลง HTML จากเทมเพลตเป็นข้อความบนกระดาษ</summary>
public class PdfHtmlTests
{
    [Fact]
    public void Div_and_br_become_line_breaks()
    {
        var lines = PdfHtml.ToLines("<div>บรรทัดแรก</div><div>บรรทัดสอง<br/>บรรทัดสาม</div>");

        lines.Should().HaveCount(4);
        lines[0].Should().Be("บรรทัดแรก");
        lines[^1].Should().Be("บรรทัดสาม");
    }

    [Fact]
    public void Entities_and_remaining_tags_are_stripped()
    {
        PdfHtml.CleanToText("<p><strong>ข้อความ</strong>&nbsp;ทดสอบ</p>")
            .Should().Be("ข้อความ ทดสอบ");
    }

    [Fact]
    public void Footer_keeps_only_the_first_three_lines()
    {
        var html = "<div>1</div><div>2</div><div>3</div><div>4</div><div>5</div>";

        PdfHtml.ToFooterLines(html).Should().HaveCount(3);
    }

    [Fact]
    public void Empty_html_still_yields_one_blank_line()
    {
        PdfHtml.ToLines(null).Should().ContainSingle().Which.Should().BeEmpty();
    }

    /// <summary>token ของเทมเพลตใบทวงถามต้องถูกแทนที่ก่อนนำไปพิมพ์</summary>
    [Fact]
    public void Template_tokens_are_replaced()
    {
        var result = NoticeTemplateRenderer.Apply(
            "เรียน {resident_name} ยอดค้าง {amount} บาท",
            new Dictionary<string, string>
            {
                ["resident_name"] = "นาย สมชาย",
                ["amount"] = "1,200.00",
            });

        result.Should().Be("เรียน นาย สมชาย ยอดค้าง 1,200.00 บาท");
    }
}

/// <summary>
/// ฟอนต์ที่ถูกฝังลงในเอกสารจริง
///
/// บั๊กที่เคยเกิด: ชื่อวงศ์ฟอนต์ในโค้ดสะกดเป็น "THSarabunNew" แต่ชื่อจริงในไฟล์ ttf
/// คือ "TH Sarabun New" QuestPDF ไม่แจ้งอะไรเลย แต่ถอยไปใช้ Lato กับ Leelawadee UI
/// ซึ่งกว้างกว่ามาก ทำให้ทุกคอลัมน์ตัดบรรทัดและเอกสารผิดรูปทั้งใบ
///
/// เทสต์ชุดเดิมผ่านหมดทั้งที่ฟอนต์ผิด เพราะตรวจแค่ว่าไฟล์เปิดได้และนับหน้าถูก
/// ชุดนี้จึงตรวจถึงชื่อฟอนต์ที่ฝังอยู่จริงในไฟล์
/// </summary>
public class PdfFontEmbeddingTests
{
    private static readonly string[] FallbackFonts = ["Lato", "Leelawadee", "Arial", "Helvetica"];

    public PdfFontEmbeddingTests() => PdfEnvironment.Initialize();

    [Fact]
    public void ชื่อวงศ์ฟอนต์ต้องตรงกับที่อยู่ในไฟล์ฟอนต์จริง() =>
        PdfEnvironment.FontFamily.Should().Be("TH Sarabun New");

    [Fact]
    public void ใบแจ้งหนี้ต้องฝัง_THSarabunNew_เท่านั้น()
    {
        var bytes = BuildInvoice();
        var content = Encoding.Latin1.GetString(bytes);

        content.Should().Contain("THSarabunNew", "ต้องใช้ฟอนต์เดียวกับระบบเดิม");

        foreach (var fallback in FallbackFonts)
        {
            content.Should().NotContain(fallback, $"ไม่ควรถอยไปใช้ฟอนต์สำรอง {fallback}");
        }
    }

    private static byte[] BuildInvoice()
    {
        var project = new ProjectInfo
        {
            ProjectName = "การ์เด้นวิว",
            ProjectAddress = "ซอยลาดพร้าว 112 แขวงพลับพลา เขตวังทองหลาง กรุงเทพมหานคร 10310",
            ProjectTel = "0-2935-2772",
        };

        var invoice = new InvoiceDocumentModel
        {
            InvoiceNo = "69091700013",
            CustomerName = "นาย สมชาย ใจดี",
            HouseNo = "556/32",
            Address = "556/32 ซอยลาดพร้าว 112 กรุงเทพมหานคร 10310",
            Date = "17/09/2569",
            Month = "9/2569",
            Items =
            [
                new DocumentItem { Name = "ค่าส่วนกลาง (ก.ย. 69)", Unit = 40m, Price = 50m, Total = 2000m },
            ],
            DebtItems = [" ", " ", " "],
            Subtotal = 2000m,
            Debt = 0m,
            Total = 2000m,
        };

        var signer = new SignInfo { Name = "นางสาว วชิราภรณ์ หอมจันทร์", Position = "ผู้จัดการนิติบุคคล" };

        return new InvoiceDocument(project, [invoice], signer, new LogoLoader().Load(null)).GeneratePdf();
    }
}
