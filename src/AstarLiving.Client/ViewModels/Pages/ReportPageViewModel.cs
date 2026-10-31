using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Api;
using AstarLiving.Domain.Billing;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>
/// แหล่งข้อมูลของแท็บ
///
/// สองแท็บท้ายไม่ได้ใช้เส้นทางรายงานปกติ แต่ไปดึงจากโมดูลสรุปค่าใช้จ่าย
/// ซึ่งนับงวดที่ชำระบางส่วนด้วย (เส้นทางรายงานปกตินับเฉพาะงวดที่ยังไม่จ่ายเลย)
/// และรวมยอดมาให้แล้วที่เครื่องแม่ ตัวเลขจึงไม่เท่ากับแท็บค้างชำระธรรมดา
/// ความต่างนี้เป็นของระบบเดิม ไม่ใช่ความผิดพลาด
/// </summary>
public enum ReportSource
{
    /// <summary>เส้นทางรายงานปกติ นับเฉพาะงวดที่ยังไม่ชำระเลย</summary>
    PaymentReport,

    /// <summary>สรุปค้างชำระรายบ้านทั้งโครงการ</summary>
    SummaryByHouse,

    /// <summary>สรุปค้างชำระของบ้านหลังเดียว แยกตามปี ต้องเลือกบ้านก่อน</summary>
    SummaryByResident,
}

/// <summary>คำอธิบายของรายงานหนึ่งชนิด: ชื่อแท็บ ชนิดที่ส่งไป API และคอลัมน์ที่จะแสดง</summary>
public sealed record ReportTab(
    string Label,
    string Type,
    IReadOnlyList<ReportColumn> Columns,
    ReportSource Source = ReportSource.PaymentReport);

/// <summary>ตัวเลือกบ้านในแท็บที่ต้องระบุบ้านก่อน</summary>
public sealed record ResidentOption(int Id, string Label);

/// <summary>
/// คอลัมน์หนึ่งของรายงาน
///
/// <paramref name="Key"/> คือคีย์ที่ใช้เก็บค่าในแถว ต้องไม่ซ้ำกันในแท็บเดียวกัน
/// <paramref name="SourceKey"/> คือคีย์ที่อ่านจาก JSON ปกติเป็นตัวเดียวกับ Key
/// แยกออกมาเพราะบางแท็บมีสองคอลัมน์ที่อ่านฟิลด์เดียวกันแต่แสดงคนละอย่าง
/// เช่น "สถานะ" กับ "หมายเหตุ" ที่อ่าน rejected_reason เหมือนกัน
/// </summary>
public sealed record ReportColumn(
    string Header,
    string Key,
    ReportColumnKind Kind = ReportColumnKind.Text,
    string? SourceKey = null,
    double? Width = null,
    double? MinWidth = null)
{
    public string ReadFrom => SourceKey ?? Key;
}

public enum ReportColumnKind
{
    Text,
    Money,
    Integer,
    Date,

    /// <summary>เลขลำดับของแถว ไม่ได้มาจากข้อมูลที่เครื่องแม่ส่งมา</summary>
    Sequence,

    /// <summary>เลขเดือน 1-12 แสดงเป็นชื่อย่อภาษาไทย เช่น ต.ค.</summary>
    MonthAbbr,

    /// <summary>
    /// สถานะใบเสร็จ อ่านจากเหตุผลที่ยกเลิก
    /// ไม่มีเหตุผล = ยังใช้งานอยู่ มีเหตุผล = ถูกยกเลิกไปแล้ว
    /// ระบบเดิมก็ดูจากช่องนี้ ไม่ได้ดูจากช่อง status
    /// </summary>
    ReceiptStatus,

    /// <summary>สถานะการชำระ คำนวณจากสถานะใบทวงถามและสถานะงวดพร้อมกัน</summary>
    PaymentStatusText,

    /// <summary>วันที่แบบสั้น วว/ดด/ปป ตามที่ระบบเดิมแสดงในรายงานค่าปรับ</summary>
    ShortDate,
}

/// <summary>หนึ่งแถวของรายงาน เก็บเป็นคู่คีย์-ค่า เพราะแต่ละชนิดมีคอลัมน์ไม่เหมือนกัน</summary>
public sealed class ReportRow(IReadOnlyDictionary<string, string> cells)
{
    public IReadOnlyDictionary<string, string> Cells { get; } = cells;

