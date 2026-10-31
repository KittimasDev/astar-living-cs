using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using AstarLiving.Shared.Versioning;
using Microsoft.Extensions.Logging;

namespace AstarLiving.Client.Services;

/// <summary>
/// แนบโทเคนให้ทุก request และต่ออายุให้อัตโนมัติเมื่อหมดอายุ
///
/// พอร์ตพฤติกรรมมาจาก axios interceptor ของเว็บเดิม: เจอ 401 ครั้งแรก
/// ให้เรียก refresh-token แล้วยิง request เดิมซ้ำหนึ่งครั้ง ถ้ายังไม่ผ่านจึงถือว่าหลุดจริง
///
/// ต่างจากของเดิมตรงที่หลาย request ที่เจอ 401 พร้อมกันจะ refresh แค่ครั้งเดียว
/// แล้วใช้โทเคนใหม่ร่วมกัน ไม่ใช่ต่างคนต่าง refresh จนโทเคนตีกันเอง
/// ประตูที่กันไว้อยู่ใน AuthSession ซึ่งเป็น singleton
/// เพราะ HttpClientFactory ทิ้งและสร้าง handler ใหม่ทุก 2 นาที
/// </summary>
public sealed class AuthTokenHandler(
    TokenStore tokens,
    AuthSession session,
    IHttpClientFactory httpClientFactory,
    ILogger<AuthTokenHandler> logger) : DelegatingHandler
{
    /// <summary>ชื่อ HttpClient ที่ไม่ผ่าน handler ตัวนี้ ใช้เรียก refresh เพื่อไม่ให้วนซ้ำ</summary>
    public const string RawClientName = "AstarLiving.Raw";

    private static readonly string ClientVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(ProductContract.ContractHeader, ProductContract.Current.ToString());
        request.Headers.TryAddWithoutValidation(ProductContract.ClientVersionHeader, ClientVersion);

        // เส้นทางของการยืนยันตัวตนเองไม่ต้องแนบโทเคนและไม่ต้องต่ออายุ
        var isAuthRoute = IsAuthRoute(request.RequestUri);

        // เก็บเนื้อหาไว้ก่อน เพราะถ้าต้องยิงซ้ำ stream เดิมจะถูกอ่านไปแล้ว
        if (request.Content is not null) await request.Content.LoadIntoBufferAsync();

        if (!isAuthRoute) ApplyBearer(request, tokens.Current?.AccessToken);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || isAuthRoute) return response;

        var staleToken = request.Headers.Authorization?.Parameter;
        var refreshed = await RefreshAsync(staleToken, cancellationToken);

        if (refreshed is null)
        {
            session.NotifySignedOut();
            return response;
        }

        response.Dispose();

        var retry = await CloneAsync(request);
        ApplyBearer(retry, refreshed);

        return await base.SendAsync(retry, cancellationToken);
    }

    /// <summary>
    /// คืน access token ตัวใหม่ หรือ null เมื่อต่ออายุไม่ได้
    /// <paramref name="staleToken"/> คือโทเคนที่เพิ่งโดนปฏิเสธ ใช้ดูว่ามีคนอื่นต่ออายุให้ไปแล้วหรือยัง
    /// </summary>
    private async Task<string?> RefreshAsync(string? staleToken, CancellationToken cancellationToken)
    {
        await session.RefreshGate.WaitAsync(cancellationToken);
        try
        {
            var currentToken = tokens.Current?.AccessToken;

            // มีคนอื่นต่ออายุให้แล้วระหว่างที่รอคิว ใช้ของเขาได้เลย
            if (!string.IsNullOrEmpty(currentToken) && currentToken != staleToken) return currentToken;

            var refreshToken = tokens.Current?.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken)) return null;

            var raw = httpClientFactory.CreateClient(RawClientName);

            using var result = await raw.PostAsJsonAsync(
                "api/v1/auth/refresh-token",
                new RefreshTokenRequest { RefreshToken = refreshToken },
                AstarJson.Options,
                cancellationToken);

            if (!result.IsSuccessStatusCode)
            {
                logger.LogInformation("ต่ออายุโทเคนไม่สำเร็จ ({Status})", (int)result.StatusCode);
                tokens.Clear();
                return null;
            }

            var payload = await result.Content.ReadFromJsonAsync<RefreshTokenResponse>(AstarJson.Options, cancellationToken);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            {
                tokens.Clear();
                return null;
            }

            tokens.UpdateAccessToken(payload.AccessToken);
            return payload.AccessToken;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // เครือข่ายล่มระหว่างต่ออายุ ไม่ใช่เหตุให้ลบโทเคนทิ้ง เพราะอาจแค่หลุดชั่วคราว
            logger.LogWarning(ex, "ต่ออายุโทเคนไม่ได้เพราะติดต่อเครื่องแม่ข่ายไม่ได้");
            return null;
        }
        finally
        {
            session.RefreshGate.Release();
        }
    }

    private static void ApplyBearer(HttpRequestMessage request, string? token)
    {
        request.Headers.Authorization = string.IsNullOrEmpty(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    private static bool IsAuthRoute(Uri? uri)
    {
        if (uri is null) return false;

        var path = uri.AbsolutePath;
        return path.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/auth/refresh-token", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        if (source.Content is not null)
        {
            var buffer = await source.Content.ReadAsByteArrayAsync();
            var content = new ByteArrayContent(buffer);

            foreach (var header in source.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)source.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        return clone;
    }
}
