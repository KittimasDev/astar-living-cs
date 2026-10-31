using System.Net.Http;
namespace AstarLiving.ServerUpdater;

/// <summary>
/// ตรวจและอัปเดตตามรอบเวลา
///
/// ตรวจครั้งแรกหลังเริ่มบริการ 2 นาที เพื่อไม่ให้ไปแย่งทรัพยากรตอนเครื่องกำลังบูต
/// ซึ่งเป็นช่วงที่ฐานข้อมูลกับ API กำลังเริ่มพร้อมกัน
/// </summary>
public sealed class UpdateWorker(UpdateRunner runner, UpdaterOptions options, ILogger<UpdateWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "บริการอัปเดตเริ่มทำงาน ตรวจทุก {Interval} ชั่วโมง",
            options.CheckInterval.TotalHours);

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var outcome = await runner.RunOnceAsync(stoppingToken);

                // Offline กับ UpToDate เป็นผลปกติของไซต์ทั่วไป จึงไม่ต้องบันทึกให้รก
                if (outcome is not (UpdateOutcome.UpToDate or UpdateOutcome.Offline))
                {
                    logger.LogInformation("ผลการตรวจอัปเดต: {Outcome}", outcome);
                }

                await Task.Delay(options.CheckInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // บริการถูกสั่งหยุด เป็นการจบงานตามปกติ
        }
    }
}

/// <summary>
/// เฝ้าดูว่า API ยังมีชีวิตอยู่
///
/// Windows มีกลไก sc failure ที่เริ่มบริการใหม่เมื่อโปรเซสตายอยู่แล้ว
/// แต่จับไม่ได้เมื่อโปรเซสยังอยู่แต่ไม่ตอบสนอง ซึ่งเป็นอาการที่เจอบ่อยกว่า
/// ตัวนี้จึงเฝ้าจากภายนอกด้วยการเรียก health check จริง
/// </summary>
public sealed class ApiWatchdog(
    UpdaterOptions options,
    WindowsServiceControl services,
    IHttpClientFactory httpClientFactory,
    ILogger<ApiWatchdog> logger) : BackgroundService
{
    /// <summary>ต้องล้มเหลวติดกันกี่ครั้งจึงจะสั่งเริ่มใหม่ กันการเริ่มใหม่เพราะสะดุดชั่วคราว</summary>
    private const int FailuresBeforeRestart = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = httpClientFactory.CreateClient("health");
        var consecutiveFailures = 0;

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var alive = await IsAliveAsync(client, stoppingToken);

                if (alive)
                {
                    consecutiveFailures = 0;
                }
                else if (++consecutiveFailures >= FailuresBeforeRestart)
                {
                    logger.LogWarning(
                        "บริการเบื้องหลังไม่ตอบสนอง {Count} ครั้งติดกัน กำลังสั่งเริ่มใหม่",
                        consecutiveFailures);

                    await services.StartAsync(options.ApiServiceName, stoppingToken);
                    consecutiveFailures = 0;
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // บริการถูกสั่งหยุด
        }
    }

    private async Task<bool> IsAliveAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            // ใช้ health/live ไม่ใช่ health/ready เพราะ ready จะเป็นเท็จระหว่าง migration
            // ซึ่งไม่ใช่เหตุให้สั่งเริ่มบริการใหม่ กลับจะทำให้ migration ถูกตัดกลางคัน
            var url = options.HealthUrl.Replace("health/ready", "health/live", StringComparison.OrdinalIgnoreCase);

            using var response = await client.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
