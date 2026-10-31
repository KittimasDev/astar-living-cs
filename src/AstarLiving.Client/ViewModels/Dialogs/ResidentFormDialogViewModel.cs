using AstarLiving.Client.Common.Dialogs;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Dialogs;

/// <summary>โหมดของฟอร์มที่ใช้ซ้ำทุกหน้าจอ</summary>
public enum FormMode
{
    Create,
    Edit,
    View,
}

/// <summary>
/// ฟอร์มลูกบ้าน — พอร์ตจาก ResidentFormModal.tsx
/// ใช้กล่องเดียวทั้งสามโหมด (เพิ่ม / แก้ไข / ดูรายละเอียด) เหมือนเว็บเดิม
/// </summary>
public sealed partial class ResidentFormDialogViewModel : DialogViewModel<bool>
{
    private readonly ApiClient _api;
    private readonly DialogService _dialogs;
    private readonly int _projectId;
    private readonly int? _residentId;

    public ResidentFormDialogViewModel(
        ApiClient api,
        DialogService dialogs,
        FormMode mode,
        int projectId,
        ResidentResponse? resident = null)
    {
        _api = api;
        _dialogs = dialogs;
        _projectId = projectId;
        _residentId = resident?.Id;

        Mode = mode;

        if (resident is not null)
        {
            HouseNo = resident.HouseNo;
            FullName = resident.FullName;
            OwnershipRatio = resident.OwnershipRatio?.ToString("0.##") ?? string.Empty;
            Phone = resident.Phone ?? string.Empty;
            Email = resident.Email ?? string.Empty;
            Address = resident.Address ?? string.Empty;
        }
    }

    public FormMode Mode { get; }

    public override double DialogWidth => 720;

    public bool IsReadOnly => Mode == FormMode.View;

    public bool CanEdit => Mode != FormMode.View;

    public string Title => Mode switch
    {
        FormMode.Create => "เพิ่มลูกบ้าน",
        FormMode.Edit => "แก้ไขลูกบ้าน",
        _ => "รายละเอียดลูกบ้าน",
    };

    public string SubmitText => Mode == FormMode.Edit ? "ยืนยัน" : "เพิ่ม";

    [ObservableProperty]
    private string _houseNo = string.Empty;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _ownershipRatio = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string? _houseNoError;

    [ObservableProperty]
    private string? _fullNameError;

    [ObservableProperty]
    private string? _ownershipRatioError;

    partial void OnHouseNoChanged(string value) => HouseNoError = null;

    partial void OnFullNameChanged(string value) => FullNameError = null;

    partial void OnOwnershipRatioChanged(string value) => OwnershipRatioError = null;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!Validate()) return;

        var ratio = ParseRatio(OwnershipRatio);

        var ok = await RunAsync(async () =>
        {
            if (Mode == FormMode.Create)
            {
                await _api.PostAsync<ResidentResponse>("api/v1/residents", new CreateResidentRequest
                {
                    ProjectId = _projectId,
                    HouseNo = HouseNo.Trim(),
                    FullName = FullName.Trim(),
                    OwnershipRatio = ratio,
                    Phone = Clean(Phone),
                    Email = Clean(Email),
                    Address = Clean(Address),
                    Active = true,
                });
            }
            else
            {
                await _api.PatchAsync<ResidentResponse>($"api/v1/residents/{_residentId}", new UpdateResidentRequest
                {
                    ProjectId = _projectId,
                    HouseNo = HouseNo.Trim(),
                    FullName = FullName.Trim(),
                    OwnershipRatio = ratio,
                    Phone = Clean(Phone),
                    Email = Clean(Email),
                    Address = Clean(Address),
                });
            }
        });

        if (ok) Close(true);
    }

    /// <summary>
    /// ปิดฟอร์ม โหมดดูอย่างเดียวปิดได้เลย โหมดที่แก้ไขได้ต้องยืนยันก่อน
    /// เพราะผู้ใช้อาจกรอกค้างไว้ ตรงกับพฤติกรรมของเว็บเดิม
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (IsReadOnly)
        {
            Close(false);
            return;
        }

        if (await _dialogs.ConfirmExitAsync()) Close(false);
    }

    private bool Validate()
    {
        ClearError();
        HouseNoError = null;
        FullNameError = null;
        OwnershipRatioError = null;

        if (string.IsNullOrWhiteSpace(HouseNo)) HouseNoError = "กรุณากรอกบ้านเลขที่";
        if (string.IsNullOrWhiteSpace(FullName)) FullNameError = "กรุณากรอกชื่อ - นามสกุล";

        if (string.IsNullOrWhiteSpace(OwnershipRatio))
        {
            OwnershipRatioError = "กรุณากรอกอัตราส่วนกรรมสิทธิ์";
        }
        else if (ParseRatio(OwnershipRatio) is null)
        {
            OwnershipRatioError = "อัตราส่วนกรรมสิทธิ์ต้องเป็นตัวเลข";
        }

        return HouseNoError is null && FullNameError is null && OwnershipRatioError is null;
    }

    /// <summary>
    /// อัตราส่วนกรรมสิทธิ์เป็นตัวคูณค่าส่วนกลางโดยตรง ผิดหนึ่งหลักคือบิลผิดทั้งโครงการ
    /// จึงต้องแปลงแบบไม่ขึ้นกับภาษาของเครื่อง และตัดจุลภาคคั่นหลักพันที่ผู้ใช้อาจพิมพ์ติดมา
    /// </summary>
    private static decimal? ParseRatio(string text)
    {
        var cleaned = text.Replace(",", string.Empty, StringComparison.Ordinal).Trim();

        return decimal.TryParse(
            cleaned,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
