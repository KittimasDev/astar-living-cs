using System.IO;

namespace AstarLiving.Client.Services;

/// <summary>
/// ที่อยู่ของไฟล์ทั้งหมดที่โปรแกรมเขียน
///
/// กฎเหล็ก: ห้ามคืนค่าที่อยู่ใต้ <see cref="AppContext.BaseDirectory"/> เด็ดขาด
/// เพราะตัวอัปเดต Velopack แทนที่โฟลเดอร์ current ทั้งก้อนทุกครั้ง
/// อะไรที่เขียนลงตรงนั้นจะหายไปเงียบ ๆ ทุกการอัปเดต
/// มีเทสต์คุมกฎข้อนี้อยู่ใน AstarLiving.Client.Tests
/// </summary>
public static class AppPaths
{
    private const string ProductFolder = "AstarLiving";

    /// <summary>ข้อมูลระดับเครื่อง ตัวติดตั้งเขียน client.json ไว้ที่นี่</summary>
    public static string MachineRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolder);

    public static string MachineConfigDirectory { get; } = Path.Combine(MachineRoot, "config");

    /// <summary>ไฟล์ที่ตัวติดตั้งเขียนตอนเลือกโหมดเครื่องลูกข่าย</summary>
    public static string MachineClientConfig { get; } = Path.Combine(MachineConfigDirectory, "client.json");

    /// <summary>ข้อมูลเฉพาะของผู้ใช้ Windows คนนั้น</summary>
    public static string UserRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductFolder);

    /// <summary>ค่าตั้งที่ผู้ใช้เปลี่ยนเองได้ เช่น ที่อยู่เครื่องแม่</summary>
    public static string UserSettings { get; } = Path.Combine(UserRoot, "user.json");

    /// <summary>โทเคนที่ป้องกันด้วย DPAPI ระดับผู้ใช้</summary>
    public static string SessionFile { get; } = Path.Combine(UserRoot, "session.dat");

    public static string LogDirectory { get; } = Path.Combine(UserRoot, "logs");

    /// <summary>ที่พักไฟล์ที่ดาวน์โหลดมาเปิด เช่น PDF ที่สั่งพิมพ์</summary>
    public static string DocumentCache { get; } = Path.Combine(
        Path.GetTempPath(), ProductFolder, "documents");

    public static void EnsureUserDirectories()
    {
        Directory.CreateDirectory(UserRoot);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(DocumentCache);
    }
}
