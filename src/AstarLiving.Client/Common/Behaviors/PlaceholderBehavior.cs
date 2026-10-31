using System.Windows;
using System.Windows.Controls;

namespace AstarLiving.Client.Common.Behaviors;

/// <summary>
/// ข้อความจาง ๆ ในช่องกรอกตอนที่ยังว่าง (placeholder ของ Ant Design)
/// WPF ไม่มีมาให้ จึงทำเป็น attached property แล้วให้ ControlTemplate หยิบไปแสดง
/// </summary>
public static class Placeholder
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(Placeholder),
            new PropertyMetadata(string.Empty));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    /// <summary>
    /// PasswordBox ไม่เปิด Password เป็น dependency property จึงดูว่าว่างหรือไม่ผ่าน binding ไม่ได้
    /// ตัวนี้คอยสะท้อนสถานะว่างให้ trigger ใน template ใช้แทน
    /// </summary>
    public static readonly DependencyProperty IsEmptyProperty =
        DependencyProperty.RegisterAttached(
            "IsEmpty",
            typeof(bool),
            typeof(Placeholder),
            new PropertyMetadata(true));

    public static bool GetIsEmpty(DependencyObject d) => (bool)d.GetValue(IsEmptyProperty);

    public static void SetIsEmpty(DependencyObject d, bool value) => d.SetValue(IsEmptyProperty, value);

    /// <summary>เปิดการติดตามความว่างของ PasswordBox (ใช้คู่กับ template ของ PasswordBox)</summary>
    public static readonly DependencyProperty MonitorPasswordProperty =
        DependencyProperty.RegisterAttached(
            "MonitorPassword",
            typeof(bool),
            typeof(Placeholder),
            new PropertyMetadata(false, OnMonitorPasswordChanged));

    public static bool GetMonitorPassword(DependencyObject d) => (bool)d.GetValue(MonitorPasswordProperty);

    public static void SetMonitorPassword(DependencyObject d, bool value) => d.SetValue(MonitorPasswordProperty, value);

    private static void OnMonitorPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        box.PasswordChanged -= OnPasswordChanged;

        if (e.NewValue is true)
        {
            box.PasswordChanged += OnPasswordChanged;
            SetIsEmpty(box, box.SecurePassword.Length == 0);
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        SetIsEmpty(box, box.SecurePassword.Length == 0);
    }
}
