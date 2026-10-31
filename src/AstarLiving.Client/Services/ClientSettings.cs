using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstarLiving.Client.Services;

/// <summary>รูปแบบไฟล์ client.json ที่ตัวติดตั้งเขียนไว้</summary>
public sealed class ClientSettingsFile
{
    [JsonPropertyName("ServerUrl")]
    public string? ServerUrl { get; set; }

    /// <summary>ลายนิ้วมือใบรับรองของเครื่องแม่ (ใช้ตอนเปิด HTTPS ในเฟสถัดไป)</summary>
    [JsonPropertyName("ServerCertificateThumbprint")]
    public string? ServerCertificateThumbprint { get; set; }

    [JsonPropertyName("SiteName")]
    public string? SiteName { get; set; }
}

/// <summary>
/// ที่อยู่เครื่องแม่และค่าตั้งอื่น ๆ ของ client
///
/// ลำดับความสำคัญ: ไฟล์ของผู้ใช้ (เปลี่ยนได้จากในโปรแกรม) ชนะไฟล์ระดับเครื่องที่ตัวติดตั้งเขียน
/// ทั้งสองไฟล์อยู่นอกโฟลเดอร์ติดตั้ง จึงรอดจากการอัปเดตทุกครั้ง
/// </summary>
public sealed class ClientSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>ใช้เมื่อยังไม่มีไฟล์ตั้งค่าเลย เช่น ตอนรันจากเครื่องนักพัฒนา</summary>
    public const string FallbackServerUrl = "http://127.0.0.1:5080";

    private ClientSettingsFile _current = new();

    public string ServerUrl { get; private set; } = FallbackServerUrl;

    public string? SiteName => _current.SiteName;

    public string? ServerCertificateThumbprint => _current.ServerCertificateThumbprint;

    /// <summary>true เมื่ออ่านค่าจากไฟล์จริงไม่ได้เลย — หน้าจอจะชวนผู้ใช้ตั้งค่าที่อยู่เครื่องแม่</summary>
    public bool IsUsingFallback { get; private set; } = true;

    public void Load()
    {
        var machine = ReadFile(AppPaths.MachineClientConfig);
        var user = ReadFile(AppPaths.UserSettings);

        _current = new ClientSettingsFile
        {
            ServerUrl = Pick(user?.ServerUrl, machine?.ServerUrl),
            ServerCertificateThumbprint = Pick(user?.ServerCertificateThumbprint, machine?.ServerCertificateThumbprint),
            SiteName = Pick(user?.SiteName, machine?.SiteName),
        };

        IsUsingFallback = string.IsNullOrWhiteSpace(_current.ServerUrl);
        ServerUrl = NormalizeServerUrl(_current.ServerUrl) ?? FallbackServerUrl;
    }

    /// <summary>
    /// บันทึกที่อยู่เครื่องแม่ที่ผู้ใช้กรอกเองลงไฟล์ของผู้ใช้
    /// ไม่แตะไฟล์ระดับเครื่อง เพราะโปรแกรมรันโดยไม่มีสิทธิ์ผู้ดูแล
    /// </summary>
    public void SaveServerUrl(string serverUrl)
    {
        var normalized = NormalizeServerUrl(serverUrl)
            ?? throw new ArgumentException("ที่อยู่เครื่องแม่ข่ายไม่ถูกต้อง", nameof(serverUrl));

        _current.ServerUrl = normalized;
        ServerUrl = normalized;
        IsUsingFallback = false;

        AppPaths.EnsureUserDirectories();
        File.WriteAllText(AppPaths.UserSettings, JsonSerializer.Serialize(_current, JsonOptions));
    }

    private static string? Pick(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static ClientSettingsFile? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ClientSettingsFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // ไฟล์ตั้งค่าเสียไม่ควรทำให้โปรแกรมเปิดไม่ขึ้น ให้ตกไปใช้ค่าสำรองแทน
            return null;
        }
    }

    /// <summary>
    /// รับได้ทั้ง "192.168.1.10" "192.168.1.10:5080" และ URL เต็ม
    /// ผู้ใช้หน้างานมักกรอกแค่เลขไอพี
    ///
    /// แยกเป็นฟังก์ชันบริสุทธิ์เพื่อให้ทดสอบได้โดยไม่ต้องเขียนไฟล์จริงลงเครื่อง
    /// คืน null เมื่อที่อยู่ใช้ไม่ได้
    /// </summary>
    public static string? NormalizeServerUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim().TrimEnd('/');

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = text.Contains(':', StringComparison.Ordinal)
                ? $"http://{text}"
                : $"http://{text}:5080";
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.GetLeftPart(UriPartial.Authority)
                : null;
    }
}
