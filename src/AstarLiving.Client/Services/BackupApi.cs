using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;

namespace AstarLiving.Client.Services;

/// <summary>
/// เรียกเส้นทางสำรองฐานข้อมูล
///
/// ไม่ใช้ <see cref="ApiClient"/> โดยตั้งใจ ด้วยเหตุผลสองข้อ
///
/// 1. ตัวเรียกหลักลองใหม่อัตโนมัติเมื่อเครื่องแม่ตอบผิดพลาด ซึ่งเหมาะกับการอ่านข้อมูล
///    แต่กับคำสั่งสำรอง การลองใหม่แปลว่าสั่ง pg_dump ซ้ำ กินดิสก์และกำลังเครื่องซ้ำซ้อน
/// 2. ตัวเรียกหลักตั้งเวลารอไว้ 60 วินาที ซึ่งดีสำหรับทุกหน้าจอ เพราะถ้าเครื่องแม่ล่ม
///    ผู้ใช้จะรู้ตัวภายในหนึ่งนาที แต่การสำรองฐานข้อมูลใหญ่กินเวลานานกว่านั้นได้
///    ตัวนี้จึงปลดเพดานเวลาแล้วคุมด้วยปุ่มยกเลิกของผู้ใช้แทน
/// </summary>
public sealed class BackupApi(IHttpClientFactory factory)
{
    public const string ClientName = "backup";

    private const string BasePath = "api/v1/system/backup";

    public async Task<SystemStatusResponse> GetStatusAsync(CancellationToken ct = default)
    {
        using var http = factory.CreateClient(ClientName);
        using var response = await http.GetAsync($"{BasePath}/status", ct);

        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<SystemStatusResponse>(AstarJson.Options, ct)
            ?? throw new ApiException("เครื่องแม่ข่ายตอบกลับข้อมูลว่างเปล่า");
    }

    /// <summary>สั่งสำรอง คืนบัตรผ่านสำหรับดาวน์โหลด ยังไม่ใช่ตัวไฟล์</summary>
    public async Task<BackupCreatedResponse> CreateAsync(CancellationToken ct = default)
    {
        using var http = factory.CreateClient(ClientName);
        using var response = await http.PostAsync(BasePath, content: null, ct);

        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<BackupCreatedResponse>(AstarJson.Options, ct)
            ?? throw new ApiException("เครื่องแม่ข่ายตอบกลับข้อมูลว่างเปล่า");
    }

    /// <summary>
    /// ดาวน์โหลดไฟล์ลงที่อยู่ที่ผู้ใช้เลือก
    ///
    /// เขียนลงไฟล์พักชื่อ .part ก่อน แล้วค่อยเปลี่ยนชื่อเมื่อครบ
    /// เพราะไฟล์สำรองที่ขาดครึ่งอันตรายกว่าไม่มีไฟล์เลย — ผู้ใช้จะเข้าใจผิดว่ามีข้อมูลสำรองอยู่
    /// แล้วไปรู้ตอนที่ต้องใช้จริงว่ากู้คืนไม่ได้
    ///
    /// อ่านทีละก้อนลงดิสก์ตรง ๆ ไม่โหลดทั้งไฟล์เข้าหน่วยความจำ
    /// ใช้หน่วยความจำคงที่ราว 80 กิโลไบต์ ไม่ว่าไฟล์จะใหญ่แค่ไหน
    /// </summary>
    public async Task<long> DownloadToFileAsync(
        string token, string targetPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        using var http = factory.CreateClient(ClientName);

        using var response = await http.GetAsync(
            $"{BasePath}/{token}/download", HttpCompletionOption.ResponseHeadersRead, ct);

        await EnsureSuccessAsync(response, ct);

        var partial = targetPath + ".part";

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = new FileStream(
                partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                    progress?.Report(written);
                }
            }

            File.Move(partial, targetPath, overwrite: true);

            return new FileInfo(targetPath).Length;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>
    /// เครื่องแม่ตอบข้อผิดพลาดมาเป็น JSON ที่มีข้อความไทยอยู่ข้างใน
    /// ต้องแกะออกมาแสดง ไม่ใช่โยนรหัสสถานะเปล่า ๆ ให้ผู้ใช้
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var message = $"เครื่องแม่ข่ายตอบกลับด้วยรหัส {(int)response.StatusCode}";

        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(AstarJson.Options, ct);
            if (!string.IsNullOrWhiteSpace(body?.Message)) message = body.Message;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // เนื้อหาไม่ใช่ JSON ก็ใช้ข้อความตั้งต้นไป
        }

        throw new ApiException(message, response.StatusCode);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ลบไฟล์พักไม่ได้ไม่ใช่เรื่องที่ต้องรบกวนผู้ใช้
        }
    }

    private sealed record ErrorBody
    {
        public string? Message { get; init; }
    }
}
