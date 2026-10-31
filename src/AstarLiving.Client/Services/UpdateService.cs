using System.IO;
using AstarLiving.Shared.Versioning;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace AstarLiving.Client.Services;

/// <summary>
/// ตรวจและติดตั้งเวอร์ชันใหม่ของโปรแกรมหน้าจอ
///
/// ดึงจากเครื่องแม่ในวงแลนเท่านั้น เครื่องลูกจึงไม่ต้องมีอินเทอร์เน็ตเลย
/// และจะไม่มีทางอัปเดตแซงเครื่องแม่ เพราะเครื่องแม่เป็นผู้วางไฟล์ feed
/// หลังอัปเดตตัวเองสำเร็จและผ่าน health check แล้วเท่านั้น
///
/// ตอนรันจากเครื่องนักพัฒนา (ไม่ได้ติดตั้งผ่านตัวติดตั้ง) Velopack จะรายงานว่าไม่ได้ติดตั้ง
/// ตัวนี้จึงตรวจก่อนทุกครั้ง แล้วรายงานสถานะว่าตรวจไม่ได้แทนการโยนข้อผิดพลาด
/// </summary>
public sealed class UpdateService(ApiClient api, ClientSettings settings, ILogger<UpdateService> logger)
{
    /// <summary>จำกัดการตรวจอัตโนมัติไม่ให้ถี่เกินไป ตรงกับที่วางไว้ว่าราววันละ 4 ครั้ง</summary>
    private static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _lastAutoCheck = DateTimeOffset.MinValue;
    private UpdateInfo? _pending;

    /// <summary>ที่อยู่ feed ที่เครื่องแม่บอกมาในการตรวจครั้งล่าสุด</summary>
    private string? _feedUrl;

    public UpdateStatus Status { get; private set; } = UpdateStatus.Unknown;

    /// <summary>เวอร์ชันใหม่ที่รออยู่ หรือ null เมื่อไม่มี</summary>
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>ข้อความสั้น ๆ ที่แสดงบนแถบท้ายหน้าจอ</summary>
    public string StatusText => Status switch
    {
        UpdateStatus.Checking => "กำลังตรวจสอบการอัปเดต...",
        UpdateStatus.UpToDate => "เป็นเวอร์ชันล่าสุดแล้ว",
        UpdateStatus.UpdateAvailable => $"มีเวอร์ชันใหม่ {PendingVersion}",
        UpdateStatus.Downloading => "กำลังดาวน์โหลดเวอร์ชันใหม่...",
        UpdateStatus.ReadyToApply => $"พร้อมติดตั้งเวอร์ชัน {PendingVersion}",
        UpdateStatus.Unavailable => "ตรวจสอบการอัปเดตไม่ได้",
        _ => string.Empty,
    };

    public event EventHandler? StatusChanged;

