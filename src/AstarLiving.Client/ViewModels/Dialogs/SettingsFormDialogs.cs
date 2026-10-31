using System.Globalization;
using System.IO;
using AstarLiving.Client.Common.Dialogs;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.ViewModels.Dialogs;

/// <summary>
/// ฐานของฟอร์มในหน้าตั้งค่า
/// รวมโหมดสามแบบ การยืนยันตอนปิด และการแปลงตัวเลขไว้ที่เดียว
/// </summary>
public abstract partial class SettingsFormDialogBase : DialogViewModel<bool>
{
    protected SettingsFormDialogBase(ApiClient api, DialogService dialogs, FormMode mode)
    {
        Api = api;
        Dialogs = dialogs;
        Mode = mode;
    }

    protected ApiClient Api { get; }

    protected DialogService Dialogs { get; }

    public FormMode Mode { get; }

    public bool CanEdit => Mode != FormMode.View;

    public string SubmitText => Mode == FormMode.Edit ? "ยืนยัน" : "เพิ่ม";

    public abstract string Title { get; }

    [ObservableProperty]
    private bool _active = true;

    protected abstract bool Validate();

    protected abstract Task SaveAsync();

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!Validate()) return;

        if (await RunAsync(SaveAsync)) Close(true);
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (!CanEdit)
        {
            Close(false);
            return;
        }

        if (await Dialogs.ConfirmExitAsync()) Close(false);
    }

    /// <summary>
    /// แปลงตัวเลขที่ผู้ใช้พิมพ์ ยอมรับจุลภาคคั่นหลักพันและไม่ขึ้นกับภาษาของเครื่อง
    /// ตัวเลขพวกนี้เป็นฐานของการคิดเงินทั้งโครงการ แปลงผิดหนึ่งหลักคือบิลผิดทุกใบ
    /// </summary>
    protected static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = text.Replace(",", string.Empty, StringComparison.Ordinal).Trim();

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    protected static string Format(decimal? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>ฟอร์มโครงการ — พอร์ตจาก ProjectFormModal.tsx</summary>
public sealed partial class ProjectFormDialogViewModel : SettingsFormDialogBase
{
    private readonly int? _projectId;

    public ProjectFormDialogViewModel(ApiClient api, DialogService dialogs, FormMode mode, ProjectResponse? project = null)
        : base(api, dialogs, mode)
    {
        _projectId = project?.Id;

        if (project is null) return;

        Name = project.Name;
        Address = project.Address ?? string.Empty;
        ContactName = project.ContactName ?? string.Empty;
        ContactPhone = project.ContactPhone ?? string.Empty;
        ContactFaxPhone = project.ContactFaxPhone ?? string.Empty;
        Active = project.Active;
        LogoUrl = project.ProjectLogo;
    }

    /// <summary>ชนิดไฟล์ที่ยอมรับ ตรงกับที่ระบบเดิมเขียนบอกผู้ใช้ไว้</summary>
    private const string LogoFilter = "ไฟล์รูปภาพ (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png";

    /// <summary>ขีดจำกัดขนาดไฟล์ 5 MB เท่าระบบเดิม</summary>
    private const long MaxLogoBytes = 5 * 1024 * 1024;

    public override double DialogWidth => 720;

    public override string Title => Mode switch
    {
        FormMode.Create => "เพิ่มโครงการ",
        FormMode.Edit => "แก้ไขโครงการ",
        _ => "รายละเอียดโครงการ",
    };

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _contactName = string.Empty;

    [ObservableProperty]
    private string _contactPhone = string.Empty;

    [ObservableProperty]
    private string _contactFaxPhone = string.Empty;

    [ObservableProperty]
    private string? _nameError;

    /// <summary>ที่อยู่รูปโลโก้ที่เครื่องแม่เก็บไว้ ใช้แสดงรูปเดิมตอนเปิดฟอร์ม</summary>
    [ObservableProperty]
    private string? _logoUrl;

    /// <summary>ไฟล์ที่ผู้ใช้เพิ่งเลือก ยังไม่ได้ส่งขึ้นเครื่องแม่จนกว่าจะกดบันทึก</summary>
    [ObservableProperty]
    private string? _selectedLogoPath;

    [ObservableProperty]
    private string? _logoError;

    /// <summary>
    /// รูปที่แสดงในกรอบ เลือกไฟล์ใหม่แล้วให้ขึ้นรูปใหม่ทันที ยังไม่เลือกก็ขึ้นรูปเดิมจากเครื่องแม่
    ///
    /// เครื่องแม่คืนที่อยู่รูปแบบสัมพัทธ์ (/api/images/...) เพราะมันไม่รู้ว่า
    /// ถูกเรียกด้วยไอพีไหนในเครือข่ายของลูกค้า ฝั่งนี้จึงต้องเติมที่อยู่เครื่องแม่ให้เอง
    /// </summary>
    public string? LogoPreview => SelectedLogoPath ?? ToAbsoluteUrl(LogoUrl);

    private string? ToAbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return absolute.ToString();

        var baseAddress = Api.Http.BaseAddress;
        if (baseAddress is null) return null;

        return Uri.TryCreate(baseAddress, url, out var combined) ? combined.ToString() : null;
    }

    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoPreview);

    partial void OnNameChanged(string value) => NameError = null;

    partial void OnSelectedLogoPathChanged(string? value) => NotifyLogoChanged();

    partial void OnLogoUrlChanged(string? value) => NotifyLogoChanged();

    private void NotifyLogoChanged()
    {
        OnPropertyChanged(nameof(LogoPreview));
        OnPropertyChanged(nameof(HasLogo));
    }

    /// <summary>
    /// เลือกไฟล์รูปจากเครื่อง
    ///
    /// ตรวจชนิดและขนาดตั้งแต่ตอนเลือก ไม่ปล่อยให้ไปรู้ตอนกดบันทึกแล้วเสียเวลาเปล่า
    /// ข้อความเตือนยกมาจากระบบเดิมคำต่อคำ ผู้ใช้เดิมจะได้ไม่ต้องเรียนรู้ใหม่
    /// </summary>
    [RelayCommand]
    private void PickLogo()
    {
        LogoError = null;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "เลือกรูปโลโก้โครงการ",
            Filter = LogoFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var info = new FileInfo(dialog.FileName);

        if (info.Length > MaxLogoBytes)
        {
            LogoError = "ไฟล์มีขนาดใหญ่เกินไป กรุณาลองอีกครั้ง";
            return;
        }

        if (!IsImageExtension(info.Extension))
        {
            LogoError = "กรุณาเลือกเฉพาะไฟล์รูปภาพ (JPG, PNG)";
            return;
        }

        SelectedLogoPath = dialog.FileName;
    }

    /// <summary>ยกเลิกรูปที่เพิ่งเลือก กลับไปใช้รูปเดิมที่เครื่องแม่เก็บไว้</summary>
    [RelayCommand]
    private void ClearLogo()
    {
        SelectedLogoPath = null;
        LogoError = null;
    }

    private static bool IsImageExtension(string extension) =>
        extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    protected override bool Validate()
    {
        ClearError();
        NameError = string.IsNullOrWhiteSpace(Name) ? "กรุณากรอกชื่อโครงการ" : null;

        return NameError is null;
    }

    protected override async Task SaveAsync()
    {
        int projectId;

        if (Mode == FormMode.Create)
        {
            // ต้องสร้างโครงการก่อนจึงจะรู้รหัสที่ใช้แนบรูปได้
            var created = await Api.PostAsync<ProjectResponse>("api/v1/project", new CreateProjectRequest
            {
                Name = Name.Trim(),
                Address = Clean(Address),
                ContactName = Clean(ContactName),
                ContactPhone = Clean(ContactPhone),
                ContactFaxPhone = Clean(ContactFaxPhone),
                Active = Active,
            });

            projectId = created.Id;
        }
        else
        {
            await Api.PatchAsync<ProjectResponse>($"api/v1/project/{_projectId}", new UpdateProjectRequest
            {
                Name = Name.Trim(),
                Address = Clean(Address),
                ContactName = Clean(ContactName),
                ContactPhone = Clean(ContactPhone),
                ContactFaxPhone = Clean(ContactFaxPhone),
                Active = Active,
            });

            projectId = _projectId!.Value;
        }

        await UploadLogoIfPickedAsync(projectId);
    }

    /// <summary>
    /// ส่งรูปขึ้นเครื่องแม่หลังบันทึกข้อมูลโครงการแล้ว
    ///
    /// แยกเป็นคนละคำขอเพราะเครื่องแม่รับรูปเป็น multipart ต่างหาก
    /// รูปส่งไม่สำเร็จไม่ควรทำให้ข้อมูลที่บันทึกไปแล้วดูเหมือนล้มเหลวทั้งหมด
    /// จึงรายงานเฉพาะส่วนของรูป แล้วให้ผู้ใช้ลองใหม่ได้
    /// </summary>
    private async Task UploadLogoIfPickedAsync(int projectId)
    {
        if (string.IsNullOrWhiteSpace(SelectedLogoPath)) return;

        try
        {
            var result = await Api.UploadAsync<ProjectLogoResponse>(
                $"api/v1/project/{projectId}/upload", SelectedLogoPath);

            LogoUrl = result.ProjectLogo;
            SelectedLogoPath = null;
        }
        catch (ApiException ex)
        {
            throw new ApiException(
                $"บันทึกข้อมูลโครงการแล้ว แต่อัปโหลดรูปไม่สำเร็จ\n{ex.Message}", ex.Status);
        }
    }
}

