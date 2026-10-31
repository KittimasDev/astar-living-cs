using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>การ์ดตัวเลขหนึ่งใบบนหัวแดชบอร์ด</summary>
public sealed record SummaryCard(string Label, string Value);

/// <summary>หนึ่งเดือนบนกราฟแท่ง — ระบบเดิมยุบสามคีย์ (billed/paid/unpaid) ของเดือนเดียวกันเป็นแถวเดียว</summary>
public sealed class MonthlyTrendPoint
{
    /// <summary>ความสูงของพื้นที่วาดกราฟเป็นพิกเซล ใช้แปลงยอดเงินเป็นความสูงของแท่ง</summary>
    public const double PlotHeight = 210;

    public string Label { get; init; } = string.Empty;

    public decimal Billed { get; set; }

    public decimal Paid { get; set; }

    public decimal Unpaid { get; set; }

    public double PaidHeight { get; private set; }

    public double UnpaidHeight { get; private set; }

    public string PaidText => ThaiFormat.Money(Math.Round(Paid));

    public string UnpaidText => ThaiFormat.Money(Math.Round(Unpaid));

    /// <summary>
    /// แปลงยอดเป็นความสูงเทียบกับยอดสูงสุดของทั้งกราฟ
    /// แท่งที่มียอดแต่เตี้ยมากจะถูกดันให้สูงอย่างน้อย 2 พิกเซล เพื่อไม่ให้ดูเหมือนไม่มีข้อมูล
    /// </summary>
    public void Scale(decimal maximum)
    {
        PaidHeight = ScaleOne(Paid, maximum);
        UnpaidHeight = ScaleOne(Unpaid, maximum);
    }

    private static double ScaleOne(decimal value, decimal maximum)
    {
        if (value <= 0 || maximum <= 0) return 0;

        var height = (double)(value / maximum) * PlotHeight;
        return Math.Max(height, 2);
    }
}

/// <summary>หนึ่งชิ้นของกราฟวงแหวนวิธีชำระเงิน</summary>
public sealed class PaymentMethodSlice
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required Brush Color { get; init; }

    public decimal Value { get; init; }

    /// <summary>สัดส่วนเป็นเปอร์เซ็นต์ ทศนิยมหนึ่งตำแหน่ง</summary>
    public decimal Share { get; init; }

    /// <summary>รูปร่างของชิ้นบนวงแหวน ว่างเมื่อวิธีนี้ไม่มียอด</summary>
    public Geometry Slice { get; init; } = Geometry.Empty;

    /// <summary>จุดวางตัวเลขกำกับกลางชิ้น</summary>
    public Point LabelPoint { get; init; }

    public bool HasValue => Value > 0;

    public string ValueText => ThaiFormat.Money(Value);

    public string ShareText => $"{Share.ToString("0.#", ThaiFormat.Culture)}%";
}

/// <summary>
/// แดชบอร์ด — พอร์ตจาก DashboardContent.tsx
///
/// ตัวกรองของระบบเดิมมีสองชั้นที่ซ้อนกันอยู่: ช่วงวันที่ (ซึ่งฝั่ง API ใช้แค่ "เดือน")
/// และปี พ.ศ. ที่ส่งแยกต่างหาก พฤติกรรมนี้ถูกคงไว้ทั้งสองฝั่ง
/// </summary>
public sealed partial class DashboardPageViewModel(ApiClient api, AuthSession session) : PageViewModel
{
    /// <summary>สีของแต่ละวิธีชำระ — ตรงกับ PAYMENT_COLORS ของเว็บเดิม</summary>
    private static readonly (string Key, string Label, string Color)[] MethodLegend =
    [
        ("TRANSFER", "โอนธนาคาร", "#74BA3D"),
        ("CASH", "เงินสด", "#EB6142"),
        ("CHEQUE", "เช็ค", "#F4B740"),
    ];

    public override string Title => "แดชบอร์ด";

    public ObservableCollection<SummaryCard> SummaryCards { get; } = [];

    public ObservableCollection<MonthlyTrendPoint> MonthlyTrend { get; } = [];

    public ObservableCollection<PaymentMethodSlice> PaymentMethods { get; } = [];

    public ObservableCollection<DashboardOutstandingResponse> Outstandings { get; } = [];

    /// <summary>ปี พ.ศ. ที่เลือกอยู่</summary>
    [ObservableProperty]
    private int _selectedYear = ThaiFormat.CurrentBuddhistYear;

    /// <summary>ปีให้เลือกย้อนหลัง 5 ปีและล่วงหน้า 1 ปี</summary>
    public IReadOnlyList<int> AvailableYears { get; } =
        Enumerable.Range(ThaiFormat.CurrentBuddhistYear - 5, 7).Reverse().ToList();

    public IReadOnlyList<MonthOption> Months { get; } =
        Enumerable.Range(1, 12).Select(m => new MonthOption(m, ThaiFormat.FullMonths[m - 1])).ToList();

    [ObservableProperty]
    private MonthOption? _selectedStartMonth;

    [ObservableProperty]
    private MonthOption? _selectedEndMonth;

    private int StartMonth => SelectedStartMonth?.Value ?? 1;

    private int EndMonth => SelectedEndMonth?.Value ?? 12;

    /// <summary>ค่าสูงสุดบนแกนตั้ง ใช้คำนวณความสูงของแท่งเองเพราะวาดกราฟด้วย XAML ล้วน</summary>
    [ObservableProperty]
    private decimal _trendMaximum = 1;

    public override Task OnNavigatedToAsync()
    {
        // ตั้งค่าเริ่มต้นเป็นทั้งปี ตรงกับช่วงที่เว็บเดิมเปิดมา
        SelectedStartMonth ??= Months[0];
        SelectedEndMonth ??= Months[11];

        return LoadAsync();
    }

