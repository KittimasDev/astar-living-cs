using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Client.ViewModels.Dialogs;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>ตั้งค่าโครงการ — เฉพาะบัญชีระดับ Astar (พอร์ตจาก ProjectContent.tsx)</summary>
public sealed partial class ProjectSettingsPageViewModel(ApiClient api, AuthSession session, DialogService dialogs)
    : ListPageViewModel<ProjectResponse>(api, session, dialogs)
{
    public override string Title => "ตั้งค่าโครงการ";

    protected override Task<PagedResult<ProjectResponse>> FetchAsync(CancellationToken ct)
    {
        var path = new QueryBuilder("api/v1/project")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("name", Query)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        return Api.GetPagedAsync<ProjectResponse>(path, ct);
    }

    [RelayCommand]
    private Task CreateAsync() => OpenAsync(FormMode.Create, null);

    [RelayCommand]
    private Task EditAsync(ProjectResponse? row) => OpenAsync(FormMode.Edit, row);

    [RelayCommand]
    private Task ViewAsync(ProjectResponse? row) => OpenAsync(FormMode.View, row);

    private async Task OpenAsync(FormMode mode, ProjectResponse? row)
    {
        var dialog = new ProjectFormDialogViewModel(Api, Dialogs, mode, row);
        if (!await Dialogs.ShowAsync(dialog)) return;

        await Dialogs.SuccessAsync(mode == FormMode.Edit ? "แก้ไขโครงการสำเร็จ" : "เพิ่มโครงการสำเร็จ");
        await LoadAsync();
    }
}

/// <summary>ตั้งค่าวิธีการชำระเงิน — พอร์ตจาก PaymentMethodContent.tsx</summary>
public sealed partial class PaymentMethodSettingsPageViewModel(ApiClient api, AuthSession session, DialogService dialogs)
    : ListPageViewModel<PaymentMethodResponse>(api, session, dialogs)
{
    public override string Title => "ตั้งค่าวิธีการชำระเงิน";

    protected override Task<PagedResult<PaymentMethodResponse>> FetchAsync(CancellationToken ct)
    {
        var path = new QueryBuilder("api/v1/payment_method")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("name", Query)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        return Api.GetPagedAsync<PaymentMethodResponse>(path, ct);
    }

    [RelayCommand]
    private Task CreateAsync() => OpenAsync(FormMode.Create, null);

    [RelayCommand]
    private Task EditAsync(PaymentMethodResponse? row) => OpenAsync(FormMode.Edit, row);

    [RelayCommand]
    private Task ViewAsync(PaymentMethodResponse? row) => OpenAsync(FormMode.View, row);

    [RelayCommand]
    private async Task DeleteAsync(PaymentMethodResponse? row)
    {
        if (row is null) return;

        var confirmed = await Dialogs.ConfirmAsync(
            "ยืนยันการลบวิธีการชำระเงิน",
            $"ต้องการลบ \"{row.Name}\" ออกจากระบบหรือไม่",
            confirmText: "ลบ",
            isDestructive: true);

        if (!confirmed) return;

        if (await RunAsync(() => Api.DeleteAsync<object>($"api/v1/payment_method/{row.Id}")))
        {
            await Dialogs.SuccessAsync("ลบวิธีการชำระเงินสำเร็จ");
            await LoadAsync();
        }
    }

    private async Task OpenAsync(FormMode mode, PaymentMethodResponse? row)
    {
        var dialog = new PaymentMethodFormDialogViewModel(Api, Dialogs, mode, row);
        if (!await Dialogs.ShowAsync(dialog)) return;

        await Dialogs.SuccessAsync(mode == FormMode.Edit ? "แก้ไขวิธีการชำระเงินสำเร็จ" : "เพิ่มวิธีการชำระเงินสำเร็จ");
        await LoadAsync();
    }
}