/// <summary>ฟอร์มวิธีการชำระเงิน — พอร์ตจาก PaymentMethodFormModal.tsx</summary>
public sealed partial class PaymentMethodFormDialogViewModel : SettingsFormDialogBase
{
    private readonly int? _methodId;

    public PaymentMethodFormDialogViewModel(
        ApiClient api, DialogService dialogs, FormMode mode, PaymentMethodResponse? method = null)
        : base(api, dialogs, mode)
    {
        _methodId = method?.Id;

        if (method is null) return;

        Code = method.Code;
        Name = method.Name;
        Description = method.Description ?? string.Empty;
        Active = method.Active;
    }

    public override double DialogWidth => 640;

    public override string Title => Mode switch
    {
        FormMode.Create => "เพิ่มวิธีการชำระเงิน",
        FormMode.Edit => "แก้ไขวิธีการชำระเงิน",
        _ => "รายละเอียดวิธีการชำระเงิน",
    };

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _codeError;

    [ObservableProperty]
    private string? _nameError;

    partial void OnCodeChanged(string value) => CodeError = null;

    partial void OnNameChanged(string value) => NameError = null;

    protected override bool Validate()
    {
        ClearError();
        CodeError = string.IsNullOrWhiteSpace(Code) ? "กรุณากรอกรหัสวิธีการชำระเงิน" : null;
        NameError = string.IsNullOrWhiteSpace(Name) ? "กรุณากรอกชื่อวิธีการชำระเงิน" : null;

        return CodeError is null && NameError is null;
    }

