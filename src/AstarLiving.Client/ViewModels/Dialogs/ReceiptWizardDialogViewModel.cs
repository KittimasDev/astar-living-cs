using System.Collections.ObjectModel;
using AstarLiving.Client.Common;
using AstarLiving.Client.Common.Dialogs;
using AstarLiving.Client.Services;
using AstarLiving.Domain.Enums;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Dialogs;

/// <summary>รายการย่อยหนึ่งบรรทัดที่ติ๊กเลือกได้ (ค่าส่วนกลาง / ค่าปรับ / ค่าทวงถาม)</summary>
public sealed partial class ReceiptItemRow(SelectableItem item) : ObservableObject
{
    public SelectableItem Item { get; } = item;

    [ObservableProperty]
    private bool _isSelected = true;

    public string Name => Item.Name;

    public string AmountText => ThaiFormat.Money(Item.Amount);

    public decimal Amount => Item.Amount;

    public ReceiptItemType Type => Enum.TryParse<ReceiptItemType>(Item.Key, out var t) ? t : ReceiptItemType.MAINTENANCE;
}

/// <summary>หนึ่งงวดพร้อมรายการย่อยในหน้าเลือกเพื่อออกใบเสร็จ</summary>
public sealed partial class ReceiptInvoiceRow : ObservableObject
{
    public ReceiptInvoiceRow(SelectableInvoice invoice)
    {
        Invoice = invoice;
        Items = new ObservableCollection<ReceiptItemRow>(invoice.Items.Select(i => new ReceiptItemRow(i)));

        foreach (var item in Items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ReceiptItemRow.IsSelected)) return;

                OnPropertyChanged(nameof(SelectedTotalText));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
        }
    }

    public SelectableInvoice Invoice { get; }

    public ObservableCollection<ReceiptItemRow> Items { get; }

    public event EventHandler? SelectionChanged;

    public int PaymentId => Invoice.PaymentId;

    public string PeriodText => Invoice.MonthTh;

    public string InvoiceNo => Invoice.InvoiceNo ?? "-";

    public string TotalText => ThaiFormat.Money(Invoice.Total);

    public decimal SelectedTotal => Items.Where(i => i.IsSelected).Sum(i => i.Amount);

    public string SelectedTotalText => ThaiFormat.Money(SelectedTotal);

    public bool HasSelection => Items.Any(i => i.IsSelected);

    /// <summary>แปลงเป็นรูปแบบที่ API ต้องการ โดยส่งไปครบทุกรายการพร้อมธงว่าเลือกหรือไม่</summary>
    public ReceiptPaymentSelection ToSelection() => new()
    {
        PaymentId = PaymentId,
        Items = Items.Select(i => new ReceiptItemSelection
        {
            Type = i.Type,
            Selected = i.IsSelected,
            Id = i.Item.Id,
        }).ToList(),
    };
}

/// <summary>
/// ขั้นตอนการออกใบเสร็จค่าส่วนกลาง — พอร์ตจากลำดับหน้าจอของ ReceiptContent.tsx
///
/// 1. เลือกบ้าน
/// 2. เลือกงวดและรายการย่อยที่จะรับชำระ
/// 3. ดูสรุปยอด (ฝั่งเซิร์ฟเวอร์เป็นผู้คำนวณ รวมส่วนลดรายปี)
/// 4. เลือกวิธีชำระเงินแล้วยืนยัน
/// </summary>
public sealed partial class ReceiptWizardDialogViewModel : DialogViewModel<bool>
{
    private readonly ApiClient _api;
    private readonly DialogService _dialogs;
    private readonly int _projectId;

    public ReceiptWizardDialogViewModel(ApiClient api, DialogService dialogs, int projectId, int? residentId = null)
    {
        _api = api;
        _dialogs = dialogs;
        _projectId = projectId;

        // เปิดจากหน้ารายการค้างชำระจะรู้บ้านอยู่แล้ว จึงข้ามขั้นตอนเลือกบ้านไปได้เลย
        if (residentId is { } id)
        {
            PreselectedResidentId = id;
            Step = 2;
        }
    }

    public override double DialogWidth => 880;

    public int? PreselectedResidentId { get; }

    /// <summary>ขั้นที่กำลังแสดง 1 ถึง 4</summary>
    [ObservableProperty]
    private int _step = 1;

    public bool IsStep1 => Step == 1;

    public bool IsStep2 => Step == 2;

    public bool IsStep3 => Step == 3;