    /// <summary>
    /// true เมื่อโปรแกรมถูกติดตั้งผ่านตัวติดตั้งจริง ไม่ใช่รันจากโฟลเดอร์ build
    ///
    /// ดูจากโครงโฟลเดอร์ที่ตัวติดตั้งสร้าง ไม่ได้ถาม UpdateManager
    /// เพราะการสร้าง UpdateManager ต้องมีที่อยู่ feed ที่ใช้ได้จริง ซึ่งตอนนี้ยังไม่รู้
    /// (ต้องถามเครื่องแม่ก่อน) การถามตรงนี้จึงต้องไม่พึ่งพามัน
    /// </summary>
    public bool IsInstalled
    {
        get
        {
            try
            {
                var exeDirectory = Path.GetDirectoryName(Environment.ProcessPath);
                if (exeDirectory is null) return false;

                // ตัวติดตั้งวางโปรแกรมไว้ใน <root>\current และวาง Update.exe ไว้ที่ <root>
                var root = Directory.GetParent(exeDirectory);

                return root is not null
                    && string.Equals(Path.GetFileName(exeDirectory), "current", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(root.FullName, "Update.exe"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// ตรวจอัตโนมัติหลังเปิดโปรแกรม
    /// กลืนข้อผิดพลาดทุกชนิด เพราะผู้ใช้ไม่ได้สั่ง จึงไม่ควรมีอะไรเด้งขึ้นมารบกวน
    /// </summary>
    public async Task CheckInBackgroundAsync()
    {
        if (DateTimeOffset.UtcNow - _lastAutoCheck < AutoCheckInterval) return;

        _lastAutoCheck = DateTimeOffset.UtcNow;

        try
        {
            await CheckAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ตรวจสอบการอัปเดตเบื้องหลังไม่สำเร็จ");
        }
    }

    /// <summary>ตรวจว่ามีเวอร์ชันใหม่ไหม คืน true เมื่อมี</summary>
    public async Task<bool> CheckAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            SetStatus(UpdateStatus.Checking);

            var policy = await api.GetRawAsync<UpdatePolicyResponse>("api/v1/update-policy", ct);

            if (!policy.UpdatesAvailable)
            {
                // เครื่องแม่ยังไม่ได้วาง feed ไว้ให้ ซึ่งเป็นสถานะปกติของไซต์ที่ยังไม่เคยอัปเดต
                SetStatus(UpdateStatus.UpToDate);
                return false;
            }

            if (!IsInstalled)
            {
                SetStatus(UpdateStatus.Unavailable);
                return false;
            }

            _feedUrl = policy.FeedUrl;

            var manager = CreateManager(policy.FeedUrl);
            var update = await manager.CheckForUpdatesAsync();

            if (update is null)
            {
                SetStatus(UpdateStatus.UpToDate);
                return false;
            }

            // ห้ามติดตั้งเวอร์ชันที่สูงกว่าเครื่องแม่
            // ถ้าเกิดขึ้นแปลว่ามีคนวางไฟล์ผิดรุ่นไว้ ปลอดภัยกว่าถ้าไม่ทำอะไร
            if (IsNewerThanServer(update, policy.AllowedMaxVersion))
            {
                logger.LogWarning(
                    "ข้ามเวอร์ชัน {Version} เพราะสูงกว่าที่เครื่องแม่อนุญาต ({Allowed})",
                    update.TargetFullRelease.Version, policy.AllowedMaxVersion);

                SetStatus(UpdateStatus.UpToDate);
                return false;
            }

            _pending = update;
            SetStatus(UpdateStatus.UpdateAvailable);
            return true;
        }
        catch (ApiException ex)
        {
            logger.LogInformation(ex, "ถามนโยบายการอัปเดตจากเครื่องแม่ข่ายไม่ได้");
            SetStatus(UpdateStatus.Unavailable);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ตรวจสอบการอัปเดตไม่สำเร็จ (feed {Feed})", _feedUrl);
            SetStatus(UpdateStatus.Unavailable);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>ดาวน์โหลดเวอร์ชันที่พบไว้ก่อน ยังไม่ติดตั้ง</summary>
    public async Task<bool> DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (_pending is null) return false;

        await _gate.WaitAsync(ct);
        try
        {
            SetStatus(UpdateStatus.Downloading);

            var manager = CreateManager(_feedUrl ?? await ResolveFeedUrlAsync(ct));
            await manager.DownloadUpdatesAsync(_pending, percent => progress?.Report(percent));

            SetStatus(UpdateStatus.ReadyToApply);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ดาวน์โหลดเวอร์ชันใหม่ไม่สำเร็จ");
            SetStatus(UpdateStatus.Unavailable);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>ติดตั้งแล้วเปิดโปรแกรมใหม่ทันที โปรแกรมจะปิดตัวเองภายในคำสั่งนี้</summary>
    public void ApplyAndRestart()
    {
        if (_pending is null) return;

        var manager = CreateManager(_feedUrl ?? settings.ServerUrl + "/api/updates");
        manager.ApplyUpdatesAndRestart(_pending);
    }

    /// <summary>
    /// ติดตั้งตอนผู้ใช้ปิดโปรแกรม
    /// เป็นค่าเริ่มต้นที่ปลอดภัยกว่า เพราะคนที่กำลังออกบิลค้างอยู่จะไม่ถูกบังคับปิดกลางคัน
    /// </summary>
    public void ApplyOnExit()
    {
        if (_pending is null) return;

        try
        {
            var manager = CreateManager(_feedUrl ?? settings.ServerUrl + "/api/updates");
            manager.WaitExitThenApplyUpdates(_pending);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ตั้งให้ติดตั้งตอนปิดโปรแกรมไม่สำเร็จ");
        }
    }

    /// <summary>
    /// ที่อยู่ feed มาจากเครื่องแม่ ไม่ได้ฝังไว้ในโปรแกรม
    /// เพราะเครื่องแม่เท่านั้นที่รู้ว่าตัวเองถูกเรียกด้วยไอพีไหนในเครือข่ายของลูกค้า
    /// </summary>
    private static UpdateManager CreateManager(string feedUrl) => new(new SimpleWebSource(feedUrl));

    private async Task<string> ResolveFeedUrlAsync(CancellationToken ct)
    {
        var policy = await api.GetRawAsync<UpdatePolicyResponse>("api/v1/update-policy", ct);
        return policy.FeedUrl;
    }

    private static bool IsNewerThanServer(UpdateInfo update, string allowedMaxVersion)
    {
        if (!NuGet.Versioning.SemanticVersion.TryParse(allowedMaxVersion, out var allowed)) return false;

        return update.TargetFullRelease.Version > allowed;
    }

    private void SetStatus(UpdateStatus status)
    {
        if (Status == status) return;

        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
