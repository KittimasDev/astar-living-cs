using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace AstarLiving.Pdf;

/// <summary>
/// ตั้งค่าเริ่มต้นของการสร้าง PDF
///
/// ต้องเรียก Initialize() ครั้งเดียวตอนโปรแกรมเริ่ม ไม่อย่างนั้น QuestPDF จะโยน exception
/// ตอน render ครั้งแรกเพราะยังไม่ได้ระบุชนิดสัญญาอนุญาต
///
/// ฟอนต์ THSarabunNew ถูกฝังเป็น embedded resource ไม่ได้อ่านจากโฟลเดอร์ข้างโปรแกรม
/// เหมือนระบบเดิม เพราะตัวอัปเดตแทนที่โฟลเดอร์ติดตั้งทั้งก้อนทุกครั้ง
/// </summary>
public static class PdfEnvironment
{
    /// <summary>
    /// ชื่อฟอนต์ที่ใช้อ้างในทุกเอกสาร
    ///
    /// ต้องเป็นชื่อวงศ์ฟอนต์ที่อยู่ข้างในไฟล์ ttf เท่านั้น ซึ่งมีเว้นวรรค
    /// ถ้าสะกดผิดแม้แต่ช่องว่างเดียว QuestPDF จะไม่แจ้งอะไรเลย
    /// แต่จะถอยไปใช้ฟอนต์สำรองของเครื่อง (Lato กับ Leelawadee UI)
    /// ซึ่งกว้างกว่ามาก ทำให้ทุกคอลัมน์ตัดบรรทัดและเอกสารหน้าตาผิดไปทั้งใบ
    /// </summary>
    public const string FontFamily = "TH Sarabun New";

    private static readonly string[] EmbeddedFonts =
    [
        "THSarabunNew.ttf",
        "THSarabunNew-Bold.ttf",
        "THSarabunNew-Italic.ttf",
        "THSarabunNew-BoldItalic.ttf",
    ];

    private static bool _initialized;
    private static readonly object Gate = new();

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;

            QuestPDF.Settings.License = LicenseType.Community;

            // ตัวเลขเงินต้องแสดงเป็นเลขอารบิกเสมอ ไม่ใช่เลขไทย แม้เครื่องจะตั้งค่าเป็นไทย
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;

            var assembly = Assembly.GetExecutingAssembly();

            foreach (var fileName in EmbeddedFonts)
            {
                var resourceName = $"{assembly.GetName().Name}.Fonts.{fileName}";
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException(
                        $"ไม่พบฟอนต์ {fileName} ที่ฝังไว้ในแอสเซมบลี (resource: {resourceName})");

                FontManager.RegisterFont(stream);
            }

            _initialized = true;
        }
    }
}
