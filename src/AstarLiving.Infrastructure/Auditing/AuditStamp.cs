using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Common;
using AstarLiving.Domain.Entities;

namespace AstarLiving.Infrastructure.Auditing;

/// <summary>
/// ประทับคอลัมน์ audit — พอร์ตจาก createDefault / updatedDefaultNonActive / deleteDefaultNonActive
/// ใน src/config/value.ts
///
/// ข้อควรระวังเรื่องเวลา ระบบเดิมใช้เวลา 2 แบบที่ต่างกันมาก
/// 1) created_at / updated_at / deleted_at เก็บ "เวลาจริง" (timestamptz) ปีเป็น ค.ศ.
/// 2) payment_date / notice_date เก็บ "วันที่แบบ พ.ศ. บวก 7 ชั่วโมง" ลงคอลัมน์ date
///    เช่นแถวที่ created_at = 2026-09-17 จะมี payment_date = 2569-09-17
///    เลขรันเอกสารอ้างวันที่แบบนี้ ถ้าเปลี่ยนจะออกเลขซ้ำกับของเก่า
/// </summary>
public class AuditStamp(ThaiCalendar calendar)
{
    private readonly ThaiCalendar _calendar = calendar;

    /// <summary>
    /// เวลาจริงสำหรับคอลัมน์ timestamptz
    /// ต้องเป็น UTC เท่านั้น เพราะ Npgsql ปฏิเสธ DateTimeOffset ที่ offset ไม่ใช่ศูนย์
    /// </summary>
    public DateTimeOffset Now => _calendar.UtcNow;

    /// <summary>วันที่แบบ พ.ศ. (+7 ชั่วโมง) สำหรับคอลัมน์ payment_date / notice_date</summary>
    public DateOnly ThaiDocumentDate => DateOnly.FromDateTime(_calendar.NowThaiPlus7Hours);

    /// <summary>createDefault() — ใช้ตอนสร้างแถวใหม่</summary>
    public void StampCreate(BaseEntityAudit entity, int? userId)
    {
        var now = Now;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.CreatedBy = userId;
        entity.UpdatedBy = userId;
    }

    /// <summary>updatedDefaultNonActive() — ใช้ตอนแก้ไขแถวเดิม</summary>
    public void StampUpdate(BaseEntityAudit entity, int? userId)
    {
        entity.UpdatedAt = Now;
        entity.UpdatedBy = userId;
    }

    /// <summary>
    /// deleteDefaultNonActive() — ลบแบบ soft delete
    /// ระบบเดิมตั้ง updated_by ด้วยทุกครั้ง แต่ไม่แตะ updated_at
    /// </summary>
    public void StampDelete(BaseEntityAudit entity, int? userId)
    {
        entity.DeletedAt = Now;
        entity.DeletedBy = userId;
        entity.UpdatedBy = userId;
    }
}

/// <summary>ตัวช่วยแปลงระหว่าง DateOnly กับ DateTime ที่ใช้บ่อยในชั้นข้อมูล</summary>
public static class DateConversions
{
    public static DateOnly ToDateOnly(this DateTime value) => DateOnly.FromDateTime(value);

    public static DateTime ToDateTime(this DateOnly value) => value.ToDateTime(TimeOnly.MinValue);
}
