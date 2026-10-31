using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AstarLiving.Shared.Contracts;

/// <summary>POST /api/v1/auth/login</summary>
public class LoginRequest
{
    [JsonPropertyName("username")]
    [Required(ErrorMessage = "username should not be empty")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    [Required(ErrorMessage = "password should not be empty")]
    [MinLength(6, ErrorMessage = "password must be longer than or equal to 6 characters")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>ผลของ login — ระบบเดิมคืน object นี้ตรง ๆ ไม่ห่อซอง</summary>
public class LoginResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>POST /api/v1/auth/refresh-token — ระบบเดิมอ่านฟิลด์ refresh_token จาก body ตรง ๆ</summary>
public class RefreshTokenRequest
{
    [JsonPropertyName("refresh_token")]
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;
}

/// <summary>PATCH /api/v1/auth/change-password</summary>
public class ChangePasswordRequest
{
    [JsonPropertyName("old_password")]
    [Required]
    public string OldPassword { get; set; } = string.Empty;

    [JsonPropertyName("new_password")]
    [Required]
    [MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>
    /// ระบบเดิมเทียบ confirm_password กับ "รหัสผ่านเดิม" ไม่ใช่รหัสผ่านใหม่
    /// เป็นพฤติกรรมที่คงไว้ตามข้อตกลง (ดู AuthService.ChangePasswordAsync)
    /// </summary>
    [JsonPropertyName("confirm_password")]
    [Required]
    [MinLength(6)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>
/// ผลของ logout — ระบบเดิมสะท้อน payload ของ JWT กลับมาในรูป { userId, username }
/// จึงต้องใช้ชื่อคีย์แบบ camelCase ตรงนี้ ต่างจากคีย์อื่นในระบบที่เป็น snake_case
/// </summary>
public class LogoutResponse
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("user")]
    public LogoutUser User { get; init; } = new();
}

public class LogoutUser
{
    [JsonPropertyName("userId")]
    public int UserId { get; init; }

    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;
}

/// <summary>ข้อความอย่างเดียว เช่น ผลของ change-password / reset-password</summary>
public class MessageResponse
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>ผู้ใช้ที่ GET /api/v1/auth/me คืนกลับ (ไม่มี password_hash)</summary>
public class CurrentUserResponse
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("project_id")]
    public int? ProjectId { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("project")]
    public ProjectSummary? Project { get; init; }
}

/// <summary>
/// โครงการแบบย่อที่ติดมากับ /auth/me — ชุดฟิลด์ตรงกับ SELECT_FIELDS_USER.project ของเดิม
/// สังเกตว่าไม่มี project_logo และไม่มี contact_fax_phone
/// </summary>
public class ProjectSummary
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("contact_name")]
    public string? ContactName { get; init; }

    [JsonPropertyName("contact_phone")]
    public string? ContactPhone { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }
}

/// <summary>
/// แถวเต็มของ gv_projects ที่ติดมากับ relations: ['project'] ของ TypeORM
/// ระบบเดิมไม่ได้ระบุ select ในเส้นทางพวกนี้ จึงได้ทุกคอลัมน์รวม audit กลับไปด้วย
/// </summary>
public class ProjectEmbedded
{
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("created_by")]
    public int? CreatedBy { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public int? UpdatedBy { get; init; }

    [JsonPropertyName("deleted_at")]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("deleted_by")]
    public int? DeletedBy { get; init; }

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("contact_name")]
    public string? ContactName { get; init; }

    [JsonPropertyName("contact_phone")]
    public string? ContactPhone { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("contact_fax_phone")]
    public string? ContactFaxPhone { get; init; }

    /// <summary>path สัมพัทธ์ตามที่เก็บในฐานข้อมูล ไม่ได้แปลงเป็น URL เหมือนเส้นทาง /v1/project</summary>
    [JsonPropertyName("project_logo")]
    public string? ProjectLogo { get; init; }
}
