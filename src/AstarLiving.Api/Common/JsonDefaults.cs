using System.Text.Encodings.Web;
using System.Text.Json;

namespace AstarLiving.Api.Common;

/// <summary>
/// ตัวเลือก JSON สำหรับการเขียนผลลัพธ์นอกเส้นทางของ MVC (เช่น 401/403 ที่ออกจาก middleware)
/// ปล่อยอักขระไทยออกมาตรง ๆ แทนการ escape เป็น \uXXXX ให้ตรงกับที่ MVC ส่งออก
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions ThaiRelaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
