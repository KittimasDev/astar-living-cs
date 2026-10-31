using System.Text.Json;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Versioning;

namespace AstarLiving.Api.Common;

/// <summary>
/// กั้นไม่ให้โปรแกรมเวอร์ชันที่คุยกันคนละภาษาเรียก API ได้
///
/// สถานการณ์ที่เกิดจริง: ผู้ใช้เปิดโปรแกรมค้างไว้ข้ามคืน คืนนั้นเครื่องแม่อัปเดตตัวเอง
/// เช้ามาโปรแกรมที่ค้างอยู่ยังเป็นเวอร์ชันเก่า ถ้าไม่กั้นไว้จะได้ข้อมูลผิดรูปแบบ
/// แล้วพังแบบหาสาเหตุไม่ได้ แทนที่จะบอกผู้ใช้ตรง ๆ ว่าให้กดอัปเดตก่อน
///
/// ตอบ 426 Upgrade Required ซึ่งฝั่งโปรแกรมดักไว้แล้วและขึ้นกล่องบังคับอัปเดตให้
/// เส้นทางที่ไม่ต้องยืนยันตัวตน (handshake, health) ถูกยกเว้น
/// ไม่อย่างนั้นโปรแกรมเก่าจะถามไม่ได้ด้วยซ้ำว่าตัวเองเก่าไปแล้ว
/// </summary>
public sealed class ContractVersionMiddleware(RequestDelegate next, ILogger<ContractVersionMiddleware> logger)
{
    private static readonly string[] ExemptPaths =
    [
        "/api/v1/handshake",
        "/api/health/live",
        "/api/health/ready",
        "/api/v1/update-policy",
        "/api/swagger",
        "/api/updates",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers[ProductContract.ContractHeader].FirstOrDefault();

        // ไม่ส่งหัวมาถือว่าไม่ใช่โปรแกรมของเรา (เช่น curl ตอนแก้ปัญหาหน้างาน) จึงปล่อยผ่าน
        // การกั้นนี้มีไว้ช่วยโปรแกรมของเราเอง ไม่ใช่กลไกความปลอดภัย
        if (string.IsNullOrWhiteSpace(header))
        {
            await next(context);
            return;
        }

        if (!int.TryParse(header, out var clientContract))
        {
            await next(context);
            return;
        }

        if (clientContract >= ProductContract.MinimumClient && clientContract <= ProductContract.Current)
        {
            await next(context);
            return;
        }

        var tooOld = clientContract < ProductContract.MinimumClient;

        logger.LogInformation(
            "ปฏิเสธคำขอจากโปรแกรมรุ่นสัญญา {Client} (เครื่องแม่รองรับ {Min} ถึง {Current})",
            clientContract, ProductContract.MinimumClient, ProductContract.Current);

        await WriteUpgradeRequiredAsync(context, tooOld);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPaths.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static async Task WriteUpgradeRequiredAsync(HttpContext context, bool clientTooOld)
    {
        if (context.Response.HasStarted) return;

        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        context.Response.ContentType = "application/json; charset=utf-8";

        var message = clientTooOld
            ? "โปรแกรมของคุณเป็นเวอร์ชันเก่า ต้องอัปเดตก่อนใช้งาน"
            : "เครื่องแม่ข่ายยังไม่ได้อัปเดต กรุณาอัปเดตเครื่องแม่ข่ายก่อน";

        await context.Response.WriteAsync(JsonSerializer.Serialize(
            new ApiError
            {
                StatusCode = StatusCodes.Status426UpgradeRequired,
                Message = message,
                Error = clientTooOld ? "ClientTooOld" : "ServerTooOld",
            },
            JsonDefaults.ThaiRelaxed));
    }
}