    public string this[string key] => Cells.TryGetValue(key, out var value) ? value : string.Empty;
}

/// <summary>
/// รายงาน — พอร์ตจาก ReportContent.tsx
///
/// ทั้งหกแท็บใช้เส้นทางเดียวกันต่างกันที่พารามิเตอร์ type
/// คอลัมน์ของแต่ละชนิดต่างกันมาก จึงอ่านผลลัพธ์เป็น JSON ดิบแล้วจัดรูปแบบตามตารางคอลัมน์
/// วิธีนี้ทำให้เพิ่มรายงานใหม่ได้โดยไม่ต้องสร้างคลาสใหม่ทุกครั้ง
/// </summary>
public sealed partial class ReportPageViewModel(
    ApiClient api,
    AuthSession session,
    DialogService dialogs,
    DocumentService documents) : PageViewModel
{
    private static readonly ReportTab[] Tabs =
    [
        // ชื่อหัวคอลัมน์และลำดับยกมาจากระบบเดิมทั้งหมด เพื่อให้รายงานที่พิมพ์ออกมาเทียบกันได้
        new("ค้างชำระ", "REPORT_OUTSTANDING",
        [
            new("ลำดับ", "index", ReportColumnKind.Sequence),
            new("บ้านเลขที่", "house_no", ReportColumnKind.Text, null, 110),
            new("ชื่อ-นามสกุล", "full_name", ReportColumnKind.Text, null, 200),
            new("เดือน", "month", ReportColumnKind.MonthAbbr, null, 90),
            new("ปี (พ.ศ.)", "year", ReportColumnKind.Text, null, 90),
            new("ค่าส่วนกลาง (บาท)", "amount_due", ReportColumnKind.Money),
            new("ค่าเบี้ยปรับ (10%)", "fine", ReportColumnKind.Money),
            new("ค่าทวงถาม", "collection_fee", ReportColumnKind.Money),
        ]),

        new("การชำระเงิน", "REPORT_PAYMENT",
        [
            new("ลำดับ", "index", ReportColumnKind.Sequence),
            new("เลขที่ใบเสร็จ", "receipt_no", ReportColumnKind.Text, null, 150),
            new("บ้านเลขที่", "house_no", ReportColumnKind.Text, null, 110),
            new("ชื่อ-นามสกุล", "full_name", ReportColumnKind.Text, null, 200),
            new("ยอดชำระ (บาท)", "total_amount", ReportColumnKind.Money),
            // ระบบเดิมใช้วันที่ออกใบเสร็จในคอลัมน์ชื่อ "วันที่ชำระ" ไม่ใช่ payment_date
            new("วันที่ชำระ", "issue_date", ReportColumnKind.Date, null, 130),
            new("วิธีการชำระเงิน", "payment_method_name", ReportColumnKind.Text, null, 150),
            new("ผู้ออกใบเสร็จ", "user_full_name", ReportColumnKind.Text, null, 170),
            new("สถานะ", "status", ReportColumnKind.ReceiptStatus, "rejected_reason", 100),
            // ชื่อหัวคอลัมน์ของเดิมมีช่องว่างต่อท้าย คงไว้ให้ตรง
            new("หมายเหตุ ", "rejected_reason"),
        ]),

        new("ใบแจ้งค่าใช้จ่าย", "REPORT_INVOICE",
        [
            new("ลำดับ", "index", ReportColumnKind.Sequence),
            new("เดือนและปี", "month_th", ReportColumnKind.Text, null, 130),
            new("จำนวนใบแจ้งหนี้", "count_pm", ReportColumnKind.Integer),
            new("ผู้ออกใบแจ้งหนี้", "created_user", ReportColumnKind.Text, null, 170),
            new("ยอดรวมทั้งหมด (บาท)", "total", ReportColumnKind.Money),
            new("ชำระแล้ว (บาท)", "total_paid", ReportColumnKind.Money),
            new("ค้างชำระ (บาท)", "total_not_paid", ReportColumnKind.Money),
        ]),

        new("ค่าปรับและค่าทวงถาม", "REPORT_FINE",
        [
            new("ลำดับ", "index", ReportColumnKind.Sequence),
            new("เดือนและปี", "month_th", ReportColumnKind.Text, null, 130),
            new("บ้านเลขที่", "resident_house_no", ReportColumnKind.Text, null, 110),
            new("ชื่อ-นามสกุล", "resident_full_name", ReportColumnKind.Text, null, 200),
            new("ค่าเบี้ยปรับ (10%)", "fine", ReportColumnKind.Money),
            new("ค่าทวงถาม (บาท)", "collection_fee", ReportColumnKind.Money),
            new("รวมทั้งหมด (บาท)", "total_due", ReportColumnKind.Money),
            new("สถานะการชำระ", "payment_status", ReportColumnKind.PaymentStatusText, null, 130),
            new("หมายเหตุ", "note"),
            new("วันที่ทำรายการ", "created_at", ReportColumnKind.ShortDate, null, 130),
            new("ผู้ทำรายการ", "created_by_name", ReportColumnKind.Text, null, 170),
        ]),

        // ระบบเดิมปิดแท็บนี้ไว้ แต่ผมเปิดไว้เพราะข้อมูลมีอยู่จริงและใช้งานได้
        // คอลัมน์ยึดตามที่ของเดิมเคยนิยามไว้ทั้งหมด
        new("รายรับอื่น ๆ", "REPORT_REVENUE",
        [
            new("ลำดับ", "index", ReportColumnKind.Sequence),
            new("เลขที่ใบเสร็จ", "receipt_no", ReportColumnKind.Text, null, 150),
            new("บ้านเลขที่", "house_no", ReportColumnKind.Text, null, 110),
            new("ชื่อ-นามสกุล", "full_name", ReportColumnKind.Text, null, 200),
            // เครื่องแม่ประกอบข้อความนี้จากรายการย่อยในใบเสร็จมาให้แล้ว
            // ปล่อยให้ยืดรับพื้นที่ที่เหลือ แต่ตั้งขั้นต่ำไว้กว้างกว่าคอลัมน์อื่น
            // เพราะเป็นคอลัมน์ที่มีข้อความยาวที่สุดในรายงานทั้งหมด
            new("รายละเอียด", "detail", ReportColumnKind.Text, null, null, 300),
            new("ยอดชำระ (บาท)", "total_amount", ReportColumnKind.Money),
            new("วันที่ชำระ", "issue_date", ReportColumnKind.Date, null, 130),
            new("สถานะ", "status", ReportColumnKind.ReceiptStatus, "rejected_reason", 100),
            new("หมายเหตุ ", "rejected_reason"),
        ]),

        // หนึ่งแถวต่อหนึ่งบ้าน รวมยอดทุกงวดแล้ว
        new("ค้างชำระแจ้งกรรมการ", "SUMMARY_BY_HOUSE",
        [
            new("ลำดับ", "index", ReportColumnKind.Sequence),
            new("บ้านเลขที่", "house_no", ReportColumnKind.Text, null, 110),
            new("ชื่อ-นามสกุล", "full_name", ReportColumnKind.Text, null, 200),
            new("ค่าส่วนกลาง (บาท)", "common_fee", ReportColumnKind.Money),
            new("ค่าเบี้ยปรับ (10%)", "fine", ReportColumnKind.Money),
            new("ค่าทวงถาม (บาท)", "collection_fee", ReportColumnKind.Money),
            new("รวมทั้งหมด (บาท)", "total", ReportColumnKind.Money),
        ], ReportSource.SummaryByHouse),

        // หนึ่งแถวต่อหนึ่งงวดของบ้านหลังเดียว ต้องเลือกบ้านก่อนจึงจะมีข้อมูล
        new("ค้างชำระแจ้งลูกบ้าน", "SUMMARY_BY_RESIDENT",
        [
            new("ลำดับ", "index", ReportColumnKind.Sequence),
            new("เดือน", "month", ReportColumnKind.MonthAbbr, null, 90),
            new("ปี (พ.ศ.)", "year", ReportColumnKind.Text, null, 90),
            new("เลขที่ใบแจ้งหนี้", "inv_no", ReportColumnKind.Text, null, 150),
            new("ค่าส่วนกลาง (บาท)", "common_fee", ReportColumnKind.Money),
            new("ค่าเบี้ยปรับ (10%)", "fine", ReportColumnKind.Money),
            new("ค่าทวงถาม (บาท)", "collection_fee", ReportColumnKind.Money),
            new("ยอดที่ต้องชำระ (บาท)", "total", ReportColumnKind.Money),
        ], ReportSource.SummaryByResident),
    ];

    public override string Title => "รายงาน";

    public IReadOnlyList<ReportTab> AvailableTabs => Tabs;

    /// <summary>
    /// นิยามคอลัมน์ของทุกแท็บ เปิดไว้ให้เทสต์ตรวจว่าคีย์ที่ประกาศไว้
    /// ตรงกับชื่อฟิลด์ที่เครื่องแม่ส่งกลับมาจริง ๆ
    /// ถ้าหลุดจะได้คอลัมน์ว่างเปล่าโดยไม่มีอะไรฟ้อง
    /// </summary>
    public static IReadOnlyList<ReportTab> DefinedTabs => Tabs;

    public ObservableCollection<ReportRow> Rows { get; } = [];

    /// <summary>คอลัมน์ของแท็บที่เลือกอยู่ หน้าจอสร้างคอลัมน์ของตารางจากรายการนี้</summary>
    public ObservableCollection<ReportColumn> Columns { get; } = [];

    [ObservableProperty]
    private ReportTab _selectedTab = Tabs[0];

    [ObservableProperty]
    private int _year = ThaiFormat.CurrentBuddhistYear;

    [ObservableProperty]
    private MonthOption? _month;

    [ObservableProperty]
    private string _houseNo = string.Empty;

    public IReadOnlyList<int> AvailableYears { get; } =
        Enumerable.Range(ThaiFormat.CurrentBuddhistYear - 5, 7).Reverse().ToList();

    public IReadOnlyList<MonthOption> Months { get; } =
        Enumerable.Range(1, 12).Select(m => new MonthOption(m, ThaiFormat.FullMonths[m - 1])).ToList();

    /// <summary>ยอดรวมสี่ก้อนที่แสดงเป็นการ์ดใต้ตาราง เฉพาะสองแท็บที่ดึงจากสรุปค่าใช้จ่าย</summary>
    [ObservableProperty]
    private SummaryExpensesTotalResponse? _summaryTotals;

    /// <summary>ข้อความบอกว่าต้องเลือกบ้านก่อน ว่างเมื่อไม่ต้องบอก</summary>
    [ObservableProperty]
    private string? _requirementMessage;

    public bool ShowSummaryCard => SummaryTotals is not null;

    partial void OnSummaryTotalsChanged(SummaryExpensesTotalResponse? value) =>
        OnPropertyChanged(nameof(ShowSummaryCard));

    /// <summary>แท็บแจ้งลูกบ้านต้องเลือกบ้านก่อน จึงต้องโชว์ช่องเลือกให้เห็นชัด</summary>
    public bool RequiresResident => SelectedTab.Source == ReportSource.SummaryByResident;

    public ObservableCollection<ResidentOption> Residents { get; } = [];

    [ObservableProperty]
    private ResidentOption? _selectedResident;

    partial void OnSelectedResidentChanged(ResidentOption? value) => _ = LoadAsync();

    public bool IsEmpty => Rows.Count == 0 && !IsBusy;

    public override Task OnNavigatedToAsync() => LoadAsync();

    partial void OnSelectedTabChanged(ReportTab value) => _ = LoadAsync();

    partial void OnYearChanged(int value) => _ = LoadAsync();

    partial void OnMonthChanged(MonthOption? value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (session.ProjectId is null) return;

        Columns.Clear();
        foreach (var column in SelectedTab.Columns) Columns.Add(column);

        OnPropertyChanged(nameof(RequiresResident));

        SummaryTotals = null;
        RequirementMessage = null;

        if (SelectedTab.Source == ReportSource.SummaryByHouse)
        {
            await LoadSummaryByHouseAsync();
            return;
        }

        if (SelectedTab.Source == ReportSource.SummaryByResident)
        {
            await LoadSummaryByResidentAsync();
            return;
        }

        await RunAsync(async () =>
        {
            var path = new QueryBuilder($"api/v1/projects/{session.ProjectId}/reports/payments")
                .Add("type", SelectedTab.Type)
                .Add("no_limit", true)
                .Add("year", Year)
                .Add("month", Month?.Value)
                .Add("house_no", string.IsNullOrWhiteSpace(HouseNo) ? null : HouseNo.Trim());

            // รายงานแต่ละชนิดคืนรูปร่างต่างกัน จึงรับเป็น JSON ดิบแล้วแปลงตามคอลัมน์
            var result = await api.GetPagedAsync<JsonElement>(path);

            Rows.Clear();
            for (var i = 0; i < result.Items.Count; i++) Rows.Add(ToRow(result.Items[i], i));
        });

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// ค้างชำระแจ้งกรรมการ — หนึ่งแถวต่อหนึ่งบ้าน
    /// เครื่องแม่รวมยอดมาให้แล้ว ฝั่งนี้แค่กางออกเป็นแถว
    /// </summary>
    private async Task LoadSummaryByHouseAsync()
    {
        await RunAsync(async () =>
        {
            var path = new QueryBuilder("api/v1/summary_expenses")
                .Add("no_limit", true)
                .Add("year", Year)
                .Add("month", Month?.Value)
                .Add("house_no", string.IsNullOrWhiteSpace(HouseNo) ? null : HouseNo.Trim());

            var result = await api.GetAsync<SummaryExpensesResponse>(path);

            Rows.Clear();

            for (var i = 0; i < result.ByHouse.Count; i++)
            {
                var house = result.ByHouse[i];

                Rows.Add(new ReportRow(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["index"] = (i + 1).ToString(CultureInfo.InvariantCulture),
                    ["house_no"] = house.HouseNo,
                    ["full_name"] = house.FullName,
                    ["common_fee"] = ThaiFormat.Money(house.CommonFee),
                    ["fine"] = ThaiFormat.Money(house.Fine),
                    ["collection_fee"] = ThaiFormat.Money(house.CollectionFee),
                    ["total"] = ThaiFormat.Money(house.Total),
                }));
            }

            SummaryTotals = result.Total;
        });

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// ค้างชำระแจ้งลูกบ้าน — หนึ่งแถวต่อหนึ่งงวดของบ้านหลังเดียว
    /// ต้องเลือกบ้านก่อน ระบบเดิมก็บังคับเหมือนกันเพราะเส้นทางนี้รับรหัสบ้านใน URL
    /// </summary>
    private async Task LoadSummaryByResidentAsync()
    {
        await EnsureResidentsLoadedAsync();

        if (SelectedResident is null)
        {
            Rows.Clear();
            RequirementMessage = "กรุณาเลือกบ้านเลขที่ก่อน จึงจะแสดงรายงานได้";
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        await RunAsync(async () =>
        {
            var path = new QueryBuilder($"api/v1/summary_expenses/{SelectedResident.Id}")
                .Add("year", Year)
                .Add("month", Month?.Value);

            var result = await api.GetAsync<SummaryExpensesByResidentResponse>(path);

            Rows.Clear();

            var index = 1;

            foreach (var year in result.ByYear)
            {
                foreach (var month in year.Months)
                {
                    Rows.Add(new ReportRow(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["index"] = index.ToString(CultureInfo.InvariantCulture),
                        ["month"] = ThaiMonths.GetAbbr(month.Month),
                        ["year"] = month.Year.ToString(CultureInfo.InvariantCulture),
                        ["inv_no"] = month.InvNo,
                        ["common_fee"] = ThaiFormat.Money(month.CommonFee),
                        ["fine"] = ThaiFormat.Money(month.Fine),
                        ["collection_fee"] = ThaiFormat.Money(month.CollectionFee),
                        ["total"] = ThaiFormat.Money(month.Total),
                    }));

                    index++;
                }
            }

            SummaryTotals = new SummaryExpensesTotalResponse
            {
                CommonFee = result.Total.CommonFee,
                Fine = result.Total.Fine,
                CollectionFee = result.Total.CollectionFee,
                Total = result.Total.Total,
                PaymentCount = result.Total.PaymentCount,
            };
        });

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>โหลดรายชื่อบ้านครั้งเดียวตอนเข้าแท็บที่ต้องใช้</summary>
    private async Task EnsureResidentsLoadedAsync()
    {
        if (Residents.Count > 0) return;

        try
        {
            var path = new QueryBuilder($"api/v1/projects/{session.ProjectId}/residents")
                .Add("no_limit", true);

            var result = await api.GetPagedAsync<ResidentBrief>(path);

            foreach (var resident in result.Items)
            {
                Residents.Add(new ResidentOption(
                    resident.Id, $"{resident.HouseNo} {resident.FullName}".Trim()));
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    [RelayCommand]
    private void SelectTab(ReportTab? tab)
    {
        if (tab is not null) SelectedTab = tab;
    }

    /// <summary>
    /// บันทึกเป็นไฟล์ CSV ให้เปิดต่อใน Excel ได้
    /// ใส่ BOM ไว้ด้วย ไม่อย่างนั้น Excel จะอ่านภาษาไทยเป็นตัวอักษรเพี้ยน
    /// </summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (Rows.Count == 0)
        {
            await dialogs.ErrorAsync("ไม่มีข้อมูลให้บันทึก");
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", Columns.Select(c => Escape(c.Header))));

            foreach (var row in Rows)
            {
                builder.AppendLine(string.Join(",", Columns.Select(c => Escape(row[c.Key]))));
            }

            var directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var fileName = $"รายงาน-{SelectedTab.Label}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            var target = Path.Combine(directory, fileName);

            await File.WriteAllTextAsync(target, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            await dialogs.SuccessAsync($"บันทึกไฟล์แล้วที่\n{target}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await dialogs.ErrorAsync("บันทึกไฟล์ไม่สำเร็จ", ex.Message);
        }
    }

    /// <summary>
    /// พิมพ์ตารางรายงานที่กำลังแสดงอยู่เป็น PDF
    ///
    /// ส่งหัวตารางและแถวที่จัดรูปแบบแล้วไปให้เครื่องแม่เป็นผู้วาด
    /// สิ่งที่พิมพ์ออกมาจึงตรงกับสิ่งที่ผู้ใช้เห็นบนจอเสมอ รวมถึงตัวกรองที่เลือกไว้
    /// </summary>
    [RelayCommand]
    private async Task PrintReportAsync()
    {
        if (Rows.Count == 0)
        {
            await dialogs.ErrorAsync("ไม่มีข้อมูลให้พิมพ์");
            return;
        }

        await documents.OpenAsync("api/v1/reports/pdf", BuildPrintRequest(), $"{SelectedTab.Label}");
    }

    /// <summary>
    /// บันทึกเป็นไฟล์ Excel
    ///
    /// ต่างจาก CSV ตรงที่ยอดเงินเป็นตัวเลขจริง รวมยอดและเรียงลำดับใน Excel ได้
    /// ซึ่งเป็นเหตุผลหลักที่ผู้ใช้ขอไฟล์ Excel
    /// </summary>
    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (Rows.Count == 0)
        {
            await dialogs.ErrorAsync("ไม่มีข้อมูลให้บันทึก");
            return;
        }

        // แท็บแจ้งลูกบ้านออกไฟล์สองชีต ซึ่งสร้างจากข้อมูลที่ยังแบ่งกลุ่มตามปีอยู่
        // ตารางแบนที่หน้าจอแสดงไม่มีข้อมูลพอจะสร้างบล็อกรายปีได้ จึงต้องใช้เส้นทางของตัวเอง
        if (SelectedTab.Source == ReportSource.SummaryByResident && SelectedResident is not null)
        {
            var path = new QueryBuilder($"api/v1/summary_expenses/{SelectedResident.Id}/xlsx")
                .Add("year", Year)
                .Add("month", Month?.Value);

            await documents.OpenAsync(path, $"{SelectedTab.Label}");
            return;
        }

        await documents.OpenAsync("api/v1/reports/xlsx", BuildPrintRequest(), $"{SelectedTab.Label}");
    }

    private ReportPrintRequest BuildPrintRequest()
    {
        var request = new ReportPrintRequest
        {
            ReportLabel = SelectedTab.Label,
            Columns = Columns.Select(c => new ReportPrintColumn
            {
                Header = c.Header,
                Kind = c.Kind switch
                {
                    ReportColumnKind.Money => ReportPrintValueKind.Money,
                    ReportColumnKind.Integer => ReportPrintValueKind.Integer,
                    _ => ReportPrintValueKind.Text,
                },
                Alignment = c.Kind switch
                {
                    // ตัวเลขเงินชิดขวาเหมือนบนหน้าจอ จะได้อ่านเทียบกันได้
                    ReportColumnKind.Money => ReportPrintAlignment.Right,
                    ReportColumnKind.Integer => ReportPrintAlignment.Right,
                    ReportColumnKind.Sequence => ReportPrintAlignment.Center,
                    _ => ReportPrintAlignment.Left,
                },
            }).ToList(),
            Rows = Rows.Select(row => Columns.Select(c => row[c.Key]).ToList()).ToList(),
        };

        // รายงานสองแท็บที่ดึงจากสรุปค่าใช้จ่ายมียอดรวมมาให้ ต้องแนบไปด้วย
        if (SummaryTotals is { } totals)
        {
            request.Summary =
            [
                new ReportSummaryLine { Label = "ยอดรวมค่าส่วนกลาง", Amount = totals.CommonFee },
                new ReportSummaryLine { Label = "ยอดรวมค่าเบี้ยปรับ", Amount = totals.Fine },
                new ReportSummaryLine { Label = "ยอดรวมค่าทวงถาม", Amount = totals.CollectionFee },
                new ReportSummaryLine
                {
                    Label = "ยอดรวมค้างชำระทั้งหมด", Amount = totals.Total, Emphasise = true,
                },
            ];
        }

        return request;
    }

    /// <summary>สรุปค่าใช้จ่ายของทั้งโครงการเป็น PDF — เส้นทางเดียวกับที่เว็บเดิมใช้</summary>
    [RelayCommand]
    private Task PrintSummaryAsync() =>
        documents.OpenAsync(
            new QueryBuilder("api/v1/summary_expenses/pdf").Add("year", Year).Add("month", Month?.Value),
            $"สรุปค่าใช้จ่าย-{Year}");

    private static string Escape(string value)
    {
        if (!value.Contains(',', StringComparison.Ordinal)
            && !value.Contains('"', StringComparison.Ordinal)
            && !value.Contains('\n', StringComparison.Ordinal))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private ReportRow ToRow(JsonElement element, int rowIndex)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var column in SelectedTab.Columns)
        {
            if (column.Kind == ReportColumnKind.Sequence)
            {
                cells[column.Key] = (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
                continue;
            }

            if (column.Kind == ReportColumnKind.PaymentStatusText)
            {
                cells[column.Key] = FormatPaymentStatus(element);
                continue;
            }

            cells[column.Key] = element.TryGetProperty(column.ReadFrom, out var value)
                ? Format(value, column.Kind)
                : string.Empty;
        }

        return new ReportRow(cells);
    }

    /// <summary>
    /// สถานะการชำระของรายงานค่าปรับ ดูสองฟิลด์พร้อมกันตามลำดับของระบบเดิม
    /// ใบทวงถามถูกยกเลิกถือว่าลดหนี้ก่อนเสมอ แล้วจึงดูสถานะของงวด
    /// </summary>
    private static string FormatPaymentStatus(JsonElement element)
    {
        if (element.TryGetProperty("status_notices", out var notices)
            && notices.ValueKind == JsonValueKind.String
            && notices.GetString() == "CANCELLED")
        {
            return "ลดหนี้";
        }

        if (!element.TryGetProperty("status_payments", out var payments)
            || payments.ValueKind != JsonValueKind.String)
        {
            return "-";
        }

        return payments.GetString() switch
        {
            "PAID" => "ชำระแล้ว",
            "NOT_PAY" => "ยังไม่ชำระ",
            _ => "-",
        };
    }

    private static string Format(JsonElement value, ReportColumnKind kind) => kind switch
    {
        ReportColumnKind.Money => value.ValueKind == JsonValueKind.Number
            ? ThaiFormat.Money(value.GetDecimal())
            : "-",

        ReportColumnKind.Integer => value.ValueKind == JsonValueKind.Number
            ? ThaiFormat.Integer(value.GetInt32())
            : "-",

        ReportColumnKind.MonthAbbr => value.ValueKind == JsonValueKind.Number
            ? AstarLiving.Domain.Billing.ThaiMonths.GetAbbr(value.GetInt32())
            : "-",

        // ไม่มีเหตุผลที่ยกเลิก = ใบเสร็จยังใช้งานอยู่
        ReportColumnKind.ReceiptStatus =>
            value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? "ยกเลิก"
                : "ใช้งาน",

        ReportColumnKind.ShortDate => value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var stamp)
                ? stamp.ToString("dd/MM/yy", CultureInfo.InvariantCulture)
                : "-",

        // วันที่ของเอกสารเก็บเป็น พ.ศ. อยู่แล้ว จึงแสดงปีตามที่เก็บ ห้ามบวก 543 ซ้ำ
        ReportColumnKind.Date => value.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var date)
                ? ThaiFormat.DocumentDate(date)
                : "-",

        _ => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "-",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "ใช่",
            JsonValueKind.False => "ไม่ใช่",
            JsonValueKind.Null or JsonValueKind.Undefined => "-",
            _ => value.ToString(),
        },
    };
}
