using System.Text.Json;
using System.Text.Json.Serialization;
using AstarLiving.Domain.Billing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AstarLiving.Domain.Tests;

/// <summary>
/// เทสต์เทียบผลกับระบบเดิมแบบตัวต่อตัว
///
/// ไฟล์ Fixtures/legacy-invoice-totals.json สร้างจากการรัน "ฟังก์ชันของระบบเดิมจริง ๆ"
/// (getPdfInvoiceDebtTotal / getPdfInvoiceGrandTotal / getDebtSummaryForInvoicePartitioned
/// ใน src/common/utils/date/dateTH.utils.ts) บนข้อมูลจริงจากฐานข้อมูล gardenview_uat
///
/// แต่ละเคสเก็บทั้ง input และ output ของเดิมไว้ เทสต์นี้คำนวณซ้ำด้วยโค้ด C#
/// ถ้าต่างกันแม้สตางค์เดียว แปลว่าพอร์ตสูตรผิด
///
/// วิธีสร้างไฟล์ใหม่ (เมื่อข้อมูลอ้างอิงเปลี่ยน):
///   cd astar-living-be
///   npx ts-node -T -r tsconfig-paths/register tools/golden-export.ts tmp/golden.json
/// แล้วคัดเคสที่ต้องการลงเป็น Fixtures/legacy-invoice-totals.json
/// </summary>
public class LegacyInvoiceGoldenTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static readonly IReadOnlyList<GoldenCase> Cases = LoadCases();

    private static IReadOnlyList<GoldenCase> LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-invoice-totals.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<GoldenCase>>(stream)
               ?? throw new InvalidOperationException("อ่านไฟล์ golden fixture ไม่ได้");
    }

    [Fact]
    public void Fixture_covers_the_cases_that_matter()
    {
        Cases.Should().NotBeEmpty();

        var withDebt = Cases.Count(c => c.PriorDebts.Count > 0);
        var withDebtFine = Cases.Count(c => c.PriorDebts.Any(d => d.Fine != 0m));
        var withDebtCollection = Cases.Count(c => c.PriorDebts.Any(d => d.CollectionFee != 0m));

        _output.WriteLine($"เคสทั้งหมด {Cases.Count} / มีหนี้ค้าง {withDebt} " +
                          $"/ หนี้ที่มีค่าปรับ {withDebtFine} / หนี้ที่มีค่าทวงถาม {withDebtCollection}");

        withDebt.Should().BeGreaterThan(100, "ต้องมีเคสหนี้ค้างพอที่จะพิสูจน์การแบ่งกองหนี้");
        withDebtFine.Should().BeGreaterThan(0);
        withDebtCollection.Should().BeGreaterThan(0);
    }

    /// <summary>ยอดค้างชำระรวมต้องตรงกับที่ระบบเดิมคำนวณทุกเคส</summary>
    [Fact]
    public void Debt_total_matches_legacy_for_every_case()
    {
        var mismatches = new List<string>();

        foreach (var c in Cases)
        {
            var actual = DebtSummary.PdfInvoiceDebtTotal(c.ToDebtLines(), c.Year, c.Month);
            if (Math.Abs(actual - c.ExpectedDebtTotal) > 0.0000001m)
            {
                mismatches.Add($"งวด {c.PaymentId} ({c.InvNo}) คาด {c.ExpectedDebtTotal} ได้ {actual}");
            }
        }

        mismatches.Should().BeEmpty(
            "ยอดค้างต้องตรงกับระบบเดิมทุกเคส\n" + string.Join("\n", mismatches.Take(10)));
    }

    /// <summary>รวมเป็นเงินทั้งสิ้นของใบแจ้งหนี้ต้องตรงกับที่ระบบเดิมคำนวณทุกเคส</summary>
    [Fact]
    public void Grand_total_matches_legacy_for_every_case()
    {
        var mismatches = new List<string>();

        foreach (var c in Cases)
        {
            var actual = DebtSummary.PdfInvoiceGrandTotal(
                c.Invoice.AmountDue, c.Invoice.Fine, c.Invoice.CollectionFee,
                c.ToDebtLines(), c.Year, c.Month);

            if (Math.Abs(actual - c.ExpectedGrandTotal) > 0.0000001m)
            {
                mismatches.Add($"งวด {c.PaymentId} ({c.InvNo}) คาด {c.ExpectedGrandTotal} ได้ {actual}");
            }
        }

        mismatches.Should().BeEmpty(
            "ยอดรวมต้องตรงกับระบบเดิมทุกเคส\n" + string.Join("\n", mismatches.Take(10)));
    }

    /// <summary>
    /// ข้อความ 3 บรรทัดในบล็อกยอดค้างต้องตรงทุกตัวอักษร รวมถึงบรรทัดว่างที่เป็น NBSP
    /// ถ้าจำนวนบรรทัดเปลี่ยน ความสูงของใบแจ้งหนี้ใน PDF จะเลื่อนทันที
    /// </summary>
    [Fact]
    public void Debt_summary_lines_match_legacy_character_for_character()
    {
        var mismatches = new List<string>();

        foreach (var c in Cases)
        {
            var summary = DebtSummary.PdfInvoiceDebtSummary(c.ToDebtLines(), c.Year, c.Month);
            var actual = summary.Items.Select(i => i.Text).ToList();

            if (actual.Count != 3)
            {
                mismatches.Add($"งวด {c.PaymentId} ได้ {actual.Count} บรรทัด");
                continue;
            }

            for (var i = 0; i < 3; i++)
            {
                if (actual[i] != c.ExpectedDebtItems[i])
                {
                    mismatches.Add(
                        $"งวด {c.PaymentId} บรรทัด {i + 1}: คาด \"{c.ExpectedDebtItems[i]}\" ได้ \"{actual[i]}\"");
                }
            }
        }

        mismatches.Should().BeEmpty(
            "ข้อความยอดค้างต้องตรงกับระบบเดิม\n" + string.Join("\n", mismatches.Take(10)));
    }

    /// <summary>
    /// ค่าส่วนกลางของงวดต้องเท่ากับ อัตราต่อตารางวา x อัตราส่วนกรรมสิทธิ์
    ///
    /// ยอมให้คลาดเคลื่อนได้ไม่เกินหนึ่งสตางค์ เพราะยอดที่ระบบเดิมบันทึกไว้บางงวด
    /// มีเศษทศนิยมจาก floating point ของ JavaScript ติดมาด้วย (ดูเทสต์ถัดไป)
    /// </summary>
    [Fact]
    public void Maintenance_amount_matches_rate_times_ownership_ratio()
    {
        var checkable = Cases
            .Where(c => c.MaintenanceFeeRate > 0m && c.OwnershipRatio > 0m && c.Invoice.AmountDue > 0m)
            .ToList();

        var mismatches = checkable
            .Where(c => Math.Abs(BillingCalculator.Maintenance(c.MaintenanceFeeRate, c.OwnershipRatio)
                                 - c.Invoice.AmountDue) > 0.01m)
            .Select(c => $"บ้าน {c.HouseNo} งวด {c.Month}/{c.Year}: " +
                         $"{c.MaintenanceFeeRate} x {c.OwnershipRatio} != {c.Invoice.AmountDue}")
            .ToList();

        _output.WriteLine($"ตรวจได้ {checkable.Count} เคส ต่างเกินหนึ่งสตางค์ {mismatches.Count} เคส");
        foreach (var line in mismatches.Take(20)) _output.WriteLine("  " + line);

        // ที่เหลือคือบ้านที่เคยแก้อัตราส่วนกรรมสิทธิ์ภายหลัง งวดเก่าจึงคิดจากอัตราส่วนเดิม
        // ไม่ใช่ความผิดของสูตร แต่ต้องไม่บานปลาย
        mismatches.Count.Should().BeLessThan(10,
            "สูตรค่าส่วนกลางต้องอธิบายยอดจริงได้เกือบทั้งหมด\n" + string.Join("\n", mismatches.Take(10)));
    }

    /// <summary>
    /// ตรึงหลักฐานว่าระบบเดิมเขียนเศษทศนิยมจาก floating point ลงฐานข้อมูลจริง
    ///
    /// ใน JavaScript: 50 * 39.8 = 1989.9999999999998 แล้วค่านี้ถูกบันทึกลง gv_payments.amount_due ตรง ๆ
    /// ใน C# ที่ใช้ decimal: 50 * 39.8 = 1990.00 พอดี
    ///
    /// หน้าจอแสดงผลทศนิยม 2 ตำแหน่งจึงมองไม่เห็นความต่างนี้ แต่เมื่อรวมยอดหลายงวด
    /// เศษจะสะสมขึ้นเรื่อย ๆ ระบบใหม่จึงให้ยอดที่ถูกต้องกว่า และจะไม่สร้างเศษแบบนี้เพิ่มอีก
    /// </summary>
    [Fact]
    public void Csharp_decimal_removes_the_javascript_float_artifacts()
    {
        var artifacts = Cases
            .Where(c => c.MaintenanceFeeRate > 0m && c.OwnershipRatio > 0m)
            .Where(c =>
            {
                var exact = BillingCalculator.Maintenance(c.MaintenanceFeeRate, c.OwnershipRatio);
                var diff = Math.Abs(exact - c.Invoice.AmountDue);
                return diff > 0m && diff <= 0.01m;
            })
            .ToList();

        _output.WriteLine($"งวดที่ยอดในฐานข้อมูลมีเศษจาก floating point = {artifacts.Count}");
        foreach (var c in artifacts.Take(5))
        {
            _output.WriteLine($"  บ้าน {c.HouseNo} งวด {c.Month}/{c.Year}: " +
                              $"ฐานข้อมูลเก็บ {c.Invoice.AmountDue} แต่ค่าที่ถูกต้องคือ " +
                              $"{BillingCalculator.Maintenance(c.MaintenanceFeeRate, c.OwnershipRatio)}");
        }

        artifacts.Should().NotBeEmpty(
            "ถ้าวันหนึ่งเทสต์นี้ไม่เจอเศษแล้ว แปลว่าข้อมูลถูกแก้ให้สะอาดแล้ว ให้ลบเทสต์นี้ทิ้งได้");

        // ยืนยันว่าฝั่ง C# ให้ค่าที่ลงตัวจริง ๆ ไม่ได้แค่ต่างจากของเดิม
        foreach (var c in artifacts)
        {
            var exact = BillingCalculator.Maintenance(c.MaintenanceFeeRate, c.OwnershipRatio);
            decimal.Round(exact, 2).Should().Be(exact, $"บ้าน {c.HouseNo} งวด {c.Month}/{c.Year}");
        }
    }
}

