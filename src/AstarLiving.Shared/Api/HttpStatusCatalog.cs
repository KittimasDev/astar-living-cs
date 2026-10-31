namespace AstarLiving.Shared.Api;

/// <summary>รหัสและข้อความมาตรฐาน — พอร์ตจาก HTTP_STATUS ของระบบเดิม</summary>
public readonly record struct HttpStatusEntry(int StatusCode, string Message);

public static class HttpStatusCatalog
{
    public static readonly HttpStatusEntry Ok = new(200, "Success");
    public static readonly HttpStatusEntry Created = new(201, "Created successfully");
    public static readonly HttpStatusEntry Accepted = new(202, "Accepted");
    public static readonly HttpStatusEntry NoContent = new(204, "No content");

    public static readonly HttpStatusEntry BadRequest = new(400, "Bad request");
    public static readonly HttpStatusEntry Unauthorized = new(401, "Unauthorized");
    public static readonly HttpStatusEntry Forbidden = new(403, "Forbidden");
    public static readonly HttpStatusEntry NotFound = new(404, "Not found");
    public static readonly HttpStatusEntry NotAcceptable = new(406, "Not acceptable");
    public static readonly HttpStatusEntry Conflict = new(409, "Conflict");
    public static readonly HttpStatusEntry UnprocessableEntity = new(422, "Unprocessable entity");

    public static readonly HttpStatusEntry InternalServerError = new(500, "Internal server error");
    public static readonly HttpStatusEntry BadGateway = new(502, "Bad gateway");
    public static readonly HttpStatusEntry ServiceUnavailable = new(503, "Service unavailable");
    public static readonly HttpStatusEntry GatewayTimeout = new(504, "Gateway timeout");

    /// <summary>ชื่อ error ที่ NestJS ใส่ในฟิลด์ "error" ของ body</summary>
    public static string ErrorName(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        426 => "Upgrade Required",
        500 => "Internal Server Error",
        _ => "Error",
    };
}

/// <summary>ตัวช่วยสร้างซองตอบกลับให้สั้นลงใน controller</summary>
public static class ApiResponseFactory
{
    public static ApiResponse<T> Create<T>(HttpStatusEntry status, T? body, bool includeTimestamp = false) =>
        new()
        {
            StatusCode = status.StatusCode,
            Message = status.Message,
            Timestamp = includeTimestamp ? DateTimeOffset.UtcNow.ToString("o") : null,
            Body = body,
        };

    public static ApiResponse<T> CreateFind<T>(
        HttpStatusEntry status, T? body, PageInformation pageInformation, bool includeTimestamp = false) =>
        new()
        {
            StatusCode = status.StatusCode,
            Message = status.Message,
            Timestamp = includeTimestamp ? DateTimeOffset.UtcNow.ToString("o") : null,
            Body = body,
            PageInformation = pageInformation,
        };

    public static ApiResponse<T> Ok<T>(T? body) => Create(HttpStatusCatalog.Ok, body);

    /// <summary>
    /// "ไม่พบข้อมูล" แบบระบบเดิม: HTTP ยังเป็น 200 แต่ statusCode ใน body เป็น 404
    /// </summary>
    public static ApiResponse<T?> NotFound<T>() => Create<T?>(HttpStatusCatalog.NotFound, default);
}
