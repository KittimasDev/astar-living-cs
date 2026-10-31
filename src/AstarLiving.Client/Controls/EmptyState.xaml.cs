using System.Windows;
using System.Windows.Controls;

namespace AstarLiving.Client.Controls;

public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata("ไม่มีข้อมูล"));

    public EmptyState() => InitializeComponent();

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
