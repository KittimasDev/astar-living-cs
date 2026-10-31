using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Entities;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace AstarLiving.Api.Modules.Maintenance;

/// <summary>
/// สำรองฐานข้อมูลและดาวน์โหลด — เฉพาะผู้ดูแลระบบสูงสุด
///
/// ไฟล์ที่ได้มีข้อมูลส่วนบุคคลของลูกบ้านทั้งโครงการ รวมถึงรหัสผ่านที่เข้ารหัสไว้ของผู้ใช้ทุกคน
/// จึงติดการคุมสิทธิ์ไว้ที่ระดับคลาส เมธอดที่เพิ่มทีหลังจะได้ถูกกั้นไปด้วยโดยอัตโนมัติ
/// </summary>
[ApiController]
[Route("v1/system/backup")]
[Authorize(Policy = AuthorizationPolicies.DatabaseBackup)]
[Tags("System")]
public class BackupController(
    BackupService backups,
    SystemStatusService status,
    BackupArtifactStore store,
    AstarLivingDbContext db,
    ThaiCalendar calendar,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>สถานะระบบสำหรับแสดงบนหน้าจอ อ่านอย่างเดียว เรียกซ้ำได้</summary>
    [HttpGet("status")]
    public Task<SystemStatusResponse> Status(CancellationToken ct) =>
        status.GetAsync(currentUser.Require().ProjectId, ct);

    /// <summary>
    /// สั่งสำรองฐานข้อมูล ณ ขณะนั้น คืนบัตรผ่านสำหรับดาวน์โหลด ไม่ใช่ตัวไฟล์
    /// เป็น POST เพราะสร้างของใหม่จริง และทำให้ได้บันทึกการทำงานอัตโนมัติ
    /// </summary>
    [HttpPost]
    public Task<BackupCreatedResponse> Create(CancellationToken ct) =>
        backups.CreateAsync(currentUser.Require(), ct);

    /// <summary>
    /// ดาวน์โหลดไฟล์ที่สำรองไว้ แล้วลบทิ้งจากเครื่องแม่
    ///
    /// รับเฉพาะบัตรผ่าน ไม่รับชื่อไฟล์หรือที่อยู่ใด ๆ การเดินข้ามโฟลเดอร์จึงเป็นไปไม่ได้
    /// </summary>
    [HttpGet("{token:length(32)}/download")]
    public async Task<IActionResult> Download(string token, CancellationToken ct)
    {
        if (!Guid.TryParseExact(token, "N", out var parsed))
        {
            throw AppException.NotFound("ลิงก์ดาวน์โหลดไม่ถูกต้อง");
        }

        var user = currentUser.Require();
        var now = new DateTimeOffset(calendar.NowBangkok, TimeSpan.FromHours(7));
        var artifact = store.Take(parsed, user.UserId, now);

        // จังหวะที่ข้อมูลออกจากเครื่องแม่จริง ๆ คือตรงนี้ ไม่ใช่ตอนสร้างไฟล์
        // ตัวบันทึกอัตโนมัติดักเฉพาะคำสั่งที่เปลี่ยนข้อมูล จึงต้องเขียนเองตรงนี้
        await WriteDownloadLogAsync(artifact, user, ct);

        // ไฟล์อาจใหญ่หลายร้อยเมกะไบต์และปลายทางอาจเป็นแฟลชไดรฟ์ที่เขียนช้า
        // ถ้าไม่ปลดเพดานอัตราการส่ง Kestrel จะตัดการเชื่อมต่อทิ้งกลางคัน
        var rate = HttpContext.Features.Get<IHttpMinResponseDataRateFeature>();
        if (rate is not null) rate.MinDataRate = null;

        // DeleteOnClose ทำให้ไฟล์หายแน่นอนแม้ผู้ใช้ปิดโปรแกรมกลางคันหรือคำขอถูกตัด
        // ซึ่งเชื่อถือได้กว่าการสั่งลบทีหลังที่พลาดได้ถ้าโปรเซสถูกฆ่า
        var stream = new FileStream(
            artifact.Path, FileMode.Open, FileAccess.Read, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        return File(stream, "application/octet-stream", artifact.FileName, enableRangeProcessing: false);
    }

    private async Task WriteDownloadLogAsync(BackupArtifact artifact, CurrentUser user, CancellationToken ct)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            UserId = user.UserId,
            ProjectId = user.ProjectId,
            Action = $"DOWNLOAD v1/system/backup ({artifact.SizeBytes} bytes)",
            TargetTable = "backup",
            Timestamp = calendar.NowBangkok,
        });

        await db.SaveChangesAsync(ct);
    }
}
