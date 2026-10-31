using System.Collections.Concurrent;
using AstarLiving.Api.Common;
using AstarLiving.Api.Configuration;
using Microsoft.Extensions.Options;

namespace AstarLiving.Api.Modules.Maintenance;

/// <summary>ไฟล์สำรองหนึ่งก้อนที่รอให้เจ้าของมาดาวน์โหลด</summary>
public sealed record BackupArtifact(
    Guid Token,
    string Path,
    string FileName,
    long SizeBytes,
    int UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// ที่เก็บไฟล์สำรองที่รอดาวน์โหลด และตัวกันไม่ให้สำรองซ้อนกัน
///
/// เหตุผลที่ต้องมีตารางแปลง token เป็นที่อยู่ไฟล์ แทนที่จะรับชื่อไฟล์จากผู้ใช้ตรง ๆ
/// คือการปิดช่องโหว่เดินข้ามโฟลเดอร์ "ด้วยการออกแบบ" ไม่ใช่ "ด้วยการตรวจ"
/// ชื่อไฟล์ที่ผู้ใช้ส่งมาจะไม่เคยถูกนำไปต่อกับที่อยู่โฟลเดอร์เลยแม้แต่ครั้งเดียว
/// </summary>
public sealed class BackupArtifactStore(IOptions<AstarLivingOptions> options, ILogger<BackupArtifactStore> logger)
{
    private readonly BackupOptions _options = options.Value.Backup;

    private readonly ConcurrentDictionary<Guid, BackupArtifact> _artifacts = new();

    /// <summary>
    /// กันการสำรองซ้อนกันที่ฝั่งเครื่องแม่
    ///
    /// ฝั่งโปรแกรมหน้าจอกันไว้แล้วชั้นหนึ่ง แต่พึ่งอย่างเดียวไม่ได้
    /// เพราะถ้าวันหน้ามีใครเผลอเอาเส้นทางนี้ไปผูกกับตัวเรียกที่ลองใหม่อัตโนมัติ
    /// pg_dump จะถูกสั่งพร้อมกันหลายตัวจนกินดิสก์และ I/O ของฐานข้อมูลหมด
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>จองสิทธิ์สำรอง ถ้ามีคนทำอยู่แล้วจะตอบ 409 ทันทีโดยไม่รอคิว</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, ct))
        {
            throw AppException.Conflict("กำลังสำรองฐานข้อมูลอยู่ กรุณารอให้เสร็จก่อน");
        }

        return new Lease(_gate);
    }

    public BackupArtifact Add(string path, string fileName, long sizeBytes, int userId, DateTimeOffset now)
    {
        var artifact = new BackupArtifact(
            Guid.NewGuid(), path, fileName, sizeBytes, userId, now, now + _options.TokenTtl);

        _artifacts[artifact.Token] = artifact;
        return artifact;
    }

    /// <summary>
    /// หยิบไฟล์ออกจากตาราง — ใช้ได้ครั้งเดียว
    ///
    /// ตัดออกตั้งแต่ก่อนเริ่มส่งไฟล์ ถ้าดาวน์โหลดขาดกลางคันต้องสั่งสำรองใหม่
    /// ยอมแลกความสะดวกกับการที่ token ที่หลุดไปอยู่ในบันทึกจะใช้ซ้ำไม่ได้
    /// </summary>
    public BackupArtifact Take(Guid token, int userId, DateTimeOffset now)
    {
        if (!_artifacts.TryRemove(token, out var artifact))
        {
            throw AppException.NotFound("ไม่พบไฟล์สำรอง หรือลิงก์หมดอายุแล้ว กรุณาสำรองใหม่อีกครั้ง");
        }

        // คนละคนกับที่สั่งสำรอง ไม่ให้ผ่านแม้จะมีสิทธิ์ระดับเดียวกัน
        if (artifact.UserId != userId)
        {
            TryDelete(artifact.Path);
            throw AppException.Forbidden("ไฟล์สำรองนี้ไม่ได้สร้างโดยบัญชีของคุณ");
        }

        if (now > artifact.ExpiresAt)
        {
            TryDelete(artifact.Path);
            throw AppException.NotFound("ลิงก์ดาวน์โหลดหมดอายุแล้ว กรุณาสำรองใหม่อีกครั้ง");
        }

        if (!File.Exists(artifact.Path))
        {
            throw AppException.NotFound("ไม่พบไฟล์สำรองบนเครื่องแม่ข่าย กรุณาสำรองใหม่อีกครั้ง");
        }

        return artifact;
    }

    /// <summary>
    /// กวาดไฟล์กำพร้าที่ค้างอยู่
    ///
    /// ปกติไฟล์ถูกลบตอนส่งเสร็จด้วย DeleteOnClose แต่ถ้าบริการถูกรีสตาร์ต
    /// ระหว่างที่ไฟล์ยังรอดาวน์โหลด จะไม่มีใครลบให้ จึงต้องกวาดก่อนเริ่มรอบใหม่ทุกครั้ง
    /// </summary>
    public void PruneExpired(DateTimeOffset now)
    {
        foreach (var (token, artifact) in _artifacts)
        {
            if (now <= artifact.ExpiresAt) continue;

            _artifacts.TryRemove(token, out _);
            TryDelete(artifact.Path);
        }

        if (!Directory.Exists(_options.WorkDirectory)) return;

        foreach (var file in Directory.EnumerateFiles(_options.WorkDirectory, "*.dump"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) > now.UtcDateTime - _options.TokenTtl) continue;

                File.Delete(file);
                logger.LogInformation("ลบไฟล์สำรองที่ค้างอยู่ {Path}", file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "ลบไฟล์สำรองที่ค้างอยู่ {Path} ไม่สำเร็จ", file);
            }
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
            logger.LogWarning(ex, "ลบไฟล์สำรอง {Path} ไม่สำเร็จ", path);
        }
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
