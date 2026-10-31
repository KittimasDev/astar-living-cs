using System.Collections.ObjectModel;
using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Domain.Enums;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>แถวในตารางใบทวงถาม ห่อข้อมูลจาก API เพิ่มสถานะการติ๊กเลือก</summary>
public sealed partial class CollectionNoticeRow(CollectionNoticeResponse notice) : ObservableObject
{
    public CollectionNoticeResponse Notice { get; } = notice;

    [ObservableProperty]
    private bool _isSelected;

    public int Id => Notice.Id;

    public string HouseNo => Notice.Resident?.HouseNo ?? "-";

    public string FullName => Notice.Resident?.FullName ?? "-";

    public string PeriodText => ThaiFormat.MonthYear(Notice.Month, Notice.Year);

    public string NoticeTypeText => Notice.NoticesType == NoticeType.FINE ? "ค่าเบี้ยปรับ" : "ค่าทวงถาม";

    public string NoticeDateText => ThaiFormat.DocumentDate(Notice.NoticeDate);

    public string FeeText => ThaiFormat.Money(Notice.FeeAmount);

    public string StatusText => Notice.Status switch
    {
        NoticeStatus.PAID => "ชำระแล้ว",
        NoticeStatus.CANCELLED => "ยกเลิกแล้ว",
        _ => "รอชำระ",
    };

    public bool IsPaid => Notice.Status == NoticeStatus.PAID;

    public bool IsCancelled => Notice.Status == NoticeStatus.CANCELLED;
}

/// <summary>
/// ใบทวงถาม — พอร์ตจาก CollectionNoticeContent.tsx
/// เลือกได้หลายแถวแล้วสั่งพิมพ์รวมเป็นไฟล์เดียว ตรงกับพฤติกรรมของเว็บเดิม
/// </summary>
public sealed partial class CollectionNoticePageViewModel(
    ApiClient api,
    AuthSession session,
    DialogService dialogs,
    DocumentService documents) : ListPageViewModel<CollectionNoticeRow>(api, session, dialogs)
{
    public override string Title => "ใบทวงถาม";

    /// <summary>ปี พ.ศ. ที่ใช้กรอง ค่าว่างแปลว่าไม่กรอง</summary>
    [ObservableProperty]
    private int? _filterYear;

    [ObservableProperty]
    private MonthOption? _filterMonth;

    public IReadOnlyList<int> AvailableYears { get; } =
        Enumerable.Range(ThaiFormat.CurrentBuddhistYear - 5, 7).Reverse().ToList();

    public IReadOnlyList<MonthOption> Months { get; } =
        Enumerable.Range(1, 12).Select(m => new MonthOption(m, ThaiFormat.FullMonths[m - 1])).ToList();

    public ObservableCollection<CollectionNoticeRow> SelectedRows { get; } = [];

    public bool HasSelection => Items.Any(r => r.IsSelected);

    partial void OnFilterYearChanged(int? value) => _ = ReloadFromFirstPageAsync();

    partial void OnFilterMonthChanged(MonthOption? value) => _ = ReloadFromFirstPageAsync();

    protected override async Task<PagedResult<CollectionNoticeRow>> FetchAsync(CancellationToken ct)
    {
        if (ProjectId is null) return PagedResult<CollectionNoticeRow>.Empty;

        var path = new QueryBuilder($"api/v1/projects/{ProjectId}/collections")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("house_no", Query)
            .Add("year", FilterYear)
            .Add("month", FilterMonth?.Value)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        var result = await Api.GetPagedAsync<CollectionNoticeResponse>(path, ct);

        var rows = result.Items.Select(n =>
        {
            var row = new CollectionNoticeRow(n);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CollectionNoticeRow.IsSelected)) OnPropertyChanged(nameof(HasSelection));
            };
            return row;
        }).ToList();

        return new PagedResult<CollectionNoticeRow>(rows, result.Page);
    }

    private Task ReloadFromFirstPageAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private Task PrintOneAsync(CollectionNoticeRow? row) =>
        row is null
            ? Task.CompletedTask
            : documents.OpenAsync($"api/v1/collections/{row.Id}/print", $"ใบทวงถาม-{row.HouseNo}");

    /// <summary>พิมพ์ทุกใบที่ติ๊กไว้รวมเป็นไฟล์เดียว</summary>
    [RelayCommand]
    private async Task PrintSelectedAsync()
    {
        var ids = Items.Where(r => r.IsSelected).Select(r => r.Id).ToList();

        if (ids.Count == 0)
        {
            await Dialogs.ErrorAsync("ยังไม่ได้เลือกใบทวงถาม", "กรุณาติ๊กเลือกอย่างน้อยหนึ่งรายการก่อนสั่งพิมพ์");
            return;
        }

        await documents.OpenAsync(
            "api/v1/collections/print",
            new PrintCollectionRequest { CollectionIds = ids },
            $"ใบทวงถามรวม-{ids.Count}ใบ");
    }

    [RelayCommand]
    private void SelectAll()
    {
        // ติ๊กทั้งหน้าในครั้งเดียว ถ้าติ๊กครบอยู่แล้วให้เอาออกทั้งหมด
        var selectAll = Items.Any(r => !r.IsSelected);

        foreach (var row in Items) row.IsSelected = selectAll;

        OnPropertyChanged(nameof(HasSelection));
    }
}
