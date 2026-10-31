using System.Collections.ObjectModel;
using System.Globalization;
using AstarLiving.Client.Common;
using AstarLiving.Client.Common.Dialogs;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Dialogs;

/// <summary>หนึ่งบรรทัดของใบเสร็จรายรับอื่น ๆ ที่ผู้ใช้กรอกเอง</summary>
public sealed partial class RevenueItemRow : ObservableObject
{
    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _unitPrice = string.Empty;

    partial void OnQuantityChanged(string value) => RaiseTotalChanged();

    partial void OnUnitPriceChanged(string value) => RaiseTotalChanged();

    /// <summary>ยอดของบรรทัดนี้ = จำนวน x ราคาต่อหน่วย</summary>
    public decimal Total => (ParseNumber(Quantity) ?? 0m) * (ParseNumber(UnitPrice) ?? 0m);

    public string TotalText => ThaiFormat.Money(Total);

    public bool IsFilled => !string.IsNullOrWhiteSpace(Description) && ParseNumber(UnitPrice) is not null;

    public event EventHandler? TotalChanged;

    private void RaiseTotalChanged()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalText));
        TotalChanged?.Invoke(this, EventArgs.Empty);
    }

    public static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = text.Replace(",", string.Empty, StringComparison.Ordinal).Trim();

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}

/// <summary>
/// ใบเสร็จรายรับอื่น ๆ — พอร์ตจาก OtherReceiptFormModal.tsx
/// ใช้กับรายรับที่ไม่ใช่ค่าส่วนกลาง เช่น ค่าทำบัตรจอดรถหรือค่าคีย์การ์ด
/// </summary>
public sealed partial class RevenueFormDialogViewModel : DialogViewModel<bool>
{
    private readonly ApiClient _api;
    private readonly DialogService _dialogs;
    private readonly int _projectId;

    public RevenueFormDialogViewModel(ApiClient api, DialogService dialogs, int projectId)
    {
        _api = api;
        _dialogs = dialogs;
        _projectId = projectId;

        AddItem();
    }

    public override double DialogWidth => 880;

    public string Title => "เพิ่มใบเสร็จรายรับอื่น ๆ";

    public ObservableCollection<ResidentBrief> Residents { get; } = [];

    public ObservableCollection<PaymentMethodResponse> PaymentMethods { get; } = [];

    public ObservableCollection<RevenueItemRow> Items { get; } = [];

    [ObservableProperty]
    private ResidentBrief? _selectedResident;

    [ObservableProperty]
    private PaymentMethodResponse? _selectedMethod;

    [ObservableProperty]
    private DateTime _issueDate = ThaiFormat.NowBangkok.Date;

    [ObservableProperty]
    private string _bankName = string.Empty;

    [ObservableProperty]
    private string _chequeNo = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    public bool NeedsBankName => SelectedMethod?.Code is "TRANSFER" or "CHEQUE";

    public bool NeedsChequeNo => SelectedMethod?.Code == "CHEQUE";

    public decimal GrandTotal => Items.Sum(i => i.Total);

    public string GrandTotalText => ThaiFormat.Money(GrandTotal);

    partial void OnSelectedMethodChanged(PaymentMethodResponse? value)
    {
        OnPropertyChanged(nameof(NeedsBankName));
        OnPropertyChanged(nameof(NeedsChequeNo));
    }

    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            var residentPath = new QueryBuilder($"api/v1/projects/{_projectId}/residents/all").Add("no_limit", true);
            var residents = await _api.GetPagedAsync<ResidentResponse>(residentPath);

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

            var methodPath = new QueryBuilder("api/v1/payment_method").Add("no_limit", true).Add("active", true);
            var methods = await _api.GetPagedAsync<PaymentMethodResponse>(methodPath);

            PaymentMethods.Clear();
            foreach (var m in methods.Items) PaymentMethods.Add(m);

            SelectedMethod = PaymentMethods.FirstOrDefault();
        });
    }

    [RelayCommand]
    private void AddItem()
    {
        var row = new RevenueItemRow();
        row.TotalChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(GrandTotal));
            OnPropertyChanged(nameof(GrandTotalText));
        };

        Items.Add(row);
    }

    [RelayCommand]
    private void RemoveItem(RevenueItemRow? row)
    {
        // เหลือบรรทัดเดียวแล้วลบไม่ได้ ไม่งั้นฟอร์มจะว่างจนกรอกต่อไม่ได้
        if (row is null || Items.Count <= 1) return;

        Items.Remove(row);
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(GrandTotalText));
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

        if (SelectedMethod is null)
        {
            ErrorMessage = "กรุณาเลือกวิธีการชำระเงิน";
            return;
        }

        var filled = Items.Where(i => i.IsFilled).ToList();
        if (filled.Count == 0)
        {
            ErrorMessage = "กรุณากรอกรายการอย่างน้อยหนึ่งบรรทัด";
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

        var issueDate = DateOnly.FromDateTime(IssueDate);

        var ok = await RunAsync(async () =>
        {
            await _api.PostAsync<RevenueTransactionResponse>("api/transactions", new CreateRevenueTransactionRequest
            {
                ProjectId = _projectId,
                IssueDate = issueDate,
                ResidentId = SelectedResident.Id,
                PaymentDetail = new RevenuePaymentDetailContract
                {
                    Items = filled.Select((item, index) => new RevenueItemContract
                    {
                        ItemNo = index + 1,
                        Description = item.Description.Trim(),
                        Quantity = RevenueItemRow.ParseNumber(item.Quantity) ?? 1m,
                        UnitPrice = RevenueItemRow.ParseNumber(item.UnitPrice) ?? 0m,
                        Total = item.Total,
                    }).ToList(),
                    GrandTotal = GrandTotal,
                },
                TotalAmount = GrandTotal,
                PaymentMethodId = SelectedMethod.Id,
                BankName = string.IsNullOrWhiteSpace(BankName) ? null : BankName.Trim(),
                ChequeNo = string.IsNullOrWhiteSpace(ChequeNo) ? null : ChequeNo.Trim(),
                Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
                PaymentDate = issueDate,
            });
        });

        if (ok) Close(true);
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (await _dialogs.ConfirmExitAsync()) Close(false);
    }
}