/// <summary>
/// ตั้งค่าอัตราค่าบริการ — พอร์ตจาก ProjectRateContent.tsx
/// โครงการหนึ่งมีอัตราชุดเดียว ตารางจึงมีแถวเดียวเสมอ ตรงกับพฤติกรรมของเว็บเดิม
/// </summary>
public sealed partial class RateSettingsPageViewModel(ApiClient api, AuthSession session, DialogService dialogs)
    : ListPageViewModel<SettingResponse>(api, session, dialogs)
{
    public override string Title => "ตั้งค่าอัตราค่าบริการ";

    public bool HasSetting => Items.Count > 0;

    protected override async Task<PagedResult<SettingResponse>> FetchAsync(CancellationToken ct)
    {
        if (ProjectId is null) return PagedResult<SettingResponse>.Empty;

        var setting = await Api.GetOrDefaultAsync<SettingResponse>($"api/v1/projects/{ProjectId}/settings", ct);

        return setting is null
            ? PagedResult<SettingResponse>.Empty
            : new PagedResult<SettingResponse>([setting], null);
    }

    [RelayCommand]
    private Task CreateAsync() => OpenAsync(FormMode.Create, null);

    [RelayCommand]
    private Task EditAsync(SettingResponse? row) => OpenAsync(FormMode.Edit, row);

    [RelayCommand]
    private Task ViewAsync(SettingResponse? row) => OpenAsync(FormMode.View, row);

    private async Task OpenAsync(FormMode mode, SettingResponse? row)
    {
        if (ProjectId is null)
        {
            await Dialogs.ErrorAsync("บัญชีนี้ยังไม่ได้ผูกกับโครงการ");
            return;
        }

        var dialog = new RateFormDialogViewModel(Api, Dialogs, mode, ProjectId.Value, row);
        if (!await Dialogs.ShowAsync(dialog)) return;

        await Dialogs.SuccessAsync(mode == FormMode.Edit ? "แก้ไขอัตราค่าบริการสำเร็จ" : "เพิ่มอัตราค่าบริการสำเร็จ");
        await LoadAsync();
    }
}

/// <summary>เทมเพลตค่าทวงถาม — พอร์ตจาก CollectionNoticeContent.tsx (หน้าตั้งค่า)</summary>
public sealed partial class NoticeTemplateSettingsPageViewModel(ApiClient api, AuthSession session, DialogService dialogs)
    : ListPageViewModel<NoticeTemplateResponse>(api, session, dialogs)
{
    public override string Title => "เทมเพลตค่าทวงถาม";

    protected override Task<PagedResult<NoticeTemplateResponse>> FetchAsync(CancellationToken ct)
    {
        var path = new QueryBuilder("api/v1/notice_template")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("topic_name", Query)
            .Add("project_id", ProjectId)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        return Api.GetPagedAsync<NoticeTemplateResponse>(path, ct);
    }

    [RelayCommand]
    private Task CreateAsync() => OpenAsync(FormMode.Create, null);

    [RelayCommand]
    private Task EditAsync(NoticeTemplateResponse? row) => OpenAsync(FormMode.Edit, row);

    [RelayCommand]
    private Task ViewAsync(NoticeTemplateResponse? row) => OpenAsync(FormMode.View, row);

    private async Task OpenAsync(FormMode mode, NoticeTemplateResponse? row)
    {
        if (ProjectId is null)
        {
            await Dialogs.ErrorAsync("บัญชีนี้ยังไม่ได้ผูกกับโครงการ");
            return;
        }

        var template = row;

        if (mode != FormMode.Create && row is not null)
        {
            template = await Api.GetOrDefaultAsync<NoticeTemplateResponse>($"api/v1/notice_template/{row.Id}") ?? row;
        }

        var dialog = new NoticeTemplateFormDialogViewModel(Api, Dialogs, mode, ProjectId.Value, template);
        if (!await Dialogs.ShowAsync(dialog)) return;

        await Dialogs.SuccessAsync(mode == FormMode.Edit ? "แก้ไขเทมเพลตสำเร็จ" : "เพิ่มเทมเพลตสำเร็จ");
        await LoadAsync();
    }
}

