using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstarLiving.Shared.Versioning;

/// <summary>
/// รายการสิ่งที่ปล่อยในเวอร์ชันหนึ่ง
///
/// ไฟล์นี้ถูกเซ็นด้วย Ed25519 และเครื่องแม่ตรวจลายเซ็นก่อนเชื่อทุกครั้ง
/// ทำให้ได้ความปลอดภัยของการปล่อยเวอร์ชันตั้งแต่วันนี้ ทั้งที่ยังไม่มีใบเซ็นโค้ด
/// ใครที่ยึดเซิร์ฟเวอร์ไฟล์ไปได้ก็ยังปล่อยเวอร์ชันปลอมไม่ได้ ถ้าไม่มีกุญแจส่วนตัว
/// </summary>
public class ReleaseManifest
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "stable";

    [JsonPropertyName("releasedAt")]
    public DateTimeOffset ReleasedAt { get; init; }

    /// <summary>ไฟล์ zip ของบริการเบื้องหลัง สัมพัทธ์กับที่อยู่ของ manifest</summary>
    [JsonPropertyName("apiPackage")]
    public string ApiPackage { get; init; } = string.Empty;

    [JsonPropertyName("apiSha256")]
    public string ApiSha256 { get; init; } = string.Empty;

    /// <summary>ไฟล์ของโปรแกรมหน้าจอที่ต้องมิเรอร์มาไว้ให้เครื่องลูกดึง</summary>
    [JsonPropertyName("clientFiles")]
    public List<string> ClientFiles { get; init; } = [];

    /// <summary>รุ่นสัญญาที่เวอร์ชันนี้พูดได้ ใช้เตือนก่อนอัปเดตข้ามรุ่น</summary>
    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; init; } = 1;

    /// <summary>บันทึกการเปลี่ยนแปลงแบบย่อ แสดงให้ผู้ดูแลเห็นก่อนกดอัปเดต</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>manifest พร้อมลายเซ็นที่วางบนเซิร์ฟเวอร์ปล่อยเวอร์ชัน</summary>
public class SignedReleaseManifest
{
    /// <summary>เนื้อหา manifest ในรูป JSON ที่ถูกเซ็น เก็บเป็นข้อความดิบเพื่อให้ตรวจลายเซ็นได้ตรงไบต์</summary>
    [JsonPropertyName("payload")]
    public string Payload { get; init; } = string.Empty;

    /// <summary>ลายเซ็น Ed25519 ของ payload เข้ารหัสแบบ base64</summary>
    [JsonPropertyName("signature")]
    public string Signature { get; init; } = string.Empty;
}

/// <summary>
/// ตรวจลายเซ็นของ manifest
///
/// .NET 8 ยังไม่มี Ed25519 ในกล่องเครื่องมือมาตรฐาน จึงใช้ ECDSA P-256 แทน
/// ซึ่งมีมาให้อยู่แล้วและให้ความแข็งแรงเทียบเท่าสำหรับงานนี้
/// กุญแจสาธารณะถูกคอมไพล์ติดไปกับโปรแกรม กุญแจส่วนตัวอยู่ใน Key Vault ของสายการ build
/// </summary>
public static class ManifestVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// กุญแจสาธารณะของการปล่อยเวอร์ชัน (SubjectPublicKeyInfo แบบ base64)
    ///
    /// ค่าว่างแปลว่ายังไม่ได้ตั้งกุญแจ ซึ่งตัวตรวจจะถือว่า "ตรวจไม่ผ่าน" เสมอ
    /// จงใจให้ล้มเหลวแบบปลอดภัย ดีกว่ายอมรับไฟล์ที่ไม่ได้เซ็นโดยไม่มีใครรู้
    /// </summary>
    public static string PublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>ตรวจลายเซ็นแล้วคืนเนื้อหาที่เชื่อถือได้ หรือ null เมื่อตรวจไม่ผ่าน</summary>
    public static ReleaseManifest? Verify(string signedJson)
    {
        try
        {
            var signed = JsonSerializer.Deserialize<SignedReleaseManifest>(signedJson, JsonOptions);
            if (signed is null || string.IsNullOrWhiteSpace(signed.Payload)) return null;

            if (!VerifySignature(signed)) return null;

            return JsonSerializer.Deserialize<ReleaseManifest>(signed.Payload, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static bool VerifySignature(SignedReleaseManifest signed)
    {
        if (string.IsNullOrWhiteSpace(PublicKeyBase64)) return false;
        if (string.IsNullOrWhiteSpace(signed.Signature)) return false;

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);

        return ecdsa.VerifyData(
            Encoding.UTF8.GetBytes(signed.Payload),
            Convert.FromBase64String(signed.Signature),
            HashAlgorithmName.SHA256);
    }

    /// <summary>ตรวจว่าไฟล์ที่ดาวน์โหลดมาตรงกับค่าแฮชที่ manifest ประกาศไว้</summary>
    public static async Task<bool> VerifyFileHashAsync(string path, string expectedSha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return false;

        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));

        return string.Equals(actual, expectedSha256.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase);
    }
}
