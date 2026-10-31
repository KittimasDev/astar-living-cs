using System.Collections.ObjectModel;
using AstarLiving.Client.Common;
using AstarLiving.Client.Common.Dialogs;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Dialogs;

/// <summary>หนึ่งบ้านในหน้าพรีวิวก่อนออกใบแจ้งหนี้</summary>
public sealed partial class InvoicePreviewItem(InvoicePreviewRow row) : ObservableObject
{
    public InvoicePreviewRow Row { get; } = row;

    /// <summary>
    /// ติ๊กไว้ = คิดค่าทวงถามให้บ้านหลังนี้ด้วย
    /// ระบบเดิมส่งเฉพาะรหัสบ้านที่ติ๊กไว้ไปกับคำสั่งออกใบแจ้งหนี้
    /// </summary>
    [ObservableProperty]
    private bool _chargeCollectionFee;

    public int Id => Row.Id;

    public string HouseNo => Row.HouseNo;

    public string FullName => Row.FullName;

    public string OwnershipRatioText => Row.OwnershipRatio?.ToString("#,##0.##") ?? "-";

    public string MaintenanceText => ThaiFormat.Money(Row.Maintenance);

    public string FineText => ThaiFormat.Money(Row.FineAmount);

    public string CollectionText => ThaiFormat.Money(Row.CollectionAmount);

    public string DebtTotalText => ThaiFormat.Money(Row.DebtTotal);

    public string TotalText => ThaiFormat.Money(Row.Total);

    public bool HasDebt => Row.DebtTotal > 0;
}

/// <summary>
/// ขั้นตอนออกใบแจ้งหนี้ทั้งงวด — พอร์ตจาก PaymentSelectModal + PaymentCreateModal ของเว็บเดิม
///
/// 1. ยืนยันงวดที่ระบบเลือกให้ (ฝั่ง API เป็นผู้ตัดสินว่างวดไหน ห้าม client เดาเอง)
/// 2. ดูรายการบ้านและยอด พร้อมติ๊กว่าบ้านไหนคิดค่าทวงถาม
/// 3. กดออกใบแจ้งหนี้
/// </summary>
public sealed partial class InvoiceWizardDialogViewModel(
    ApiClient api, DialogService dialogs, int projectId) : DialogViewModel<bool>
{
    public override double DialogWidth => 1000;

    [ObservableProperty]
    private int _step = 1;

    public bool IsStep1 => Step == 1;

    public bool IsStep2 => Step == 2;

    public string Title => Step == 1 ? "งวดที่จะออกใบแจ้งหนี้" : "ตรวจสอบรายการก่อนออกใบแจ้งหนี้";

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(Title));
    }

    // ── ขั้นที่ 1 ───────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _periodText = string.Empty;

    /// <summary>true = เป็นการเติมใบย้อนหลังให้บ้านที่ขาด ไม่ใช่การออกงวดใหม่</summary>
    [ObservableProperty]
    private bool _isSupplement;

    private InvoicePeriodResponse? _period;

    // ── ขั้นที่ 2 ───────────────────────────────────────────────────────────

    public ObservableCollection<InvoicePreviewItem> PreviewItems { get; } = [];

    public decimal GrandTotal => PreviewItems.Sum(i => i.Row.Total);

    public string GrandTotalText => ThaiFormat.Money(GrandTotal);

    public int HouseCount => PreviewItems.Count;

    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            _period = await api.GetAsync<InvoicePeriodResponse>($"api/v1/projects/{projectId}/payments/month-range");

            PeriodText = $"{_period.MonthTh} {_period.Year}";
            IsSupplement = _period.IsSupplement;
        });
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (Step == 1)
        {
            await RunAsync(LoadPreviewAsync);
            return;
        }

        await CreateAsync();
    }

    private async Task LoadPreviewAsync()
    {
        // เส้นทางพรีวิวเป็น POST ที่รับทั้งตัวแบ่งหน้าทาง query และ body ว่าง ๆ ตามระบบเดิม
        // ขอมาทั้งหมดในครั้งเดียวเพราะหน้านี้ต้องเห็นยอดรวมของทั้งงวด
        var rows = await api.PostAsync<List<InvoicePreviewRow>>(
            new QueryBuilder($"api/v1/projects/{projectId}/payments/invoices").Add("no_limit", true),
            new CreateInvoicesRequest());

        Apply(rows);

        if (PreviewItems.Count == 0)
        {
            ErrorMessage = "ไม่มีบ้านที่ต้องออกใบแจ้งหนี้ในงวดนี้";
            return;
        }

        Step = 2;
    }

    private void Apply(IEnumerable<InvoicePreviewRow> rows)
    {
        PreviewItems.Clear();

        foreach (var row in rows)
        {
            var item = new InvoicePreviewItem(row)
            {
                // ตั้งต้นให้คิดค่าทวงถามเฉพาะบ้านที่มีหนี้ค้าง ตรงกับที่หน้างานทำจริง
                ChargeCollectionFee = row.DebtTotal > 0,
            };

            PreviewItems.Add(item);
        }

        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(HouseCount));
    }

    private async Task CreateAsync()
    {
        var selected = PreviewItems.Where(i => i.ChargeCollectionFee).Select(i => i.Id).ToList();

        var confirmed = await dialogs.ConfirmAsync(
            $"ยืนยันการออกใบแจ้งหนี้งวด {PeriodText}",
            $"จะออกใบแจ้งหนี้ {HouseCount} ใบ รวมเป็นเงิน {GrandTotalText} บาท");

        if (!confirmed) return;

        var ok = await RunAsync(async () =>
        {
            await api.PostAsync<CreateInvoicesResult>(
                $"api/v1/projects/{projectId}/payments/invoices/create",
                new CreateInvoicesRequest { ResidentIds = selected });
        });

        if (ok) Close(true);
    }

    [RelayCommand]
    private void Back()
    {
        ClearError();
        if (Step > 1) Step--;
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (Step == 1)
        {
            Close(false);
            return;
        }

        if (await dialogs.ConfirmExitAsync()) Close(false);
    }
}

