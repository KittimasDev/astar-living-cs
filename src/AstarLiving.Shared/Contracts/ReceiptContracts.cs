using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;
using System.ComponentModel.DataAnnotations;
using AstarLiving.Domain.Enums;

namespace AstarLiving.Shared.Contracts;

/// <summary>gv_receipts หนึ่งใบ</summary>
public class ReceiptResponse
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string? ReceiptNo { get; init; }
    public DateOnly IssueDate { get; init; }
    public int ResidentId { get; init; }
    /// <summary>คอลัมน์ payment_ids ที่จริงเก็บชื่อเดือนภาษาอังกฤษ เช่น ["JAN","FEB"]</summary>
    public List<string>? PaymentIds { get; init; }
    public decimal TotalAmount { get; init; }
    public int? PaymentMethodId { get; init; }
    public string? BankName { get; init; }
    public string? ChequeNo { get; init; }
    public string? Note { get; init; }
    /// <summary>เหตุผลที่ยกเลิกใบเสร็จ</summary>
    public string? RejectedReason { get; init; }
    public DateOnly PaymentDate { get; init; }
    /// <summary>false = ใบเสร็จถูกยกเลิกแล้ว</summary>
    public bool Active { get; init; }
    public bool HaveDiscount { get; init; }
    public decimal? DiscountRate { get; init; }
    public int? CreatedBy { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public ResidentBrief? Resident { get; init; }
}

/// <summary>หนึ่งรายการที่ผู้ใช้ติ๊กเลือกในหน้าออกใบเสร็จ</summary>
public class ReceiptItemSelection
{
    /// <summary>MAINTENANCE / FINE / COLLECTION</summary>
    [Required]
    public ReceiptItemType Type { get; set; }

    public bool Selected { get; set; }

    /// <summary>
    /// MAINTENANCE: รหัสของงวด (gv_payments.id)
    /// FINE / COLLECTION: รหัสของใบทวงถาม (gv_collection_notices.id)
    /// การใช้ฟิลด์เดียวแทนสองความหมายเป็นรูปแบบของระบบเดิมที่คงไว้
    /// </summary>
    public int Id { get; set; }
}

/// <summary>งวดหนึ่งพร้อมรายการย่อยที่ถูกเลือก</summary>
public class ReceiptPaymentSelection
{
    [Required]
    public int PaymentId { get; set; }

    public List<ReceiptItemSelection> Items { get; set; } = [];
}

public class CreateReceiptRequest
{
    [Required] public int ProjectId { get; set; }
    [Required] public DateOnly IssueDate { get; set; }
    [Required] public int ResidentId { get; set; }

    /// <summary>ชื่อเดือนภาษาอังกฤษ เช่น ["Jan","Feb"] — ระบบเดิมเก็บลงคอลัมน์ payment_ids</summary>
    public List<string> Months { get; set; } = [];

