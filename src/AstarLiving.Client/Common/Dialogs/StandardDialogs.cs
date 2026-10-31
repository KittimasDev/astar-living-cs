using CommunityToolkit.Mvvm.Input;

namespace AstarLiving.Client.Common.Dialogs;

/// <summary>
/// กล่องยืนยันออกจากหน้า — พอร์ตจาก ConfirmExitModal.tsx
/// ขึ้นเมื่อผู้ใช้กำลังจะปิดฟอร์มที่กรอกค้างไว้
/// </summary>
public sealed partial class ConfirmExitDialogViewModel : DialogViewModel<bool>
{
    public string Title => "ยืนยันออกจากหน้านี้";

    public string Message => "การเปลี่ยนแปลงอาจไม่ถูกบันทึก";

    public string ImagePath => "pack://application:,,,/Assets/Images/exit.png";

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}

/// <summary>กล่องแจ้งว่าทำสำเร็จ — พอร์ตจาก SuccessModal ของแต่ละหน้า</summary>
public sealed partial class SuccessDialogViewModel(string title) : DialogViewModel<bool>
{
    public string Title { get; } = title;

    public string ImagePath => "pack://application:,,,/Assets/Images/complete.png";

    [RelayCommand]
    private void Back() => Close(true);
}

/// <summary>กล่องแจ้งข้อผิดพลาดที่ผู้ใช้ต้องรับรู้ก่อนทำต่อ</summary>
public sealed partial class ErrorDialogViewModel(string title, string? detail = null) : DialogViewModel<bool>
{
    public string Title { get; } = title;

    public string? Detail { get; } = detail;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public string ImagePath => "pack://application:,,,/Assets/Images/cant.png";

    [RelayCommand]
    private void Back() => Close(true);
}

/// <summary>กล่องยืนยันทั่วไป ใช้ก่อนทำสิ่งที่ย้อนกลับไม่ได้ เช่น ลบหรือยกเลิกเอกสาร</summary>
public sealed partial class ConfirmDialogViewModel(
    string title,
    string? message = null,
    string confirmText = "ยืนยัน",
    string cancelText = "ยกเลิก",
    bool isDestructive = false) : DialogViewModel<bool>
{
    public string Title { get; } = title;

    public string? Message { get; } = message;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public string ConfirmText { get; } = confirmText;

    public string CancelText { get; } = cancelText;

    /// <summary>true = ปุ่มยืนยันเป็นสีแดง ใช้กับการลบและการยกเลิกเอกสาร</summary>
    public bool IsDestructive { get; } = isDestructive;

    public string ImagePath => IsDestructive
        ? "pack://application:,,,/Assets/Images/cant.png"
        : "pack://application:,,,/Assets/Images/check.png";

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}

/// <summary>
/// กล่องรับข้อความสั้น ๆ เช่น เหตุผลที่ยกเลิกใบเสร็จหรือเหตุผลที่ลดหนี้
/// ระบบเดิมบังคับให้กรอกก่อนกดยืนยันได้
/// </summary>
public sealed partial class TextInputDialogViewModel(
    string title,
    string label,
    string placeholder = "",
    bool required = true,
    int? maxLength = null) : DialogViewModel<string>
{
    public string Title { get; } = title;

    public string Label { get; } = label;

    public string Placeholder { get; } = placeholder;

    public bool Required { get; } = required;

    /// <summary>ความยาวสูงสุดที่ยอมรับ ไม่กำหนดแปลว่าไม่จำกัด</summary>
    public int? MaxLength { get; } = maxLength;

    public override double DialogWidth => 460;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _text = string.Empty;

    partial void OnTextChanged(string value) => ClearError();

    [RelayCommand]
    private void Confirm()
    {
        if (Required && string.IsNullOrWhiteSpace(Text))
        {
            ErrorMessage = "กรุณากรอกข้อมูลก่อนยืนยัน";
            return;
        }

        if (MaxLength is { } limit && Text.Trim().Length > limit)
        {
            ErrorMessage = $"กรอกได้สูงสุด {limit} ตัวอักษร";
            return;
        }

        Close(Text.Trim());
    }

    [RelayCommand]
    private void Cancel() => Close(null);
}
