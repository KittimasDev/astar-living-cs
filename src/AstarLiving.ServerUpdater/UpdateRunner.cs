using System.Net.Http;
using System.Diagnostics;
using System.IO.Compression;
using AstarLiving.Shared.Security;
using AstarLiving.Shared.Versioning;

namespace AstarLiving.ServerUpdater;

/// <summary>ผลของการพยายามอัปเดตหนึ่งรอบ</summary>
public enum UpdateOutcome
{
    /// <summary>ไม่มีอะไรต้องทำ เป็นเวอร์ชันล่าสุดอยู่แล้ว</summary>
    UpToDate,

    /// <summary>อัปเดตสำเร็จและปล่อย feed ให้เครื่องลูกแล้ว</summary>
    Updated,

    /// <summary>ติดต่อเซิร์ฟเวอร์ปล่อยเวอร์ชันไม่ได้ ถือเป็นเรื่องปกติของไซต์ที่ไม่มีเน็ต</summary>
    Offline,

    /// <summary>ตรวจลายเซ็นหรือค่าแฮชไม่ผ่าน ไม่แตะอะไรทั้งสิ้น</summary>
    Rejected,

    /// <summary>อัปเดตแล้วไม่ผ่าน health check จึงย้อนกลับไปเวอร์ชันเดิม</summary>
    RolledBack,

    /// <summary>ล้มเหลวด้วยเหตุอื่น</summary>
    Failed,
}

