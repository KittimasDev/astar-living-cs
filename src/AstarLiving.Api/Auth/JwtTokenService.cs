using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AstarLiving.Api.Common;
using AstarLiving.Api.Configuration;
using AstarLiving.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AstarLiving.Api.Auth;

/// <summary>ชื่อ claim ที่ใช้ในระบบ — sub/username/role คงชื่อเดิมของ payload ฝั่ง NestJS</summary>
public static class AstarClaims
{
    public const string Username = "username";
    public const string Role = "role";
    public const string ProjectId = "project_id";

    /// <summary>แยกชนิด token เพื่อไม่ให้ refresh token ถูกใช้แทน access token</summary>
    public const string TokenType = "typ_use";

    public const string AccessTokenType = "access";
    public const string RefreshTokenType = "refresh";
}

public class JwtTokenService(IOptions<AstarLivingOptions> options)
{
    private readonly JwtOptions _jwt = options.Value.Jwt;
    /// <summary>
    /// MapInboundClaims = false สำคัญมาก
    /// ค่าเริ่มต้นของ handler จะแปลงชื่อ claim เช่น "sub" เป็น URI ยาว ๆ ของ ClaimTypes
    /// ทำให้หา claim ตามชื่อที่เราใส่ไว้ไม่เจอ
    /// </summary>
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public string CreateAccessToken(User user) =>
        Create(user, AstarClaims.AccessTokenType, _jwt.AccessSecret, _jwt.AccessExpires);

    public string CreateRefreshToken(User user) =>
        Create(user, AstarClaims.RefreshTokenType, _jwt.RefreshSecret, _jwt.RefreshExpires);

    /// <summary>
    /// ตรวจ refresh token แล้วคืน id ของผู้ใช้
    /// ปฏิเสธ token ที่ไม่ใช่ชนิด refresh เพื่อปิดช่องโหว่ของระบบเดิมที่ใช้แทนกันได้
    /// </summary>
    public int ValidateRefreshToken(string refreshToken)
    {
        try
        {
            var principal = _handler.ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = SigningKey(_jwt.RefreshSecret),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);

            var tokenType = principal.FindFirstValue(AstarClaims.TokenType);
            if (tokenType != AstarClaims.RefreshTokenType)
            {
                throw AppException.Unauthorized("Refresh token ไม่ถูกต้องหรือหมดอายุ");
            }

            var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (!int.TryParse(subject, out var userId))
            {
                throw AppException.Unauthorized("Refresh token ไม่ถูกต้องหรือหมดอายุ");
            }

            return userId;
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception)
        {
            throw AppException.Unauthorized("Refresh token ไม่ถูกต้องหรือหมดอายุ");
        }
    }

    public static SymmetricSecurityKey SigningKey(string secret) =>
        new(Encoding.UTF8.GetBytes(secret));

    private string Create(User user, string tokenType, string secret, TimeSpan lifetime)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AstarClaims.Username, user.Username),

            // ระบบเดิมใส่ role ลง payload แต่ jwt.strategy ทิ้งทีหลัง ทำให้ไม่มีการตรวจสิทธิ์เลย
            // ระบบใหม่เก็บไว้จริงและใช้บังคับสิทธิ์
            new(AstarClaims.Role, user.Role.ToString()),
            new(AstarClaims.TokenType, tokenType),
        };

        if (user.ProjectId is { } projectId)
        {
            claims.Add(new Claim(AstarClaims.ProjectId, projectId.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(SigningKey(secret), SecurityAlgorithms.HmacSha256));

        return _handler.WriteToken(token);
    }
}
