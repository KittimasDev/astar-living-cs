using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Enums;

namespace AstarLiving.Api.Auth;

/// <summary>
/// ผู้ใช้ที่กำลังเรียก API — แทน @CurrentUser() ของเดิม
/// ต่างจากเดิมตรงที่มี Role และ ProjectId จริง (ของเดิมทิ้งไปตอน validate)
/// </summary>
public sealed record CurrentUser(int UserId, string Username, UserRole Role, int? ProjectId)
{
    public bool IsSuperAdmin => Role == UserRole.SUPERADMIN;

    /// <summary>
    /// โครงการที่ผู้ใช้มีสิทธิ์ — ใช้บังคับ scope ทุก query
    /// SUPERADMIN ไม่ผูกกับโครงการใด จึงคืน null และต้องระบุ project_id มาเอง
    /// </summary>
    public int RequireProjectId() =>
        ProjectId ?? throw AppException.NotFound("ไม่พบโครงการของผู้ใช้งาน");
}

public interface ICurrentUserAccessor
{
    /// <summary>คืน null เมื่อคำขอนั้นไม่ได้ผ่านการยืนยันตัวตน</summary>
    CurrentUser? Current { get; }

    /// <summary>ใช้ใน endpoint ที่บังคับ auth อยู่แล้ว</summary>
    CurrentUser Require();
}

public class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public CurrentUser? Current
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true) return null;

            var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                          ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(subject, out var userId)) return null;

            var username = principal.FindFirstValue(AstarClaims.Username) ?? string.Empty;

            if (!Enum.TryParse<UserRole>(principal.FindFirstValue(AstarClaims.Role), out var role))
            {
                return null;
            }

            var projectClaim = principal.FindFirstValue(AstarClaims.ProjectId);
            int? projectId = int.TryParse(projectClaim, out var parsed) ? parsed : null;

            return new CurrentUser(userId, username, role, projectId);
        }
    }

    public CurrentUser Require() =>
        Current ?? throw AppException.Unauthorized("กรุณาเข้าสู่ระบบใหม่");
}