/// <summary>ผู้ใช้งาน — พอร์ตจาก UserContent.tsx</summary>
public sealed partial class UserSettingsPageViewModel(ApiClient api, AuthSession session, DialogService dialogs)
    : ListPageViewModel<UserResponse>(api, session, dialogs)
{
    /// <summary>
    /// ชื่อสิทธิ์ภาษาไทยชุดเดียวกับที่แสดงบนหัวหน้าจอ
    /// SUPERADMIN ตั้งให้คนอื่นได้เฉพาะบัญชีระดับ Astar ด้วยกัน
    /// </summary>
    private static readonly RoleOption[] AllRoles =
    [
        new("SUPERADMIN", "Astar"),
        new("ADMIN", "ผู้จัดการโครงการ"),
        new("MANAGER", "หัวหน้านิติ"),
        new("USER", "นิติ"),
    ];

    public override string Title => "ผู้ใช้งาน";

    /// <summary>สิทธิ์ที่ผู้ใช้คนปัจจุบันมอบให้คนอื่นได้</summary>
    public IReadOnlyList<RoleOption> AssignableRoles =>
        Session.Role == AppMenu.SuperAdmin
            ? AllRoles
            : AllRoles.Where(r => r.Value != "SUPERADMIN").ToList();

    protected override Task<PagedResult<UserResponse>> FetchAsync(CancellationToken ct)
    {
        var path = new QueryBuilder("api/v1/user")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("full_name", Query)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        return Api.GetPagedAsync<UserResponse>(path, ct);
    }

    [RelayCommand]
    private Task CreateAsync() => OpenAsync(FormMode.Create, null);

    [RelayCommand]
    private Task EditAsync(UserResponse? row) => OpenAsync(FormMode.Edit, row);

    [RelayCommand]
    private Task ViewAsync(UserResponse? row) => OpenAsync(FormMode.View, row);

    [RelayCommand]
    private async Task ResetPasswordAsync(UserResponse? row)
    {
        if (row is null) return;

        var confirmed = await Dialogs.ConfirmAsync(
            "ยืนยันการรีเซ็ตรหัสผ่าน",
            $"ระบบจะตั้งรหัสผ่านใหม่ให้ \"{row.Username}\" และแสดงให้ดูครั้งเดียว");

        if (!confirmed) return;

        await RunAsync(async () =>
        {
            var result = await Api.PostAsync<MessageResponse>($"api/v1/auth/reset-password/{row.Id}");
            await Dialogs.SuccessAsync(result.Message);
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(UserResponse? row)
    {
        if (row is null) return;

        var confirmed = await Dialogs.ConfirmAsync(
            "ยืนยันการลบผู้ใช้งาน",
            $"ต้องการลบบัญชี \"{row.Username}\" ออกจากระบบหรือไม่",
            confirmText: "ลบ",
            isDestructive: true);

        if (!confirmed) return;

        if (await RunAsync(() => Api.DeleteAsync<object>($"api/v1/user/{row.Id}")))
        {
            await Dialogs.SuccessAsync("ลบผู้ใช้งานสำเร็จ");
            await LoadAsync();
        }
    }

    /// <summary>โครงการทั้งหมดที่ผู้ใช้คนนี้มองเห็น โหลดครั้งเดียวแล้วใช้ซ้ำ</summary>
    private IReadOnlyList<ProjectOption>? _projects;

    private async Task<IReadOnlyList<ProjectOption>> LoadProjectsAsync()
    {
        if (_projects is not null) return _projects;

        var path = new QueryBuilder("api/v1/project").Add("no_limit", true);
        var result = await Api.GetPagedAsync<ProjectResponse>(path);

        _projects = result.Items.Select(p => new ProjectOption(p.Id, p.Name)).ToList();

        return _projects;
    }

    private async Task OpenAsync(FormMode mode, UserResponse? row)
    {
        var projects = await LoadProjectsAsync();
        var dialog = new UserFormDialogViewModel(Api, Dialogs, mode, AssignableRoles, projects, ProjectId, row);
        if (!await Dialogs.ShowAsync(dialog)) return;

        await Dialogs.SuccessAsync(mode == FormMode.Edit ? "แก้ไขผู้ใช้งานสำเร็จ" : "เพิ่มผู้ใช้งานสำเร็จ");
        await LoadAsync();
    }
}

/// <summary>
/// บันทึกการทำงานของระบบ — หน้าจอของเว็บเดิมเป็นข้อมูลปลอม
/// ระบบใหม่อ่านจากตาราง gv_activity_logs จริง
/// </summary>
public sealed class SystemLogsPageViewModel(ApiClient api, AuthSession session, DialogService dialogs)
    : ListPageViewModel<ActivityLogResponse>(api, session, dialogs)
{
    public override string Title => "บันทึกการทำงานของระบบ";

    protected override Task<PagedResult<ActivityLogResponse>> FetchAsync(CancellationToken ct)
    {
        var path = new QueryBuilder("api/v1/logs")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("action", Query)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        return Api.GetPagedAsync<ActivityLogResponse>(path, ct);
    }
}
