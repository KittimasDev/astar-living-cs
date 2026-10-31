using AstarLiving.Api.Common;
using AstarLiving.Domain.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Auth;

/// <summary>
/// พอร์ตจาก src/modules/auth/auth.service.ts
/// ข้อความแสดงข้อผิดพลาดทุกบรรทัดคงข้อความไทยเดิมไว้ เพราะหน้าจอเอาไปแสดงตรง ๆ
/// </summary>
public class AuthService(
    AstarLivingDbContext db,
    JwtTokenService tokens,
    IClock clock,
    ILogger<AuthService> logger)
{
    /// <summary>ระบบเดิมใช้ bcrypt cost 12 — ต้องใช้ค่าเดิมเพื่อให้ hash ที่มีอยู่ตรวจผ่าน</summary>
    private const int BcryptWorkFactor = 12;

    private readonly AstarLivingDbContext _db = db;
    private readonly JwtTokenService _tokens = tokens;
    private readonly IClock _clock = clock;
    private readonly ILogger<AuthService> _logger = logger;

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        // ระบบเดิม trim + ทำเป็นตัวพิมพ์เล็กก่อนค้นหาเสมอ
        var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct)
                   ?? throw AppException.Unauthorized("ไม่พบผู้ใช้งานในระบบ");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw AppException.Unauthorized("รหัสผ่านไม่ถูกต้อง");
        }

        return new LoginResponse
        {
            AccessToken = _tokens.CreateAccessToken(user),
            RefreshToken = _tokens.CreateRefreshToken(user),
        };
    }

    public async Task<RefreshTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var userId = _tokens.ValidateRefreshToken(refreshToken);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.Unauthorized("Refresh token ไม่ถูกต้องหรือหมดอายุ");

        // ต่างจากระบบเดิมที่เซ็น token ใหม่จากข้อมูลใน token เก่า
        // ระบบใหม่อ่านสิทธิ์ล่าสุดจากฐานข้อมูล ผู้ใช้ที่ถูกปรับ role หรือปิดใช้งานจึงมีผลทันที
        if (!user.Active)
        {
            throw AppException.Unauthorized("บัญชีผู้ใช้งานนี้ถูกปิดการใช้งาน");
        }

        return new RefreshTokenResponse { AccessToken = _tokens.CreateAccessToken(user) };
    }

    public async Task<CurrentUserResponse> GetProfileAsync(int userId, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Project)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw AppException.NotFound("ไม่พบผู้ใช้งาน");

        return ToResponse(user);
    }

    /// <summary>
    /// เปลี่ยนรหัสผ่าน
    ///
    /// หมายเหตุ: ระบบเดิมเทียบ confirm_password กับ "รหัสผ่านเดิม" ไม่ใช่รหัสผ่านใหม่
    /// ดูแปลกแต่เป็นพฤติกรรมที่หน้าจอเดิมส่งข้อมูลมาให้สอดคล้องอยู่แล้ว จึงคงไว้
    /// </summary>
    public async Task<MessageResponse> ChangePasswordAsync(
        CurrentUser current, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, ct)
                   ?? throw AppException.NotFound("ไม่พบผู้ใช้งาน");

        if (request.OldPassword != request.ConfirmPassword)
        {
            throw AppException.Forbidden("รหัสผ่านเดิมและยืนยันรหัสผ่านต้องตรงกัน");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash))
        {
            throw AppException.Forbidden("รหัสผ่านเดิมไม่ถูกต้อง");
        }

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
        {
            throw AppException.Forbidden("รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสผ่านเดิม");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, BcryptWorkFactor);
        user.UpdatedBy = current.UserId;
        user.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new MessageResponse { Message = "เปลี่ยนรหัสผ่านสำเร็จ" };
    }

    /// <summary>
    /// รีเซ็ตรหัสผ่านให้ผู้ใช้คนอื่น
    ///
    /// ต่างจากระบบเดิม 2 จุดตามข้อ 2 ของตารางบั๊กในแผน:
    /// 1) ระบบเดิมผู้ใช้ที่ล็อกอินคนไหนก็เรียกได้ ระบบใหม่จำกัดที่ระดับ policy
    /// 2) ระบบเดิมคืนรหัสผ่านใหม่เป็น plaintext ใน message ระบบใหม่ยังคืนให้ผู้ดูแลแจ้งต่อ
    ///    แต่บันทึก log ว่าใครรีเซ็ตให้ใคร เพื่อให้ตรวจสอบย้อนหลังได้
    /// </summary>
    public async Task<MessageResponse> ResetPasswordAsync(CurrentUser current, int targetUserId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct)
                   ?? throw AppException.NotFound("ไม่พบผู้ใช้งานที่ต้องการรีเซ็ตรหัสผ่าน");

        // ผู้ดูแลระดับโครงการรีเซ็ตได้เฉพาะคนในโครงการตัวเอง
        if (!current.IsSuperAdmin && user.ProjectId != current.ProjectId)
        {
            throw AppException.Forbidden("ไม่มีสิทธิ์รีเซ็ตรหัสผ่านของผู้ใช้งานนอกโครงการ");
        }

        var password = PasswordGenerator.Generate();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
        user.UpdatedBy = current.UserId;
        user.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ผู้ใช้ {ActorId} รีเซ็ตรหัสผ่านให้ผู้ใช้ {TargetId}", current.UserId, targetUserId);

        return new MessageResponse { Message = $"รีเซ็ตรหัสผ่านสำเร็จ รหัสใหม่คือ \"{password}\"" };
    }

    public static CurrentUserResponse ToResponse(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        Email = user.Email,
        Phone = user.Phone,
        Role = user.Role.ToString(),
        ProjectId = user.ProjectId,
        Active = user.Active,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
        Project = user.Project is null
            ? null
            : new ProjectSummary
            {
                Id = user.Project.Id,
                Name = user.Project.Name,
                Address = user.Project.Address,
                ContactName = user.Project.ContactName,
                ContactPhone = user.Project.ContactPhone,
                Active = user.Project.Active,
            },
    };
}