    public decimal? TotalAmount { get; set; }
    public decimal? DiscountRate { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? BankName { get; set; }
    public string? ChequeNo { get; set; }
    public string? Note { get; set; }

    /// <summary>true = ใช้ส่วนลดรายปี ระบบจะสร้างงวดล่วงหน้าเดือน 2-12 ให้อัตโนมัติ</summary>
    public bool HaveDiscount { get; set; }

    [Required]
    public List<ReceiptPaymentSelection> Payments { get; set; } = [];
}

public class UpdateReceiptRequest
{
    public DateOnly? IssueDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? BankName { get; set; }
    public string? ChequeNo { get; set; }
    public string? Note { get; set; }
    public bool? Active { get; set; }
}

/// <summary>PATCH /v1/receipts/rejected/:id — ยกเลิกใบเสร็จ</summary>
public class RejectReceiptRequest
{
    public string? RejectedReason { get; set; }
}

// ─── หน้าเลือกงวดเพื่อออกใบเสร็จ ────────────────────────────────────────────

/// <summary>รายการย่อยที่เลือกได้ในงวดหนึ่ง</summary>
public class SelectableItem
{
    public string Name { get; init; } = string.Empty;
    /// <summary>MAINTENANCE / FINE / COLLECTION</summary>
    public string Key { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    /// <summary>รหัสงวดสำหรับ MAINTENANCE หรือรหัสใบทวงถามสำหรับอีกสองชนิด</summary>
    public int Id { get; init; }
    public NoticeStatus? Status { get; init; }
}

public class SelectableInvoice
{
    public int PaymentId { get; init; }
    public PaymentStatus Status { get; init; }
    public string? InvoiceNo { get; init; }
    public short Month { get; init; }
    public int Year { get; init; }
    /// <summary>เช่น "พ.ค. 68"</summary>
    public string MonthTh { get; init; } = string.Empty;
    /// <summary>เช่น "MAY"</summary>
    public string MonthEn { get; init; } = string.Empty;
    /// <summary>true = ใบทวงถามของงวดนี้ถูกยกเลิกไปหมดแล้ว</summary>
    public bool IsNoticesCancelled { get; init; }
    public List<SelectableItem> Items { get; init; } = [];
    public decimal Total { get; init; }
}

public class ResidentNameBrief
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string HouseNo { get; init; } = string.Empty;
}

public class SelectableInvoicesResponse
{
    /// <summary>true = บ้านหลังนี้เข้าเงื่อนไขส่วนลดรายปี</summary>
    public bool HaveDiscount { get; init; }
    public ResidentNameBrief? Resident { get; init; }
    public List<SelectableInvoice> Invoices { get; init; } = [];
}

// ─── หน้าสรุปยอดก่อนออกใบเสร็จ ──────────────────────────────────────────────

public class CalculateReceiptPreviewRequest
{
    public bool HaveDiscount { get; set; }
    public List<ReceiptPaymentSelection> Payments { get; set; } = [];
}

/// <summary>
/// หนึ่งบรรทัดในใบสรุป
/// count กับ unit_price เป็นสตริงว่างในแถวส่วนลด ตามรูปแบบของระบบเดิม
/// </summary>
public class ReceiptSummaryItem
{
    public string Name { get; init; } = string.Empty;
    public object Count { get; init; } = 0;
    public object UnitPrice { get; init; } = 0m;
    public decimal Total { get; init; }

    /// <summary>
    /// ราคาต่อหน่วยสำหรับแสดงบนหน้าจอ ทศนิยมสองตำแหน่งเสมอ ตรงกับระบบเดิม
    ///
    /// ช่องนี้ประกาศเป็น object เพราะแถวส่วนลดส่งสตริงว่างมา
    /// พอผ่าน System.Text.Json ค่าที่ได้จึงเป็น JsonElement ไม่ใช่ decimal
    /// การใส่ StringFormat ที่ XAML จึงไม่มีผล ต้องแปลงเองตรงนี้
    /// </summary>
    [JsonIgnore]
    public string UnitPriceText => FormatMoney(UnitPrice);

    [JsonIgnore]
    public string CountText => Count switch
    {
        JsonElement { ValueKind: JsonValueKind.String } text => text.GetString() ?? string.Empty,
        JsonElement { ValueKind: JsonValueKind.Number } number => number.ToString(),
        null => string.Empty,
        _ => Count.ToString() ?? string.Empty,
    };

    private static string FormatMoney(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.String } text => text.GetString() ?? string.Empty,
        JsonElement { ValueKind: JsonValueKind.Number } number =>
            number.GetDecimal().ToString("#,##0.00", CultureInfo.InvariantCulture),
        decimal money => money.ToString("#,##0.00", CultureInfo.InvariantCulture),
        string text => text,
        null => string.Empty,
        _ => value.ToString() ?? string.Empty,
    };
}

public class ReceiptSummary
{
    public List<ReceiptSummaryItem> Items { get; init; } = [];
    public bool HaveDiscount { get; init; }
    public decimal Discount { get; init; }
    public decimal TotalAmount { get; init; }
}

public class ReceiptPreviewResponse
{
    public ResidentNameBrief? Resident { get; init; }
    public ReceiptSummary? Summary { get; init; }
}
