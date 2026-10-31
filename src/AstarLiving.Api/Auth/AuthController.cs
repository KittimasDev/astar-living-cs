using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Auth;

/// <summary>
/// พอร์ตจาก src/modules/auth/auth.controller.ts — คง path เดิมทุกเส้นทาง
///
/// หมายเหตุเรื่องรูปแบบผลลัพธ์: login / logout / refresh-token ของระบบเดิม
/// คืน object ดิบ ไม่ได้ห่อซอง ส่วน me ห่อซอง ต้องคงความไม่สม่ำเสมอนี้ไว้
/// เพราะหน้าจอเดิมอ่านคนละแบบกัน
/// </summary>
[ApiController]
[Route("v1/auth")]
[Authorize]
public class AuthController(AuthService auth, ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly AuthService _auth = auth;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>เข้าสู่ระบบ</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<LoginResponse> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        await _auth.LoginAsync(request, ct);

    /// <summary>ออกจากระบบ — ระบบเดิมไม่มี token store จึงเป็นการตอบกลับเฉย ๆ</summary>
    [HttpPost("logout")]
    public LogoutResponse Logout()
    {
        var user = _currentUser.Require();
        return new LogoutResponse
        {
            Message = "ออกจากระบบสำเร็จ",
            User = new LogoutUser { UserId = user.UserId, Username = user.Username },
        };
    }

    /// <summary>ข้อมูลผู้ใช้ที่ล็อกอินอยู่</summary>
    [HttpGet("me")]
    public async Task<ApiResponse<CurrentUserResponse>> Me(CancellationToken ct)
    {
        var user = _currentUser.Require();
        return ApiResponseFactory.Ok(await _auth.GetProfileAsync(user.UserId, ct));
    }

    /// <summary>ขอ access token ใหม่จาก refresh token</summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<RefreshTokenResponse> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct) =>
        await _auth.RefreshAsync(request.RefreshToken, ct);

    /// <summary>เปลี่ยนรหัสผ่านของตัวเอง</summary>
    [HttpPatch("change-password")]
    public async Task<MessageResponse> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct) =>
        await _auth.ChangePasswordAsync(_currentUser.Require(), request, ct);

    /// <summary>
    /// รีเซ็ตรหัสผ่านให้ผู้ใช้คนอื่น
    /// ระบบเดิมเปิดให้ผู้ใช้ที่ล็อกอินคนไหนก็เรียกได้ ระบบใหม่จำกัดไว้ที่ผู้ดูแล
    /// </summary>
    [HttpPost("reset-password/{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UserManagement)]
    public async Task<MessageResponse> ResetPassword(int id, CancellationToken ct) =>
        await _auth.ResetPasswordAsync(_currentUser.Require(), id, ct);
}
