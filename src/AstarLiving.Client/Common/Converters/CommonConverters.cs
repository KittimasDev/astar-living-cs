using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AstarLiving.Client.Common.Converters;

/// <summary>true ให้เห็น false ให้หายไปและไม่กินพื้นที่</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>มีข้อความให้เห็น ไม่มีให้หายไป</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>มีค่า (ไม่ null) ให้เห็น</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// จำนวนรายการเป็นศูนย์ให้เห็น ใช้กับข้อความ "ไม่พบข้อมูล" ใต้ตาราง
/// ส่งพารามิเตอร์ "invert" เพื่อสลับเป็น "มีรายการจึงให้เห็น"
/// </summary>
public sealed class EmptyCollectionToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int number => number,
            System.Collections.ICollection collection => collection.Count,
            null => 0,
            _ => 1,
        };

        var visible = count == 0;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>เงินแบบคั่นหลักพัน ไม่มีทศนิยม ตามที่เว็บเดิมแสดง</summary>
public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => "-",
            decimal d => ThaiFormat.Money(d),
            double d => ThaiFormat.Money((decimal)d),
            int i => ThaiFormat.Money(i),
            _ => value.ToString() ?? "-",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        decimal.TryParse(value as string, NumberStyles.Any, ThaiFormat.Culture, out var parsed)
            ? parsed
            : DependencyProperty.UnsetValue;
}

/// <summary>เงินแบบทศนิยมสองตำแหน่ง</summary>
public sealed class Money2Converter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d ? ThaiFormat.Money2(d) : "-";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>เปรียบเทียบค่ากับพารามิเตอร์ ใช้กับ RadioButton และแท็บ</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : Binding.DoNothing;
}

/// <summary>
/// เทียบค่าสองค่าว่าเท่ากันหรือไม่ ใช้ไฮไลต์ปุ่มเลขหน้าที่ตรงกับหน้าปัจจุบัน
/// เทียบผ่านข้อความเพราะฝั่งหนึ่งมาจาก Content ของปุ่มซึ่งเป็น object
/// </summary>
public sealed class AreEqualConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;

        return string.Equals(
            values[0]?.ToString(),
            values[1]?.ToString(),
            StringComparison.Ordinal);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// เลขลำดับของแถวในตาราง นับต่อเนื่องข้ามหน้า
/// รับ [แถว, รายการทั้งหมดในหน้านี้, เลขหน้า] แล้วคืน (หน้า-1) x ขนาดหน้า + ตำแหน่ง + 1
/// ตรงกับที่เว็บเดิมคำนวณในคอลัมน์ "ลำดับ"
///
/// หาตำแหน่งจากรายการเองแทนการใช้ AlternationIndex ของ WPF
/// เพราะค่านั้นว่างเปล่าเมื่อตารางเปิดการใช้ virtualization
/// </summary>
public sealed class RowNumberConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3) return string.Empty;
        if (values[1] is not System.Collections.IList items) return string.Empty;

        var index = items.IndexOf(values[0]);
        if (index < 0) return string.Empty;

        var page = values[2] as int? ?? 1;

        var number = ((page - 1) * ListPage.PageSize) + index + 1;

        // ต้องคืนเป็นข้อความ ไม่ใช่ตัวเลข
        // TextBlock.Text รับเฉพาะ string ถ้าคืน int ออกไป WPF จะทิ้งค่าเงียบ ๆ
        // แล้วปล่อยช่องว่างเปล่า ไม่มีอะไรฟ้องทั้งตอนคอมไพล์และตอนรัน
        // ต้องเปิดบันทึกการผูกข้อมูลถึงจะเห็นว่า "Value produced is not valid for target property"
        return number.ToString(CultureInfo.InvariantCulture);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>กลับค่าจริงเท็จ ใช้กับคู่ปุ่มสลับโหมดที่ผูกกับค่าเดียวกัน</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// แปลงที่อยู่รูป (ไฟล์ในเครื่อง หรือ URL ของเครื่องแม่) เป็นรูปที่แสดงได้
///
/// ไฟล์ในเครื่องอ่านผ่านสตรีมแล้วตรึงทันที เพื่อไม่ให้ไฟล์ที่ผู้ใช้เพิ่งเลือกถูกล็อกไว้
///
/// ส่วนรูปจากเครื่องแม่ WPF จะดาวน์โหลดแบบไม่รอ ห้ามเรียก Freeze ตรงนั้นเด็ดขาด
/// เพราะรูปยังโหลดไม่เสร็จ มันจะโยนข้อผิดพลาดแล้วได้ภาพว่างเปล่าแทน
///
/// โลโก้ถูกบีบเป็น WebP แต่ตั้งชื่อลงท้าย .png ตามระบบเดิม
/// เครื่องที่ไม่มีตัวถอดรหัส WebP จึงอ่านไม่ออก ซึ่งไม่ควรทำให้โปรแกรมพัง
/// </summary>
public sealed class ImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri)) return null;

        try
        {
            return uri.IsFile ? LoadLocal(uri) : LoadRemote(uri);
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException
                                      or ArgumentException or InvalidOperationException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>อ่านจนจบแล้วตรึง ไฟล์จึงไม่ถูกล็อกและใช้ข้ามเธรดได้</summary>
    private static System.Windows.Media.Imaging.BitmapImage LoadLocal(Uri uri)
    {
        using var stream = System.IO.File.OpenRead(uri.LocalPath);

        // ห้ามตั้ง IgnoreImageCache คู่กับ StreamSource
        // WPF จะไปหาคีย์แคชจาก UriSource ที่ไม่มี แล้วโยน "Key cannot be null"
        // ซึ่งไม่จำเป็นอยู่แล้วเพราะอ่านจากสตรีมไม่ได้ใช้แคชตั้งแต่แรก
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }

    /// <summary>ปล่อยให้ WPF โหลดเอง แล้วมันจะอัปเดตภาพให้เมื่อเสร็จ</summary>
    private static System.Windows.Media.Imaging.BitmapImage LoadRemote(Uri uri)
    {
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = uri;
        image.EndInit();

        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