public class GoldenCase
{
    [JsonPropertyName("payment_id")] public int PaymentId { get; init; }
    [JsonPropertyName("inv_no")] public string? InvNo { get; init; }
    [JsonPropertyName("house_no")] public string? HouseNo { get; init; }
    [JsonPropertyName("year")] public int Year { get; init; }
    [JsonPropertyName("month")] public int Month { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("ownership_ratio")] public decimal OwnershipRatio { get; init; }
    [JsonPropertyName("maintenance_fee_rate")] public decimal MaintenanceFeeRate { get; init; }
    [JsonPropertyName("fine_rate")] public decimal FineRate { get; init; }
    [JsonPropertyName("invoice")] public GoldenInvoice Invoice { get; init; } = new();
    [JsonPropertyName("prior_debts")] public List<GoldenDebt> PriorDebts { get; init; } = [];
    [JsonPropertyName("expected_debt_total")] public decimal ExpectedDebtTotal { get; init; }
    [JsonPropertyName("expected_grand_total")] public decimal ExpectedGrandTotal { get; init; }
    [JsonPropertyName("expected_debt_items")] public List<string> ExpectedDebtItems { get; init; } = [];

    public IReadOnlyList<DebtLine> ToDebtLines() =>
        PriorDebts.Select(d => new DebtLine(d.Year, d.Month, d.AmountDue, d.Fine, d.CollectionFee)).ToList();
}

public class GoldenInvoice
{
    [JsonPropertyName("amount_due")] public decimal AmountDue { get; init; }
    [JsonPropertyName("fine")] public decimal Fine { get; init; }
    [JsonPropertyName("collection_fee")] public decimal CollectionFee { get; init; }
}

public class GoldenDebt
{
    [JsonPropertyName("year")] public int Year { get; init; }
    [JsonPropertyName("month")] public int Month { get; init; }
    [JsonPropertyName("amount_due")] public decimal AmountDue { get; init; }
    [JsonPropertyName("fine")] public decimal Fine { get; init; }
    [JsonPropertyName("collection_fee")] public decimal CollectionFee { get; init; }
}
