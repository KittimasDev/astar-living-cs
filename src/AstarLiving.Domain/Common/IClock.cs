namespace AstarLiving.Domain.Common;

/// <summary>นาฬิกาที่ inject ได้ เพื่อให้ทดสอบสูตรที่อิง "วันนี้" ได้</summary>
public interface IClock
{
    /// <summary>เวลาปัจจุบันตามโซน Asia/Bangkok (ปียังเป็น ค.ศ.)</summary>
    DateTime NowBangkok { get; }

    /// <summary>
    /// เวลาปัจจุบันแบบ UTC — ใช้กับคอลัมน์ timestamptz ทุกตัว
    ///
    /// Npgsql ยอมรับ DateTimeOffset ที่ offset เป็น 0 เท่านั้น การส่ง +07:00 เข้าไปจะถูกปฏิเสธ
    /// ค่านี้เป็น "ขณะเวลาเดียวกัน" กับ NowBangkok จึงเทียบเท่ากับที่ระบบเดิมบันทึก
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo Bangkok = ResolveBangkok();

    public DateTime NowBangkok => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bangkok).DateTime;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    private static TimeZoneInfo ResolveBangkok()
    {
        // .NET 8 รองรับ IANA id บน Windows ผ่าน ICU แต่กันพลาดไว้ด้วย id ฝั่ง Windows
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }
}

/// <summary>นาฬิกาแบบตรึงเวลา สำหรับ unit test</summary>
public sealed class FixedClock(DateTime nowBangkok) : IClock
{
    public DateTime NowBangkok { get; } = nowBangkok;

    public DateTimeOffset UtcNow { get; } =
        new DateTimeOffset(DateTime.SpecifyKind(nowBangkok, DateTimeKind.Unspecified), TimeSpan.FromHours(7))
            .ToUniversalTime();
}
