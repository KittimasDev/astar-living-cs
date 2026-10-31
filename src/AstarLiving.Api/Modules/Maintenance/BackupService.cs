using System.Diagnostics;
using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Api.Configuration;
using AstarLiving.Domain.Billing;
using AstarLiving.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace AstarLiving.Api.Modules.Maintenance;

/// <summary>
/// สำรองฐานข้อมูลด้วยมือจากหน้าจอ
///
/// ยกวิธีเรียก pg_dump มาจากตัวอัปเดต แต่แก้สองอย่างที่ของเดิมขาด
///   1. อ่านค่าเชื่อมต่อจากค่าตั้ง ไม่ฮาร์ดโค้ด
///   2. ฆ่าโปรเซสทั้งต้นไม้เมื่อถูกยกเลิก ไม่งั้น pg_dump จะค้างกินการเชื่อมต่อฐานข้อมูล
/// </summary>
public sealed class BackupService(
    IOptions<AstarLivingOptions> options,
    BackupArtifactStore store,
    ThaiCalendar calendar,
    ILogger<BackupService> logger)
{
    private readonly AstarLivingOptions _options = options.Value;

    public async Task<BackupCreatedResponse> CreateAsync(CurrentUser current, CancellationToken ct)
    {
        var backup = _options.Backup;

        if (!File.Exists(backup.PgDumpPath))
        {
            throw AppException.ServiceUnavailable(
                "ไม่พบเครื่องมือสำรองฐานข้อมูลบนเครื่องแม่ข่าย " +
                $"(คาดว่าอยู่ที่ {backup.PgDumpPath}) " +
                "กรุณาตรวจค่า AstarLiving:Backup:PgDumpPath ในไฟล์ machine.json");
        }

        using var lease = await store.AcquireAsync(ct);

        var now = new DateTimeOffset(calendar.NowBangkok, TimeSpan.FromHours(7));

        store.PruneExpired(now);
        Directory.CreateDirectory(backup.WorkDirectory);

        EnsureEnoughDiskSpace(backup.WorkDirectory);

        var fileName = PgDumpCommand.BuildFileName(now);
        var target = Path.Combine(backup.WorkDirectory, fileName);

        await RunPgDumpAsync(target, backup, ct);

        var size = new FileInfo(target).Length;
        var artifact = store.Add(target, fileName, size, current.UserId, now);

        // ไฟล์จะถูกลบทิ้งหลังผู้ใช้ดาวน์โหลดเสร็จ ถ้าไม่จดเวลาไว้
        // หน้าจอจะขึ้นว่า "ยังไม่เคยสำรอง" ทั้งที่เพิ่งสำรองไปเมื่อครู่
        WriteLastBackupMarker(now);

        logger.LogInformation(
            "สำรองฐานข้อมูลเรียบร้อย {FileName} ขนาด {Size} ไบต์ โดยผู้ใช้ {UserId}",
            fileName, size, current.UserId);

        return new BackupCreatedResponse
        {
            Token = artifact.Token.ToString("N"),
            FileName = artifact.FileName,
            SizeBytes = artifact.SizeBytes,
            CreatedAt = artifact.CreatedAt,
            ExpiresAt = artifact.ExpiresAt,
        };
    }

    private async Task RunPgDumpAsync(string target, BackupOptions backup, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(backup.PgDumpPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        foreach (var argument in PgDumpCommand.BuildArguments(_options.Database, target))
        {
            startInfo.ArgumentList.Add(argument);
        }

        // รหัสผ่านส่งทางตัวแปรสภาพแวดล้อมเท่านั้น ไม่ให้ไปโผล่ในรายการโปรเซส
        startInfo.Environment["PGPASSWORD"] = SecretResolver.ResolveDatabasePassword(_options.Database);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(backup.TimeLimit);

        using var process = Process.Start(startInfo)
            ?? throw AppException.Internal("เริ่มเครื่องมือสำรองฐานข้อมูลไม่สำเร็จ");

        try
        {
            var error = await process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);

            if (process.ExitCode != 0)
            {
                // รายละเอียดมี host, port, ชื่อผู้ใช้ และที่อยู่ไฟล์ จึงเก็บไว้ฝั่งเครื่องแม่
                logger.LogError("pg_dump จบด้วยรหัส {Code}: {Error}", process.ExitCode, error);
                TryDelete(target);

                throw AppException.Internal("สำรองฐานข้อมูลไม่สำเร็จ กรุณาติดต่อผู้ดูแลระบบ");
            }
        }
        catch (OperationCanceledException)
        {
            // ต้องฆ่าทั้งต้นไม้ ไม่งั้น pg_dump จะทำงานต่อจนจบและค้างการเชื่อมต่อฐานข้อมูลไว้
            KillQuietly(process);
            TryDelete(target);

            throw;
        }
    }

    /// <summary>
    /// ไฟล์สำรองใหญ่ได้ราวขนาดฐานข้อมูล ถ้าดิสก์ไม่พอ pg_dump จะล้มกลางคัน
    /// แล้วทิ้งไฟล์ไม่สมบูรณ์ไว้ บอกล่วงหน้าดีกว่าปล่อยให้ไปล้มเอง
    /// </summary>
    private void EnsureEnoughDiskSpace(string workDirectory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(workDirectory));
            if (string.IsNullOrWhiteSpace(root)) return;

            var free = new DriveInfo(root).AvailableFreeSpace;
            const long minimum = 512L * 1024 * 1024;

            if (free < minimum)
            {
                throw AppException.ServiceUnavailable(
                    $"พื้นที่ว่างบนเครื่องแม่ข่ายเหลือน้อยเกินไป ({free / 1024 / 1024} MB) " +
                    "กรุณาเพิ่มพื้นที่ก่อนสำรองข้อมูล");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "ตรวจพื้นที่ว่างไม่ได้ จึงข้ามการตรวจ");
        }
    }

    private void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
                                      or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "หยุดเครื่องมือสำรองฐานข้อมูลไม่สำเร็จ");
        }
    }

    private void WriteLastBackupMarker(DateTimeOffset at)
    {
        try
        {
            Directory.CreateDirectory(_options.Backup.BackupRoot);

            File.WriteAllText(
                BackupMarker.PathFor(_options.Backup.BackupRoot),
                at.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "จดเวลาสำรองล่าสุดไม่สำเร็จ");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "ลบไฟล์สำรองที่ไม่สมบูรณ์ {Path} ไม่สำเร็จ", path);
        }
    }
}