    protected override async Task SaveAsync()
    {
        var request = new UpsertPaymentMethodRequest
        {
            Code = Code.Trim(),
            Name = Name.Trim(),
            Description = Clean(Description),
            Active = Active,
        };

        if (Mode == FormMode.Create)
        {
            await Api.PostAsync<PaymentMethodResponse>("api/v1/payment_method", request);
        }
        else
        {
            await Api.PatchAsync<PaymentMethodResponse>($"api/v1/payment_method/{_methodId}", request);
        }
    }
}

/// <summary>
/// ฟอร์มอัตราค่าบริการ — พอร์ตจาก ProjectRateFormModal.tsx
///
/// ตัวเลขในฟอร์มนี้เป็นฐานของการคิดเงินทุกใบในโครงการ
/// ค่าส่วนกลางคูณอัตราส่วนกรรมสิทธิ์ ค่าเบี้ยปรับคิดเป็นเปอร์เซ็นต์ของยอดที่ค้าง
/// </summary>
public sealed partial class RateFormDialogViewModel : SettingsFormDialogBase
{
    private readonly int _projectId;
    private readonly int? _settingId;

    public RateFormDialogViewModel(
        ApiClient api, DialogService dialogs, FormMode mode, int projectId, SettingResponse? setting = null)
        : base(api, dialogs, mode)
    {
        _projectId = projectId;
        _settingId = setting?.Id;

        if (setting is null) return;

        MaintenanceFeeRate = Format(setting.MaintenanceFeeRate);
        FineRate = Format(setting.FineRate);
        CollectionFee = Format(setting.CollectionFee);
        DiscountRate = Format(setting.DiscountRate);
        InvoicePrefix = setting.InvoicePrefix ?? string.Empty;
        ReceiptPrefix = setting.ReceiptPrefix ?? string.Empty;
        InvoiceNote = setting.InvoiceNote ?? string.Empty;
        ReceiptNote = setting.ReceiptNote ?? string.Empty;
        Active = setting.Active;
    }

