namespace AstarLiving.Client.Common.Dialogs;

/// <summary>
/// ฐานของหน้าต่างซ้อนทุกแบบ
///
/// หน้าต่างซ้อนในระบบนี้ไม่ใช่ Window จริงของ Windows แต่เป็นชั้นที่วาดทับเนื้อหา
/// เหมือน Modal ของเว็บเดิม ทำให้ซ้อนกันหลายชั้นได้ (เช่น กล่องยืนยันออกทับฟอร์มที่กรอกค้างไว้)
/// และไม่มีปัญหาหน้าต่างหลุดไปอยู่หลังจอหลักเวลาผู้ใช้สลับโปรแกรม
/// </summary>
public abstract class DialogViewModelBase : ObservableObjectBase
{
    private readonly TaskCompletionSource<object?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>ความกว้างของกล่อง หน้าจอที่ต้องการกว้างกว่านี้ให้เขียนทับ</summary>
    public virtual double DialogWidth => 400;

    /// <summary>คลิกพื้นหลังแล้วปิดได้หรือไม่ — กล่องที่กำลังทำงานอยู่ควรปิดแบบนี้ไม่ได้</summary>
    public virtual bool CanDismissByBackdrop => false;

    internal Task<object?> Completion => _completion.Task;

    internal bool IsClosed => _completion.Task.IsCompleted;

    /// <summary>ปิดกล่องพร้อมส่งผลลัพธ์กลับไปให้ผู้เรียก</summary>
    protected void CloseWith(object? result) => _completion.TrySetResult(result);

    /// <summary>ปิดโดยไม่มีผลลัพธ์ ใช้ตอนผู้ใช้กดพื้นหลังหรือปุ่มปิด</summary>
    internal void Dismiss() => _completion.TrySetResult(null);
}

/// <summary>หน้าต่างซ้อนที่คืนผลลัพธ์ชนิดหนึ่งกลับไป</summary>
public abstract class DialogViewModel<TResult> : DialogViewModelBase
{
    protected void Close(TResult? result) => CloseWith(result);
}
