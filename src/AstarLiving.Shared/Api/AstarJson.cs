using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstarLiving.Shared.Api;

/// <summary>
/// การตั้งค่า JSON ที่ API และ client ต้องใช้ให้ตรงกันเป๊ะ
///
/// ถ้าสองฝั่งตั้งไม่ตรงกัน อาการที่ได้คือฟิลด์กลายเป็นค่าว่างเงียบ ๆ ไม่ใช่ error
/// ซึ่งหาสาเหตุยากมาก จึงบังคับให้ทั้งสองฝั่งอ่านจากที่เดียวกันตรงนี้
/// </summary>
public static class AstarJson
{
    /// <summary>ใส่ค่าลงในตัวเลือกที่มีอยู่แล้ว — ฝั่ง API เรียกผ่าน AddJsonOptions</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        // คีย์ส่วนใหญ่ของระบบเดิมเป็น snake_case (full_name, house_no, page_information)
        // ส่วนที่เป็น camelCase เช่น statusCode ระบุด้วย [JsonPropertyName] รายตัว
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new JsonStringEnumConverter());

        // ตัวเลขทศนิยมของเงินต้องไม่ถูกแปลงเป็น exponent
        options.NumberHandling = JsonNumberHandling.Strict;
    }

    /// <summary>ชุดตัวเลือกสำเร็จรูปสำหรับฝั่งที่เรียก JsonSerializer ตรง ๆ</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        Configure(options);

        // ต้องส่ง true เพื่อให้ผูกตัวอ่านชนิดข้อมูลแบบ reflection ให้ด้วย
        // ถ้าเรียกแบบไม่มีพารามิเตอร์จะโยน InvalidOperationException ทันทีที่ใช้งานครั้งแรก
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }
}
