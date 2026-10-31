using System.Globalization;

namespace AstarLiving.Api.Modules.Maintenance;

/// <summary>
/// บันทึกเวลาที่สำรองด้วยมือครั้งล่าสุด
///
/// จำเป็นเพราะไฟล์สำรองถูกลบทิ้งทันทีที่ผู้ใช้ดาวน์โหลดเสร็จ
/// ถ้าดูจากไฟล์ในโฟลเดอร์อย่างเดียว หน้าจอจะบอกว่ายังไม่เคยสำรอง
/// ทั้งที่เพิ่งสำรองไปเมื่อครู่ ซึ่งทำให้ผู้ใช้ไม่เชื่อถือหน้าจอนี้ทั้งหน้า
/// </summary>
public static class BackupMarker
{
    private const string FileName = "last-manual-backup.txt";

    public static string PathFor(string backupRoot) => Path.Combine(backupRoot, FileName);

    /// <summary>คืน null เมื่อยังไม่เคยสำรองด้วยมือ หรืออ่านไฟล์ไม่ได้</summary>
    public static DateTimeOffset? Read(string backupRoot)
    {
        try
        {
            var path = PathFor(backupRoot);
            if (!File.Exists(path)) return null;

            var text = File.ReadAllText(path).Trim();

            return DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
