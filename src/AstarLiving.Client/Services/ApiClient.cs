using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AstarLiving.Shared.Api;
using Microsoft.Extensions.Logging;

namespace AstarLiving.Client.Services;

/// <summary>ผลลัพธ์แบบแบ่งหน้า คู่กับ page_information ที่ API ส่งมา</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, PageInformation? Page)
{
    public static PagedResult<T> Empty { get; } = new([], null);

    public int Total => Page?.Total ?? Items.Count;

    public int TotalPage => Page?.TotalPage ?? (Items.Count == 0 ? 0 : 1);
}

/// <summary>
/// ตัวเรียก API ตัวเดียวของทั้งโปรแกรม
///
/// หน้าที่หลักคือแกะ "ซอง" ของระบบเดิมออกให้หน้าจอไม่ต้องรู้จักมัน
/// จุดที่ต้องระวัง: ระบบเดิมคืน HTTP 200 พร้อม statusCode 404 ในเนื้อหาเมื่อไม่พบข้อมูล
/// จึงต้องดูรหัสในเนื้อหาด้วย ไม่ใช่ดูแค่ HTTP status
/// </summary>
public sealed class ApiClient(HttpClient http, ClientSettings settings, AuthSession session, ILogger<ApiClient> logger)
{
    // ต้องเป็นชุดเดียวกับฝั่ง API ไม่อย่างนั้นคีย์ snake_case จะอ่านไม่ติดและได้ค่าว่างเงียบ ๆ
    private static readonly JsonSerializerOptions JsonOptions = AstarJson.Options;

    public HttpClient Http => http;

