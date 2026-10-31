using System.Diagnostics;

namespace AstarLiving.ServerUpdater;

/// <summary>
/// สั่งหยุดและเริ่มบริการ Windows
///
/// เรียก sc.exe แทนการใช้ System.ServiceProcess เพราะแพ็กเกจนั้นต้องการสิทธิ์เพิ่ม
/// และ sc.exe มีมากับ Windows ทุกเครื่องอยู่แล้ว ทำให้ไม่ต้องพ่วงอะไรเพิ่มในตัวติดตั้ง
/// </summary>
public sealed class WindowsServiceControl(ILogger<WindowsServiceControl> logger)
{
    public Task<bool> StopAsync(string serviceName, CancellationToken ct) =>
        RunAsync($"stop \"{serviceName}\"", ct);

    public Task<bool> StartAsync(string serviceName, CancellationToken ct) =>
        RunAsync($"start \"{serviceName}\"", ct);

    /// <summary>
    /// รอจนบริการหยุดสนิท
    /// ต้องรอจริง ๆ เพราะถ้าเริ่มเขียนทับไฟล์ขณะที่โปรเซสยังถือไฟล์อยู่ การคัดลอกจะล้ม
    /// </summary>
    public async Task<bool> WaitUntilStoppedAsync(string serviceName, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await QueryStateAsync(serviceName, ct);
            if (state.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return true;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        logger.LogWarning("บริการ {Service} ยังไม่หยุดภายในเวลาที่กำหนด", serviceName);
        return false;
    }

    private async Task<string> QueryStateAsync(string serviceName, CancellationToken ct)
    {
        var output = await CaptureAsync($"query \"{serviceName}\"", ct);
        return output ?? string.Empty;
    }

    private async Task<bool> RunAsync(string arguments, CancellationToken ct)
    {
        var output = await CaptureAsync(arguments, ct);

        if (output is null)
        {
            logger.LogWarning("สั่ง sc.exe {Arguments} ไม่สำเร็จ", arguments);
            return false;
        }

        return true;
    }

    private async Task<string?> CaptureAsync(string arguments, CancellationToken ct)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "เรียก sc.exe ไม่ได้");
            return null;
        }
    }
}
