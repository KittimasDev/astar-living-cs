using System.Text.Json.Serialization;

namespace AstarLiving.Shared.Api;

/// <summary>
/// ซองตอบกลับของ API — พอร์ตตรงจาก createResponse / createFindResponse ของระบบเดิม
/// รูปแบบคีย์ต้องเหมือนเป๊ะเพื่อให้เทียบผลกับ NestJS ตัวเดิมได้ระหว่างพัฒนา
/// </summary>
public class ApiResponse<T>
{
    /// <summary>
    /// รหัสสถานะ "ในเนื้อหา" ซึ่งไม่จำเป็นต้องตรงกับ HTTP status จริง
    /// ระบบเดิมคืน HTTP 200 พร้อม statusCode 404 ในกรณีไม่พบข้อมูล และตกลงกันแล้วว่าคงพฤติกรรมนี้ไว้
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>ใส่มาเฉพาะตอนที่ผู้เรียกขอ (พารามิเตอร์ times ของเดิม)</summary>
    [JsonPropertyName("timestamp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Timestamp { get; init; }

    [JsonPropertyName("body")]
    public T? Body { get; init; }

    [JsonPropertyName("page_information")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PageInformation? PageInformation { get; init; }
}

/// <summary>ข้อมูลการแบ่งหน้า — คีย์ทั้งหมดเป็น snake_case ตามของเดิม</summary>
public class PageInformation
{
    /// <summary>จำนวนแถวที่คืนในหน้านี้</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>ขนาดหน้า (เท่ากับ total เมื่อ no_limit)</summary>
    [JsonPropertyName("size")]
    public int Size { get; init; }

    /// <summary>จำนวนแถวทั้งหมดที่ตรงเงื่อนไข</summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("total_page")]
    public int TotalPage { get; init; }

    [JsonPropertyName("no_limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NoLimit { get; init; }

    [JsonPropertyName("next_page")]
    public bool NextPage { get; init; }

    [JsonPropertyName("prev_page")]
    public bool PrevPage { get; init; }

    [JsonPropertyName("order_field")]
    public string? OrderField { get; init; }

    [JsonPropertyName("order_type")]
    public string? OrderType { get; init; }
}

/// <summary>
/// รูปแบบ body ของข้อผิดพลาด — ตรงกับที่ NestJS โยนออกมาจาก HttpException
/// { "statusCode": 401, "message": "...", "error": "Unauthorized" }
/// frontend เดิมอ่าน error.response.data.message มาแสดง จึงต้องคงคีย์นี้ไว้
/// </summary>
public class ApiError
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("message")]
    public object Message { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}
