using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Client.ViewModels.Dialogs;
using AstarLiving.Domain.Enums;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>แถวในตารางใบแจ้งหนี้</summary>
public sealed class InvoiceRow(PaymentResponse payment)
{
    public PaymentResponse Payment { get; } = payment;

    public int Id => Payment.Id;

    public string InvNo => Payment.InvNo ?? "-";

    public string HouseNo => Payment.Resident?.HouseNo ?? "-";

    public string FullName => Payment.Resident?.FullName ?? "-";

    public string PeriodText => ThaiFormat.MonthYear(Payment.Month, Payment.Year);

    public string AmountDueText => ThaiFormat.Money(Payment.AmountDue);

    public string FineText => ThaiFormat.Money(Payment.Fine ?? 0m);

    public string CollectionFeeText => ThaiFormat.Money(Payment.CollectionFee ?? 0m);

    public string TotalDueText => ThaiFormat.Money(Payment.TotalDue);

    public string IssueDateText => ThaiFormat.DocumentDate(Payment.PaymentDate);

    public string StatusText => Payment.Status switch
    {
        PaymentStatus.PAID => "ชำระแล้ว",
        PaymentStatus.PARTIAL_PAY => "ชำระบางส่วน",
        PaymentStatus.CANCELLED => "ยกเลิก",
        _ => "ยังไม่ชำระ",
    };

    /// <summary>งวดที่เกิดจากใบแจ้งหนี้ล่วงหน้า แสดงป้ายกำกับไว้ให้เห็นชัด</summary>
    public string KindText => Payment.Advance ? "ล่วงหน้า" : "ปกติ";

    public bool IsAdvance => Payment.Advance;
}

