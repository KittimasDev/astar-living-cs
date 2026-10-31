using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace AstarLiving.Pdf;

/// <summary>
/// โหลดโลโก้โครงการมาฝังในเอกสาร — พอร์ตจาก loadLogoBuffer ใน invoice-utils.helper.ts
///
/// ย่อให้อยู่ในกรอบ 200x200 แล้วเข้ารหัสเป็น PNG เก็บผลไว้ในหน่วยความจำโดยใช้
/// path กับเวลาแก้ไขไฟล์เป็นกุญแจ เพื่อไม่ต้องย่อซ้ำทุกครั้งที่พิมพ์
///
/// เมื่อหาไฟล์ไม่เจอจะคืนภาพโปร่งใส 1x1 แทนการโยน exception
/// เพราะโลโก้หายไม่ควรทำให้พิมพ์ใบแจ้งหนี้ไม่ได้
/// </summary>
public class LogoLoader
{
    private const int MaxSize = 200;

    /// <summary>PNG โปร่งใสขนาด 1x1 สร้างครั้งเดียวแล้วใช้ซ้ำ</summary>
    private static readonly Lazy<byte[]> TransparentPixel = new(() =>
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1);
        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    });

    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    /// <summary>
    /// คืนไบต์ของโลโก้ที่พร้อมฝังในเอกสาร
    /// </summary>
    /// <param name="physicalPath">
    /// path จริงบนดิสก์ — ผู้เรียกเป็นคนแปลงจาก path สัมพัทธ์ในฐานข้อมูลมาให้แล้ว
    /// </param>
    public byte[] Load(string? physicalPath)
    {
        if (string.IsNullOrWhiteSpace(physicalPath) || !File.Exists(physicalPath))
        {
            return TransparentPixel.Value;
        }

        try
        {
            // ใส่เวลาแก้ไขไฟล์ในกุญแจ เพื่อให้เปลี่ยนโลโก้แล้วเห็นผลทันทีโดยไม่ต้องรีสตาร์ท
            var key = $"{physicalPath}::{File.GetLastWriteTimeUtc(physicalPath).Ticks}";

            return _cache.GetOrAdd(key, _ => Resize(physicalPath));
        }
        catch (Exception)
        {
            // ไฟล์เสียหรืออ่านไม่ได้ ก็ยังต้องพิมพ์เอกสารออกมาได้
            return TransparentPixel.Value;
        }
    }

    private static byte[] Resize(string physicalPath)
    {
        using var image = Image.Load(physicalPath);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(MaxSize, MaxSize),
            Mode = ResizeMode.Max,
        }));

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression });
        return buffer.ToArray();
    }
}
