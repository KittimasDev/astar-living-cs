using AstarLiving.Api.Auth;
using AstarLiving.Domain.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Common;

/// <summary>
/// บันทึกทุกคำสั่งที่เปลี่ยนแปลงข้อมูลลงตาราง gv_activity_logs
///
/// แทนที่ LogActionInterceptor ของระบบเดิม ซึ่งมีปัญหาสามข้อ
/// 1. ต้องติด decorator รายเมธอด เมธอดที่ลืมติดจึงไม่มีบันทึกเลย — ตัวนี้ดักทุกคำสั่งอัตโนมัติ
/// 2. เขียน user_id เป็น 0 เสมอ เพราะอ่าน request.user.id แต่ payload ของ JWT ใช้ชื่อ userId
/// 3. เขียนแบบไม่รอผล ถ้าบันทึกพลาดก็เงียบหาย — ตัวนี้บันทึกไว้ใน log ของระบบแทน
///
/// บันทึกหลังคำสั่งทำงานสำเร็จเท่านั้น คำขอที่ล้มเหลวไม่ถือเป็นการกระทำที่เกิดขึ้นจริง
/// </summary>
public sealed class ActivityLogFilter(
    AstarLivingDbContext db,
    ICurrentUserAccessor currentUser,
    IClock clock,
    ILogger<ActivityLogFilter> logger) : IAsyncActionFilter
{
    /// <summary>เมธอดที่ถือว่าเปลี่ยนแปลงข้อมูล การอ่านไม่ต้องบันทึก</summary>
    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PATCH", "PUT", "DELETE" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        if (!ShouldLog(context, executed)) return;

        try
        {
            var user = currentUser.Current;
            if (user is null) return;

            db.ActivityLogs.Add(new ActivityLog
            {
                UserId = user.UserId,
                ProjectId = user.ProjectId,
                Action = $"{context.HttpContext.Request.Method} {context.ActionDescriptor.AttributeRouteInfo?.Template}",
                TargetTable = ResolveTargetTable(context),
                TargetId = ResolveTargetId(context),

                // คอลัมน์นี้เป็น timestamp ไม่มีโซนเวลา และของเดิมเขียนเวลาไทยลงไป
                Timestamp = clock.NowBangkok,
            });

            await db.SaveChangesAsync(context.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is DbUpdateException or OperationCanceledException)
        {
            // บันทึกไม่ได้ต้องไม่ทำให้คำสั่งที่ผู้ใช้สั่งไปแล้วล้มตาม
            logger.LogWarning(ex, "บันทึกประวัติการใช้งานไม่สำเร็จ");
        }
    }

    private static bool ShouldLog(ActionExecutingContext context, ActionExecutedContext executed)
    {
        if (!MutatingMethods.Contains(context.HttpContext.Request.Method)) return false;
        if (executed.Exception is not null && !executed.ExceptionHandled) return false;

        // การเข้าสู่ระบบและการต่ออายุโทเคนเกิดถี่มากและไม่ใช่การแก้ข้อมูล
        var template = context.ActionDescriptor.AttributeRouteInfo?.Template ?? string.Empty;

        return !template.Contains("auth/login", StringComparison.OrdinalIgnoreCase)
            && !template.Contains("auth/refresh-token", StringComparison.OrdinalIgnoreCase)
            && !template.Contains("auth/logout", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>เดาชื่อตารางจากชื่อ controller เช่น ResidentController → gv_residents</summary>
    private static string? ResolveTargetTable(ActionExecutingContext context)
    {
        var controller = context.RouteData.Values["controller"] as string;
        return string.IsNullOrEmpty(controller) ? null : controller.ToLowerInvariant();
    }

    /// <summary>รหัสของแถวที่ถูกกระทำ มาจากพารามิเตอร์ชื่อ id ในเส้นทาง</summary>
    private static int? ResolveTargetId(ActionExecutingContext context)
    {
        foreach (var key in new[] { "id", "paymentId", "residentId", "projectId" })
        {
            if (context.ActionArguments.TryGetValue(key, out var value) && value is int id) return id;
        }

        return null;
    }
}
