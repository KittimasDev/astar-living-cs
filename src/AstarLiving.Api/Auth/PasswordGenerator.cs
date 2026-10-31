using System.Security.Cryptography;

namespace AstarLiving.Api.Auth;

/// <summary>
/// สร้างรหัสผ่านชั่วคราว — พอร์ตจาก generateSecurePassword ของเดิม
/// คงองค์ประกอบเดิมไว้ (ตัวใหญ่ ตัวเล็ก ตัวเลข อักขระพิเศษ อย่างละอย่างน้อย 1 ตัว ความยาว 8)
///
/// ต่างจากเดิมตรงที่ใช้ RandomNumberGenerator แทน Math.random และสลับตำแหน่งด้วย
/// Fisher-Yates แทน sort ที่ใช้ตัวเปรียบเทียบแบบสุ่ม ซึ่งให้การกระจายที่ไม่สม่ำเสมอ
/// </summary>
public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Special = "!@#$%^&*()-_=+[]{}|;:,.<>?";
    private const string Filler = Lower + Digits;

    public static string Generate(int length = 8)
    {
        if (length < 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), "ความยาวรหัสผ่านต้องไม่น้อยกว่า 4 ตัวอักษร");
        }

        var chars = new List<char>(length)
        {
            Pick(Upper),
            Pick(Lower),
            Pick(Digits),
            Pick(Special),
        };

        while (chars.Count < length) chars.Add(Pick(Filler));

        Shuffle(chars);
        return new string([.. chars]);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(IList<char> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
