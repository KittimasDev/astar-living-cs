using System.Collections.ObjectModel;
using AstarLiving.Client.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.Common;

/// <summary>ค่าคงที่ของหน้าจอแบบรายการที่โค้ดนอกคลาส generic ต้องใช้ด้วย</summary>
public static class ListPage
{
    /// <summary>จำนวนแถวต่อหน้า — ค่าเดียวกับเว็บเดิมทุกหน้า</summary>
    public const int PageSize = 10;
}

/// <summary>
/// ฐานของหน้าจอแบบรายการทุกหน้า
///
/// เว็บเดิมคัดลอกตรรกะชุดนี้ซ้ำในทุกหน้า (ค้นหาแบบหน่วงเวลา แบ่งหน้า เรียงลำดับ)
/// ที่นี่รวมไว้ที่เดียว หน้าจอแต่ละหน้าเหลือแค่บอกว่าจะดึงข้อมูลอย่างไร
/// </summary>
public abstract partial class ListPageViewModel<TItem> : PageViewModel
{
    /// <summary>จำนวนแถวต่อหน้า</summary>
    public const int PageSize = ListPage.PageSize;

    private CancellationTokenSource? _searchDebounce;

    protected ListPageViewModel(ApiClient api, AuthSession session, DialogService dialogs)
    {
        Api = api;
        Session = session;
        Dialogs = dialogs;
    }

    protected ApiClient Api { get; }

    protected AuthSession Session { get; }

    protected DialogService Dialogs { get; }

    public ObservableCollection<TItem> Items { get; } = [];

    /// <summary>เลขหน้าที่จะให้กดได้ คำนวณใหม่ทุกครั้งที่โหลด</summary>
    public ObservableCollection<int> PageNumbers { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _page = 1;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private int _totalItems;

    /// <summary>คอลัมน์ที่ใช้เรียง ต้องเป็นชื่อที่ฝั่ง API ยอมรับตาม whitelist เดิม</summary>
    [ObservableProperty]
    private string? _orderField;

    /// <summary>"ASC" หรือ "DESC"</summary>
    [ObservableProperty]
    private string? _orderType;

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public bool IsEmpty => Items.Count == 0 && !IsBusy;

    /// <summary>ข้อความค้นหาที่ส่งไป API — ตัดช่องว่างหัวท้ายและคืน null เมื่อว่าง</summary>
    protected string? Query => string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

    /// <summary>โครงการของผู้ใช้ที่ล็อกอินอยู่ หน้ารายการเกือบทั้งหมดต้องใช้</summary>
    protected int? ProjectId => Session.ProjectId;

    public override Task OnNavigatedToAsync() => LoadAsync();

    /// <summary>ดึงข้อมูลหนึ่งหน้า — หน้าจอแต่ละหน้าประกอบ query เอง</summary>
    protected abstract Task<PagedResult<TItem>> FetchAsync(CancellationToken ct);

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var result = await FetchAsync(CancellationToken.None);
            Apply(result);
        });

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// พิมพ์ค้นหาแล้วหน่วง 300 มิลลิวินาทีก่อนยิง เหมือน debounce ของเว็บเดิม
    /// ไม่อย่างนั้นการพิมพ์ชื่อหนึ่งชื่อจะยิงคำขอเป็นสิบครั้ง
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();

        var token = _searchDebounce.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                Page = 1;
                await LoadAsync();
            });
        }, token);
    }

    [RelayCommand]
    private Task GoToPageAsync(int page)
    {
        if (page < 1 || page > Math.Max(TotalPages, 1) || page == Page) return Task.CompletedTask;

        Page = page;
        return LoadAsync();
    }

    [RelayCommand]
    private Task PreviousPageAsync() => HasPreviousPage ? GoToPageAsync(Page - 1) : Task.CompletedTask;

    [RelayCommand]
    private Task NextPageAsync() => HasNextPage ? GoToPageAsync(Page + 1) : Task.CompletedTask;

    /// <summary>สลับทิศทางการเรียงของคอลัมน์ ไม่เรียง → ASC → DESC → ไม่เรียง</summary>
    [RelayCommand]
    private Task SortByAsync(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return Task.CompletedTask;

        if (OrderField != field)
        {
            OrderField = field;
            OrderType = "ASC";
        }
        else
        {
            (OrderField, OrderType) = OrderType switch
            {
                "ASC" => (field, "DESC"),
                _ => (null, null),
            };
        }

        Page = 1;
        return LoadAsync();
    }

    protected void Apply(PagedResult<TItem> result)
    {
        Items.Clear();
        foreach (var item in result.Items) Items.Add(item);

        TotalItems = result.Total;
        TotalPages = result.TotalPage;

        RebuildPageNumbers();

        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// แสดงเลขหน้าแบบหน้าต่างเลื่อน ครั้งละไม่เกิน 7 ปุ่ม
    /// โครงการที่มีลูกบ้านหลายพันหลังจะมีหลายร้อยหน้า ถ้าแสดงหมดแถบจะยาวเกินจอ
    /// </summary>
    private void RebuildPageNumbers()
    {
        const int window = 7;

        PageNumbers.Clear();
        if (TotalPages <= 0) return;

        var first = Math.Max(1, Page - (window / 2));
        var last = Math.Min(TotalPages, first + window - 1);

        first = Math.Max(1, last - window + 1);

        for (var i = first; i <= last; i++) PageNumbers.Add(i);
    }

    /// <summary>เรียกหลังเพิ่มหรือแก้ไขข้อมูล เพื่อให้ตารางสดโดยไม่เด้งกลับหน้าแรก</summary>
    protected Task ReloadKeepingPageAsync() => LoadAsync();
}