/// <summary>
/// ขั้นตอนอัปเดตเครื่องแม่หนึ่งรอบ
///
/// ลำดับนี้สำคัญทุกขั้นและห้ามสลับ
/// 1. โหลด manifest แล้วตรวจลายเซ็น — ไม่ผ่านคือจบ ไม่แตะอะไรเลย
/// 2. โหลดไฟล์ API แล้วตรวจค่าแฮช
/// 3. สำรองฐานข้อมูล — สำรองไม่สำเร็จคือไม่อัปเดต ถือเป็นกันชนสุดท้าย
/// 4. หยุดบริการ แตกเวอร์ชันใหม่ ชี้ทางลัด เริ่มบริการ
/// 5. รอ health check ผ่าน ไม่ผ่านให้ชี้ทางลัดกลับแล้วเริ่มของเดิม
/// 6. ผ่านแล้วจึงค่อยปล่อย feed ให้เครื่องลูกเห็น
///
/// ขั้นที่ 6 อยู่ท้ายสุดโดยตั้งใจ เครื่องลูกจึงไม่มีทางอัปเดตแซงเครื่องแม่ได้
/// </summary>
public sealed class UpdateRunner(
    UpdaterOptions options,
    WindowsServiceControl services,
    IHttpClientFactory httpClientFactory,
    ILogger<UpdateRunner> logger)
{
    public async Task<UpdateOutcome> RunOnceAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.FeedUrl))
        {
            logger.LogDebug("ยังไม่ได้ตั้งที่อยู่เซิร์ฟเวอร์ปล่อยเวอร์ชัน จึงข้ามการตรวจ");
            return UpdateOutcome.UpToDate;
        }

        ReleaseManifest? manifest;

        try
        {
            manifest = await FetchManifestAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // ไซต์ที่ไม่มีเน็ตจะมาถึงตรงนี้ทุกรอบ จึงบันทึกเป็นข้อมูลธรรมดา ไม่ใช่ข้อผิดพลาด
            logger.LogDebug(ex, "ติดต่อเซิร์ฟเวอร์ปล่อยเวอร์ชันไม่ได้");
            return UpdateOutcome.Offline;
        }

        if (manifest is null)
        {
            logger.LogWarning("ตรวจลายเซ็นของรายการปล่อยเวอร์ชันไม่ผ่าน จึงไม่ดำเนินการต่อ");
            return UpdateOutcome.Rejected;
        }

        if (!IsNewer(manifest.Version)) return UpdateOutcome.UpToDate;

        logger.LogInformation("พบเวอร์ชันใหม่ {Version} เริ่มขั้นตอนอัปเดต", manifest.Version);

        return await ApplyAsync(manifest, ct);
    }

    // ── ขั้นตอนย่อย ────────────────────────────────────────────────────────

    private async Task<ReleaseManifest?> FetchManifestAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("release-feed");
        var signedJson = await client.GetStringAsync(ManifestUrl(), ct);

        return ManifestVerifier.Verify(signedJson);
    }

    private async Task<UpdateOutcome> ApplyAsync(ReleaseManifest manifest, CancellationToken ct)
    {
        var staging = Path.Combine(options.StagingRoot, manifest.Version);

        try
        {
            Directory.CreateDirectory(staging);

            var apiZip = await DownloadApiPackageAsync(manifest, staging, ct);
            if (apiZip is null) return UpdateOutcome.Rejected;

            if (!await BackupDatabaseAsync(manifest.Version, ct))
            {
                logger.LogError("สำรองฐานข้อมูลไม่สำเร็จ จึงยกเลิกการอัปเดต");
                return UpdateOutcome.Failed;
            }

            var previousVersion = ReadCurrentVersionTarget();
            var targetDirectory = Path.Combine(options.ApiVersionsRoot, manifest.Version);

            if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, recursive: true);
            ZipFile.ExtractToDirectory(apiZip, targetDirectory);

            await services.StopAsync(options.ApiServiceName, ct);
            await services.WaitUntilStoppedAsync(options.ApiServiceName, TimeSpan.FromSeconds(60), ct);

            PointCurrentTo(targetDirectory);

            await services.StartAsync(options.ApiServiceName, ct);

            if (await WaitUntilHealthyAsync(ct))
            {
                await PublishClientFeedAsync(manifest, staging, ct);
                CleanOldVersions(manifest.Version);

                logger.LogInformation("อัปเดตเครื่องแม่เป็นเวอร์ชัน {Version} สำเร็จ", manifest.Version);
                return UpdateOutcome.Updated;
            }

            logger.LogError("เวอร์ชันใหม่ไม่ผ่านการตรวจสุขภาพ กำลังย้อนกลับไปเวอร์ชันเดิม");

            await services.StopAsync(options.ApiServiceName, ct);
            await services.WaitUntilStoppedAsync(options.ApiServiceName, TimeSpan.FromSeconds(60), ct);

            if (previousVersion is not null) PointCurrentTo(previousVersion);

            await services.StartAsync(options.ApiServiceName, ct);

            return UpdateOutcome.RolledBack;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "อัปเดตไม่สำเร็จ");
            return UpdateOutcome.Failed;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private async Task<string?> DownloadApiPackageAsync(ReleaseManifest manifest, string staging, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("release-feed");
        var target = Path.Combine(staging, Path.GetFileName(manifest.ApiPackage));

        await using (var source = await client.GetStreamAsync(Relative(manifest.ApiPackage), ct))
        await using (var destination = File.Create(target))
        {
            await source.CopyToAsync(destination, ct);
        }

        if (await ManifestVerifier.VerifyFileHashAsync(target, manifest.ApiSha256, ct)) return target;

        logger.LogWarning("ค่าแฮชของไฟล์บริการเบื้องหลังไม่ตรงกับที่ประกาศไว้ จึงไม่ติดตั้ง");
        return null;
    }

    /// <summary>
    /// สำรองฐานข้อมูลก่อนอัปเดตเสมอ
    /// นี่คือกันชนสุดท้ายถ้า migration ของเวอร์ชันใหม่ทำข้อมูลเสีย
    /// </summary>
    private async Task<bool> BackupDatabaseAsync(string version, CancellationToken ct)
    {
        if (!File.Exists(options.PgDumpPath))
        {
            logger.LogWarning("ไม่พบ pg_dump ที่ {Path} จึงข้ามการสำรอง", options.PgDumpPath);
            return false;
        }

        Directory.CreateDirectory(options.BackupRoot);

        var target = Path.Combine(
            options.BackupRoot,
            $"before-{version}-{DateTime.Now:yyyyMMdd-HHmmss}.dump");

        var secretPath = Path.Combine(options.ConfigRoot, "db.secret");

        var startInfo = new ProcessStartInfo(options.PgDumpPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[]
                 {
                     "-h", "127.0.0.1", "-p", "54329", "-U", "asliving_app",
                     "-d", "astarliving", "-Fc", "-f", target,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        // ส่งรหัสผ่านทางตัวแปรสภาพแวดล้อม ไม่ใช่บรรทัดคำสั่ง เพื่อไม่ให้ไปโผล่ในรายการโปรเซส
        if (File.Exists(secretPath)) startInfo.Environment["PGPASSWORD"] = MachineSecret.Read(secretPath);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return false;

            var error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0)
            {
                logger.LogInformation("สำรองฐานข้อมูลไว้ที่ {Path}", target);
                return true;
            }

            logger.LogError("pg_dump จบด้วยรหัส {Code}: {Error}", process.ExitCode, error);
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "เรียก pg_dump ไม่ได้");
            return false;
        }
    }

    /// <summary>
    /// ปล่อย feed ให้เครื่องลูกเห็น
    ///
    /// ต้องเป็นขั้นตอนสุดท้ายเสมอ เพราะวินาทีที่ไฟล์นี้โผล่ เครื่องลูกจะเริ่มอัปเดตตาม
    /// เขียนด้วยชื่อชั่วคราวก่อนแล้วค่อยเปลี่ยนชื่อ เพราะการเปลี่ยนชื่อเป็น atomic
    /// เครื่องลูกจึงไม่มีทางอ่านไฟล์ที่ยังโหลดไม่เสร็จ
    /// </summary>
    private async Task PublishClientFeedAsync(ReleaseManifest manifest, string staging, CancellationToken ct)
    {
        Directory.CreateDirectory(options.FeedRoot);

        var client = httpClientFactory.CreateClient("release-feed");

        foreach (var file in manifest.ClientFiles)
        {
            var name = Path.GetFileName(file);
            var isReleasesFile = string.Equals(name, UpdateFeedNames.Releases, StringComparison.OrdinalIgnoreCase);

            var target = Path.Combine(options.FeedRoot, isReleasesFile ? UpdateFeedNames.Staged : name);

            await using (var source = await client.GetStreamAsync(Relative(file), ct))
            await using (var destination = File.Create(target))
            {
                await source.CopyToAsync(destination, ct);
            }

            logger.LogDebug("มิเรอร์ไฟล์ {Name} มาไว้ให้เครื่องลูกแล้ว", name);
        }

        var staged = Path.Combine(options.FeedRoot, UpdateFeedNames.Staged);
        var live = Path.Combine(options.FeedRoot, UpdateFeedNames.Releases);

        if (File.Exists(staged)) File.Move(staged, live, overwrite: true);
    }

    private async Task<bool> WaitUntilHealthyAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("health");
        var deadline = DateTimeOffset.UtcNow + options.HealthTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(options.HealthUrl, ct);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // ระหว่างที่บริการกำลังเริ่ม การต่อไม่ติดเป็นเรื่องปกติ
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        return false;
    }

    // ── ตัวช่วย ────────────────────────────────────────────────────────────

    private string ManifestUrl() => $"{options.FeedUrl!.TrimEnd('/')}/{options.Channel}/manifest.json";

    private string Relative(string file) => $"{options.FeedUrl!.TrimEnd('/')}/{options.Channel}/{file.TrimStart('/')}";

    private bool IsNewer(string version)
    {
        var current = ReadCurrentVersion();

        return Version.TryParse(version, out var candidate)
            && Version.TryParse(current, out var running)
            && candidate > running;
    }

    /// <summary>เวอร์ชันที่กำลังทำงานอยู่ อ่านจากชื่อโฟลเดอร์ที่ทางลัดชี้ไป</summary>
    private string ReadCurrentVersion()
    {
        var target = ReadCurrentVersionTarget();
        return target is null ? "0.0.0" : Path.GetFileName(target);
    }

    private string? ReadCurrentVersionTarget()
    {
        try
        {
            if (!Directory.Exists(options.ApiCurrentPath)) return null;

            var info = new DirectoryInfo(options.ApiCurrentPath);
            return info.LinkTarget ?? info.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// ชี้ทางลัด current ไปยังเวอร์ชันที่ต้องการ
    /// ใช้ junction ของ NTFS เพื่อให้การย้อนกลับเป็นแค่การชี้กลับ ไม่ต้องคัดลอกไฟล์ใหม่
    /// </summary>
    private void PointCurrentTo(string versionDirectory)
    {
        if (Directory.Exists(options.ApiCurrentPath))
        {
            var info = new DirectoryInfo(options.ApiCurrentPath);

            // ถ้าเป็นโฟลเดอร์จริง (ติดตั้งครั้งแรกยังไม่ได้ทำเป็นทางลัด) ให้เก็บไว้เป็นเวอร์ชันหนึ่ง
            if (info.LinkTarget is null)
            {
                var preserved = Path.Combine(options.ApiVersionsRoot, "0.0.0");
                Directory.CreateDirectory(options.ApiVersionsRoot);

                if (!Directory.Exists(preserved)) Directory.Move(options.ApiCurrentPath, preserved);
                else Directory.Delete(options.ApiCurrentPath, recursive: true);
            }
            else
            {
                Directory.Delete(options.ApiCurrentPath);
            }
        }

        Directory.CreateSymbolicLink(options.ApiCurrentPath, versionDirectory);
    }

    private void CleanOldVersions(string currentVersion)
    {
        try
        {
            if (!Directory.Exists(options.ApiVersionsRoot)) return;

            var keep = Directory.GetDirectories(options.ApiVersionsRoot)
                .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0))
                .Take(options.KeepVersions)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in Directory.GetDirectories(options.ApiVersionsRoot))
            {
                if (keep.Contains(directory)) continue;
                if (Path.GetFileName(directory) == currentVersion) continue;

                TryDelete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "ล้างเวอร์ชันเก่าไม่สำเร็จ");
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ลบไม่ได้ไม่ใช่เรื่องคอขาดบาดตาย รอบหน้าค่อยลบใหม่
        }
    }
}

/// <summary>ชื่อไฟล์ feed ที่ API และ ServerUpdater ต้องใช้ตรงกัน</summary>
public static class UpdateFeedNames
{
    public const string Releases = "releases.win.json";

    public const string Staged = "releases.win.json.staged";
}