    public override double DialogWidth => 760;

    public override string Title => Mode switch
    {
        FormMode.Create => "เพิ่มอัตราค่าบริการ",
        FormMode.Edit => "แก้ไขอัตราค่าบริการ",
        _ => "รายละเอียดอัตราค่าบริการ",
    };

    [ObservableProperty]
    private string _maintenanceFeeRate = string.Empty;

    [ObservableProperty]
    private string _fineRate = string.Empty;

    [ObservableProperty]
    private string _collectionFee = string.Empty;

    [ObservableProperty]
    private string _discountRate = string.Empty;

    [ObservableProperty]
    private string _invoicePrefix = string.Empty;

    [ObservableProperty]
    private string _receiptPrefix = string.Empty;

    [ObservableProperty]
    private string _invoiceNote = string.Empty;

    [ObservableProperty]
    private string _receiptNote = string.Empty;

    [ObservableProperty]
    private string? _maintenanceFeeRateError;

    [ObservableProperty]
    private string? _fineRateError;

    [ObservableProperty]
    private string? _collectionFeeError;

    protected override bool Validate()
    {
        ClearError();

        MaintenanceFeeRateError = ValidateNumber(MaintenanceFeeRate, "ค่าส่วนกลางต่อตารางวา", required: true);
        FineRateError = ValidateNumber(FineRate, "ค่าเบี้ยปรับ", required: false);
        CollectionFeeError = ValidateNumber(CollectionFee, "ค่าทวงถามต่อเดือน", required: false);

        return MaintenanceFeeRateError is null && FineRateError is null && CollectionFeeError is null;
    }

    private static string? ValidateNumber(string text, string label, bool required)
    {
        if (string.IsNullOrWhiteSpace(text)) return required ? $"กรุณากรอก{label}" : null;

        var value = ParseNumber(text);
        if (value is null) return $"{label}ต้องเป็นตัวเลข";

        return value < 0 ? $"{label}ต้องไม่ติดลบ" : null;
    }

    protected override async Task SaveAsync()
    {
        var request = new UpsertSettingRequest
        {
            ProjectId = _projectId,
            MaintenanceFeeRate = ParseNumber(MaintenanceFeeRate),
            FineRate = ParseNumber(FineRate),
            CollectionFee = ParseNumber(CollectionFee),
            DiscountRate = ParseNumber(DiscountRate),
            InvoicePrefix = Clean(InvoicePrefix),
            ReceiptPrefix = Clean(ReceiptPrefix),
            InvoiceNote = Clean(InvoiceNote),
            ReceiptNote = Clean(ReceiptNote),
            Active = Active,
        };

        if (_settingId is null)
        {
            await Api.PostAsync<SettingResponse>($"api/v1/projects/{_projectId}/settings", request);
        }
        else
        {
            await Api.PatchAsync<SettingResponse>($"api/v1/settings/{_settingId}", request);
        }
    }
}

