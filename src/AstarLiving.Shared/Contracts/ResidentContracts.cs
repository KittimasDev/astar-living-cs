using System.ComponentModel.DataAnnotations;

namespace AstarLiving.Shared.Contracts;

/// <summary>gv_residents — ชุดฟิลด์ตรงกับ SELECT_FIELDS_RESIDENT ของเดิม</summary>
public class ResidentResponse
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    /// <summary>อัตราส่วนกรรมสิทธิ์ (ตารางวา) ใช้เป็นตัวคูณค่าส่วนกลาง</summary>
    public decimal? OwnershipRatio { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public bool Active { get; init; }
    public int? CreatedBy { get; init; }
    public int? UpdatedBy { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public class CreateResidentRequest
{
    [Required(ErrorMessage = "กรุณาระบุโครงการ")]
    public int ProjectId { get; set; }

    [Required(ErrorMessage = "กรุณากรอกบ้านเลขที่")]
    [MaxLength(50)]
    public string HouseNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกชื่อ-นามสกุล")]
    [MaxLength(255)]
    public string FullName { get; set; } = string.Empty;

    public decimal? OwnershipRatio { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    public string? Address { get; set; }

    public bool? Active { get; set; } = true;
}

/// <summary>PartialType ของเดิม — ส่งมาเฉพาะฟิลด์ที่ต้องการแก้</summary>
public class UpdateResidentRequest
{
    public int? ProjectId { get; set; }

    [MaxLength(50)]
    public string? HouseNo { get; set; }

    [MaxLength(255)]
    public string? FullName { get; set; }

    public decimal? OwnershipRatio { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    public string? Address { get; set; }

    public bool? Active { get; set; }
}