    // ── อ่านข้อมูล ──────────────────────────────────────────────────────────

    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Get, path, body: null, ct);

    /// <summary>
    /// อ่านเส้นทางที่ไม่ได้ห่อซอง
    ///
    /// ระบบเดิมห่อซองไม่ครบทุกเส้นทาง (login logout refresh-token คืน object ดิบ)
    /// เส้นทางระดับระบบที่เพิ่มใหม่อย่าง handshake กับ update-policy ก็คืนดิบเช่นกัน
    /// เพราะต้องอ่านได้แม้ตอนที่ระบบยังไม่พร้อม การใช้ GetAsync กับเส้นทางพวกนี้
    /// จะได้ค่า null กลับมาเงียบ ๆ แล้วไปพังที่อื่นแทน
    /// </summary>
    public async Task<T> GetRawAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await ExecuteAsync(HttpMethod.Get, path, body: null, ct);

        if (!response.IsSuccessStatusCode) throw await BuildHttpErrorAsync(response, ct);

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                ?? throw new ApiException("เครื่องแม่ข่ายตอบกลับข้อมูลว่างเปล่า", response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new ApiException("เครื่องแม่ข่ายตอบกลับข้อมูลที่อ่านไม่ได้", response.StatusCode, null, ex);
        }
    }

    /// <summary>ใช้เมื่อ "ไม่พบข้อมูล" เป็นผลลัพธ์ปกติ ไม่ใช่ข้อผิดพลาด</summary>
    public async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<T>(path, ct);
        }
        catch (ApiException ex) when (ex.BodyStatusCode == 404 || ex.Status == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    public async Task<PagedResult<T>> GetPagedAsync<T>(string path, CancellationToken ct = default)
    {
        var envelope = await SendEnvelopeAsync<List<T>>(HttpMethod.Get, path, body: null, ct);

        // "ไม่พบข้อมูล" ในหน้ารายการแปลว่าตารางว่าง ไม่ใช่ความผิดพลาด
        if (envelope.StatusCode == 404) return PagedResult<T>.Empty;

        EnsureBodyOk(envelope.StatusCode, envelope.Message, null);

        return new PagedResult<T>(envelope.Body ?? [], envelope.PageInformation);
    }

    // ── เขียนข้อมูล ─────────────────────────────────────────────────────────

    public Task<T> PostAsync<T>(string path, object? body = null, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Post, path, body, ct);

    public Task<T> PatchAsync<T>(string path, object? body = null, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Patch, path, body, ct);

    public Task<T> DeleteAsync<T>(string path, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Delete, path, body: null, ct);

    /// <summary>
    /// เรียกเส้นทางที่ไม่สนใจผลลัพธ์ เช่น logout
    /// ยังตรวจรหัสในเนื้อหาให้ เพื่อให้ข้อผิดพลาดไม่เงียบหายไป
    /// </summary>
    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await ExecuteAsync(method, path, body, ct);
        await ReadEnvelopeAsync<object>(response, ct);
    }

    // ── เอกสาร ─────────────────────────────────────────────────────────────

    /// <summary>ดึงไฟล์ PDF จากเส้นทางพิมพ์เอกสาร</summary>
    public async Task<byte[]> GetFileAsync(string path, CancellationToken ct = default)
    {
        using var response = await ExecuteAsync(HttpMethod.Get, path, body: null, ct);
        return await ReadFileAsync(response, ct);
    }

    public async Task<byte[]> PostFileAsync(string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await ExecuteAsync(HttpMethod.Post, path, body, ct);
        return await ReadFileAsync(response, ct);
    }

    /// <summary>อัปโหลดไฟล์แบบ multipart เช่น โลโก้โครงการ</summary>
    public async Task<T> UploadAsync<T>(string path, string filePath, string fieldName = "file", CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        var file = new ByteArrayContent(bytes);
        file.Headers.TryAddWithoutValidation("Content-Type", GuessContentType(filePath));
        content.Add(file, fieldName, Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        using var response = await SendRequestAsync(request, ct);

        var envelope = await ReadEnvelopeAsync<T>(response, ct);
        return envelope.Body!;
    }

    // ── ส่วนกลาง ───────────────────────────────────────────────────────────

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var envelope = await SendEnvelopeAsync<T>(method, path, body, ct);
        EnsureBodyOk(envelope.StatusCode, envelope.Message, null);
        return envelope.Body!;
    }

    private async Task<ApiResponse<T>> SendEnvelopeAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await ExecuteAsync(method, path, body, ct);
        return await ReadEnvelopeAsync<T>(response, ct);
    }

    private async Task<HttpResponseMessage> ExecuteAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        return await SendRequestAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            // เครื่องแม่บอกว่าโปรแกรมเวอร์ชันนี้คุยด้วยไม่ได้แล้ว
            // แจ้งให้หน้าจอหลักขึ้นกล่องบังคับอัปเดต แทนที่จะปล่อยให้ผู้ใช้เจอข้อผิดพลาดซ้ำ ๆ
            if (response.StatusCode == HttpStatusCode.UpgradeRequired)
            {
                session.NotifyUpgradeRequired(await ReadUpgradeMessageAsync(response, ct));
            }

            return response;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ApiException.Timeout(settings.ServerUrl, ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "เรียก {Method} {Path} ไม่สำเร็จ", request.Method, request.RequestUri);
            throw ApiException.Connectivity(settings.ServerUrl, ex);
        }
    }

    /// <summary>อ่านข้อความไทยที่เครื่องแม่ส่งมาพร้อม 426 เพื่อบอกผู้ใช้ว่าต้องทำอะไร</summary>
    private static async Task<string> ReadUpgradeMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct);
            var message = ExtractMessage(error?.Message);

            return string.IsNullOrWhiteSpace(message)
                ? "โปรแกรมของคุณเป็นเวอร์ชันเก่า ต้องอัปเดตก่อนใช้งาน"
                : message;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            return "โปรแกรมของคุณเป็นเวอร์ชันเก่า ต้องอัปเดตก่อนใช้งาน";
        }
    }

    private async Task<ApiResponse<T>> ReadEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw await BuildHttpErrorAsync(response, ct);

        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions, ct);

            return envelope ?? throw new ApiException("เครื่องแม่ข่ายตอบกลับข้อมูลว่างเปล่า", response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new ApiException("เครื่องแม่ข่ายตอบกลับข้อมูลที่อ่านไม่ได้", response.StatusCode, null, ex);
        }
    }

    private async Task<byte[]> ReadFileAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw await BuildHttpErrorAsync(response, ct);

        var mediaType = response.Content.Headers.ContentType?.MediaType;

        // เส้นทางพิมพ์เอกสารจะคืน JSON กลับมาเมื่อหาข้อมูลไม่เจอ แทนที่จะเป็นไฟล์
        if (mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var envelope = await ReadEnvelopeAsync<object>(response, ct);
            EnsureBodyOk(envelope.StatusCode, envelope.Message, null);
            throw new ApiException("ไม่พบเอกสารที่ต้องการพิมพ์", response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static async Task<ApiException> BuildHttpErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var message = DefaultMessageFor(response.StatusCode);

        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct);
            var fromBody = ExtractMessage(error?.Message);
            if (!string.IsNullOrWhiteSpace(fromBody)) message = fromBody;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            // เนื้อหาไม่ใช่ JSON ก็ใช้ข้อความมาตรฐานตาม HTTP status ไป
        }

        return new ApiException(message, response.StatusCode);
    }

    /// <summary>
    /// ตรวจรหัสที่อยู่ในเนื้อหา ซึ่งอาจเป็น 404 ทั้งที่ HTTP เป็น 200
    /// เป็นพฤติกรรมของระบบเดิมที่ตกลงกันแล้วว่าคงไว้
    /// </summary>
    private static void EnsureBodyOk(int bodyStatusCode, string message, HttpStatusCode? httpStatus)
    {
        if (bodyStatusCode < 400) return;

        throw new ApiException(
            string.IsNullOrWhiteSpace(message) ? DefaultMessageForCode(bodyStatusCode) : message,
            httpStatus,
            bodyStatusCode);
    }

    /// <summary>
    /// NestJS ส่ง message เป็นได้ทั้งข้อความเดียวและอาร์เรย์ของข้อความ (กรณี validation)
    /// ฝั่งเว็บเดิมแสดงเฉพาะตัวแรก จึงทำแบบเดียวกัน
    /// </summary>
    private static string? ExtractMessage(object? raw)
    {
        if (raw is null) return null;

        if (raw is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Array when element.GetArrayLength() > 0 => element[0].GetString(),
                _ => null,
            };
        }

        return raw.ToString();
    }

    private static string DefaultMessageFor(HttpStatusCode status) => DefaultMessageForCode((int)status);

    private static string DefaultMessageForCode(int code) => code switch
    {
        400 => "ข้อมูลที่ส่งไปไม่ถูกต้อง",
        401 => "กรุณาเข้าสู่ระบบใหม่อีกครั้ง",
        403 => "บัญชีนี้ไม่มีสิทธิ์ใช้งานส่วนนี้",
        404 => "ไม่พบข้อมูลที่ต้องการ",
        409 => "ข้อมูลนี้มีอยู่แล้วในระบบ",
        426 => "โปรแกรมของคุณเป็นเวอร์ชันเก่า ต้องอัปเดตก่อนใช้งาน",
        >= 500 => "เครื่องแม่ข่ายทำงานผิดพลาด กรุณาลองใหม่อีกครั้ง",
        _ => "เกิดข้อผิดพลาด กรุณาลองใหม่อีกครั้ง",
    };

    private static string GuessContentType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}

