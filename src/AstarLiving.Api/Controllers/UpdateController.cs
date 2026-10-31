using AstarLiving.Api.Configuration;
using AstarLiving.Shared.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AstarLiving.Api.Controllers;

/// <summary>
/// นโยบายการอัปเดตที่เครื่องแม่ประกาศให้เครื่องลูก
///
/// หลักการเดียวที่ทำให้ระบบนี้ปลอดภัย: เครื่องลูกดึงอัปเดตจากเครื่องแม่เท่านั้น
/// และเครื่องแม่จะยังไม่วางไฟล์ feed ให้เห็นจนกว่าตัวเองจะอัปเดตเสร็จและผ่าน health check
/// เครื่องลูกจึงไม่มีทางอัปเดตแซงเครื่องแม่ได้ และไม่ต้องมีอินเทอร์เน็ตเลย
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("v1/update-policy")]
[Tags("System")]
public class UpdateController(IOptions<AstarLivingOptions> options) : ControllerBase
{
    private readonly AstarLivingOptions _options = options.Value;

    [HttpGet]
    public UpdatePolicyResponse Get()
    {
        var feedRoot = UpdateFeed.Root;

        // ถือว่าพร้อมปล่อยอัปเดตก็ต่อเมื่อมีไฟล์รายการเวอร์ชันวางอยู่จริง
        // ServerUpdater เป็นผู้วางไฟล์นี้เป็นขั้นตอนสุดท้ายหลังเครื่องแม่อัปเดตตัวเองสำเร็จ
        var available = System.IO.File.Exists(System.IO.Path.Combine(feedRoot, UpdateFeed.ReleasesFileName));

        return new UpdatePolicyResponse
        {
            AllowedMaxVersion = ProductVersion,
            ServerVersion = ProductVersion,
            UpdatesAvailable = available,
            FeedUrl = BuildFeedUrl(),
        };
    }

    /// <summary>
    /// ที่อยู่ feed ที่เครื่องลูกใช้ ประกอบจากที่อยู่ที่เครื่องลูกเรียกเข้ามาจริง
    /// ทำแบบนี้เพราะเครื่องแม่ไม่รู้ว่าตัวเองถูกเรียกด้วยไอพีไหนในเครือข่ายของลูกค้า
    /// </summary>
    private string BuildFeedUrl()
    {
        var request = HttpContext.Request;
        var host = request.Host.HasValue ? request.Host.Value : "127.0.0.1";

        return $"{request.Scheme}://{host}/api/updates";
    }

    private static string ProductVersion =>
        typeof(UpdateController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

/// <summary>ที่อยู่ของไฟล์ feed บนเครื่องแม่ ใช้ร่วมกันระหว่าง API และ ServerUpdater</summary>
public static class UpdateFeed
{
    /// <summary>ไฟล์รายการเวอร์ชันที่ Velopack อ่าน</summary>
    public const string ReleasesFileName = "releases.win.json";

    /// <summary>
    /// ชื่อชั่วคราวระหว่างที่ยังไม่พร้อมปล่อย
    /// ServerUpdater ดาวน์โหลดมาวางด้วยชื่อนี้ก่อน แล้วค่อยเปลี่ยนชื่อเป็นชื่อจริง
    /// การเปลี่ยนชื่อไฟล์เป็น atomic ทำให้เครื่องลูกไม่มีทางอ่านไฟล์ที่ยังโหลดไม่เสร็จ
    /// </summary>
    public const string StagedFileName = "releases.win.json.staged";

    public static string Root { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AstarLiving",
        "updates");
}
