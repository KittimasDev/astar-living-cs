using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AstarLiving.Shared.Api;
using Microsoft.Extensions.Logging;

namespace AstarLiving.Client.Services;

/// <summary>สถานะของเครื่องแม่ข่ายที่เครื่องลูกมองเห็น</summary>
public enum ServerState
{
    /// <summary>ต่อไม่ติดเลย — เครื่องแม่ยังไม่เปิด หรืออยู่คนละเครือข่าย</summary>
    Unreachable,

    /// <summary>ต่อติดแล้วแต่ยังให้บริการไม่ได้ เช่น ฐานข้อมูลยังไม่ขึ้น</summary>
    Starting,

    /// <summary>กำลังปรับปรุงฐานข้อมูลหลังอัปเดตเวอร์ชัน</summary>
    Migrating,

    /// <summary>ปรับปรุงฐานข้อมูลไม่สำเร็จ ต้องให้คนเข้าไปดู</summary>
    Failed,

    /// <summary>พร้อมใช้งาน</summary>
    Ready,
}

public sealed record ServerStatus(ServerState State, string Message);

/// <summary>
/// รอให้เครื่องแม่ข่ายพร้อมก่อนเปิดให้เข้าสู่ระบบ
///
/// เหตุผลที่ต้องมี: ผู้ใช้เปิดเครื่องแล้วกดไอคอนโปรแกรมทันที
/// ตอนนั้นบริการฐานข้อมูลกับบริการ API ยังเริ่มไม่เสร็จ (ใช้เวลาราว 20-40 วินาที)
/// ถ้าไม่รอ ผู้ใช้จะเจอ "เชื่อมต่อเครื่องแม่ข่ายไม่ได้" ทั้งที่ไม่มีอะไรเสีย
/// แล้วต้องมานั่งกดเองซ้ำ ซึ่งเป็นสิ่งเดียวกับที่ลูกค้าไม่อยากเจอจาก docker
///
/// ตัวนี้ไม่โยนข้อผิดพลาด เพราะ "ต่อไม่ติด" คือคำตอบที่ถูกต้องอย่างหนึ่ง ไม่ใช่ความผิดพลาด
/// </summary>
public sealed class ServerReadiness(ApiClient api, ILogger<ServerReadiness> logger)
{
    /// <summary>ระยะห่างระหว่างการถามแต่ละครั้ง ถี่กว่านี้ไม่ได้ช่วยอะไรเพราะบริการใช้เวลาเริ่มเป็นสิบวินาที</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// รอจนกว่าเครื่องแม่จะพร้อม หรือจนกว่าจะเลิกรอ
    ///
    /// 3 นาทีครอบคลุมการบูตเครื่องแม่ที่ช้าที่สุดเท่าที่วัดได้ รวมเวลาที่ฐานข้อมูลกู้ WAL
    /// หลังไฟดับ ซึ่งเป็นกรณีที่ใช้เวลานานที่สุดและเป็นกรณีที่ผู้ใช้ต้องการให้รอมากที่สุดด้วย
    /// </summary>
    public async Task<ServerStatus> WaitAsync(
        IProgress<ServerStatus>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(3));
        var status = await ProbeAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            progress?.Report(status);

            // สองสถานะนี้ไม่มีทางหายไปเองด้วยการรอ จึงต้องคืนทันทีให้ผู้ใช้ตัดสินใจ
            if (status.State is ServerState.Ready or ServerState.Failed) return status;

            if (DateTimeOffset.UtcNow >= deadline) return status;

            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return status;
            }

            status = await ProbeAsync(ct);
        }

        return status;
    }

    /// <summary>ถามครั้งเดียวว่าตอนนี้เครื่องแม่อยู่ในสถานะไหน</summary>
    public async Task<ServerStatus> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            // ใช้ HttpClient ตรง ๆ ไม่ผ่าน GetRawAsync เพราะเส้นทางนี้ตอบ 503 ตอนยังไม่พร้อม
            // ซึ่งเป็นคำตอบที่เราต้องการอ่านเนื้อหา ไม่ใช่ข้อผิดพลาดที่ต้องโยนทิ้ง
            using var response = await api.Http.GetAsync("api/health/ready", ct);

            var body = await response.Content.ReadFromJsonAsync<ReadyResponse>(AstarJson.Options, ct);

            if (body is null) return new(ServerState.Starting, "เครื่องแม่ข่ายกำลังเริ่มทำงาน กรุณารอสักครู่...");

            return body.MigrationState switch
            {
                "Migrating" => new(ServerState.Migrating, "กำลังปรับปรุงฐานข้อมูล กรุณารอสักครู่..."),

                "Failed" => new(ServerState.Failed,
                    "ระบบฐานข้อมูลมีปัญหา กรุณาติดต่อผู้ดูแลระบบ"),

                _ when response.IsSuccessStatusCode => new(ServerState.Ready, string.Empty),

                _ => new(ServerState.Starting, "เครื่องแม่ข่ายกำลังเริ่มทำงาน กรุณารอสักครู่..."),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ถามสถานะเครื่องแม่ข่ายไม่ได้");

            return new(ServerState.Unreachable,
                "กำลังรอเครื่องแม่ข่ายเริ่มทำงาน หากเพิ่งเปิดเครื่องอาจใช้เวลาสักครู่...");
        }
    }

    /// <summary>เนื้อหาที่ /api/health/ready ตอบกลับ อ่านเฉพาะส่วนที่ใช้ตัดสินใจ</summary>
    private sealed record ReadyResponse
    {
        [JsonPropertyName("migration_state")]
        public string? MigrationState { get; init; }
    }
}
