using System.Collections.ObjectModel;
using AstarLiving.Client.Common.Dialogs;

namespace AstarLiving.Client.Services;

/// <summary>
/// เปิดหน้าต่างซ้อนและรอผลลัพธ์
///
/// เก็บเป็นกองซ้อน (stack) เพราะหน้าจอหลายหน้าของระบบเดิมเปิดกล่องทับกัน
/// เช่น ฟอร์มลูกบ้าน แล้วกดปิด จะมีกล่อง "ยืนยันออกจากหน้านี้" ทับขึ้นมาอีกชั้น
/// </summary>
public sealed class DialogService
{
    /// <summary>กล่องที่เปิดอยู่ เรียงจากชั้นล่างสุดไปบนสุด</summary>
    public ObservableCollection<DialogViewModelBase> Stack { get; } = [];

    public bool HasDialog => Stack.Count > 0;

    public event EventHandler? StackChanged;

    /// <summary>เปิดกล่องแล้วรอจนผู้ใช้ปิด คืนผลลัพธ์ที่กล่องส่งกลับมา</summary>
    public async Task<TResult?> ShowAsync<TResult>(DialogViewModel<TResult> dialog)
    {
        Stack.Add(dialog);
        StackChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var result = await dialog.Completion;
            return result is TResult typed ? typed : default;
        }
        finally
        {
            Stack.Remove(dialog);
            StackChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── กล่องมาตรฐานที่ใช้ซ้ำทุกหน้าจอ ──────────────────────────────────────

    /// <summary>ถามก่อนทิ้งข้อมูลที่กรอกค้างไว้ คืน true เมื่อผู้ใช้ยืนยันว่าจะออก</summary>
    public Task<bool> ConfirmExitAsync() => ShowAsync(new ConfirmExitDialogViewModel());

    public Task<bool> SuccessAsync(string title) => ShowAsync(new SuccessDialogViewModel(title));

    public Task<bool> ErrorAsync(string title, string? detail = null) =>
        ShowAsync(new ErrorDialogViewModel(title, detail));

    public Task<bool> ConfirmAsync(
        string title,
        string? message = null,
        string confirmText = "ยืนยัน",
        string cancelText = "ยกเลิก",
        bool isDestructive = false) =>
        ShowAsync(new ConfirmDialogViewModel(title, message, confirmText, cancelText, isDestructive));

    /// <summary>รับข้อความสั้น ๆ เช่น เหตุผลที่ยกเลิกเอกสาร คืน null เมื่อผู้ใช้ยกเลิก</summary>
    public Task<string?> PromptAsync(
        string title, string label, string placeholder = "", bool required = true, int? maxLength = null) =>
        ShowAsync(new TextInputDialogViewModel(title, label, placeholder, required, maxLength));

    /// <summary>
    /// ปิดกล่องบนสุด ใช้ตอนผู้ใช้กดพื้นหลังหรือกด Esc
    /// กล่องที่ห้ามปิดแบบนี้จะไม่ถูกแตะ
    /// </summary>
    public void DismissTop()
    {
        var top = Stack.LastOrDefault();
        if (top is null || !top.CanDismissByBackdrop) return;

        top.Dismiss();
    }
}
