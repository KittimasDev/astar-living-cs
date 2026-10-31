using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Client.ViewModels.Dialogs;
using AstarLiving.Domain.Enums;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>แถวในตารางรายการค้างชำระ</summary>
public sealed class OverdueRow(PaymentResponse payment)
{
    public PaymentResponse Payment { get; } = payment;

    public int Id => Payment.Id;

    public int ResidentId => Payment.ResidentId;

    public string HouseNo => Payment.Resident?.HouseNo ?? "-";

    public string FullName => Payment.Resident?.FullName ?? "-";

    public string InvNo => Payment.InvNo ?? "-";

    public string PeriodText => ThaiFormat.MonthYear(Payment.Month, Payment.Year);

    public string AmountDueText => ThaiFormat.Money(Payment.AmountDue);

    public string FineText => ThaiFormat.Money(Payment.Fine ?? 0m);

    public string CollectionFeeText => ThaiFormat.Money(Payment.CollectionFee ?? 0m);

    /// <summary>ยอดรวมที่ต้องชำระ ซึ่งฝั่ง API คำนวณใหม่ตอนอ่าน ไม่ใช่ค่าที่เก็บไว้</summary>
    public string TotalDueText => ThaiFormat.Money(Payment.TotalDue);

    public string StatusText => Payment.Status switch
    {
        PaymentStatus.PAID => "ชำระแล้ว",
        PaymentStatus.PARTIAL_PAY => "ชำระบางส่วน",
        PaymentStatus.CANCELLED => "ยกเลิก",
        _ => "ยังไม่ชำระ",
    };

    public bool CanReceive => Payment.Status is PaymentStatus.NOT_PAY or PaymentStatus.PARTIAL_PAY;
}

/// <summary>
/// รายการค้างชำระ — พอร์ตจาก OverdueContent.tsx
/// กดรับชำระที่แถวไหนจะเปิดขั้นตอนออกใบเสร็จโดยข้ามหน้าเลือกบ้านไปเลย
/// </summary>
public sealed partial class OverduePageViewModel(
    ApiClient api,
    AuthSession session,
    DialogService dialogs,
    DocumentService documents) : ListPageViewModel<OverdueRow>(api, session, dialogs)
{
    public override string Title => "รายการค้างชำระ";

    protected override async Task<PagedResult<OverdueRow>> FetchAsync(CancellationToken ct)
    {
        if (ProjectId is null) return PagedResult<OverdueRow>.Empty;

        // ใช้เส้นทางรายการงวดพร้อมตัวกรอง OVERDUE เพราะรองรับการแบ่งหน้าและค้นหา
        // ต่างจาก payments/outstanding ที่คืนทุกแถวรวดเดียว
        var path = new QueryBuilder($"api/v1/projects/{ProjectId}/payments")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("house_no", Query)
            .Add("type", nameof(PaymentFilterType.OVERDUE))
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        var result = await Api.GetPagedAsync<PaymentResponse>(path, ct);

        return new PagedResult<OverdueRow>(result.Items.Select(p => new OverdueRow(p)).ToList(), result.Page);
    }

    [RelayCommand]
    private async Task ReceiveAsync(OverdueRow? row)
    {
        if (row is null || ProjectId is null) return;

        if (!row.CanReceive)
        {
            await Dialogs.ErrorAsync("งวดนี้ชำระเรียบร้อยแล้ว");
            return;
        }

        var wizard = new ReceiptWizardDialogViewModel(Api, Dialogs, ProjectId.Value, row.ResidentId);
        _ = wizard.InitializeAsync();

        if (!await Dialogs.ShowAsync(wizard)) return;

        await Dialogs.SuccessAsync("ออกใบเสร็จรับเงินสำเร็จ");
        await LoadAsync();
    }

    /// <summary>พิมพ์ใบแจ้งหนี้ของงวดที่ค้างอยู่ เพื่อนำไปทวงถาม</summary>
    [RelayCommand]
    private Task PrintInvoiceAsync(OverdueRow? row) =>
        row is null || ProjectId is null
            ? Task.CompletedTask
            : documents.OpenAsync(
                $"api/v1/projects/{ProjectId}/payments/invoices/pdf/{row.Id}",
                new { },
                $"ใบแจ้งหนี้-{row.HouseNo}-{row.PeriodText}");
}
