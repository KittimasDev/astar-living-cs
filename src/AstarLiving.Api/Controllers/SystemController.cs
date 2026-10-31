using System.Diagnostics;
using AstarLiving.Api.Configuration;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AstarLiving.Api.Controllers;

/// <summary>
/// เส้นทางระดับระบบ: health check และ handshake ของการคุมเวอร์ชัน
/// ทั้งหมดต้องตอบได้โดยไม่ต้องล็อกอิน และต้องตอบได้แม้ตอนกำลัง migrate ฐานข้อมูล
/// </summary>
[ApiController]
[AllowAnonymous]
public class SystemController(
    AstarLivingDbContext db,
    IOptions<AstarLivingOptions> options,
    IHostEnvironment environment) : ControllerBase
{
    private readonly AstarLivingDbContext _db = db;
    private readonly AstarLivingOptions _options = options.Value;
    private readonly IHostEnvironment _environment = environment;

    /// <summary>หน้าต้อนรับที่ราก /api</summary>
    [HttpGet("")]
    public ContentResult Welcome() => Content(
        $"A star Living API {ProductVersion} — {_environment.EnvironmentName}", "text/plain; charset=utf-8");

    /// <summary>โปรเซสยังมีชีวิตอยู่ ใช้เป็น watchdog ของ ServerUpdater</summary>
    [HttpGet("health/live")]
    public object Live() => new { status = "ok", version = ProductVersion };

    /// <summary>
    /// พร้อมให้บริการจริงหรือไม่ — ต้องต่อฐานข้อมูลได้ และ migration ต้องเสร็จแล้ว
    /// ServerUpdater ใช้เส้นทางนี้ตัดสินว่าการอัปเดตสำเร็จหรือต้องย้อนกลับ
    /// </summary>
    [HttpGet("health/ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var canConnect = await _db.Database.CanConnectAsync(ct);
        stopwatch.Stop();

        var healthy = canConnect && MigrationStateAccessor.Current == MigrationState.Ready;

        var body = new
        {
            status = healthy ? "ready" : "degraded",
            database = canConnect ? "up" : "down",
            latency_ms = stopwatch.ElapsedMilliseconds,
            migration_state = MigrationStateAccessor.Current.ToString(),
            version = ProductVersion,
        };

        return healthy ? Ok(body) : StatusCode(503, body);
    }

    /// <summary>
    /// ข้อมูลที่เครื่องลูกใช้ตัดสินว่าคุยกับเครื่องแม่เครื่องนี้ได้หรือไม่
    /// ต้องไม่ต้องยืนยันตัวตน เพราะ client เรียกตั้งแต่ก่อนหน้าจอล็อกอิน
    /// </summary>
    [HttpGet("v1/handshake")]
    public HandshakeResponse Handshake() => new()
    {
        ProductVersion = ProductVersion,
        ContractVersion = ProductContract.Current,
        MinClientContractVersion = ProductContract.MinimumClient,
        SchemaVersion = MigrationStateAccessor.SchemaVersion,
        MigrationState = MigrationStateAccessor.Current,
        AllowedMaxClientVersion = ProductVersion,
        ServerTimeUtc = DateTimeOffset.UtcNow,
        SiteName = _options.Site.Name,
    };

    private static string ProductVersion =>
        typeof(SystemController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

/// <summary>
/// สถานะ migration ที่แชร์กันทั้งโปรเซส
/// ตั้งค่าโดยขั้นตอน startup ที่รัน EF migration (จะเพิ่มในเฟสถัดไป)
/// </summary>
public static class MigrationStateAccessor
{
    public static MigrationState Current { get; set; } = MigrationState.Ready;

    public static string SchemaVersion { get; set; } = "legacy-baseline";
}
