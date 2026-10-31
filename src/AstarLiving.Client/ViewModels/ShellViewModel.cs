using System.Collections.ObjectModel;
using System.Reflection;
using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Client.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels;

/// <summary>
/// หน้าต่างหลัก — แถบเมนูซ้าย 280 หัวหน้าจอ พื้นที่เนื้อหา และแถบท้าย
/// พอร์ตโครงมาจาก _app.tsx + Navbar.tsx + Header.tsx + Footer.tsx ของเว็บเดิม
/// </summary>
public sealed partial class ShellViewModel : ObservableObjectBase
{
    private readonly NavigationService _navigation;
    private readonly AuthApi _auth;
    private readonly AuthSession _session;

    private readonly UpdateService _updates;

    public ShellViewModel(
        NavigationService navigation,
        AuthApi auth,
        AuthSession session,
        DialogService dialogs,
        UpdateService updates)
    {
        _navigation = navigation;
        _auth = auth;
        _session = session;
        _updates = updates;
        Dialogs = dialogs;

        _updates.StatusChanged += (_, _) => OnPropertyChanged(nameof(UpdateStatusText));

        _navigation.Navigated += OnNavigated;
        _session.UserChanged += (_, _) => RefreshUserInfo();

        RefreshUserInfo();
    }

    /// <summary>หน้าต่างซ้อนที่เปิดอยู่ หน้าต่างหลักเป็นผู้วาดให้</summary>
    public DialogService Dialogs { get; }

    public ObservableCollection<NavigationNode> MenuItems { get; } = [];

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _userDisplayName = "-";

    [ObservableProperty]
    private string _userRoleName = string.Empty;

    [ObservableProperty]
    private bool _isUserMenuOpen;

    [ObservableProperty]
    private bool _isSigningOut;

    /// <summary>ข้อความลิขสิทธิ์ท้ายหน้าจอ รวมเลขเวอร์ชันของโปรแกรม</summary>
    public string FooterText { get; } =
        $"Copyright © A STAR TECHNOLOGY CO., LTD. (v.{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"})";

    /// <summary>ยิงเมื่อออกจากระบบแล้ว ให้โปรแกรมพากลับไปหน้าเข้าสู่ระบบ</summary>
    public event EventHandler? SignedOut;

    /// <summary>ข้อความสถานะการอัปเดตบนแถบท้าย</summary>
    public string UpdateStatusText => _updates.StatusText;

    /// <summary>สร้างเมนูตามสิทธิ์แล้วเปิดหน้าแรก</summary>
    public async Task InitializeAsync()
    {
        var role = _session.Role;

        MenuItems.Clear();
        foreach (var node in AppMenu.Build(role)) MenuItems.Add(node);

        await _navigation.NavigateAsync(AppMenu.LandingPage(role));

        // ตรวจอัปเดตเงียบ ๆ หลังหน้าแรกขึ้นแล้ว
        // หน่วงไว้ก่อนเพื่อไม่ให้แย่งแบนด์วิดท์กับการโหลดข้อมูลของหน้าแรก
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            await _updates.CheckInBackgroundAsync();
        });
    }

    /// <summary>
    /// ปุ่มตรวจสอบการอัปเดตบนแถบท้าย
    /// ต้องตอบผู้ใช้เสมอแม้เป็นเวอร์ชันล่าสุดอยู่แล้ว ไม่อย่างนั้นจะดูเหมือนกดแล้วไม่มีอะไรเกิดขึ้น
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var dialog = new UpdateDialogViewModel(_updates);
        _ = dialog.InitializeAsync();

        await Dialogs.ShowAsync(dialog);
    }

    /// <summary>
    /// เครื่องแม่บอกว่าโปรแกรมเวอร์ชันนี้ใช้ต่อไม่ได้แล้ว
    /// กล่องนี้ปิดไม่ได้ เพราะถ้าปล่อยให้ใช้ต่อจะได้ข้อมูลผิดรูปแบบซึ่งอันตรายกว่า
    /// </summary>
    public async Task ShowForcedUpdateAsync(string message)
    {
        if (Dialogs.Stack.Any(d => d is UpdateDialogViewModel)) return;

        var dialog = new UpdateDialogViewModel(_updates, forced: true, forcedMessage: message);
        _ = dialog.InitializeAsync();

        await Dialogs.ShowAsync(dialog);
    }

    [RelayCommand]
    private async Task SelectAsync(NavigationNode? node)
    {
        if (node is null) return;

        // รายการที่มีลูกทำหน้าที่กางหรือหุบเท่านั้น ไม่ได้เปิดหน้าเอง
        if (node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        if (node.PageType is null) return;

        await _navigation.NavigateAsync(node.PageType);
    }

    [RelayCommand]
    private void ToggleUserMenu() => IsUserMenuOpen = !IsUserMenuOpen;

    [RelayCommand]
    private async Task SignOutAsync()
    {
        IsUserMenuOpen = false;
        IsSigningOut = true;

        try
        {
            await _auth.LogoutAsync();
        }
        finally
        {
            IsSigningOut = false;
            SignedOut?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnNavigated(object? sender, PageViewModel page)
    {
        CurrentPage = page;
        PageTitle = page.Title;
        MarkSelected(page.GetType());
    }

    /// <summary>ไฮไลต์รายการเมนูที่ตรงกับหน้าปัจจุบัน และกางหัวข้อแม่ของมันให้ด้วย</summary>
    private void MarkSelected(Type pageType)
    {
        foreach (var node in MenuItems)
        {
            var isMatch = node.PageType == pageType;
            node.IsSelected = isMatch;

            var childMatched = false;
            foreach (var child in node.Children)
            {
                child.IsSelected = child.PageType == pageType;
                childMatched |= child.IsSelected;
            }

            if (childMatched) node.IsExpanded = true;
        }
    }

    private void RefreshUserInfo()
    {
        UserDisplayName = _session.DisplayName;
        UserRoleName = _session.RoleDisplayName;
    }
}
