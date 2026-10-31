using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Client.ViewModels.Dialogs;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstarLiving.Domain.Billing;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>แถวในตารางใบเสร็จ ใช้ได้ทั้งใบเสร็จค่าส่วนกลางและใบเสร็จรายรับอื่น</summary>
public sealed class ReceiptRow
{
    public ReceiptRow(ReceiptResponse receipt)
    {
        Id = receipt.Id;
        ReceiptNo = receipt.ReceiptNo ?? "-";
        HouseNo = receipt.Resident?.HouseNo ?? "-";
        FullName = receipt.Resident?.FullName ?? "-";
        IssueDateText = ThaiFormat.DocumentDate(receipt.IssueDate);
        TotalText = ThaiFormat.Money(receipt.TotalAmount);
        Note = receipt.Note ?? "-";
        Active = receipt.Active;
        RejectedReason = receipt.RejectedReason;
        IsRevenue = false;
        // payment_ids เก็บเป็นรหัสเดือนภาษาอังกฤษ เช่น ["SEP","AUG"]
        // ถ้าเอามาแสดงดิบ ๆ ผู้ใช้จะเห็นคำว่า SEP ซึ่งอ่านไม่รู้เรื่อง
        PeriodText = receipt.PaymentIds is { Count: > 0 }
            ? string.Join(", ", receipt.PaymentIds.Select(ToThaiMonth))
            : "-";
    }

    public ReceiptRow(RevenueTransactionResponse revenue)
    {
        Id = revenue.Id;
        ReceiptNo = revenue.ReceiptNo;
        HouseNo = revenue.Resident?.HouseNo ?? "-";
        FullName = revenue.Resident?.FullName ?? "-";
        IssueDateText = ThaiFormat.DocumentDate(revenue.IssueDate);
        TotalText = ThaiFormat.Money(revenue.TotalAmount);
        Note = revenue.Note ?? "-";
        Active = revenue.Active;
        RejectedReason = revenue.RejectedReason;
        IsRevenue = true;

        var count = revenue.PaymentDetail?.Items.Count ?? 0;
        PeriodText = count == 0 ? "-" : $"{count} รายการ";
    }

    public int Id { get; }
    public string ReceiptNo { get; }
    public string HouseNo { get; }
    public string FullName { get; }
    public string IssueDateText { get; }
    public string TotalText { get; }
    /// <summary>
    /// รหัสเดือนอังกฤษเป็นตัวย่อไทย
    /// รหัสที่แปลไม่ได้ให้คืนค่าเดิม ดีกว่าแสดงช่องว่างจนไม่รู้ว่าข้อมูลหาย
    /// </summary>
    private static string ToThaiMonth(string code) =>
        ThaiMonths.FromEnAbbr(code) is { } month ? ThaiMonths.GetAbbr(month) : code;

    public string PeriodText { get; }
    public string Note { get; }
    public bool Active { get; }
    public string? RejectedReason { get; }
    public bool IsRevenue { get; }

    public string StatusText => Active ? "ใช้งาน" : "ยกเลิกแล้ว";

    public bool CanCancel => Active;
}

