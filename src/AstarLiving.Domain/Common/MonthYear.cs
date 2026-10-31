namespace AstarLiving.Domain.Common;

/// <summary>คู่ (เดือน, ปี พ.ศ.) ที่ใช้ทั่วระบบแทน { month, year } ของเดิม</summary>
public readonly record struct MonthYear(int Month, int Year) : IComparable<MonthYear>
{
    public int CompareTo(MonthYear other)
    {
        var byYear = Year.CompareTo(other.Year);
        return byYear != 0 ? byYear : Month.CompareTo(other.Month);
    }

    public static bool operator <(MonthYear a, MonthYear b) => a.CompareTo(b) < 0;

    public static bool operator >(MonthYear a, MonthYear b) => a.CompareTo(b) > 0;

    public static bool operator <=(MonthYear a, MonthYear b) => a.CompareTo(b) <= 0;

    public static bool operator >=(MonthYear a, MonthYear b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Month}/{Year}";
}
