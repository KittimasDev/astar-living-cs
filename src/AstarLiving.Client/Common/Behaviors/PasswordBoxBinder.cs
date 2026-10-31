using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace AstarLiving.Client.Common.Behaviors;

/// <summary>
/// ส่งรหัสผ่านจาก PasswordBox ไปให้ view model โดยไม่ต้องให้ view model อ้างถึงตัวคอนโทรล
/// ส่งเป็น <see cref="SecureString"/> ไม่ใช่ string เพื่อไม่ให้รหัสผ่านค้างอยู่ใน managed heap
/// </summary>
public static class PasswordBoxBinder
{
    public static readonly DependencyProperty SecurePasswordProperty =
        DependencyProperty.RegisterAttached(
            "SecurePassword",
            typeof(SecureString),
            typeof(PasswordBoxBinder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static SecureString? GetSecurePassword(DependencyObject d) => (SecureString?)d.GetValue(SecurePasswordProperty);

    public static void SetSecurePassword(DependencyObject d, SecureString? value) => d.SetValue(SecurePasswordProperty, value);

    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached(
            "Attach",
            typeof(bool),
            typeof(PasswordBoxBinder),
            new PropertyMetadata(false, OnAttachChanged));

    public static bool GetAttach(DependencyObject d) => (bool)d.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject d, bool value) => d.SetValue(AttachProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        box.PasswordChanged -= OnPasswordChanged;
        if (e.NewValue is true) box.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        SetSecurePassword(box, box.SecurePassword.Copy());
    }
}
