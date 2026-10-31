using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace AstarLiving.Infrastructure.Storage;

/// <summary>ค่าตั้งของที่เก็บไฟล์ ให้ฝั่ง API ส่งเข้ามา</summary>
public sealed record ImageStorageSettings(string Root, string PublicBaseUrl);

/// <summary>ผลการบันทึกไฟล์: path สัมพัทธ์ที่จะเก็บลงฐานข้อมูล</summary>
public sealed record StoredFile(string RelativePath);

/// <summary>
/// จัดการไฟล์ภาพที่ผู้ใช้อัปโหลด — พอร์ตจาก src/common/utils/file/file.util.ts
///
/// ระบบเดิมเก็บไฟล์ใต้โฟลเดอร์ public ของตัวโปรแกรมเอง ซึ่งบนเครื่องลูกค้าจะหายทุกครั้งที่อัปเดต
/// ระบบใหม่ย้ายไปไว้ที่ %ProgramData%\AstarLiving\uploads แต่คง path สัมพัทธ์ในฐานข้อมูลไว้เหมือนเดิม
/// (เช่น uploads/project/8/2026_05_26_14-32-16.png) เพื่อให้ข้อมูลเก่าใช้ต่อได้ทันที
/// </summary>
public class ImageStorage(ImageStorageSettings settings)
{
    /// <summary>ค่าเดิมของระบบเมื่อไม่มีรูป</summary>
    private const string DefaultImagePath = "/image/profile.png";

    private const int TargetMaxBytes = 500 * 1024;
    private const int MaxWidth = 1024;

    private readonly ImageStorageSettings _settings = settings;

    /// <summary>
    /// getFileUrl() — ประกอบ URL สาธารณะของไฟล์
    /// รูปแบบ {base}/images/{path} โดย base ลงท้ายด้วย /api
    /// </summary>
    public string GetFileUrl(string? relativePath)
    {
        var baseUrl = _settings.PublicBaseUrl.TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(relativePath) ? DefaultImagePath : relativePath;
        var suffix = path.StartsWith('/') ? path : $"/{path}";
        return $"{baseUrl}/images{suffix}";
    }

    /// <summary>
    /// compressImageTo500KB() — ย่อกว้างไม่เกิน 1024 แล้วเข้ารหัส WebP
    /// ลดคุณภาพลงทีละ 5 จาก 80 จนไฟล์เล็กกว่า 500 KB หรือคุณภาพถึง 30
    /// </summary>
    public static async Task<byte[]> CompressAsync(byte[] source, CancellationToken ct = default)
    {
        using var image = Image.Load(source);

        if (image.Width > MaxWidth)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(MaxWidth, 0),
                Mode = ResizeMode.Max,
            }));
        }

        var quality = 80;
        byte[] output;

        do
        {
            using var buffer = new MemoryStream();
            await image.SaveAsync(buffer, new WebpEncoder { Quality = quality }, ct);
            output = buffer.ToArray();
            quality -= 5;
        }
        while (output.Length > TargetMaxBytes && quality > 30);

        return output;
    }

    /// <summary>
    /// uploadFile() — เขียนไฟล์ลง {root}/{customPath}/{filename}
    /// หมายเหตุ: ระบบเดิมตั้งชื่อไฟล์ลงท้าย .png แต่เนื้อไฟล์เป็น WebP
    /// คงพฤติกรรมนี้ไว้เพื่อให้ path ที่เก็บในฐานข้อมูลเข้ากันได้กับข้อมูลเก่า
    /// </summary>
    public async Task<StoredFile> SaveAsync(
        byte[] content, string customPath, string fileName, CancellationToken ct = default)
    {
        var directory = Path.Combine(_settings.Root, customPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), content, ct);

        return new StoredFile($"{customPath}/{fileName}");
    }

    /// <summary>คืน path จริงบนดิสก์ของ path สัมพัทธ์ที่เก็บในฐานข้อมูล (ใช้ตอนฝังโลโก้ลง PDF)</summary>
    public string? ResolvePhysicalPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var trimmed = relativePath.TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(_settings.Root, trimmed.Replace('/', Path.DirectorySeparatorChar)));

        // กันการหลุดออกนอกโฟลเดอร์ที่กำหนด เผื่อค่าในฐานข้อมูลมี ../ ติดมา
        var root = Path.GetFullPath(_settings.Root);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;

        return File.Exists(full) ? full : null;
    }

    /// <summary>deleteImageFolder() — ลบโฟลเดอร์ภาพทั้งก้อน</summary>
    public void DeleteFolder(string customPath)
    {
        var directory = Path.Combine(_settings.Root, customPath.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