    public bool IsStep4 => Step == 4;

    public string Title => Step switch
    {
        1 => "เลือกบ้านที่ต้องการออกใบเสร็จ",
        2 => "เลือกงวดที่รับชำระ",
        3 => "สรุปยอดที่ต้องชำระ",
        _ => "วิธีการชำระเงิน",
    };

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(Title));
    }

    // ── ขั้นที่ 1: เลือกบ้าน ─────────────────────────────────────────────────

    public ObservableCollection<ResidentBrief> Residents { get; } = [];

    [ObservableProperty]
    private ResidentBrief? _selectedResident;

    [ObservableProperty]
    private string _residentSearch = string.Empty;

    partial void OnResidentSearchChanged(string value) => ApplyResidentFilter();

    private List<ResidentBrief> _allResidents = [];

    private void ApplyResidentFilter()
    {
        var keyword = ResidentSearch.Trim();

        var filtered = string.IsNullOrEmpty(keyword)
            ? _allResidents
            : _allResidents.Where(r =>
                r.HouseNo.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || r.FullName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        Residents.Clear();
        foreach (var r in filtered) Residents.Add(r);
    }

    // ── ขั้นที่ 2: เลือกงวด ─────────────────────────────────────────────────

    public ObservableCollection<ReceiptInvoiceRow> Invoices { get; } = [];

    [ObservableProperty]
    private string _residentLabel = string.Empty;

    /// <summary>true = บ้านหลังนี้เข้าเงื่อนไขส่วนลดรายปี ผู้ใช้เลือกใช้หรือไม่ใช้ได้</summary>
    [ObservableProperty]
    private bool _discountAvailable;

    [ObservableProperty]
    private bool _useDiscount;

    public decimal SelectedTotal => Invoices.Sum(i => i.SelectedTotal);

    public string SelectedTotalText => ThaiFormat.Money(SelectedTotal);

    // ── ขั้นที่ 3: สรุปยอด ──────────────────────────────────────────────────

    public ObservableCollection<ReceiptSummaryItem> SummaryItems { get; } = [];

    [ObservableProperty]
    private string _summaryDiscountText = "0";

    [ObservableProperty]
    private string _summaryTotalText = "0";

    [ObservableProperty]
    private bool _summaryHasDiscount;

    private decimal _summaryTotal;

    // ── ขั้นที่ 4: วิธีชำระเงิน ───────────────────────────────────────────────

    public ObservableCollection<PaymentMethodResponse> PaymentMethods { get; } = [];

    [ObservableProperty]
    private PaymentMethodResponse? _selectedMethod;

    [ObservableProperty]
    private string _bankName = string.Empty;

    [ObservableProperty]
    private string _chequeNo = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private DateTime _issueDate = ThaiFormat.NowBangkok.Date;

    public bool NeedsBankName => SelectedMethod?.Code is "TRANSFER" or "CHEQUE";

    public bool NeedsChequeNo => SelectedMethod?.Code == "CHEQUE";

    partial void OnSelectedMethodChanged(PaymentMethodResponse? value)
    {
        OnPropertyChanged(nameof(NeedsBankName));
        OnPropertyChanged(nameof(NeedsChequeNo));
    }

    // ── การโหลดข้อมูลของแต่ละขั้น ────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            if (PreselectedResidentId is { } id)
            {
                await LoadInvoicesAsync(id);
                return;
            }

            var path = new QueryBuilder($"api/v1/projects/{_projectId}/payments/residents")
                .Add("type", nameof(PaymentFilterType.RECEIPT))
                .Add("no_limit", true);

            var result = await _api.GetPagedAsync<ResidentBrief>(path);

            _allResidents = result.Items.ToList();
            ApplyResidentFilter();
        });
    }

    private async Task LoadInvoicesAsync(int residentId)
    {
        var data = await _api.GetAsync<SelectableInvoicesResponse>($"api/v1/receipts/residents/{residentId}");

        ResidentLabel = data.Resident is null
            ? string.Empty
            : $"บ้านเลขที่ {data.Resident.HouseNo} · {data.Resident.Name}";

        DiscountAvailable = data.HaveDiscount;
        UseDiscount = false;

        Invoices.Clear();
        foreach (var invoice in data.Invoices)
        {
            var row = new ReceiptInvoiceRow(invoice);
            row.SelectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(SelectedTotal));
                OnPropertyChanged(nameof(SelectedTotalText));
            };
            Invoices.Add(row);
        }

        OnPropertyChanged(nameof(SelectedTotal));
        OnPropertyChanged(nameof(SelectedTotalText));

        Step = 2;
    }

    // ── คำสั่งของปุ่ม ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task NextAsync()
    {
        switch (Step)
        {
            case 1:
                if (SelectedResident is null)
                {
                    ErrorMessage = "กรุณาเลือกบ้านก่อน";
                    return;
                }

                await RunAsync(() => LoadInvoicesAsync(SelectedResident.Id));
                return;

            case 2:
                if (!Invoices.Any(i => i.HasSelection))
                {
                    ErrorMessage = "กรุณาเลือกอย่างน้อยหนึ่งรายการ";
                    return;
                }

                await RunAsync(LoadSummaryAsync);
                return;

            case 3:
                await RunAsync(LoadPaymentMethodsAsync);
                return;

            default:
                await SubmitAsync();
                return;
        }
    }

    private async Task LoadSummaryAsync()
    {
        var residentId = CurrentResidentId();

        var preview = await _api.PostAsync<ReceiptPreviewResponse>(
            $"api/v1/receipts/residents/{residentId}",
            new CalculateReceiptPreviewRequest
            {
                HaveDiscount = UseDiscount,
                Payments = Invoices.Select(i => i.ToSelection()).ToList(),
            });

        SummaryItems.Clear();
        foreach (var item in preview.Summary?.Items ?? []) SummaryItems.Add(item);

        SummaryHasDiscount = preview.Summary?.HaveDiscount ?? false;
        SummaryDiscountText = ThaiFormat.Money(preview.Summary?.Discount ?? 0m);
        _summaryTotal = preview.Summary?.TotalAmount ?? 0m;
        SummaryTotalText = ThaiFormat.Money(_summaryTotal);

        Step = 3;
    }

    private async Task LoadPaymentMethodsAsync()
    {
        if (PaymentMethods.Count == 0)
        {
            var path = new QueryBuilder("api/v1/payment_method").Add("no_limit", true).Add("active", true);
            var methods = await _api.GetPagedAsync<PaymentMethodResponse>(path);

            foreach (var method in methods.Items) PaymentMethods.Add(method);

            SelectedMethod = PaymentMethods.FirstOrDefault();
        }

        Step = 4;
    }

    private async Task SubmitAsync()
    {
        if (SelectedMethod is null)
        {
            ErrorMessage = "กรุณาเลือกวิธีการชำระเงิน";
            return;
        }

        if (NeedsBankName && string.IsNullOrWhiteSpace(BankName))
        {
            ErrorMessage = "กรุณากรอกชื่อธนาคาร";
            return;
        }

        if (NeedsChequeNo && string.IsNullOrWhiteSpace(ChequeNo))
        {
            ErrorMessage = "กรุณากรอกเลขที่เช็ค";
            return;
        }

        var ok = await RunAsync(async () =>
        {
            await _api.PostAsync<ReceiptResponse>("api/v1/receipts", new CreateReceiptRequest
            {
                ProjectId = _projectId,
                IssueDate = DateOnly.FromDateTime(IssueDate),
                ResidentId = CurrentResidentId(),

                // คอลัมน์ payment_ids ของระบบเดิมเก็บชื่อเดือนภาษาอังกฤษ ไม่ใช่รหัสงวด
                Months = Invoices.Where(i => i.HasSelection).Select(i => i.Invoice.MonthEn).ToList(),
                TotalAmount = _summaryTotal,
                PaymentMethodId = SelectedMethod.Id,
                BankName = string.IsNullOrWhiteSpace(BankName) ? null : BankName.Trim(),
                ChequeNo = string.IsNullOrWhiteSpace(ChequeNo) ? null : ChequeNo.Trim(),
                Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
                HaveDiscount = UseDiscount,
                Payments = Invoices.Select(i => i.ToSelection()).ToList(),
            });
        });

        if (ok) Close(true);
    }

    [RelayCommand]
    private void Back()
    {
        ClearError();

        // บ้านที่ถูกเลือกมาจากหน้าก่อนแล้ว ย้อนกลับไปขั้นเลือกบ้านไม่ได้
        var minimum = PreselectedResidentId is null ? 1 : 2;

        if (Step > minimum) Step--;
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (await _dialogs.ConfirmExitAsync()) Close(false);
    }

    private int CurrentResidentId() => PreselectedResidentId ?? SelectedResident!.Id;
}
