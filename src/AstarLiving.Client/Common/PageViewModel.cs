namespace AstarLiving.Client.Common;

/// <summary>
/// ฐานของหน้าจอที่แสดงในพื้นที่เนื้อหาของ shell
/// ชื่อหน้าถูกนำไปแสดงบนแถบหัว เหมือน pageTitle ของเว็บเดิม
/// </summary>
public abstract class PageViewModel : ObservableObjectBase
{
    public abstract string Title { get; }

    /// <summary>
    /// เรียกทุกครั้งที่ผู้ใช้เข้ามาที่หน้านี้
    /// โหลดข้อมูลตรงนี้ ไม่ใช่ใน constructor เพื่อให้หน้าจอขึ้นก่อนแล้วค่อยเติมข้อมูล
    /// </summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}
