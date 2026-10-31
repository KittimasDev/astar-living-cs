using AstarLiving.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AstarLiving.Api.Common;

/// <summary>
/// แปลง exception เป็น body รูปแบบเดียวกับที่ NestJS ส่งออกมา
/// { "statusCode": 401, "message": "ไม่พบผู้ใช้งานในระบบ", "error": "Unauthorized" }
///
/// frontend เดิมอ่าน error.response.data.message มาแสดงเป็น notification
/// ถ้าเปลี่ยนรูปนี้ ข้อความแจ้งเตือนภาษาไทยทั้งระบบจะหายไปหมด
/// </summary>
public class NestStyleExceptionFilter(ILogger<NestStyleExceptionFilter> logger) : IExceptionFilter
{
    private readonly ILogger<NestStyleExceptionFilter> _logger = logger;

    public void OnException(ExceptionContext context)
    {
        var (statusCode, message) = context.Exception switch
        {
            AppException app => (app.StatusCode, app.Message),
            OperationCanceledException => (499, "คำขอถูกยกเลิก"),
            _ => (500, "เกิดข้อผิดพลาด กรุณาลองใหม่อีกครั้งภายหลัง"),
        };

        if (statusCode >= 500)
        {
            _logger.LogError(context.Exception, "คำขอ {Method} {Path} ล้มเหลว",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        }

        context.Result = new ObjectResult(new ApiError
        {
            StatusCode = statusCode,
            Message = message,
            Error = HttpStatusCatalog.ErrorName(statusCode),
        })
        {
            StatusCode = statusCode,
        };

        context.ExceptionHandled = true;
    }
}

/// <summary>
/// แปลงผลการตรวจ DTO ให้เหมือน ValidationPipe ของ NestJS
/// ซึ่งคืน message เป็น "อาร์เรย์ของข้อความ" ไม่ใช่ปัญหาแบบ ProblemDetails
/// </summary>
public static class ValidationResponseFactory
{
    public static IActionResult Create(ActionContext context)
    {
        var messages = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? $"{entry.Key} is invalid"
                    : error.ErrorMessage))
            .ToList();

        return new ObjectResult(new ApiError
        {
            StatusCode = 400,
            Message = messages,
            Error = HttpStatusCatalog.ErrorName(400),
        })
        {
            StatusCode = 400,
        };
    }
}
