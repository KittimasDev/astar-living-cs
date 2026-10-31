using CommunityToolkit.Mvvm.ComponentModel;

namespace AstarLiving.Client.Common;

/// <summary>
/// ฐานของ view model ทุกตัว
/// รวมสถานะกำลังโหลดและข้อความผิดพลาดไว้ที่เดียว เพราะทุกหน้าจอใช้เหมือนกันหมด
/// </summary>
public abstract partial class ObservableObjectBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    protected void ClearError() => ErrorMessage = null;

    /// <summary>
    /// ห่องานที่คุยกับเครื่องแม่ข่าย ให้แสดงสถานะกำลังโหลดและแปลงข้อผิดพลาดเป็นข้อความไทยให้เอง
    /// คืน true เมื่อทำสำเร็จ
    /// </summary>
    protected async Task<bool> RunAsync(Func<Task> work)
    {
        if (IsBusy) return false;

        IsBusy = true;
        ClearError();

        try
        {
            await work();
            return true;
        }
        catch (Services.ApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
