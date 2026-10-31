using System.Security.Cryptography;
using System.Text;

namespace AstarLiving.Shared.Security;

/// <summary>
/// ปกป้องรหัสผ่านฐานข้อมูลบนเครื่องแม่ด้วย DPAPI ระดับเครื่อง
///
/// ข้อจำกัดที่ต้องเข้าใจให้ตรงกัน: นี่คือการอำพราง ไม่ใช่ความลับที่แท้จริง
/// ใครก็ตามที่มีสิทธิ์ผู้ดูแลบนเครื่องนั้นและมีไฟล์โปรแกรม ย่อมถอดค่ากลับได้
///
/// ความปลอดภัยจริงมาจากสามอย่าง: PostgreSQL ฟังเฉพาะ loopback,
/// บัญชีที่แอปใช้ไม่ใช่ superuser และสิทธิ์ไฟล์ระดับ NTFS
/// สิ่งที่ DPAPI ช่วยคือกันคนทั่วไปเปิดไฟล์แล้วเห็นรหัสผ่านเป็นข้อความธรรมดา
/// </summary>
public static class MachineSecret
{
    /// <summary>
    /// ค่าสุ่มเพิ่มเติมที่คอมไพล์ติดไปกับโปรแกรม ทำให้ไฟล์ที่คัดลอกไปเครื่องอื่นถอดไม่ได้
    /// แม้จะเป็นเครื่อง Windows เหมือนกัน
    /// </summary>
    private static readonly byte[] Entropy =
    [
        0x41, 0x73, 0x74, 0x61, 0x72, 0x4C, 0x69, 0x76,
        0x69, 0x6E, 0x67, 0x2D, 0x64, 0x62, 0x2D, 0x76,
        0x31, 0x9C, 0x3E, 0x7A, 0x12, 0xBD, 0x55, 0x08,
        0xE4, 0x66, 0x2F, 0x91, 0xAA, 0x0D, 0x73, 0xC6,
    ];

    public static void Write(string path, string secret)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.LocalMachine);

        File.WriteAllBytes(path, protectedBytes);
    }

    public static string Read(string path)
    {
        var protectedBytes = File.ReadAllBytes(path);

        var plainBytes = ProtectedData.Unprotect(
            protectedBytes, Entropy, DataProtectionScope.LocalMachine);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// รหัสผ่านสุ่มที่ปลอดภัยและไม่มีอักขระที่ทำให้บรรทัดคำสั่งของ Windows เพี้ยน
    /// (เลี่ยง เครื่องหมายคำพูด, ^, %, &amp; ซึ่ง cmd และ pg_ctl ตีความต่างกัน)
    /// </summary>
    public static string GeneratePassword(int length = 32)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_";

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(chars);
    }
}
