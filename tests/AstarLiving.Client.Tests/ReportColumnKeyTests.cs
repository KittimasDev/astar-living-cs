using System.Text.Json;
using AstarLiving.Client.ViewModels.Pages;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Client.Tests;

/// <summary>
/// ตรวจว่าคีย์ของทุกคอลัมน์ในหน้ารายงานตรงกับชื่อฟิลด์ที่เครื่องแม่ส่งกลับมาจริง
///
/// ที่ต้องมีเทสต์ชุดนี้เพราะหน้ารายงานอ่านผลลัพธ์เป็น JSON ดิบ
/// สะกดคีย์ผิดหนึ่งตัวจะได้คอลัมน์ว่างเปล่าเงียบ ๆ ไม่มีอะไรฟ้องทั้งตอนคอมไพล์และตอนรัน
/// เคยพลาดมาแล้วตอนแท็บรายรับอื่น ๆ ใส่คอลัมน์วิธีชำระเงิน
/// ทั้งที่ฝั่งเครื่องแม่ไม่เคยส่งฟิลด์นั้นมาเลย
/// </summary>
public class ReportColumnKeyTests
{
    [Theory]
    [InlineData("REPORT_OUTSTANDING")]
    [InlineData("REPORT_PAYMENT")]
    [InlineData("REPORT_INVOICE")]
    [InlineData("REPORT_FINE")]
    [InlineData("REPORT_REVENUE")]
    public void ทุกคอลัมน์ต้องมีฟิลด์รองรับในผลลัพธ์จากเครื่องแม่(string type)
    {
        var tab = ReportPageViewModel.DefinedTabs.Single(t => t.Type == type);
        var available = SerializeSample(type);

        var missing = tab.Columns
            .Where(c => c.Kind != ReportColumnKind.Sequence)
            .SelectMany(KeysUsedBy)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !available.Contains(key))
            .ToList();

        missing.Should().BeEmpty("แท็บ {0} ต้องไม่อ้างถึงฟิลด์ที่เครื่องแม่ไม่ได้ส่งมา", type);
    }

    [Fact]
    public void คีย์ของคอลัมน์ในแท็บเดียวกันต้องไม่ซ้ำกัน()
    {
        // แถวหนึ่งเก็บค่าเป็นดิกชันนารี คีย์ซ้ำแปลว่าคอลัมน์หลังทับค่าของคอลัมน์หน้า
        // แท็บการชำระเงินมีสองคอลัมน์ที่อ่าน rejected_reason เหมือนกัน
        // จึงต้องพึ่ง SourceKey แยกคีย์เก็บออกจากคีย์อ่าน
        foreach (var tab in ReportPageViewModel.DefinedTabs)
        {
            var duplicates = tab.Columns
                .GroupBy(c => c.Key, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            duplicates.Should().BeEmpty("แท็บ {0} ต้องไม่มีคีย์คอลัมน์ซ้ำกัน", tab.Label);
        }
    }

    /// <summary>สถานะการชำระต้องดูสองฟิลด์ ไม่ใช่ฟิลด์เดียวตามชื่อคีย์</summary>
    private static IEnumerable<string> KeysUsedBy(ReportColumn column) =>
        column.Kind == ReportColumnKind.PaymentStatusText
            ? ["status_notices", "status_payments"]
            : [column.ReadFrom];

    private static HashSet<string> SerializeSample(string type)
    {
        object sample = type switch
        {
            "REPORT_OUTSTANDING" => new OutstandingReportRow(),
            "REPORT_PAYMENT" => new PaymentReportRow(),
            "REPORT_INVOICE" => new InvoiceReportRow(),
            "REPORT_FINE" => new FineReportRow(),

            // รูปร่างนี้สะท้อน ReportService.RevenueAsync ซึ่งคืนชนิดนิรนาม
            // ไม่มีสัญญาให้อ้างอิง แก้ที่นั่นแล้วต้องมาแก้ที่นี่ด้วย
            "REPORT_REVENUE" => new
            {
                Id = 0,
                ProjectId = 0,
                ReceiptNo = string.Empty,
                IssueDate = (DateOnly?)null,
                ResidentId = 0,
                PaymentDetail = (string?)null,
                detail = string.Empty,
                TotalAmount = 0m,
                PaymentMethodId = (int?)null,
                BankName = (string?)null,
                ChequeNo = (string?)null,
                Note = (string?)null,
                PaymentDate = (DateOnly?)null,
                Active = true,
                RejectedReason = (string?)null,
                house_no = string.Empty,
                full_name = string.Empty,
            },

            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "ไม่รู้จักชนิดรายงานนี้"),
        };

        var json = JsonSerializer.Serialize(sample, AstarJson.Options);

        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
    }
}
