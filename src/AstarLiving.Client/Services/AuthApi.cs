using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using AstarLiving.Shared.Versioning;
using Microsoft.Extensions.Logging;

namespace AstarLiving.Client.Services;

/// <summary>
/// เส้นทางของการยืนยันตัวตน
///
/// ระบบเดิมไม่ห่อซองให้ login / logout / refresh-token แต่ห่อให้ me
/// ความไม่สม่ำเสมอนี้ถูกคงไว้ทั้งสองฝั่ง จึงต้องเรียกคนละวิธีตรงนี้
/// </summary>
public sealed class AuthApi(
    ApiClient api,
    TokenStore tokens,
    AuthSession session,
    ClientSettings settings,
    ILogger<AuthApi> logger)
{
    /// <summary>เข้าสู่ระบบแล้วโหลดข้อมูลผู้ใช้ต่อทันที เหมือนที่หน้าเข้าสู่ระบบเดิมทำ</summary>
    public async Task<CurrentUserResponse> LoginAsync(string username, SecureString password, CancellationToken ct = default)
    {
        var request = new LoginRequest
        {
            Username = username.Trim(),
            Password = Reveal(password),
        };

        try
        {
            using var response = await api.Http.PostAsJsonAsync("api/v1/auth/login", request, AstarJson.Options, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException(
                    response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง"
                        : "เกิดข้อผิดพลาดในการเข้าสู่ระบบ กรุณาลองใหม่อีกครั้ง",
                    response.StatusCode);
            }

            var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(AstarJson.Options, ct)
                ?? throw new ApiException("เครื่องแม่ข่ายตอบกลับข้อมูลว่างเปล่า");

            tokens.Save(new TokenPair(payload.AccessToken, payload.RefreshToken));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ApiException.Timeout(settings.ServerUrl, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ApiException.Connectivity(settings.ServerUrl, ex);
        }
        finally
        {
            // ล้างรหัสผ่านที่แปลงเป็นข้อความธรรมดาออกจาก object ทันทีที่ใช้เสร็จ
            request.Password = string.Empty;
        }

        return await LoadProfileAsync(ct);
    }

    /// <summary>โหลดโปรไฟล์ด้วยโทเคนที่เก็บไว้ ใช้ตอนเปิดโปรแกรมเพื่อข้ามหน้าเข้าสู่ระบบ</summary>
    public async Task<CurrentUserResponse?> TryResumeAsync(CancellationToken ct = default)
    {
        if (tokens.Current is null) return null;

        try
        {
            return await LoadProfileAsync(ct);
        }
        catch (ApiException ex) when (ex.IsUnauthorized)
        {
            tokens.Clear();
            return null;
        }
    }

    public async Task<CurrentUserResponse> LoadProfileAsync(CancellationToken ct = default)
    {
        var profile = await api.GetAsync<CurrentUserResponse>("api/v1/auth/me", ct);
        session.SetUser(profile);
        return profile;
    }

    /// <summary>
    /// ออกจากระบบ
    /// ล้างโทเคนในเครื่องเสมอ แม้เครื่องแม่จะไม่ตอบ เพราะผู้ใช้ตั้งใจออกแล้ว
    /// </summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Post, "api/v1/auth/logout", body: null, ct);
        }
        catch (ApiException ex)
        {
            logger.LogInformation(ex, "เรียกออกจากระบบที่เครื่องแม่ข่ายไม่สำเร็จ แต่ล้างข้อมูลในเครื่องแล้ว");
        }
        finally
        {
            tokens.Clear();
            session.Clear();
        }
    }

    public async Task<string> ChangePasswordAsync(
        SecureString oldPassword, SecureString newPassword, SecureString confirmPassword, CancellationToken ct = default)
    {
        var request = new ChangePasswordRequest
        {
            OldPassword = Reveal(oldPassword),
            NewPassword = Reveal(newPassword),
            ConfirmPassword = Reveal(confirmPassword),
        };

        try
        {
            using var response = await api.Http.PatchAsJsonAsync("api/v1/auth/change-password", request, AstarJson.Options, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException("เปลี่ยนรหัสผ่านไม่สำเร็จ กรุณาตรวจสอบรหัสผ่านเดิม", response.StatusCode);
            }

            var payload = await response.Content.ReadFromJsonAsync<MessageResponse>(AstarJson.Options, ct);
            return payload?.Message ?? "เปลี่ยนรหัสผ่านสำเร็จ";
        }
        catch (HttpRequestException ex)
        {
            throw ApiException.Connectivity(settings.ServerUrl, ex);
        }
        finally
        {
            request.OldPassword = string.Empty;
            request.NewPassword = string.Empty;
            request.ConfirmPassword = string.Empty;
        }
    }

    /// <summary>
    /// ทักทายเครื่องแม่เพื่อดูเวอร์ชันและสถานะฐานข้อมูล
    /// เส้นทางนี้ไม่ต้องยืนยันตัวตนและต้องตอบได้แม้ระหว่างปรับปรุงฐานข้อมูล
    /// </summary>
    public async Task<HandshakeResponse> HandshakeAsync(CancellationToken ct = default) =>
        await api.GetRawAsync<HandshakeResponse>("api/v1/handshake", ct);

    /// <summary>
    /// แปลง SecureString เป็นข้อความเท่าที่จำเป็นตอนส่งขึ้นสาย
    /// WPF ไม่มีทางส่งรหัสผ่านโดยไม่แปลงเป็นข้อความสักครั้ง จึงล้างหน่วยความจำทันทีที่แปลงเสร็จ
    /// </summary>
    private static string Reveal(SecureString? secure)
    {
        if (secure is null || secure.Length == 0) return string.Empty;

        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
            return Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }
}