/// <summary>เดือนที่เลือกได้ในใบแจ้งหนี้ล่วงหน้า</summary>
public sealed partial class AdvanceMonthRow(AdvanceInvoiceMonthOption option) : ObservableObject
{
    public AdvanceInvoiceMonthOption Option { get; } = option;

    [ObservableProperty]
    private bool _isSelected;

    public int Month => Option.Month;

    public string MonthTh => Option.MonthTh;

    /// <summary>false = เดือนนี้มีงวดอยู่แล้ว จึงเลือกซ้ำไม่ได้</summary>
    public bool CanSelect => Option.Active;
}

/// <summary>
/// ใบแจ้งหนี้ล่วงหน้า — พอร์ตจากชุด pre-invoice ของเว็บเดิม
/// ออกให้บ้านเดียวหลายเดือนพร้อมกัน แล้วสร้างงวดสถานะชำระแล้วให้ทุกเดือนที่เลือก
/// </summary>
public sealed partial class AdvanceInvoiceDialogViewModel(
    ApiClient api, DialogService dialogs, int projectId) : DialogViewModel<bool>
{
    public override double DialogWidth => 820;

    public string Title => "ออกใบแจ้งหนี้ล่วงหน้า";

    public ObservableCollection<ResidentBrief> Residents { get; } = [];

    public ObservableCollection<AdvanceMonthRow> Months { get; } = [];

    public ObservableCollection<PaymentMethodResponse> PaymentMethods { get; } = [];

    [ObservableProperty]
    private ResidentBrief? _selectedResident;

    [ObservableProperty]
    private int _year = ThaiFormat.CurrentBuddhistYear;

    [ObservableProperty]
    private PaymentMethodResponse? _selectedMethod;

    [ObservableProperty]
    private DateTime _issueDate = ThaiFormat.NowBangkok.Date;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string _previewLabel = string.Empty;

    [ObservableProperty]
    private string _previewTotalText = "0";

    private decimal _previewTotal;

    public IReadOnlyList<int> AvailableYears { get; private set; } =
        Enumerable.Range(ThaiFormat.CurrentBuddhistYear, 3).ToList();

    public bool HasSelectedMonths => Months.Any(m => m.IsSelected);

    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            var residentPath = new QueryBuilder($"api/v1/projects/{projectId}/residents/all").Add("no_limit", true);
            var residents = await api.GetPagedAsync<ResidentResponse>(residentPath);

            Residents.Clear();
            foreach (var r in residents.Items)
            {
                Residents.Add(new ResidentBrief
                {
                    Id = r.Id,
                    ProjectId = r.ProjectId,
                    HouseNo = r.HouseNo,
                    FullName = r.FullName,
                    Address = r.Address,
                });
            }

            var methods = await api.GetPagedAsync<PaymentMethodResponse>(
                new QueryBuilder("api/v1/payment_method").Add("no_limit", true).Add("active", true));

            PaymentMethods.Clear();
            foreach (var m in methods.Items) PaymentMethods.Add(m);

            SelectedMethod = PaymentMethods.FirstOrDefault();
        });
    }

    partial void OnSelectedResidentChanged(ResidentBrief? value) => _ = ReloadMonthsAsync();

    partial void OnYearChanged(int value) => _ = ReloadMonthsAsync();

    /// <summary>ถามฝั่ง API ว่าปีนี้เดือนไหนยังออกได้บ้าง ห้ามคำนวณเองที่ฝั่ง client</summary>
    private async Task ReloadMonthsAsync()
    {
        if (SelectedResident is null) return;

        await RunAsync(async () =>
        {
            var yearInfo = await api.PostAsync<AdvanceInvoiceYearResponse>(
                $"api/v1/projects/{projectId}/payments/advance-invoices/year",
                new AdvanceInvoicePreviewRequest { ResidentId = SelectedResident.Id });

            AvailableYears = Enumerable.Range(yearInfo.Min, 3).ToList();
            OnPropertyChanged(nameof(AvailableYears));

            var options = await api.PostAsync<List<AdvanceInvoiceMonthOption>>(
                $"api/v1/projects/{projectId}/payments/advance-invoices/months",
                new AdvanceInvoicePreviewRequest { ResidentId = SelectedResident.Id, Year = Year });

            Months.Clear();
            foreach (var option in options)
            {
                var row = new AdvanceMonthRow(option);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(AdvanceMonthRow.IsSelected)) return;

                    OnPropertyChanged(nameof(HasSelectedMonths));
                    _ = RefreshPreviewAsync();
                };

                Months.Add(row);
            }

            PreviewLabel = string.Empty;
            PreviewTotalText = "0";
        });
    }

    private async Task RefreshPreviewAsync()
    {
        var months = Months.Where(m => m.IsSelected).Select(m => m.Month).ToList();

        if (SelectedResident is null || months.Count == 0)
        {
            PreviewLabel = string.Empty;
            PreviewTotalText = "0";
            _previewTotal = 0m;
            return;
        }

        await RunAsync(async () =>
        {
            var preview = await api.PostAsync<AdvanceInvoicePreviewResponse>(
                $"api/v1/projects/{projectId}/payments/advance-invoices/preview",
                new AdvanceInvoicePreviewRequest
                {
                    ResidentId = SelectedResident.Id,
                    Year = Year,
                    Months = months,
                });

            PreviewLabel = preview.Label;
            _previewTotal = preview.Total;
            PreviewTotalText = ThaiFormat.Money(preview.Total);
        });
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ClearError();

        if (SelectedResident is null)
        {
            ErrorMessage = "กรุณาเลือกบ้าน";
            return;
        }

        var months = Months.Where(m => m.IsSelected).Select(m => m.Month).ToList();
        if (months.Count == 0)
        {
            ErrorMessage = "กรุณาเลือกอย่างน้อย 1 เดือน";
            return;
        }

        if (SelectedMethod is null)
        {
            ErrorMessage = "กรุณาเลือกวิธีการชำระเงิน";
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "ยืนยันการออกใบแจ้งหนี้ล่วงหน้า",
            $"{PreviewLabel}\nรวมเป็นเงิน {PreviewTotalText} บาท");

        if (!confirmed) return;

        var ok = await RunAsync(async () =>
        {
            await api.PostAsync<AdvanceInvoiceCreateResult>(
                $"api/v1/projects/{projectId}/payments/advance-invoices",
                new AdvanceInvoiceCreateRequest
                {
                    ResidentId = SelectedResident.Id,
                    Year = Year,
                    Months = months,
                    TotalAmount = _previewTotal,
                    PaymentMethodId = SelectedMethod.Id,
                    Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
                    IssueDate = DateOnly.FromDateTime(IssueDate),
                });
        });

        if (ok) Close(true);
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (await dialogs.ConfirmExitAsync()) Close(false);
    }
}
