using System.Runtime.InteropServices;
using System.Security;
using AstarLiving.Client.Services;
using AstarLiving.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstarLiving.Client.ViewModels.Dialogs;

/// <summary>ตัวเลือกสิทธิ์ผู้ใช้งานพร้อมชื่อภาษาไทยที่แสดงบนหน้าจอ</summary>
public sealed record RoleOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>ตัวเลือกโครงการที่จะผูกบัญชีผู้ใช้เข้าไว้</summary>
public sealed record ProjectOption(int Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// ฟอร์มผู้ใช้งาน — พอร์ตจาก UserFormModal.tsx
///
/// รหัสผ่านบังคับกรอกเฉพาะตอนสร้างใหม่ ตอนแก้ไขปล่อยว่างไว้แปลว่าไม่เปลี่ยน
/// เก็บเป็น SecureString จนถึงวินาทีที่ส่งขึ้นสาย เพื่อไม่ให้ค้างอยู่ในหน่วยความจำ
/// </summary>
public sealed partial class UserFormDialogViewModel : SettingsFormDialogBase
{
    private readonly int? _userId;

    public UserFormDialogViewModel(
        ApiClient api,
        DialogService dialogs,
        FormMode mode,
        IReadOnlyList<RoleOption> roles,
        IReadOnlyList<ProjectOption> projects,
        int? projectId,
        UserResponse? user = null)
        : base(api, dialogs, mode)
    {
        Roles = roles;
        Projects = projects;
        _userId = user?.Id;

        var targetProject = user?.ProjectId ?? projectId;
        SelectedProject = projects.FirstOrDefault(p => p.Id == targetProject);

        if (user is null)
        {
            SelectedRole = roles.FirstOrDefault();
            return;
        }

        Username = user.Username;
        FullName = user.FullName;
        Email = user.Email ?? string.Empty;
        Phone = user.Phone ?? string.Empty;
        Active = user.Active;
        SelectedRole = roles.FirstOrDefault(r => r.Value == user.Role) ?? roles.FirstOrDefault();
    }

    public override double DialogWidth => 720;

    public override string Title => Mode switch
    {
        FormMode.Create => "เพิ่มผู้ใช้งาน",
        FormMode.Edit => "แก้ไขผู้ใช้งาน",
        _ => "รายละเอียดผู้ใช้งาน",
    };

    public IReadOnlyList<RoleOption> Roles { get; }

    /// <summary>
    /// โครงการที่เลือกได้
    ///
    /// ระบบเดิมบังคับให้เลือกโครงการเสมอ ของใหม่เคยผูกกับโครงการของคนที่ล็อกอินอัตโนมัติ
    /// ทำให้ผู้ดูแลระบบสร้างบัญชีให้โครงการอื่นไม่ได้เลย
    /// </summary>
    public IReadOnlyList<ProjectOption> Projects { get; }

    /// <summary>มีโครงการเดียวก็ไม่ต้องให้เลือก ซ่อนช่องไปเลยจะได้ไม่รก</summary>
    public bool CanChooseProject => Projects.Count > 1;

    /// <summary>ตอนแก้ไข ช่องรหัสผ่านมีไว้ตั้งรหัสใหม่เท่านั้น ไม่ใช่ช่องบังคับ</summary>
    public string PasswordHint => Mode == FormMode.Edit
        ? "เว้นว่างไว้ถ้าไม่ต้องการเปลี่ยนรหัสผ่าน"
        : "อย่างน้อย 6 ตัวอักษร";

    public bool IsPasswordRequired => Mode == FormMode.Create;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private SecureString? _password;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private RoleOption? _selectedRole;

    [ObservableProperty]
    private ProjectOption? _selectedProject;

    [ObservableProperty]
    private string? _usernameError;

    [ObservableProperty]
    private string? _passwordError;

    [ObservableProperty]
    private string? _fullNameError;

    partial void OnUsernameChanged(string value) => UsernameError = null;

    partial void OnPasswordChanged(SecureString? value) => PasswordError = null;

    partial void OnFullNameChanged(string value) => FullNameError = null;

    protected override bool Validate()
    {
        ClearError();
        UsernameError = null;
        PasswordError = null;
        FullNameError = null;

        if (string.IsNullOrWhiteSpace(Username)) UsernameError = "กรุณากรอกชื่อผู้ใช้งาน";
        if (string.IsNullOrWhiteSpace(FullName)) FullNameError = "กรุณากรอกชื่อ-นามสกุล";

        var passwordLength = Password?.Length ?? 0;

        if (IsPasswordRequired && passwordLength == 0)
        {
            PasswordError = "กรุณากรอกรหัสผ่าน";
        }
        else if (passwordLength is > 0 and < 6)
        {
            PasswordError = "รหัสผ่านต้องมีอย่างน้อย 6 ตัวอักษร";
        }

        if (SelectedRole is null) ErrorMessage = "กรุณาเลือกสิทธิ์ผู้ใช้งาน";
        else if (SelectedProject is null) ErrorMessage = "กรุณาเลือกโครงการ";

        return UsernameError is null && PasswordError is null && FullNameError is null
            && SelectedRole is not null && SelectedProject is not null;
    }

    protected override async Task SaveAsync()
    {
        var password = Reveal(Password);

        try
        {
            if (Mode == FormMode.Create)
            {
                await Api.PostAsync<UserResponse>("api/v1/user", new CreateUserRequest
                {
                    Username = Username.Trim(),
                    PasswordHash = password,
                    FullName = FullName.Trim(),
                    Email = Clean(Email),
                    Phone = Clean(Phone),
                    Role = SelectedRole!.Value,
                    ProjectId = SelectedProject?.Id,
                    Active = Active,
                });
            }
            else
            {
                await Api.PatchAsync<UserResponse>($"api/v1/user/{_userId}", new UpdateUserRequest
                {
                    Username = Username.Trim(),

                    // ส่งไปเฉพาะตอนที่ผู้ใช้ตั้งรหัสใหม่จริง ๆ
                    PasswordHash = string.IsNullOrEmpty(password) ? null : password,
                    FullName = FullName.Trim(),
                    Email = Clean(Email),
                    Phone = Clean(Phone),
                    Role = SelectedRole!.Value,
                    ProjectId = SelectedProject?.Id,
                    Active = Active,
                });
            }
        }
        finally
        {
            Password?.Dispose();
            Password = null;
        }
    }

    private static string Reveal(SecureString? secure)
    {
        if (secure is null || secure.Length == 0) return string.Empty;

        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
            return Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }
}
