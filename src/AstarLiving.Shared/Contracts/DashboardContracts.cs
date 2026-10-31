using System.Text.Json.Serialization;

namespace AstarLiving.Shared.Contracts;

/// <summary>การ์ดตัวเลขด้านบนของหน้าแดชบอร์ด</summary>
public class DashboardSummaryResponse
{
    /// <summary>จำนวนลูกบ้านที่เปิดใช้งาน</summary>
    public int TotalResident { get; init; }

    /// <summary>ยอดค่าส่วนกลางที่ออกบิลไปทั้งหมด (ไม่รวมค่าปรับและค่าทวงถาม)</summary>
    public decimal TotalBilled { get; init; }

    public decimal TotalPaid { get; init; }

    public decimal TotalUnpaid { get; init; }

    /// <summary>อัตราการชำระเป็นเปอร์เซ็นต์ ทศนิยม 2 ตำแหน่ง</summary>
    public decimal PaymentRate { get; init; }
}

/// <summary>หนึ่งจุดบนกราฟ ใช้ทั้งกราฟแท่งรายเดือนและกราฟวงกลมวิธีชำระ</summary>
public class DashboardPointResponse
{
    public string Label { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

public class DashboardOutstandingResponse
{
    public int ResidentId { get; init; }
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    /// <summary>จำนวนงวดที่ค้าง</summary>
    public int Count { get; init; }
    public decimal AmountDue { get; init; }
    public decimal Fine { get; init; }
    public decimal CollectionFee { get; init; }
    public decimal TotalDue { get; init; }
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// ผลของ GET /api/v1/dashboard
/// สองคีย์กลางเป็น camelCase มาแต่เดิม จึงต้องระบุชื่อไว้ไม่ให้ถูกแปลงเป็น snake_case
/// </summary>
public class DashboardResponse
{
    [JsonPropertyName("summary")]
    public DashboardSummaryResponse Summary { get; init; } = new();

    [JsonPropertyName("monthlyTrend")]
    public List<DashboardPointResponse> MonthlyTrend { get; init; } = [];

    [JsonPropertyName("paymentMethod")]
    public List<DashboardPointResponse> PaymentMethod { get; init; } = [];

    [JsonPropertyName("outstandings")]
    public List<DashboardOutstandingResponse> Outstandings { get; init; } = [];
}
