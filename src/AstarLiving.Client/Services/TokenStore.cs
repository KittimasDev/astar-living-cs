using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AstarLiving.Client.Services;

/// <summary>คู่โทเคนที่ได้จากการเข้าสู่ระบบ</summary>
public sealed record TokenPair(string AccessToken, string RefreshToken);

/// <summary>
/// เก็บโทเคนไว้ให้ไม่ต้องล็อกอินใหม่ทุกครั้งที่เปิดโปรแกรม
///
/// เว็บเดิมเก็บใน localStorage ซึ่งอ่านได้ด้วยตาเปล่า ฝั่ง desktop ใช้ DPAPI ระดับผู้ใช้แทน
/// ผู้ใช้ Windows คนอื่นบนเครื่องเดียวกันจึงถอดไฟล์นี้ไม่ได้
/// ไฟล์อยู่ใน AppData ไม่ใช่โฟลเดอร์ติดตั้ง จึงรอดจากการอัปเดต
/// </summary>
public sealed class TokenStore
{
    private static readonly byte[] Entropy =
    [
        0x41, 0x73, 0x74, 0x61, 0x72, 0x4C, 0x69, 0x76,
        0x69, 0x6E, 0x67, 0x2D, 0x73, 0x65, 0x73, 0x73,
        0x69, 0x6F, 0x6E, 0x2D, 0x76, 0x31, 0x5B, 0xE2,
        0x37, 0xC4, 0x90, 0x1A, 0x6D, 0xF8, 0x23, 0xB7,
    ];

    private readonly object _gate = new();
    private TokenPair? _cached;
    private bool _loaded;

    public TokenPair? Current
    {
        get
        {
            lock (_gate)
            {
                if (!_loaded)
                {
                    _cached = ReadFromDisk();
                    _loaded = true;
                }

                return _cached;
            }
        }
    }

    public void Save(TokenPair tokens)
    {
        lock (_gate)
        {
            _cached = tokens;
            _loaded = true;

            try
            {
                AppPaths.EnsureUserDirectories();
                var json = JsonSerializer.SerializeToUtf8Bytes(tokens);
                var sealedBytes = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(AppPaths.SessionFile, sealedBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                // เขียนไม่ได้ก็แค่ต้องล็อกอินใหม่รอบหน้า ไม่ใช่เหตุให้ล้มการเข้าสู่ระบบครั้งนี้
            }
        }
    }

    /// <summary>อัปเดตเฉพาะ access token หลัง refresh โดยคง refresh token เดิมไว้</summary>
    public void UpdateAccessToken(string accessToken)
    {
        lock (_gate)
        {
            var refresh = Current?.RefreshToken;
            if (string.IsNullOrEmpty(refresh)) return;

            Save(new TokenPair(accessToken, refresh));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _cached = null;
            _loaded = true;

            try
            {
                if (File.Exists(AppPaths.SessionFile)) File.Delete(AppPaths.SessionFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ลบไม่ได้ก็ยังถือว่าออกจากระบบแล้ว เพราะค่าที่อยู่ในหน่วยความจำถูกล้างไปแล้ว
            }
        }
    }

    private static TokenPair? ReadFromDisk()
    {
        try
        {
            if (!File.Exists(AppPaths.SessionFile)) return null;

            var sealedBytes = File.ReadAllBytes(AppPaths.SessionFile);
            var json = ProtectedData.Unprotect(sealedBytes, Entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<TokenPair>(Encoding.UTF8.GetString(json));
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or UnauthorizedAccessException)
        {
            // ไฟล์เสียหรือถูกคัดลอกมาจากโปรไฟล์อื่น ถือว่ายังไม่เคยล็อกอิน
            return null;
        }
    }
}
