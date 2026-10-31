using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstarLiving.Shared.Versioning;
using FluentAssertions;
using Xunit;

namespace AstarLiving.Api.Tests;

/// <summary>
/// การตรวจลายเซ็นของรายการปล่อยเวอร์ชัน
///
/// นี่คือสิ่งเดียวที่กันไม่ให้ใครปล่อยเวอร์ชันปลอมเข้าเครื่องลูกค้าได้
/// ตราบใดที่ยังไม่มีใบเซ็นโค้ด ถ้าตรงนี้พลาด ใครที่ยึดเซิร์ฟเวอร์ไฟล์ได้
/// ก็สั่งให้เครื่องแม่ของลูกค้าติดตั้งอะไรก็ได้
/// </summary>
public class ReleaseManifestTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _originalPublicKey = ManifestVerifier.PublicKeyBase64;

    public ReleaseManifestTests() =>
        ManifestVerifier.PublicKeyBase64 = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public void Dispose()
    {
        ManifestVerifier.PublicKeyBase64 = _originalPublicKey;
        _key.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void รายการที่เซ็นถูกต้องถูกยอมรับ()
    {
        var signed = Sign(SampleManifest());

        var verified = ManifestVerifier.Verify(signed);

        verified.Should().NotBeNull();
        verified!.Version.Should().Be("1.0.1");
        verified.ApiSha256.Should().Be("ABC123");
    }

    [Fact]
    public void รายการที่ถูกแก้เนื้อหาหลังเซ็นถูกปฏิเสธ()
    {
        var signed = Sign(SampleManifest());

        // จำลองคนที่แก้ไฟล์บนเซิร์ฟเวอร์ให้ชี้ไปแพ็กเกจของตัวเอง
        // แก้ที่ตัวเลขเวอร์ชันโดยตรง เพราะเนื้อหาถูกหนีอักขระอยู่ในสตริงอีกชั้น
        var tampered = signed.Replace("1.0.1", "9.9.9", StringComparison.Ordinal);
        tampered.Should().NotBe(signed, "การทดสอบนี้ต้องแก้เนื้อหาได้จริงจึงจะมีความหมาย");

        ManifestVerifier.Verify(tampered).Should().BeNull();
    }

    [Fact]
    public void รายการที่เซ็นด้วยกุญแจอื่นถูกปฏิเสธ()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var signed = Sign(SampleManifest(), attacker);

        ManifestVerifier.Verify(signed).Should().BeNull();
    }

    [Fact]
    public void รายการที่ไม่มีลายเซ็นถูกปฏิเสธ()
    {
        var payload = JsonSerializer.Serialize(SampleManifest(), JsonOptions);
        var unsigned = JsonSerializer.Serialize(
            new SignedReleaseManifest { Payload = payload, Signature = string.Empty }, JsonOptions);

        ManifestVerifier.Verify(unsigned).Should().BeNull();
    }

    [Fact]
    public void เมื่อยังไม่ได้ตั้งกุญแจสาธารณะต้องปฏิเสธทุกรายการ()
    {
        var signed = Sign(SampleManifest());

        // เครื่องที่ผู้ดูแลยังไม่ได้ใส่กุญแจต้องไม่ยอมรับอะไรเลย
        // ปลอดภัยกว่าการเปิดรับทุกอย่างเพราะ "ยังไม่ได้ตั้งค่า"
        ManifestVerifier.PublicKeyBase64 = string.Empty;

        ManifestVerifier.Verify(signed).Should().BeNull();
    }

    [Fact]
    public async Task ค่าแฮชของไฟล์ที่ไม่ตรงถูกปฏิเสธ()
    {
        var path = Path.Combine(Path.GetTempPath(), $"astar-test-{Guid.NewGuid():N}.bin");
        await File.WriteAllTextAsync(path, "เนื้อหาของแพ็กเกจ");

        try
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            await stream.DisposeAsync();

            (await ManifestVerifier.VerifyFileHashAsync(path, actual)).Should().BeTrue();
            (await ManifestVerifier.VerifyFileHashAsync(path, new string('0', 64))).Should().BeFalse();
            (await ManifestVerifier.VerifyFileHashAsync(path, string.Empty)).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ReleaseManifest SampleManifest() => new()
    {
        Version = "1.0.1",
        Channel = "stable",
        ReleasedAt = DateTimeOffset.UnixEpoch,
        ApiPackage = "api/AstarLiving.Api-1.0.1-win-x64.zip",
        ApiSha256 = "ABC123",
        ClientFiles = ["AstarLiving-1.0.1-full.nupkg", "releases.win.json"],
        ContractVersion = 1,
    };

    private string Sign(ReleaseManifest manifest, ECDsa? signer = null)
    {
        var payload = JsonSerializer.Serialize(manifest, JsonOptions);

        var signature = Convert.ToBase64String(
            (signer ?? _key).SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256));

        return JsonSerializer.Serialize(
            new SignedReleaseManifest { Payload = payload, Signature = signature }, JsonOptions);
    }
}
