using AstarLiving.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace AstarLiving.Api.Auth;

/// <summary>
/// สิทธิ์การเข้าถึง — ระบบเดิมไม่มีการตรวจสิทธิ์เลย (roles.guard.ts เป็นไฟล์ว่าง
/// และ jwt.strategy ทิ้ง role ทิ้ง) มีแต่การซ่อนเมนูฝั่งหน้าจอ
///
/// ตารางด้านล่างยึดตามเมนูที่ frontend เดิมแสดงให้แต่ละ role ซึ่งเป็นสเปกที่ใช้งานจริงอยู่
///
/// | เมนู                          | SUPERADMIN | ADMIN | MANAGER | USER |
/// | แดชบอร์ด/ลูกบ้าน/บิล/ใบเสร็จ/รายงาน |     -      |   ✓   |    ✓    |  ✓   |
/// | ตั้งค่าโครงการ                  |     ✓      |   -   |    -    |  -   |
/// | ตั้งค่าวิธีชำระ/อัตรา/เทมเพลต      |     -      |   ✓   |    ✓    |  -   |
/// | ตั้งค่าผู้ใช้งาน                  |     ✓      |   ✓   |    ✓    |  -   |
/// | บันทึกการทำงานของระบบ            |     ✓      |   ✓   |    ✓    |  -   |
/// | สำรองฐานข้อมูล                  |     ✓      |   -   |    -    |  -   |
///
/// สองแถวล่างต่างจากระบบเดิม เพราะระบบเดิมไม่มีหน้าสำรองข้อมูล
/// และผู้ที่สั่งสำรองต้องตรวจสอบบันทึกการทำงานของตัวเองได้
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>งานประจำวัน: แดชบอร์ด ลูกบ้าน ใบแจ้งหนี้ ลดหนี้ ค้างชำระ ใบทวงถาม ใบเสร็จ รายงาน</summary>
    public const string Operations = "operations";

    /// <summary>ตั้งค่าที่ผูกกับโครงการ: วิธีชำระเงิน อัตราค่าบริการ เทมเพลตใบทวงถาม</summary>
    public const string ProjectSettings = "project-settings";

    /// <summary>ตั้งค่าโครงการ — เฉพาะ Astar</summary>
    public const string ProjectAdministration = "project-administration";

    /// <summary>จัดการผู้ใช้งาน</summary>
    public const string UserManagement = "user-management";

    /// <summary>บันทึกการทำงานของระบบ</summary>
    public const string SystemLogs = "system-logs";

    /// <summary>
    /// สำรองฐานข้อมูลและดาวน์โหลด — เฉพาะ Astar
    ///
    /// จงใจแยกจาก ProjectAdministration ทั้งที่ตอนนี้ให้สิทธิ์ชุดเดียวกัน
    /// เพราะวันหนึ่งถ้าเปิดให้ผู้จัดการโครงการแก้ข้อมูลโครงการตัวเองได้
    /// การขยาย ProjectAdministration จะพลอยเปิดทางให้ดูดฐานข้อมูลทั้งก้อนไปด้วย
    /// แยกไว้แล้วเส้นนี้จะถูกพิจารณาแยกเสมอ
    /// </summary>
    public const string DatabaseBackup = "database-backup";

    public static AuthorizationOptions AddAstarLivingPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Operations, p => p.RequireRole(
            nameof(UserRole.ADMIN), nameof(UserRole.MANAGER), nameof(UserRole.USER)));

        options.AddPolicy(ProjectSettings, p => p.RequireRole(
            nameof(UserRole.ADMIN), nameof(UserRole.MANAGER)));

        options.AddPolicy(ProjectAdministration, p => p.RequireRole(
            nameof(UserRole.SUPERADMIN)));

        options.AddPolicy(UserManagement, p => p.RequireRole(
            nameof(UserRole.SUPERADMIN), nameof(UserRole.ADMIN), nameof(UserRole.MANAGER)));

        // ผู้ดูแลสูงสุดต้องตรวจสอบได้ว่าใครสั่งสำรองและดาวน์โหลดฐานข้อมูลไปบ้าง
        options.AddPolicy(SystemLogs, p => p.RequireRole(
            nameof(UserRole.SUPERADMIN), nameof(UserRole.ADMIN), nameof(UserRole.MANAGER)));

        options.AddPolicy(DatabaseBackup, p => p.RequireRole(
            nameof(UserRole.SUPERADMIN)));

        return options;
    }
}
