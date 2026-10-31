using AstarLiving.Shared.Api;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Common;

/// <summary>
/// ตัวกรองที่รับมาทาง query string
///
/// การ bind จาก query string ไม่ได้ใช้นโยบายตั้งชื่อของ JSON จึงต้องระบุชื่อพารามิเตอร์
/// ด้วย [FromQuery(Name = ...)] ทุกตัวเพื่อให้ตรงกับที่หน้าจอเดิมส่งมา
///
/// คลาสนี้ "ไม่" สืบทอด BaseFilter โดยตรง เพราะการประกาศ property ซ้อนด้วย new
/// จะทำให้ตัว binder เขียนค่าลง property ของคลาสลูก แต่โค้ดที่รับเป็น BaseFilter
/// ไปอ่าน property ของคลาสแม่ที่ยังเป็นค่าเริ่มต้น (บั๊กที่ทำให้ limit ไม่มีผล)
/// </summary>
public class BaseFilterQuery
{
    [FromQuery(Name = "limit")]
    public int Limit { get; set; } = Pagination.DefaultLimit;

    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    [FromQuery(Name = "active")]
    public bool? Active { get; set; }

    [FromQuery(Name = "no_limit")]
    public bool NoLimit { get; set; }

    [FromQuery(Name = "order_field")]
    public string? OrderField { get; set; }

    /// <summary>"ASC" หรือ "DESC" ค่าอื่นถือว่าไม่ได้ระบุ</summary>
    [FromQuery(Name = "order_type")]
    public string? OrderType { get; set; }

    public BaseFilter ToFilter() => new()
    {
        Limit = Limit,
        Page = Page,
        Active = Active,
        NoLimit = NoLimit,
        OrderField = OrderField,
        OrderType = OrderType,
    };
}

public class ProjectFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "name")]
    public string? Name { get; set; }
}

public class PaymentMethodFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "name")]
    public string? Name { get; set; }
}

public class NoticeTemplateFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "topic_name")]
    public string? TopicName { get; set; }

    [FromQuery(Name = "project_id")]
    public int? ProjectId { get; set; }
}

public class SettingFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "project_id")]
    public int? ProjectId { get; set; }
}

public class ResidentFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "full_name")]
    public string? FullName { get; set; }

    [FromQuery(Name = "house_no")]
    public string? HouseNo { get; set; }
}

/// <summary>ตัวกรองของรายการงวดค่าส่วนกลาง — ตรงกับ FilterPaymentDto ของเดิม</summary>
public class PaymentFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "resident_id")]
    public int? ResidentId { get; set; }

    [FromQuery(Name = "inv_no")]
    public string? InvNo { get; set; }

    [FromQuery(Name = "house_no")]
    public string? HouseNo { get; set; }

    [FromQuery(Name = "full_name")]
    public string? FullName { get; set; }

    /// <summary>เดือน 1-12 (ของเดิมรับมาเป็นสตริงแล้วแปลงเป็นตัวเลข)</summary>
    [FromQuery(Name = "month")]
    public int? Month { get; set; }

    /// <summary>ปี พ.ศ.</summary>
    [FromQuery(Name = "year")]
    public int? Year { get; set; }

    [FromQuery(Name = "advance")]
    public bool? Advance { get; set; }

    [FromQuery(Name = "status")]
    public AstarLiving.Domain.Enums.PaymentStatus? Status { get; set; }

    /// <summary>OVERDUE = เฉพาะงวดที่ยังค้างและมียอดมากกว่าศูนย์ · RECEIPT = ใช้ในหน้าออกใบเสร็จ</summary>
    [FromQuery(Name = "type")]
    public AstarLiving.Domain.Enums.PaymentFilterType? Type { get; set; }
}

/// <summary>ตัวกรองรายการใบเสร็จ — ตรงกับ FilterReceiptDto ของเดิม</summary>
public class ReceiptFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "resident_id")]
    public int? ResidentId { get; set; }

    [FromQuery(Name = "receipt_no")]
    public string? ReceiptNo { get; set; }

    [FromQuery(Name = "house_no")]
    public string? HouseNo { get; set; }

    [FromQuery(Name = "issue_start_date")]
    public DateOnly? IssueStartDate { get; set; }

    [FromQuery(Name = "issue_end_date")]
    public DateOnly? IssueEndDate { get; set; }
}

/// <summary>ตัวกรองใบทวงถามและหน้าลดหนี้ — ตรงกับ FilterCollectionDto ของเดิม</summary>
public class CollectionFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "resident_id")]
    public int? ResidentId { get; set; }

    [FromQuery(Name = "house_no")]
    public string? HouseNo { get; set; }

    [FromQuery(Name = "inv_no")]
    public string? InvNo { get; set; }

    [FromQuery(Name = "month")]
    public int? Month { get; set; }

    [FromQuery(Name = "year")]
    public int? Year { get; set; }

    [FromQuery(Name = "status")]
    public AstarLiving.Domain.Enums.NoticeStatus? Status { get; set; }

    /// <summary>
    /// สถานะของงวดที่ผูกอยู่ รับได้ทั้งแบบคั่นด้วยจุลภาคและแบบส่งซ้ำหลายครั้ง
    /// เช่น ?payment_status=NOT_PAY,PARTIAL_PAY
    /// </summary>
    [FromQuery(Name = "payment_status")]
    public List<AstarLiving.Domain.Enums.PaymentStatus>? PaymentStatus { get; set; }
}