/// <summary>
/// ฟอร์มเทมเพลตใบทวงถาม — พอร์ตจาก CollectionNoticeFormModal.tsx
///
/// เว็บเดิมใช้ตัวแก้ไขข้อความแบบ rich text แล้วเก็บเป็น HTML
/// ฝั่ง desktop รับเป็นข้อความธรรมดาแล้วห่อด้วยแท็ก div ให้ รูปแบบที่เก็บจึงยังเหมือนเดิม
/// ตอนพิมพ์เอกสาร ฝั่ง API แปลง HTML กลับเป็นข้อความด้วย cleanHtmlToText อยู่แล้ว
/// </summary>
public sealed partial class NoticeTemplateFormDialogViewModel : SettingsFormDialogBase
{
    private readonly int _projectId;
    private readonly int? _templateId;

    public NoticeTemplateFormDialogViewModel(
        ApiClient api, DialogService dialogs, FormMode mode, int projectId, NoticeTemplateResponse? template = null)
        : base(api, dialogs, mode)
    {
        _projectId = projectId;
        _templateId = template?.Id;

        if (template is null) return;

        TopicName = template.TopicName;
        IntroductionDetail = StripHtml(template.IntroductionDetail);
        AdditionalDetail = StripHtml(template.AdditionalDetail);
        ClosureText = StripHtml(template.ClosureText);
        ClosingWord = template.ClosingWord;
        Remark = StripHtml(template.Remark);
        Active = template.Active;
    }

    public override double DialogWidth => 760;

    public override string Title => Mode switch
    {
        FormMode.Create => "เพิ่มเทมเพลตใบทวงถาม",
        FormMode.Edit => "แก้ไขเทมเพลตใบทวงถาม",
        _ => "รายละเอียดเทมเพลตใบทวงถาม",
    };

    /// <summary>ตัวแทนที่ในเนื้อหา ผู้ใช้ต้องรู้ว่าพิมพ์อะไรได้บ้าง</summary>
    public string TokenHint => "ใช้ {resident_name} แทนชื่อลูกบ้าน และ {amount} แทนยอดเงินได้";

    [ObservableProperty]
    private string _topicName = string.Empty;

    [ObservableProperty]
    private string _introductionDetail = string.Empty;

    [ObservableProperty]
    private string _additionalDetail = string.Empty;

    [ObservableProperty]
    private string _closureText = string.Empty;

    [ObservableProperty]
    private string _closingWord = string.Empty;

    [ObservableProperty]
    private string _remark = string.Empty;

    [ObservableProperty]
    private string? _topicNameError;

    partial void OnTopicNameChanged(string value) => TopicNameError = null;

    protected override bool Validate()
    {
        ClearError();
        TopicNameError = string.IsNullOrWhiteSpace(TopicName) ? "กรุณากรอกชื่อหัวข้อ" : null;

        return TopicNameError is null;
    }

    protected override async Task SaveAsync()
    {
        var request = new UpsertNoticeTemplateRequest
        {
            TopicName = TopicName.Trim(),
            IntroductionDetail = WrapHtml(IntroductionDetail),
            AdditionalDetail = WrapHtml(AdditionalDetail),
            ClosureText = WrapHtml(ClosureText),
            ClosingWord = Clean(ClosingWord),
            Remark = WrapHtml(Remark),
            ProjectId = _projectId,
            Active = Active,
        };

        if (Mode == FormMode.Create)
        {
            await Api.PostAsync<NoticeTemplateResponse>("api/v1/notice_template", request);
        }
        else
        {
            await Api.PatchAsync<NoticeTemplateResponse>($"api/v1/notice_template/{_templateId}", request);
        }
    }

    /// <summary>ห่อแต่ละย่อหน้าด้วย div ให้ตรงกับที่ตัวแก้ไขของเว็บเดิมบันทึกไว้</summary>
    private static string? WrapHtml(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        return string.Concat(lines.Select(line =>
            $"<div>{System.Net.WebUtility.HtmlEncode(line)}</div>"));
    }

    /// <summary>แปลง HTML ที่เก็บไว้กลับเป็นข้อความธรรมดาเพื่อแสดงในช่องกรอก</summary>
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var withBreaks = System.Text.RegularExpressions.Regex.Replace(
            html, @"</\s*(div|p|br)\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var text = System.Text.RegularExpressions.Regex.Replace(withBreaks, "<.*?>", string.Empty);

        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }
}
