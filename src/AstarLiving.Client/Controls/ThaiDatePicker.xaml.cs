using System.Windows;
using System.Windows.Controls;
using AstarLiving.Client.Common;

namespace AstarLiving.Client.Controls;

/// <summary>ตัวเลือกวันในกล่องเลื่อนลง</summary>
public sealed record DayOption(int Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>ตัวเลือกปี พ.ศ.</summary>
public sealed record YearOption(int BuddhistYear)
{
    public override string ToString() => BuddhistYear.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// ช่องเลือกวันที่แบบไทย
///
/// ตัวเลือกวันที่ของ WPF ใช้ปฏิทินเกรโกเรียนเสมอ แม้จะตั้งวัฒนธรรมของเธรดเป็นไทยแล้วก็ตาม
/// (DateTimeHelper ภายในของมันไล่หาปฏิทินเกรโกเรียนใน OptionalCalendars มาใช้แทน)
/// จึงทำคอนโทรลเองด้วยกล่องเลื่อนลงสามช่อง วัน เดือน และปี พ.ศ.
///
/// ค่าที่คืนออกไปเป็น DateTime แบบ ค.ศ. ตามปกติ การแปลงปีอยู่ในคอนโทรลนี้ที่เดียว
/// </summary>
public partial class ThaiDatePicker : UserControl
{
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(
            nameof(SelectedDate),
            typeof(DateTime?),
            typeof(ThaiDatePicker),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedDateChanged));

    /// <summary>จำนวนปีย้อนหลังและล่วงหน้าที่ให้เลือก</summary>
    public static readonly DependencyProperty YearRangeProperty =
        DependencyProperty.Register(nameof(YearRange), typeof(int), typeof(ThaiDatePicker), new PropertyMetadata(5));

    private bool _updating;

    public ThaiDatePicker()
    {
        InitializeComponent();

        Days = Enumerable.Range(1, 31).Select(d => new DayOption(d)).ToList();
        Months = Enumerable.Range(1, 12).Select(m => new MonthOption(m, ThaiFormat.ShortMonths[m - 1])).ToList();

        var current = ThaiFormat.CurrentBuddhistYear;
        Years = Enumerable.Range(current - YearRange, (YearRange * 2) + 1)
            .Reverse()
            .Select(y => new YearOption(y))
            .ToList();

        DayBox.ItemsSource = Days;
        MonthBox.ItemsSource = Months;
        YearBox.ItemsSource = Years;

        Apply(SelectedDate);
    }

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public int YearRange
    {
        get => (int)GetValue(YearRangeProperty);
        set => SetValue(YearRangeProperty, value);
    }

    public IReadOnlyList<DayOption> Days { get; }

    public IReadOnlyList<MonthOption> Months { get; }

    public IReadOnlyList<YearOption> Years { get; }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ThaiDatePicker picker) picker.Apply(e.NewValue as DateTime?);
    }

    /// <summary>เอาค่าจากภายนอกมาแสดงในกล่องทั้งสาม</summary>
    private void Apply(DateTime? value)
    {
        if (_updating) return;

        _updating = true;
        try
        {
            if (value is null)
            {
                DayBox.SelectedItem = null;
                MonthBox.SelectedItem = null;
                YearBox.SelectedItem = null;
                return;
            }

            var date = value.Value;

            DayBox.SelectedItem = Days.FirstOrDefault(x => x.Value == date.Day);
            MonthBox.SelectedItem = Months.FirstOrDefault(x => x.Value == date.Month);

            var buddhistYear = ThaiFormat.ToBuddhistYear(date.Year);
            YearBox.SelectedItem = Years.FirstOrDefault(x => x.BuddhistYear == buddhistYear);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>ประกอบค่าจากกล่องทั้งสามกลับเป็นวันที่</summary>
    private void OnPartChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;

        if (DayBox.SelectedItem is not DayOption day
            || MonthBox.SelectedItem is not MonthOption month
            || YearBox.SelectedItem is not YearOption year)
        {
            return;
        }

        var gregorianYear = ThaiFormat.ToGregorianYear(year.BuddhistYear);

        // ผู้ใช้เลือกวันที่ 31 ของเดือนที่มีไม่ถึง ให้ปัดลงมาเป็นวันสุดท้ายของเดือนแทนการโยนข้อผิดพลาด
        var dayCount = DateTime.DaysInMonth(gregorianYear, month.Value);
        var safeDay = Math.Min(day.Value, dayCount);

        _updating = true;
        try
        {
            SelectedDate = new DateTime(gregorianYear, month.Value, safeDay);

            if (safeDay != day.Value) DayBox.SelectedItem = Days.FirstOrDefault(x => x.Value == safeDay);
        }
        finally
        {
            _updating = false;
        }
    }
}
