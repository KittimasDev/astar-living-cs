using System.Text.RegularExpressions;

namespace AstarLiving.Pdf;

/// <summary>
/// แปลง HTML จาก TinyMCE เป็นข้อความธรรมดา — พอร์ตจาก src/common/pdf/pdf-html.util.ts
/// ลำดับการแทนที่สำคัญ: ต้องเปลี่ยน div และ br เป็นขึ้นบรรทัดใหม่ก่อนจะลบแท็กที่เหลือทั้งหมด
/// </summary>
public static partial class PdfHtml
{
    /// <summary>จำนวนบรรทัดสูงสุดในกล่องหมายเหตุท้ายเอกสาร</summary>
    public const int FooterMaxLines = 3;

    public static string CleanToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var text = DivTag().Replace(html, "\n");
        text = BrTag().Replace(text, "\n");
        text = text.Replace("&nbsp;", " ");
        text = AnyTag().Replace(text, string.Empty);

        return text.Trim();
    }

    /// <summary>htmlToPdfStack() — แตกเป็นบรรทัดทั้งหมด</summary>
    public static IReadOnlyList<string> ToLines(string? html)
    {
        var cleaned = CleanToText(html);
        return cleaned.Length == 0 ? [string.Empty] : cleaned.Split('\n');
    }

    /// <summary>htmlToPdfStackFooter() — ตัดเหลือ 3 บรรทัดแรกสำหรับกล่องหมายเหตุ</summary>
    public static IReadOnlyList<string> ToFooterLines(string? html) =>
        ToLines(html).Take(FooterMaxLines).ToList();

    [GeneratedRegex(@"</?div[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DivTag();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();
}

/// <summary>
/// แทนที่ token ในเทมเพลตใบทวงถาม เช่น {resident_name} และ {amount}
/// พอร์ตจาก applyTemplate ใน NoticeTemplate-info.util.ts
/// </summary>
public static class NoticeTemplateRenderer
{
    public static string Apply(string? text, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = text;
        foreach (var (key, value) in values)
        {
            result = result.Replace($"{{{key}}}", value);
        }

        return result;
    }
}
