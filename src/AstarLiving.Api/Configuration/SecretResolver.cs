using AstarLiving.Shared.Security;

namespace AstarLiving.Api.Configuration;

/// <summary>
/// อ่านค่าลับของเครื่อง (รหัสผ่านฐานข้อมูล กุญแจ JWT) ตามลำดับเดียวกันทั้งระบบ
///
/// เดิมตรรกะนี้เป็นฟังก์ชันท้ายไฟล์ Program.cs ซึ่งเรียกจากที่อื่นไม่ได้
/// พอมีบริการใหม่ต้องใช้ จึงย้ายออกมาไว้ที่เดียว ห้ามคัดลอกไปไว้สองที่
/// เพราะถ้าสองที่ตีความลำดับไม่ตรงกัน จะได้รหัสผ่านคนละตัวโดยไม่มีอะไรฟ้อง
/// </summary>
public static class SecretResolver
{
    /// <summary>
    /// ลำดับ: ไฟล์ลับของเครื่อง → ค่าในไฟล์ตั้งค่า
    ///
    /// ถ้าไฟล์ลับมีอยู่แต่อ่านไม่ได้ จะปล่อยให้ข้อผิดพลาดขึ้นมาเลย
    /// ดีกว่าเงียบแล้วใช้ค่าว่างจนไปพังที่คำขอแรกของผู้ใช้
    /// </summary>
    public static string Resolve(string? secretFilePath, string inlineValue)
    {
        if (string.IsNullOrWhiteSpace(secretFilePath)) return inlineValue;
        if (!File.Exists(secretFilePath)) return inlineValue;

        return MachineSecret.Read(secretFilePath);
    }

    /// <summary>รหัสผ่านฐานข้อมูลของเครื่องนี้</summary>
    public static string ResolveDatabasePassword(DatabaseOptions database) =>
        Resolve(database.SecretFilePath, database.Password);
}
