using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AstarLiving.Client.Common.Converters;

/// <summary>
/// แปลงชื่อคีย์ของไอคอน (เช่น "IconDashboard") เป็นรูปร่างจริงใน Themes/Icons.xaml
///
/// เมนูเก็บแค่ชื่อคีย์เป็นข้อความ เพราะเป็นข้อมูลล้วน ๆ ไม่ควรผูกกับชนิดของ WPF
/// การค้นหาจริงจึงเกิดตรงนี้ที่ชั้นหน้าจอ
/// </summary>
public sealed class IconLookupConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key)) return null;

        return Application.Current?.TryFindResource(key) as Geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
