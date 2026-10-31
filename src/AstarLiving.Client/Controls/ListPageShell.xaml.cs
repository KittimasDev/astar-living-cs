using System.Windows;
using System.Windows.Controls;

namespace AstarLiving.Client.Controls;

/// <summary>
/// โครงหน้าจอแบบรายการที่ทุกหน้าใช้ร่วมกัน
/// แถบค้นหา ปุ่มเพิ่ม แถบสถานะกำลังโหลด ตาราง และแถบเลขหน้า
///
/// หน้าจอแต่ละหน้าใส่เฉพาะตารางของตัวเองเข้ามาทาง Table
/// ส่วนตัวกรองเพิ่มเติม (เช่น เดือน ปี สถานะ) ใส่ทาง Filters
/// </summary>
public partial class ListPageShell : UserControl
{
    public static readonly DependencyProperty SearchPlaceholderProperty =
        DependencyProperty.Register(nameof(SearchPlaceholder), typeof(string), typeof(ListPageShell),
            new PropertyMetadata("ค้นหา"));

    public static readonly DependencyProperty CreateTextProperty =
        DependencyProperty.Register(nameof(CreateText), typeof(string), typeof(ListPageShell),
            new PropertyMetadata("เพิ่มข้อมูล"));

    public static readonly DependencyProperty ShowCreateProperty =
        DependencyProperty.Register(nameof(ShowCreate), typeof(bool), typeof(ListPageShell),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowSearchProperty =
        DependencyProperty.Register(nameof(ShowSearch), typeof(bool), typeof(ListPageShell),
            new PropertyMetadata(true));

    public static readonly DependencyProperty EmptyMessageProperty =
        DependencyProperty.Register(nameof(EmptyMessage), typeof(string), typeof(ListPageShell),
            new PropertyMetadata("ไม่มีข้อมูล"));

    /// <summary>
    /// คำสั่งของปุ่มมุมขวาบน ปล่อยว่างไว้จะใช้ CreateCommand ของ view model
    /// หน้าที่ปุ่มนี้ไม่ได้ใช้เพิ่มข้อมูล (เช่น หน้าใบทวงถามที่ใช้สั่งพิมพ์) ให้ผูกคำสั่งเองผ่านช่องนี้
    /// </summary>
    public static readonly DependencyProperty CreateCommandProperty =
        DependencyProperty.Register(nameof(CreateCommand), typeof(System.Windows.Input.ICommand), typeof(ListPageShell));

    public static readonly DependencyProperty TableProperty =
        DependencyProperty.Register(nameof(Table), typeof(object), typeof(ListPageShell));

    public static readonly DependencyProperty FiltersProperty =
        DependencyProperty.Register(nameof(Filters), typeof(object), typeof(ListPageShell));

    public ListPageShell()
    {
        InitializeComponent();

        // ปุ่มมุมขวาบนใช้คำสั่งที่หน้าจอกำหนดมาถ้ามี ไม่อย่างนั้นใช้ CreateCommand ของ view model
        // ผูกตอน Loaded เพราะต้องรอให้ค่าที่หน้าจอตั้งไว้ถูกกำหนดเสร็จก่อน
        Loaded += (_, _) =>
        {
            if (CreateCommand is not null)
            {
                ToolbarButton.Command = CreateCommand;
                return;
            }

            ToolbarButton.SetBinding(
                System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new System.Windows.Data.Binding(nameof(CreateCommand)));
        };
    }

    public string SearchPlaceholder
    {
        get => (string)GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value);
    }

    public string CreateText
    {
        get => (string)GetValue(CreateTextProperty);
        set => SetValue(CreateTextProperty, value);
    }

    public bool ShowCreate
    {
        get => (bool)GetValue(ShowCreateProperty);
        set => SetValue(ShowCreateProperty, value);
    }

    public bool ShowSearch
    {
        get => (bool)GetValue(ShowSearchProperty);
        set => SetValue(ShowSearchProperty, value);
    }

    public string EmptyMessage
    {
        get => (string)GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    public System.Windows.Input.ICommand? CreateCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(CreateCommandProperty);
        set => SetValue(CreateCommandProperty, value);
    }

    /// <summary>ตารางของหน้านั้น ๆ</summary>
    public object? Table
    {
        get => GetValue(TableProperty);
        set => SetValue(TableProperty, value);
    }

    /// <summary>ตัวกรองเพิ่มเติมที่วางต่อจากช่องค้นหา</summary>
    public object? Filters
    {
        get => GetValue(FiltersProperty);
        set => SetValue(FiltersProperty, value);
    }
}
