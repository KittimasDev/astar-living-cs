using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Domain.Enums;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>แถวในตารางลดหนี้</summary>
public sealed class CreditNoteRow(ReduceNoticeResponse notice)
{
    public ReduceNoticeResponse Notice { get; } = notice;

    public int Id => Notice.Id;

    public string HouseNo => Notice.Resident?.HouseNo ?? "-";

    public string FullName => Notice.Resident?.FullName ?? "-";

    public string InvNo => Notice.Payment?.InvNo ?? "-";

    public string PeriodText => ThaiFormat.MonthYear(Notice.Month, Notice.Year);

    public string FineText => ThaiFormat.Money(Notice.FeeAmountFine ?? 0m);

    public string CollectionText => ThaiFormat.Money(Notice.FeeAmountCollection ?? 0m);

    public string TotalText => ThaiFormat.Money(Notice.FeeAmountTotal ?? 0m);

    public string StatusText => Notice.Status switch
    {
        NoticeStatus.PAID => "ชำระแล้ว",
        NoticeStatus.CANCELLED => "ลดหนี้แล้ว",
        _ => "รอดำเนินการ",
    };

    /// <summary>ลดหนี้ได้เฉพาะใบที่ยังรอดำเนินการและผูกกับงวดอยู่</summary>
    public bool CanReduce => Notice.Status == NoticeStatus.PENDING && Notice.PaymentId is not null;

    public string DeductedReason => Notice.DeductedReason ?? "-";

    public string DeductedName => Notice.DeductedName ?? "-";
}

/// <summary>
/// ลดหนี้ — พอร์ตจาก CreditNoteContent.tsx
///
/// ขั้นตอนของระบบเดิม: เลือกแถว → เรียก reduce-check เพื่อดูคำเตือน →
/// ยืนยันพร้อมกรอกเหตุผล → เรียก PATCH reduce
/// คำเตือนที่พบบ่อยคือ "งวดเดือนก่อนหน้ายังค้างชำระอยู่" ซึ่งไม่ได้ห้ามทำต่อ
/// </summary>
public sealed partial class CreditNotePageViewModel(
    ApiClient api,
    AuthSession session,
    DialogService dialogs) : ListPageViewModel<CreditNoteRow>(api, session, dialogs)
{
    public override string Title => "ลดหนี้";

    [ObservableProperty]
    private int? _filterYear;

    [ObservableProperty]
    private MonthOption? _filterMonth;

    public IReadOnlyList<int> AvailableYears { get; } =
        Enumerable.Range(ThaiFormat.CurrentBuddhistYear - 5, 7).Reverse().ToList();

    public IReadOnlyList<MonthOption> Months { get; } =
        Enumerable.Range(1, 12).Select(m => new MonthOption(m, ThaiFormat.FullMonths[m - 1])).ToList();

    partial void OnFilterYearChanged(int? value) => _ = ReloadFromFirstPageAsync();

    partial void OnFilterMonthChanged(MonthOption? value) => _ = ReloadFromFirstPageAsync();

    protected override async Task<PagedResult<CreditNoteRow>> FetchAsync(CancellationToken ct)
    {
        var path = new QueryBuilder("api/v1/reduce")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("house_no", Query)
            .Add("year", FilterYear)
            .Add("month", FilterMonth?.Value)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        var result = await Api.GetPagedAsync<ReduceNoticeResponse>(path, ct);

        return new PagedResult<CreditNoteRow>(
            result.Items.Select(n => new CreditNoteRow(n)).ToList(),
            result.Page);
    }

    private Task ReloadFromFirstPageAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private async Task ReduceAsync(CreditNoteRow? row)
    {
        if (row is null) return;

        if (!row.CanReduce)
        {
            await Dialogs.ErrorAsync(
                "ลดหนี้รายการนี้ไม่ได้",
                row.Notice.PaymentId is null
                    ? "ใบทวงถามนี้ไม่ได้ผูกกับงวดค่าส่วนกลาง"
                    : "รายการนี้ถูกดำเนินการไปแล้ว");
            return;
        }

        ReduceCheckResponse? check = null;

        var checkOk = await RunAsync(async () =>
        {
            check = await Api.GetAsync<ReduceCheckResponse>($"api/v1/reduce-check/{row.Id}");
        });

        if (!checkOk || check is null) return;

        // คำเตือนจากการตรวจไม่ได้ห้ามทำต่อ แค่ให้ผู้ใช้รู้ก่อนตัดสินใจ
        var message = string.IsNullOrWhiteSpace(check.Message)
            ? $"ยอดที่จะถูกลด {ThaiFormat.Money(check.FeeAmountTotal ?? 0m)} บาท"
            : $"{check.Message}\nยอดที่จะถูกลด {ThaiFormat.Money(check.FeeAmountTotal ?? 0m)} บาท";

        if (!await Dialogs.ConfirmAsync($"ยืนยันการลดหนี้ บ้านเลขที่ {row.HouseNo}", message)) return;

        // ระบบเดิมจำกัดเหตุผลไว้ 50 ตัวอักษร คงข้อจำกัดเดิมไว้ให้ข้อมูลเทียบกันได้
        var reason = await Dialogs.PromptAsync(
            "เหตุผลในการลดหนี้",
            "ระบุเหตุผล",
            "เช่น ผ่อนผันตามมติที่ประชุม",
            maxLength: 50);

        if (reason is null) return;

        var done = await RunAsync(async () =>
        {
            await Api.PatchAsync<CancelReduceResponse>(
                $"api/v1/reduce/{row.Id}",
                new CancelReduceRequest { DeductedReason = reason });
        });

        if (!done) return;

        await Dialogs.SuccessAsync("ลดหนี้สำเร็จ");
        await LoadAsync();
    }
}
