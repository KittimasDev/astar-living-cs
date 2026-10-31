using AstarLiving.Api.Common;
using AstarLiving.Api.Configuration;
using AstarLiving.Api.Modules.Maintenance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AstarLiving.Api.Tests;

/// <summary>
/// บัตรผ่านสำหรับดาวน์โหลดไฟล์สำรอง
///
/// ไฟล์สำรองมีข้อมูลส่วนบุคคลของลูกบ้านทั้งโครงการและรหัสผ่านที่เข้ารหัสไว้ของผู้ใช้ทุกคน
/// ชุดนี้จึงคุมว่าบัตรผ่านใช้ซ้ำไม่ได้ ข้ามคนไม่ได้ และหมดอายุจริง
/// </summary>
public class BackupArtifactStoreTests : IDisposable
{
    private const int Owner = 7;
    private const int SomeoneElse = 8;

    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(), "astarliving-backup-" + Guid.NewGuid().ToString("N"));

    private readonly BackupArtifactStore _store;
    private readonly DateTimeOffset _now = new(2569, 10, 4, 14, 0, 0, TimeSpan.FromHours(7));

    public BackupArtifactStoreTests()
    {
        Directory.CreateDirectory(_workDirectory);

        var options = new AstarLivingOptions
        {
            Backup = new BackupOptions
            {
                WorkDirectory = _workDirectory,
                BackupRoot = _workDirectory,
                TokenTtl = TimeSpan.FromMinutes(15),
            },
        };

        _store = new BackupArtifactStore(
            Options.Create(options), NullLogger<BackupArtifactStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void เจ้าของดาวน์โหลดได้()
    {
        var artifact = AddFile();

        var taken = _store.Take(artifact.Token, Owner, _now);

        taken.FileName.Should().Be(artifact.FileName);
    }

    /// <summary>บัตรผ่านที่หลุดไปอยู่ในบันทึกต้องใช้ซ้ำไม่ได้</summary>
    [Fact]
    public void บัตรผ่านใช้ได้ครั้งเดียว()
    {
        var artifact = AddFile();

        _store.Take(artifact.Token, Owner, _now);

        var act = () => _store.Take(artifact.Token, Owner, _now);

        act.Should().Throw<AppException>().Which.StatusCode.Should().Be(404);
    }

    /// <summary>ผู้ดูแลสูงสุดคนอื่นก็เอาบัตรผ่านของคนแรกไปใช้ไม่ได้</summary>
    [Fact]
    public void คนอื่นใช้บัตรผ่านของเจ้าของไม่ได้()
    {
        var artifact = AddFile();

        var act = () => _store.Take(artifact.Token, SomeoneElse, _now);

        act.Should().Throw<AppException>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public void บัตรผ่านหมดอายุแล้วใช้ไม่ได้_และไฟล์ถูกลบ()
    {
        var artifact = AddFile();

        var act = () => _store.Take(artifact.Token, Owner, _now.AddMinutes(16));

        act.Should().Throw<AppException>().Which.StatusCode.Should().Be(404);
        File.Exists(artifact.Path).Should().BeFalse("ไฟล์ที่หมดอายุต้องไม่ค้างอยู่บนเครื่องแม่");
    }

    [Fact]
    public void บัตรผ่านที่ไม่มีอยู่จริงต้องถูกปฏิเสธ()
    {
        var act = () => _store.Take(Guid.NewGuid(), Owner, _now);

        act.Should().Throw<AppException>().Which.StatusCode.Should().Be(404);
    }

    /// <summary>
    /// ถ้าบริการถูกรีสตาร์ตระหว่างที่ไฟล์ยังรอดาวน์โหลด จะไม่มีใครลบให้
    /// การกวาดก่อนเริ่มรอบใหม่จึงเป็นทางเดียวที่ไฟล์พวกนี้จะหายไป
    /// </summary>
    [Fact]
    public void กวาดไฟล์ที่ค้างอยู่เกินอายุทิ้ง()
    {
        var stale = Path.Combine(_workDirectory, "astarliving-เก่า.dump");
        File.WriteAllText(stale, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

        var fresh = Path.Combine(_workDirectory, "astarliving-ใหม่.dump");
        File.WriteAllText(fresh, "x");

        _store.PruneExpired(DateTimeOffset.UtcNow);

        File.Exists(stale).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue("ไฟล์ที่เพิ่งสร้างยังรอเจ้าของมาดาวน์โหลดอยู่");
    }

    [Fact]
    public async Task สำรองซ้อนกันไม่ได้()
    {
        using var first = await _store.AcquireAsync(CancellationToken.None);

        var act = async () => await _store.AcquireAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ปล่อยสิทธิ์แล้วสำรองรอบถัดไปได้()
    {
        using (await _store.AcquireAsync(CancellationToken.None)) { }

        var act = async () => (await _store.AcquireAsync(CancellationToken.None)).Dispose();

        await act.Should().NotThrowAsync();
    }

    private BackupArtifact AddFile()
    {
        var name = $"astarliving-{Guid.NewGuid():N}.dump";
        var path = Path.Combine(_workDirectory, name);
        File.WriteAllText(path, "pretend this is a dump");

        return _store.Add(path, name, new FileInfo(path).Length, Owner, _now);
    }
}