/// <summary>ประกอบ query string โดยข้ามค่าที่ว่าง เพื่อให้ URL ตรงกับที่เว็บเดิมยิง</summary>
public sealed class QueryBuilder(string path)
{
    private readonly List<string> _parts = [];

    public QueryBuilder Add(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        return this;
    }

    public QueryBuilder Add(string key, int? value) =>
        value.HasValue ? Add(key, value.Value.ToString(CultureInfo.InvariantCulture)) : this;

    public QueryBuilder Add(string key, decimal? value) =>
        value.HasValue ? Add(key, value.Value.ToString(CultureInfo.InvariantCulture)) : this;

    public QueryBuilder Add(string key, bool? value) =>
        value.HasValue ? Add(key, value.Value ? "true" : "false") : this;

    public QueryBuilder Add(string key, DateOnly? value) =>
        value.HasValue ? Add(key, value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : this;

    /// <summary>ใส่ตัวกรองพื้นฐานที่ทุกหน้ารายการใช้ร่วมกัน</summary>
    public QueryBuilder AddFilter(BaseFilter filter) =>
        Add("limit", filter.Limit)
            .Add("page", filter.Page)
            .Add("active", filter.Active)
            .Add("no_limit", filter.NoLimit ? true : null)
            .Add("order_field", filter.OrderField)
            .Add("order_type", filter.OrderType);

    public override string ToString()
    {
        if (_parts.Count == 0) return path;

        var builder = new StringBuilder(path);
        builder.Append(path.Contains('?', StringComparison.Ordinal) ? '&' : '?');
        builder.AppendJoin('&', _parts);

        return builder.ToString();
    }

    public static implicit operator string(QueryBuilder builder) => builder.ToString();
}