/// <summary>
/// ใบแจ้งหนี้ — พอร์ตจาก PaymentContent.tsx
///
/// งวดที่จะออกถัดไปถูกตัดสินโดยฝั่ง API เสมอ (InvoicePeriodResolver)
/// ฝั่งหน้าจอห้ามเดาเอง ไม่อย่างนั้นจะออกซ้ำหรือข้ามงวดได้
/// </summary>
public sealed partial class InvoicePageViewModel(
    ApiClient api,
    AuthSession session,
    DialogService dialogs,
    DocumentService documents) : ListPageViewModel<InvoiceRow>(api, session, dialogs)
{
    public override string Title => "ใบแจ้งหนี้";

    [ObservableProperty]
    private int? _filterYear;

    [ObservableProperty]
    private MonthOption? _filterMonth;

    public IReadOnlyList<int> AvailableYears { get; } =
        Enumerable.Range(ThaiFormat.CurrentBuddhistYear - 5, 7).Reverse().ToList();

    public IReadOnlyList<MonthOption> Months { get; } =
        Enumerable.Range(1, 12).Select(m => new MonthOption(m, ThaiFormat.FullMonths[m - 1])).ToList();

    public bool CanDeletePeriod => FilterYear is not null && FilterMonth is not null;

    partial void OnFilterYearChanged(int? value)
    {
        OnPropertyChanged(nameof(CanDeletePeriod));
        _ = ReloadFromFirstPageAsync();
    }

    partial void OnFilterMonthChanged(MonthOption? value)
    {
        OnPropertyChanged(nameof(CanDeletePeriod));
        _ = ReloadFromFirstPageAsync();
    }

    protected override async Task<PagedResult<InvoiceRow>> FetchAsync(CancellationToken ct)
    {
        if (ProjectId is null) return PagedResult<InvoiceRow>.Empty;

        var path = new QueryBuilder($"api/v1/projects/{ProjectId}/payments")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("house_no", Query)
            .Add("year", FilterYear)
            .Add("month", FilterMonth?.Value)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        var result = await Api.GetPagedAsync<PaymentResponse>(path, ct);

        return new PagedResult<InvoiceRow>(result.Items.Select(p => new InvoiceRow(p)).ToList(), result.Page);
    }

    private Task ReloadFromFirstPageAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    /// <summary>ออกใบแจ้งหนี้ทั้งงวด</summary>
    [RelayCommand]
    private async Task CreateAsync()
    {
        if (ProjectId is null)
        {
            await Dialogs.ErrorAsync("บัญชีนี้ยังไม่ได้ผูกกับโครงการ");
            return;
        }

        var wizard = new InvoiceWizardDialogViewModel(Api, Dialogs, ProjectId.Value);
        _ = wizard.InitializeAsync();

        if (!await Dialogs.ShowAsync(wizard)) return;

        await Dialogs.SuccessAsync("ออกใบแจ้งหนี้สำเร็จ");
        await LoadAsync();
    }

    /// <summary>ออกใบแจ้งหนี้ล่วงหน้าให้บ้านเดียวหลายเดือน</summary>
    [RelayCommand]
    private async Task CreateAdvanceAsync()
    {
        if (ProjectId is null) return;

        var dialog = new AdvanceInvoiceDialogViewModel(Api, Dialogs, ProjectId.Value);
        await dialog.InitializeAsync();

        if (!await Dialogs.ShowAsync(dialog)) return;

        await Dialogs.SuccessAsync("ออกใบแจ้งหนี้ล่วงหน้าสำเร็จ");
        await LoadAsync();
    }

    // เส้นทางพิมพ์เป็น POST ที่บังคับให้มี body จึงต้องส่ง object ว่างไปแม้ไม่มีเงื่อนไขเพิ่ม
    [RelayCommand]
    private Task PrintAsync(InvoiceRow? row) =>
        row is null || ProjectId is null
            ? Task.CompletedTask
            : documents.OpenAsync(
                $"api/v1/projects/{ProjectId}/payments/invoices/pdf/{row.Id}",
                new { },
                $"ใบแจ้งหนี้-{row.HouseNo}-{row.PeriodText}");

    /// <summary>พิมพ์ใบแจ้งหนี้ทั้งงวดรวมเป็นไฟล์เดียว ต้องเลือกเดือนและปีก่อน</summary>
    [RelayCommand]
    private async Task PrintPeriodAsync()
    {
        if (ProjectId is null) return;

        if (!CanDeletePeriod)
        {
            await Dialogs.ErrorAsync("กรุณาเลือกเดือนและปีก่อน", "การพิมพ์ทั้งงวดต้องระบุงวดที่ต้องการ");
            return;
        }

        await documents.OpenAsync(
            $"api/v1/projects/{ProjectId}/payments/invoices/pdf",
            new { month = FilterMonth!.Value, year = FilterYear },
            $"ใบแจ้งหนี้-{FilterMonth.Label}-{FilterYear}");
    }

    /// <summary>
    /// ลบใบแจ้งหนี้ทั้งงวด ใช้เมื่อออกผิดงวดแล้วต้องการเริ่มใหม่
    /// เป็นคำสั่งที่ย้อนกลับไม่ได้ จึงให้ยืนยันสองชั้นเหมือนระบบเดิม
    /// </summary>
    [RelayCommand]
    private async Task DeletePeriodAsync()
    {
        if (ProjectId is null) return;

        if (!CanDeletePeriod)
        {
            await Dialogs.ErrorAsync("กรุณาเลือกเดือนและปีก่อน", "การลบทั้งงวดต้องระบุงวดที่ต้องการลบ");
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            $"ยืนยันการลบใบแจ้งหนี้งวด {FilterMonth!.Label} {FilterYear}",
            "ใบแจ้งหนี้ทุกใบของงวดนี้จะถูกลบ และย้อนกลับไม่ได้",
            confirmText: "ลบทั้งงวด",
            isDestructive: true);

        if (!confirmed) return;

        var done = await RunAsync(async () =>
        {
            var result = await Api.DeleteAsync<DeleteByMonthResult>(
                $"api/v1/projects/{ProjectId}/payments/month/{FilterMonth.Value}/{FilterYear}");

            await Dialogs.SuccessAsync($"ลบใบแจ้งหนี้แล้ว {result.Deleted} ใบ");
        });

        if (done) await LoadAsync();
    }
}