/// <summary>
/// ใบเสร็จรับเงิน — พอร์ตจาก ReceiptContent.tsx
///
/// หน้านี้มีสองโหมดเหมือนเว็บเดิม: ใบเสร็จค่าส่วนกลาง และใบเสร็จรายรับอื่น ๆ
/// ทั้งสองโหมดใช้ตารางเดียวกันแต่คนละเส้นทางของ API
/// </summary>
public sealed partial class ReceiptPageViewModel(
    ApiClient api,
    AuthSession session,
    DialogService dialogs,
    DocumentService documents) : ListPageViewModel<ReceiptRow>(api, session, dialogs)
{
    public override string Title => "ใบเสร็จรับเงิน";

    /// <summary>false = ใบเสร็จค่าส่วนกลาง · true = ใบเสร็จรายรับอื่น ๆ</summary>
    [ObservableProperty]
    private bool _isRevenueMode;

    public string CreateText => IsRevenueMode ? "เพิ่มรายรับอื่น ๆ" : "ออกใบเสร็จ";

    partial void OnIsRevenueModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CreateText));
        Page = 1;
        _ = LoadAsync();
    }

    protected override async Task<PagedResult<ReceiptRow>> FetchAsync(CancellationToken ct)
    {
        if (ProjectId is null) return PagedResult<ReceiptRow>.Empty;

        if (IsRevenueMode)
        {
            var revenuePath = new QueryBuilder($"api/transactions/projects/{ProjectId}/revenues")
                .Add("page", Page)
                .Add("limit", PageSize)
                .Add("house_no", Query)
                .Add("order_field", OrderField)
                .Add("order_type", OrderType);

            var revenues = await Api.GetPagedAsync<RevenueTransactionResponse>(revenuePath, ct);

            return new PagedResult<ReceiptRow>(
                revenues.Items.Select(r => new ReceiptRow(r)).ToList(),
                revenues.Page);
        }

        var path = new QueryBuilder($"api/v1/projects/{ProjectId}/receipts")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("house_no", Query)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        var result = await Api.GetPagedAsync<ReceiptResponse>(path, ct);

        return new PagedResult<ReceiptRow>(result.Items.Select(r => new ReceiptRow(r)).ToList(), result.Page);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (ProjectId is null)
        {
            await Dialogs.ErrorAsync("บัญชีนี้ยังไม่ได้ผูกกับโครงการ");
            return;
        }

        if (IsRevenueMode)
        {
            var revenueDialog = new RevenueFormDialogViewModel(Api, Dialogs, ProjectId.Value);
            await revenueDialog.InitializeAsync();

            if (!await Dialogs.ShowAsync(revenueDialog)) return;

            await Dialogs.SuccessAsync("บันทึกรายรับอื่น ๆ สำเร็จ");
            await LoadAsync();
            return;
        }

        var wizard = new ReceiptWizardDialogViewModel(Api, Dialogs, ProjectId.Value);
        _ = wizard.InitializeAsync();

        if (!await Dialogs.ShowAsync(wizard)) return;

        await Dialogs.SuccessAsync("ออกใบเสร็จรับเงินสำเร็จ");
        await LoadAsync();
    }

    [RelayCommand]
    private Task PrintAsync(ReceiptRow? row) =>
        row is null
            ? Task.CompletedTask
            : documents.OpenAsync(
                row.IsRevenue
                    ? $"api/transactions/revenues/{row.Id}/print"
                    : $"api/v1/receipts/{row.Id}/print",
                $"ใบเสร็จ-{row.ReceiptNo}");

    /// <summary>
    /// ยกเลิกใบเสร็จ
    /// ฝั่ง API จะคืนสถานะงวดกลับเป็นยังไม่ชำระ ลบงวดล่วงหน้าที่ระบบสร้างให้
    /// และคืนใบทวงถามกลับเป็นรอชำระให้ทั้งหมดในทรานแซกชันเดียว
    /// </summary>
    [RelayCommand]
    private async Task CancelReceiptAsync(ReceiptRow? row)
    {
        if (row is null) return;

        if (!row.CanCancel)
        {
            await Dialogs.ErrorAsync("ใบเสร็จนี้ถูกยกเลิกไปแล้ว");
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(
            $"ยืนยันการยกเลิกใบเสร็จ {row.ReceiptNo}",
            "งวดที่อยู่ในใบเสร็จนี้จะกลับไปเป็นยังไม่ชำระทั้งหมด",
            confirmText: "ยกเลิกใบเสร็จ",
            cancelText: "ไม่ใช่ตอนนี้",
            isDestructive: true);

        if (!confirmed) return;

        var reason = await Dialogs.PromptAsync("เหตุผลในการยกเลิก", "ระบุเหตุผล", "เช่น ออกใบเสร็จผิดบ้าน");
        if (reason is null) return;

        var done = await RunAsync(async () =>
        {
            if (row.IsRevenue)
            {
                await Api.PatchAsync<RevenueTransactionResponse>(
                    $"api/transactions/rejected/{row.Id}",
                    new RejectRevenueTransactionRequest { RejectedReason = reason });
            }
            else
            {
                await Api.PatchAsync<ReceiptResponse>(
                    $"api/v1/receipts/rejected/{row.Id}",
                    new RejectReceiptRequest { RejectedReason = reason });
            }
        });

        if (!done) return;

        await Dialogs.SuccessAsync("ยกเลิกใบเสร็จสำเร็จ");
        await LoadAsync();
    }

    [RelayCommand]
    private void ShowMaintenanceMode() => IsRevenueMode = false;

    [RelayCommand]
    private void ShowRevenueMode() => IsRevenueMode = true;
}
