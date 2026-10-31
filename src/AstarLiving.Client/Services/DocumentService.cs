using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace AstarLiving.Client.Services;

/// <summary>
/// ขอเอกสาร PDF จากเครื่องแม่ข่ายแล้วเปิดด้วยโปรแกรมอ่าน PDF ของเครื่อง
///
/// ไม่ฝังตัวอ่าน PDF ไว้ในโปรแกรมเอง เพราะไลบรารีที่ทำได้ดีมีขนาดหลายสิบเมกะไบต์
/// และ Windows 10/11 มีตัวอ่านมาให้อยู่แล้ว การสั่งพิมพ์จริงก็ทำจากตัวอ่านได้เลย
///
/// ไฟล์ถูกเขียนลงโฟลเดอร์ชั่วคราวของผู้ใช้ ไม่ใช่โฟลเดอร์ติดตั้ง
/// </summary>
public sealed class DocumentService(ApiClient api, DialogService dialogs, ILogger<DocumentService> logger)
{
    /// <summary>ดึง PDF ด้วย GET แล้วเปิดให้ดู</summary>
    public Task<bool> OpenAsync(string path, string fileName, CancellationToken ct = default) =>
        FetchAndOpenAsync(() => api.GetFileAsync(path, ct), fileName);

    /// <summary>ดึง PDF ด้วย POST (เส้นทางพิมพ์ที่ต้องส่งเงื่อนไขไปด้วย) แล้วเปิดให้ดู</summary>
    public Task<bool> OpenAsync(string path, object? body, string fileName, CancellationToken ct = default) =>
        FetchAndOpenAsync(() => api.PostFileAsync(path, body, ct), fileName);

    private async Task<bool> FetchAndOpenAsync(Func<Task<byte[]>> fetch, string fileName)
    {
        try
        {
            var bytes = await fetch();

            AppPaths.EnsureUserDirectories();

            // ใส่เวลาไว้ในชื่อไฟล์ เพื่อไม่ให้ทับไฟล์ที่ผู้ใช้ยังเปิดค้างอยู่
            var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            var target = Path.Combine(
                AppPaths.DocumentCache,
                $"{Path.GetFileNameWithoutExtension(safeName)}-{DateTime.Now:HHmmss}.pdf");

            await File.WriteAllBytesAsync(target, bytes);

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (ApiException ex)
        {
            await dialogs.ErrorAsync("พิมพ์เอกสารไม่สำเร็จ", ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "เขียนไฟล์เอกสารไม่ได้");
            await dialogs.ErrorAsync("บันทึกไฟล์เอกสารไม่ได้", "กรุณาตรวจสอบพื้นที่ว่างในเครื่อง");
            return false;
        }
        catch (Exception ex)
        {
            // ไม่มีโปรแกรมอ่าน PDF ติดตั้งอยู่ ซึ่งเกิดได้บน Windows ที่เพิ่งลงใหม่
            logger.LogWarning(ex, "เปิดไฟล์เอกสารไม่ได้");
            await dialogs.ErrorAsync(
                "เปิดไฟล์เอกสารไม่ได้",
                $"ไฟล์ถูกบันทึกไว้ที่ {AppPaths.DocumentCache} กรุณาเปิดด้วยโปรแกรมอ่าน PDF");
            return false;
        }
    }
}
