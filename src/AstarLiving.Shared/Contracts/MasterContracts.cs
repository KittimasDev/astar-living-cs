using System.ComponentModel.DataAnnotations;

namespace AstarLiving.Shared.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
// หมายเหตุเรื่องชื่อคีย์ JSON
// API ตั้งนโยบายเป็น snake_case ทั้งระบบ ดังนั้น ContactName จะกลายเป็น contact_name
// โดยอัตโนมัติ ไม่ต้องใส่ [JsonPropertyName] ยกเว้นคีย์ที่เป็น camelCase อยู่แต่เดิม
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>gv_projects — โครงการ</summary>
public class ProjectResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? ContactName { get; init; }
    public string? ContactPhone { get; init; }
    public string? ContactFaxPhone { get; init; }
    /// <summary>URL เต็มของโลโก้ (ฐานข้อมูลเก็บเป็น path สัมพัทธ์)</summary>
    public string? ProjectLogo { get; init; }
    public bool Active { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public class CreateProjectRequest
{
    [Required(ErrorMessage = "กรุณากรอกชื่อโครงการ")]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    [MaxLength(50)]
    public string? ContactFaxPhone { get; set; }

    [MaxLength(255)]
    public string? ContactName { get; set; }

    [MaxLength(50)]
    public string? ContactPhone { get; set; }

    public bool? Active { get; set; } = true;
}

/// <summary>PartialType ของเดิม — ทุกฟิลด์ไม่บังคับ</summary>
public class UpdateProjectRequest
{
    [MaxLength(255)]
    public string? Name { get; set; }

    public string? Address { get; set; }

    [MaxLength(50)]
    public string? ContactFaxPhone { get; set; }

    [MaxLength(255)]
    public string? ContactName { get; set; }

    [MaxLength(50)]
    public string? ContactPhone { get; set; }

    public bool? Active { get; set; }
}

/// <summary>ผลของการอัปโหลดโลโก้ — ระบบเดิมคืนแค่ 3 ฟิลด์นี้</summary>
public class ProjectLogoResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? ProjectLogo { get; init; }
}

/// <summary>gv_settings — อัตราค่าบริการของโครงการ</summary>
public class SettingResponse
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    /// <summary>ค่าส่วนกลางต่อตารางวา</summary>
    public decimal? MaintenanceFeeRate { get; init; }
    /// <summary>ค่าเบี้ยปรับ (%)</summary>
    public decimal? FineRate { get; init; }
    /// <summary>ค่าทวงถามต่อเดือน (บาท)</summary>
    public decimal? CollectionFee { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public string? InvoicePrefix { get; init; }
    public string? ReceiptPrefix { get; init; }
    public string? InvoiceNote { get; init; }
    public string? ReceiptNote { get; init; }
    /// <summary>ส่วนลดรายปี (%)</summary>
    public decimal? DiscountRate { get; init; }
    public bool Active { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public class UpsertSettingRequest
{
    public int? ProjectId { get; set; }
    public decimal? MaintenanceFeeRate { get; set; }
    public decimal? FineRate { get; set; }
    public decimal? CollectionFee { get; set; }
    public DateOnly? EffectiveDate { get; set; }

    [MaxLength(10)]
    public string? InvoicePrefix { get; set; }

    [MaxLength(10)]
    public string? ReceiptPrefix { get; set; }

    public string? InvoiceNote { get; set; }
    public string? ReceiptNote { get; set; }
    public decimal? DiscountRate { get; set; }
    public bool? Active { get; set; }
}

/// <summary>gv_payment_methods — วิธีการชำระเงิน</summary>
public class PaymentMethodResponse
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Active { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public class UpsertPaymentMethodRequest
{
    [Required(ErrorMessage = "กรุณากรอกรหัสวิธีการชำระเงิน")]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกชื่อวิธีการชำระเงิน")]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool? Active { get; set; } = true;
}

/// <summary>gv_notice_template — เทมเพลตใบทวงถาม (เนื้อหาเป็น HTML)</summary>
public class NoticeTemplateResponse
{
    public int Id { get; init; }
    public string TopicName { get; init; } = string.Empty;
    public string? IntroductionDetail { get; init; }
    public string? AdditionalDetail { get; init; }
    public string? ClosureText { get; init; }
    public string ClosingWord { get; init; } = string.Empty;
    public string? Remark { get; init; }
    public int ProjectId { get; init; }
    public bool Active { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// ระบบเดิมโหลด relations: ['project'] ในเส้นทางนี้ จึงมี object โครงการติดมาด้วย
    /// (ต่างจาก gv_settings ที่ RELATIONS_SETTING = [] จึงไม่มี)
    /// </summary>
    public ProjectEmbedded? Project { get; init; }
}

public class UpsertNoticeTemplateRequest
{
    [Required(ErrorMessage = "กรุณากรอกชื่อหัวข้อ")]
    [MaxLength(50)]
    public string TopicName { get; set; } = string.Empty;

    public string? IntroductionDetail { get; set; }
    public string? AdditionalDetail { get; set; }
    public string? ClosureText { get; set; }

    [MaxLength(255)]
    public string? ClosingWord { get; set; }

    public string? Remark { get; set; }
    public int? ProjectId { get; set; }
    public bool? Active { get; set; } = true;
}

/// <summary>
/// รายการตัวเลือกสำหรับ dropdown — ระบบเดิมคืนอาร์เรย์นี้ตรง ๆ ไม่ห่อซอง
/// </summary>
public class OptionItem
{
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
}
