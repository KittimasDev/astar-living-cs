namespace AstarLiving.Infrastructure.Persistence;

/// <summary>
/// สิ่งที่ต้องสร้างตอนฐานข้อมูลยังว่าง
///
/// แยกไซต์จริงออกจากเครื่องสาธิตให้ชัด เพราะสองแบบนี้ต้องการคนละอย่างกันสิ้นเชิง
/// ไซต์จริงต้องได้โครงเปล่าที่พร้อมกรอกข้อมูลของลูกค้าเอง
/// ส่วนเครื่องสาธิตต้องมีข้อมูลปลอมให้กดดูได้ทุกหน้าจอ
///
/// ค่าเริ่มต้นของ <see cref="IncludeDemoData"/> คือ false เสมอ
/// เพราะความผิดพลาดที่ยอมรับไม่ได้คือข้อมูลปลอมหลุดไปอยู่ในฐานข้อมูลของลูกค้า
/// ส่วนการลืมเปิดโหมดสาธิตนั้นเห็นทันทีและแก้ได้ง่าย
/// </summary>
public sealed class SeedPlan
{
    /// <summary>true = สร้างลูกบ้าน งวดค่าส่วนกลาง และใบเสร็จปลอมให้ลองใช้</summary>
    public bool IncludeDemoData { get; init; }

    /// <summary>ชื่อโครงการตั้งต้น ลูกค้าแก้ได้ภายหลังในหน้าตั้งค่า</summary>
    public string ProjectName { get; init; } = "โครงการของฉัน";

    /// <summary>บัญชีผู้ดูแลระบบที่จะสร้างให้ ไม่ระบุ = ไม่สร้างบัญชีจริง</summary>
    public string? AdminUsername { get; init; }

    /// <summary>รหัสผ่านของบัญชีผู้ดูแล ช่างผู้ติดตั้งเป็นคนกำหนดตอนติดตั้ง</summary>
    public string? AdminPassword { get; init; }

    public bool HasAdminAccount =>
        !string.IsNullOrWhiteSpace(AdminUsername) && !string.IsNullOrWhiteSpace(AdminPassword);

    /// <summary>ชุดที่ใช้ตอนทดสอบและสาธิต มีข้อมูลปลอมครบและใช้รหัสผ่านที่รู้กันอยู่แล้ว</summary>
    public static SeedPlan Demo { get; } = new() { IncludeDemoData = true };
}
