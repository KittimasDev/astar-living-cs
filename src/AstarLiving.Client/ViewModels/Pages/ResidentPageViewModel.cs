using AstarLiving.Client.Common;
using AstarLiving.Client.Services;
using AstarLiving.Client.ViewModels.Dialogs;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Pages;

/// <summary>
/// ข้อมูลลูกบ้าน / ผู้อยู่อาศัย — พอร์ตจาก ResidentContent.tsx
/// ค้นหาด้วยชื่อ เรียงได้ทุกคอลัมน์ และเปิดฟอร์มเดียวกันทั้งเพิ่ม แก้ไข และดูรายละเอียด
/// </summary>
public sealed partial class ResidentPageViewModel(ApiClient api, AuthSession session, DialogService dialogs)
    : ListPageViewModel<ResidentResponse>(api, session, dialogs)
{
    public override string Title => "ข้อมูลลูกบ้าน / ผู้อยู่อาศัย";

    protected override async Task<PagedResult<ResidentResponse>> FetchAsync(CancellationToken ct)
    {
        if (ProjectId is null) return PagedResult<ResidentResponse>.Empty;

        var path = new QueryBuilder($"api/v1/projects/{ProjectId}/residents")
            .Add("page", Page)
            .Add("limit", PageSize)
            .Add("full_name", Query)
            .Add("order_field", OrderField)
            .Add("order_type", OrderType);

        return await Api.GetPagedAsync<ResidentResponse>(path, ct);
    }

    [RelayCommand]
    private Task CreateAsync() => OpenFormAsync(FormMode.Create, null);

    [RelayCommand]
    private Task EditAsync(ResidentResponse? row) => OpenFormAsync(FormMode.Edit, row);

    [RelayCommand]
    private Task ViewAsync(ResidentResponse? row) => OpenFormAsync(FormMode.View, row);

    private async Task OpenFormAsync(FormMode mode, ResidentResponse? row)
    {
        if (ProjectId is null)
        {
            await Dialogs.ErrorAsync("บัญชีนี้ยังไม่ได้ผูกกับโครงการ", "กรุณาติดต่อผู้ดูแลระบบให้กำหนดโครงการให้ก่อน");
            return;
        }

        var resident = row;

        // แถวในตารางมีไม่ครบทุกฟิลด์ที่ฟอร์มต้องใช้ จึงดึงรายตัวก่อนเปิด เหมือน getOne ของเว็บเดิม
        if (mode != FormMode.Create && row is not null)
        {
            try
            {
                resident = await Api.GetAsync<ResidentResponse>($"api/v1/residents/{row.Id}");
            }
            catch (ApiException ex)
            {
                await Dialogs.ErrorAsync("ไม่สามารถโหลดข้อมูลลูกบ้านได้", ex.Message);
                return;
            }
        }

        var dialog = new ResidentFormDialogViewModel(Api, Dialogs, mode, ProjectId.Value, resident);

        if (!await Dialogs.ShowAsync(dialog)) return;

        await Dialogs.SuccessAsync(mode == FormMode.Edit ? "แก้ไขลูกบ้านสำเร็จ" : "เพิ่มลูกบ้านสำเร็จ");
        await LoadAsync();
    }
}
