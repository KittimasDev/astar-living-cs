using System.ComponentModel.DataAnnotations;

namespace AstarLiving.Shared.Contracts;

/// <summary>
/// gv_users — ชุดฟิลด์ตรงกับ SELECT_FIELDS_USER ของเดิม
/// สังเกตว่าไม่มี password_hash ติดมาด้วยเด็ดขาด
/// </summary>
public class UserResponse
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string Role { get; init; } = string.Empty;
    public int? ProjectId { get; init; }
    public bool Active { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>โครงการแบบย่อที่ relations: ['project'] ของเดิมแนบมาด้วย</summary>
    public ProjectSummary? Project { get; init; }
}

public class CreateUserRequest
{
    [Required(ErrorMessage = "กรุณากรอกชื่อผู้ใช้งาน")]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    /// <summary>รหัสผ่านแบบข้อความธรรมดา API เป็นผู้เข้ารหัสก่อนบันทึก (ชื่อฟิลด์ตามของเดิม)</summary>
    [Required(ErrorMessage = "กรุณากรอกรหัสผ่าน")]
    [MinLength(6, ErrorMessage = "รหัสผ่านต้องมีอย่างน้อย 6 ตัวอักษร")]
    public string PasswordHash { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกชื่อ-นามสกุล")]
    [MaxLength(255)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกสิทธิ์ผู้ใช้งาน")]
    public string Role { get; set; } = string.Empty;

    public int? ProjectId { get; set; }

    public bool? Active { get; set; } = true;
}

/// <summary>PartialType ของเดิม — ส่งมาเฉพาะฟิลด์ที่ต้องการแก้ ไม่ส่งรหัสผ่านก็ได้</summary>
public class UpdateUserRequest
{
    [MaxLength(100)]
    public string? Username { get; set; }

    [MinLength(6, ErrorMessage = "รหัสผ่านต้องมีอย่างน้อย 6 ตัวอักษร")]
    public string? PasswordHash { get; set; }

    [MaxLength(255)]
    public string? FullName { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    public string? Role { get; set; }

    public int? ProjectId { get; set; }

    public bool? Active { get; set; }
}

/// <summary>
/// gv_activity_logs — หนึ่งบรรทัดของบันทึกการทำงาน
///
/// หน้าจอของเว็บเดิมเป็นข้อมูลปลอม ระบบใหม่อ่านจากตารางจริง
/// และเพิ่มชื่อผู้ใช้เข้ามาให้ด้วย เพราะตารางเก็บแค่รหัส
/// </summary>
public class ActivityLogResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string? Username { get; init; }
    public string? FullName { get; init; }
    public int? ProjectId { get; init; }
    /// <summary>คำกริยาของการกระทำ เช่น CREATE UPDATE DELETE</summary>
    public string Action { get; init; } = string.Empty;
    public string? TargetTable { get; init; }
    public int? TargetId { get; init; }
    public DateTime Timestamp { get; init; }
}
