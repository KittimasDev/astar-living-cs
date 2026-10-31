using AstarLiving.Client.Common;
using Microsoft.Extensions.DependencyInjection;

namespace AstarLiving.Client.Services;

/// <summary>
/// เปลี่ยนหน้าในพื้นที่เนื้อหาของหน้าต่างหลัก
///
/// สร้าง view model ใหม่ทุกครั้งที่เข้าหน้า เหมือนเว็บเดิมที่โหลดหน้าใหม่ทุกครั้ง
/// ทำให้ตัวกรองและข้อมูลสดเสมอ ไม่ต้องคอยล้างสถานะเก่าเอง
/// </summary>
public sealed class NavigationService(IServiceProvider services)
{
    public PageViewModel? Current { get; private set; }

    /// <summary>ยิงทันทีที่หน้าใหม่ถูกสร้าง ก่อนที่ข้อมูลของหน้านั้นจะโหลดเสร็จ</summary>
    public event EventHandler<PageViewModel>? Navigated;

    public Task NavigateAsync<TPage>() where TPage : PageViewModel => NavigateAsync(typeof(TPage));

    public async Task NavigateAsync(Type pageType)
    {
        if (!typeof(PageViewModel).IsAssignableFrom(pageType))
        {
            throw new ArgumentException($"{pageType.Name} ไม่ใช่หน้าจอที่เปิดได้", nameof(pageType));
        }

        var page = (PageViewModel)services.GetRequiredService(pageType);

        Current = page;
        Navigated?.Invoke(this, page);

        // โหลดข้อมูลหลังจากหน้าขึ้นแล้ว เพื่อให้ผู้ใช้เห็นโครงหน้าทันทีไม่ค้างหน้าขาว
        await page.OnNavigatedToAsync();
    }
}
