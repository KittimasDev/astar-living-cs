using System.IdentityModel.Tokens.Jwt;
using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Api.Configuration;
using AstarLiving.Domain.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AstarLiving.Api.Tests;

/// <summary>
/// ทดสอบเส้นทางล็อกอินสำเร็จและการออก token โดยไม่แตะฐานข้อมูลของลูกค้า
/// ใช้ผู้ใช้จำลองที่ hash ด้วย bcrypt cost 12 แบบเดียวกับข้อมูลจริง
/// </summary>
public class AuthServiceTests
{
    private const string Password = "AstarLiving#2568";

    private static readonly AstarLivingOptions Options = new()
    {
        Jwt = new JwtOptions
        {
            AccessSecret = "test-access-secret-that-is-long-enough-0123456789",
            RefreshSecret = "test-refresh-secret-that-is-long-enough-0123456789",
            AccessExpires = TimeSpan.FromHours(1),
            RefreshExpires = TimeSpan.FromDays(1),
        },
    };

    private static (AuthService Service, JwtTokenService Tokens, AstarLivingDbContext Db) Build(
        params User[] users)
    {
        var db = new AstarLivingDbContext(
            new DbContextOptionsBuilder<AstarLivingDbContext>()
                .UseInMemoryDatabase($"auth-{Guid.NewGuid()}")
                .Options);

        db.Users.AddRange(users);
        db.SaveChanges();

        var tokens = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options));
        var clock = new FixedClock(new DateTime(2025, 11, 19, 10, 0, 0));
        var service = new AuthService(db, tokens, clock, NullLogger<AuthService>.Instance);

        return (service, tokens, db);
    }

    private static User MakeUser(
        int id = 1,
        string username = "astar",
        UserRole role = UserRole.ADMIN,
        int? projectId = 8,
        bool active = true) => new()
        {
            Id = id,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 12),
            FullName = "ผู้ทดสอบ ระบบ",
            Role = role,
            ProjectId = projectId,
            Active = active,
        };

    [Fact]
    public async Task Login_returns_both_tokens()
    {
        var (service, _, _) = Build(MakeUser());

        var result = await service.LoginAsync(
            new LoginRequest { Username = "astar", Password = Password }, default);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.AccessToken.Should().NotBe(result.RefreshToken);
    }

    /// <summary>
    /// ระบบเดิมทิ้ง claim role ทำให้บังคับสิทธิ์ไม่ได้เลย
    /// access token ของระบบใหม่ต้องมี role และ project_id ติดไปด้วย
    /// </summary>
    [Fact]
    public async Task Access_token_carries_role_and_project()
    {
        var (service, _, _) = Build(MakeUser(role: UserRole.MANAGER, projectId: 8));

        var result = await service.LoginAsync(
            new LoginRequest { Username = "astar", Password = Password }, default);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        token.Claims.Should().Contain(c => c.Type == AstarClaims.Role && c.Value == "MANAGER");
        token.Claims.Should().Contain(c => c.Type == AstarClaims.ProjectId && c.Value == "8");
        token.Claims.Should().Contain(c => c.Type == AstarClaims.TokenType && c.Value == AstarClaims.AccessTokenType);
    }

    [Fact]
    public async Task Login_trims_and_lowercases_the_username()
    {
        var (service, _, _) = Build(MakeUser(username: "astar"));

        var result = await service.LoginAsync(
            new LoginRequest { Username = "  ASTAR  ", Password = Password }, default);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_use_the_legacy_thai_messages()
    {
        var (service, _, _) = Build(MakeUser());

        var unknown = await Assert.ThrowsAsync<AppException>(() => service.LoginAsync(
            new LoginRequest { Username = "ไม่มีคนนี้", Password = Password }, default));
        unknown.StatusCode.Should().Be(401);
        unknown.Message.Should().Be("ไม่พบผู้ใช้งานในระบบ");

        var wrong = await Assert.ThrowsAsync<AppException>(() => service.LoginAsync(
            new LoginRequest { Username = "astar", Password = "ผิด" }, default));
        wrong.StatusCode.Should().Be(401);
        wrong.Message.Should().Be("รหัสผ่านไม่ถูกต้อง");
    }

    /// <summary>refresh token ต้องแลก access token ใหม่ได้</summary>
    [Fact]
    public async Task Refresh_issues_a_new_access_token()
    {
        var (service, _, _) = Build(MakeUser());
        var login = await service.LoginAsync(
            new LoginRequest { Username = "astar", Password = Password }, default);

        var refreshed = await service.RefreshAsync(login.RefreshToken, default);

        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
        new JwtSecurityTokenHandler().ReadJwtToken(refreshed.AccessToken)
            .Claims.Should().Contain(c => c.Type == AstarClaims.TokenType && c.Value == AstarClaims.AccessTokenType);
    }

    /// <summary>
    /// ช่องโหว่ของระบบเดิม: access token กับ refresh token เซ็นด้วยกุญแจและ payload เดียวกัน
    /// ทำให้ refresh token ใช้เรียก API ได้เลย ระบบใหม่ต้องปฏิเสธ
    /// </summary>
    [Fact]
    public async Task Access_token_cannot_be_used_as_a_refresh_token()
    {
        var (service, _, _) = Build(MakeUser());
        var login = await service.LoginAsync(
            new LoginRequest { Username = "astar", Password = Password }, default);

        var act = () => service.RefreshAsync(login.AccessToken, default);

        (await act.Should().ThrowAsync<AppException>())
            .Which.Message.Should().Be("Refresh token ไม่ถูกต้องหรือหมดอายุ");
    }

    /// <summary>ผู้ใช้ที่ถูกปิดการใช้งานต้องต่ออายุ session ไม่ได้ แม้ refresh token ยังไม่หมดอายุ</summary>
    [Fact]
    public async Task Deactivated_user_cannot_refresh()
    {
        var (service, _, db) = Build(MakeUser());
        var login = await service.LoginAsync(
            new LoginRequest { Username = "astar", Password = Password }, default);

        var user = await db.Users.SingleAsync();
        user.Active = false;
        await db.SaveChangesAsync();

        var act = () => service.RefreshAsync(login.RefreshToken, default);

        (await act.Should().ThrowAsync<AppException>())
            .Which.Message.Should().Be("บัญชีผู้ใช้งานนี้ถูกปิดการใช้งาน");
    }

    [Fact]
    public async Task Change_password_rejects_when_confirm_does_not_match_the_old_password()
    {
        var (service, _, _) = Build(MakeUser());
        var current = new CurrentUser(1, "astar", UserRole.ADMIN, 8);

        var act = () => service.ChangePasswordAsync(current, new ChangePasswordRequest
        {
            OldPassword = Password,
            NewPassword = "รหัสผ่านใหม่123",
            ConfirmPassword = "ไม่ตรงกับรหัสเดิม",
        }, default);

        (await act.Should().ThrowAsync<AppException>())
            .Which.Message.Should().Be("รหัสผ่านเดิมและยืนยันรหัสผ่านต้องตรงกัน");
    }

    [Fact]
    public async Task Change_password_updates_the_hash()
    {
        var (service, _, db) = Build(MakeUser());
        var current = new CurrentUser(1, "astar", UserRole.ADMIN, 8);
        const string newPassword = "รหัสผ่านใหม่123";

        var result = await service.ChangePasswordAsync(current, new ChangePasswordRequest
        {
            OldPassword = Password,
            NewPassword = newPassword,
            ConfirmPassword = Password,
        }, default);

        result.Message.Should().Be("เปลี่ยนรหัสผ่านสำเร็จ");

        var user = await db.Users.SingleAsync();
        BCrypt.Net.BCrypt.Verify(newPassword, user.PasswordHash).Should().BeTrue();
        user.UpdatedBy.Should().Be(1);
    }

    /// <summary>ผู้ดูแลระดับโครงการต้องรีเซ็ตรหัสผ่านให้คนนอกโครงการตัวเองไม่ได้</summary>
    [Fact]
    public async Task Reset_password_is_scoped_to_the_actors_project()
    {
        var (service, _, _) = Build(
            MakeUser(id: 1, username: "admin8", projectId: 8),
            MakeUser(id: 2, username: "admin9", projectId: 9));

        var actor = new CurrentUser(1, "admin8", UserRole.ADMIN, 8);

        var act = () => service.ResetPasswordAsync(actor, targetUserId: 2, default);

        (await act.Should().ThrowAsync<AppException>())
            .Which.Message.Should().Be("ไม่มีสิทธิ์รีเซ็ตรหัสผ่านของผู้ใช้งานนอกโครงการ");
    }

    [Fact]
    public async Task Reset_password_returns_a_usable_new_password()
    {
        var (service, _, db) = Build(MakeUser(id: 2, username: "polly", projectId: 8, role: UserRole.USER));
        var actor = new CurrentUser(1, "admin8", UserRole.ADMIN, 8);

        var result = await service.ResetPasswordAsync(actor, targetUserId: 2, default);

        // ข้อความมีรูปแบบเดิม: รีเซ็ตรหัสผ่านสำเร็จ รหัสใหม่คือ "xxxxxxxx"
        var generated = result.Message.Split('"')[1];
        generated.Should().HaveLength(8);

        var user = await db.Users.SingleAsync(u => u.Id == 2);
        BCrypt.Net.BCrypt.Verify(generated, user.PasswordHash).Should().BeTrue();
    }
}