    partial void OnSelectedYearChanged(int value) => _ = LoadAsync();

    partial void OnSelectedStartMonthChanged(MonthOption? value) => _ = LoadAsync();

    partial void OnSelectedEndMonthChanged(MonthOption? value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var projectId = session.ProjectId;
        if (projectId is null) return;

        await RunAsync(async () =>
        {
            // API อ่านเฉพาะส่วนเดือนของ start_date/end_date ส่วนปีใช้จากคีย์ year ที่เป็น พ.ศ.
            var gregorianYear = ThaiFormat.ToGregorianYear(SelectedYear);

            var path = new QueryBuilder("api/v1/dashboard")
                .Add("project_id", projectId)
                .Add("year", SelectedYear)
                .Add("start_date", $"{gregorianYear:0000}-{StartMonth:00}-01")
                .Add("end_date", $"{gregorianYear:0000}-{EndMonth:00}-28");

            var data = await api.GetAsync<DashboardResponse>(path);
            Apply(data);
        });
    }

    private void Apply(DashboardResponse data)
    {
        SummaryCards.Clear();
        SummaryCards.Add(new SummaryCard("จำนวนลูกบ้าน", ThaiFormat.Integer(data.Summary.TotalResident)));
        SummaryCards.Add(new SummaryCard("ยอดรวมค่าส่วนกลาง (บาท)", ThaiFormat.Money(data.Summary.TotalBilled)));
        SummaryCards.Add(new SummaryCard("ยอดที่ชำระแล้ว (บาท)", ThaiFormat.Money(data.Summary.TotalPaid)));
        SummaryCards.Add(new SummaryCard("ยอดค้างชำระ (บาท)", ThaiFormat.Money(data.Summary.TotalUnpaid)));
        SummaryCards.Add(new SummaryCard("อัตราการชำระ (%)", ThaiFormat.Percent(data.Summary.PaymentRate)));

        ApplyTrend(data.MonthlyTrend);
        ApplyPaymentMethods(data.PaymentMethod);

        Outstandings.Clear();
        foreach (var row in data.Outstandings) Outstandings.Add(row);
    }

    /// <summary>ยุบสามคีย์ของเดือนเดียวกันเป็นแถวเดียว — ตรงกับ normalizeMonthlyTrend ของเว็บเดิม</summary>
    private void ApplyTrend(IEnumerable<DashboardPointResponse> points)
    {
        var byLabel = new Dictionary<string, MonthlyTrendPoint>(StringComparer.Ordinal);
        var ordered = new List<MonthlyTrendPoint>();

        foreach (var point in points)
        {
            if (!byLabel.TryGetValue(point.Label, out var row))
            {
                row = new MonthlyTrendPoint { Label = point.Label };
                byLabel[point.Label] = row;
                ordered.Add(row);
            }

            switch (point.Key)
            {
                case "billed": row.Billed = point.Value; break;
                case "paid": row.Paid = point.Value; break;
                case "unpaid": row.Unpaid = point.Value; break;
            }
        }

        var peak = ordered.Count == 0 ? 0m : ordered.Max(r => Math.Max(r.Paid, r.Unpaid));
        TrendMaximum = peak <= 0 ? 1 : peak;

        foreach (var row in ordered) row.Scale(TrendMaximum);

        MonthlyTrend.Clear();
        foreach (var row in ordered) MonthlyTrend.Add(row);
    }

    private void ApplyPaymentMethods(IReadOnlyList<DashboardPointResponse> points)
    {
        var total = points.Sum(p => p.Value);

        PaymentMethods.Clear();

        var center = new Point(DonutSize / 2, DonutSize / 2);
        var startAngle = 0d;

        // แสดงครบทั้งสามวิธีเสมอ วิธีที่ไม่มียอดจะถูกทำให้จางลงในหน้าจอ
        // เหมือนคำอธิบายกราฟของเว็บเดิมที่คงรายการไว้แต่หรี่ความทึบลง
        foreach (var (key, label, color) in MethodLegend)
        {
            var value = points.FirstOrDefault(p => p.Key == key)?.Value ?? 0m;
            var share = total <= 0 ? 0m : Math.Round(value * 100 / total, 1);
            var sweep = total <= 0 ? 0d : (double)(value / total) * 360d;

            PaymentMethods.Add(new PaymentMethodSlice
            {
                Key = key,
                Label = label,
                Color = MethodBrushes.TryGetValue(key, out var brush) ? brush : System.Windows.Media.Brushes.Gray,
                Value = value,
                Share = share,
                Slice = DonutGeometry.Build(startAngle, sweep, center, OuterRadius, InnerRadius),
                LabelPoint = DonutGeometry.LabelPoint(startAngle, sweep, center, (OuterRadius + InnerRadius) / 2),
            });

            startAngle += sweep;
        }
    }

    // ขนาดของวงแหวน ตรงกับสัดส่วน innerRadius 45 / outerRadius 90 ของเว็บเดิม
    private const double DonutSize = 200;
    private const double OuterRadius = 90;
    private const double InnerRadius = 45;

    /// <summary>แปลงสีของแต่ละวิธีชำระครั้งเดียวแล้วใช้ซ้ำ ไม่ต้องแปลงใหม่ทุกครั้งที่โหลด</summary>
    private static readonly Dictionary<string, Brush> MethodBrushes = MethodLegend.ToDictionary(
        m => m.Key,
        m =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(m.Color));
            brush.Freeze();
            return (Brush)brush;
        },
        StringComparer.Ordinal);
}
