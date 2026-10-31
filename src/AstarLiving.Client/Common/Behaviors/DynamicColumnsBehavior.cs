using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AstarLiving.Client.ViewModels.Pages;

namespace AstarLiving.Client.Common.Behaviors;

/// <summary>
/// สร้างคอลัมน์ของตารางจากรายการที่ view model กำหนด
///
/// หน้ารายงานมีหกชนิดที่คอลัมน์ไม่เหมือนกันเลย การเขียนตารางแยกหกตัวจะซ้ำซ้อนมาก
/// และเพิ่มรายงานใหม่ทีต้องแก้ XAML ทุกครั้ง วิธีนี้ทำให้เพิ่มรายงานได้ที่ view model ที่เดียว
/// </summary>
public static class DynamicColumns
{
    /// <summary>
    /// ความกว้างของคอลัมน์ตัวเลข
    /// ต้องพอกับหัวคอลัมน์ที่ยาวที่สุดคือ "ยอดรวมทั้งหมด (บาท)"
    /// หัวตารางไม่ตัดบรรทัด ถ้าแคบกว่านี้วงเล็บปิดจะหายไปเฉย ๆ
    /// </summary>
    private const double NumericWidth = 175;

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source",
            typeof(IEnumerable<ReportColumn>),
            typeof(DynamicColumns),
            new PropertyMetadata(null, OnSourceChanged));

    public static IEnumerable<ReportColumn>? GetSource(DependencyObject d) =>
        (IEnumerable<ReportColumn>?)d.GetValue(SourceProperty);

    public static void SetSource(DependencyObject d, IEnumerable<ReportColumn>? value) =>
        d.SetValue(SourceProperty, value);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnCollectionChanged;
            Trackers.Remove(oldCollection);
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            Trackers.AddOrUpdate(newCollection, grid);
            newCollection.CollectionChanged += OnCollectionChanged;
        }

        Rebuild(grid, e.NewValue as IEnumerable<ReportColumn>);
    }

    /// <summary>
    /// ผูกคอลเลกชันกับตารางที่ใช้มันอยู่
    /// ใช้ ConditionalWeakTable เพื่อไม่ให้ตารางที่ถูกปิดไปแล้วค้างอยู่ในหน่วยความจำ
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<INotifyCollectionChanged, DataGrid> Trackers = [];

    private static void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is not INotifyCollectionChanged collection) return;
        if (!Trackers.TryGetValue(collection, out var grid)) return;

        Rebuild(grid, collection as IEnumerable<ReportColumn>);
    }

    private static void Rebuild(DataGrid grid, IEnumerable<ReportColumn>? columns)
    {
        grid.Columns.Clear();
        if (columns is null) return;

        foreach (var column in columns)
        {
            var isNumeric = column.Kind is ReportColumnKind.Money or ReportColumnKind.Integer;
            var isSequence = column.Kind is ReportColumnKind.Sequence;

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,

                // ค่าถูกจัดรูปแบบเป็นข้อความมาแล้วจาก view model จึงผูกผ่านตัวทำดัชนีได้ตรง ๆ
                Binding = new Binding($"[{column.Key}]"),
                Width = ResolveWidth(column, isNumeric, isSequence),

                // ความกว้างขั้นต่ำสำคัญมากกับคอลัมน์ยืด
                // ถ้าไม่ใส่ เมื่อพื้นที่ไม่พอ WPF จะบีบคอลัมน์ยืดจนเหลือศูนย์
                // คอลัมน์หายไปจากหน้าจอเงียบ ๆ โดยไม่มีแถบเลื่อนให้เลื่อนไปหา
                // พอมีขั้นต่ำ ความกว้างรวมจะเกินตารางแล้วแถบเลื่อนแนวนอนจึงโผล่
                MinWidth = ResolveMinWidth(column, isNumeric, isSequence),
                ElementStyle = Application.Current.TryFindResource(
                    isNumeric ? "NumberCell" : isSequence ? "CenterCell" : "LeftCell") as Style,
            });
        }
    }

    /// <summary>
    /// ความกว้างของคอลัมน์
    ///
    /// เดิมคอลัมน์ข้อความทุกตัวได้ส่วนแบ่งเท่ากันหมด ทำให้ "ลำดับ" กว้างเท่า "ชื่อ-นามสกุล"
    /// พอรายงานมีสิบคอลัมน์ เลขที่ใบเสร็จกับชื่อคนจึงถูกตัดจนอ่านไม่ออก
    /// ตอนนี้แต่ละแท็บกำหนดความกว้างมาเองตามที่ระบบเดิมใช้
    /// คอลัมน์ที่ไม่ได้กำหนดจะยืดรับพื้นที่ที่เหลือ เช่น ช่องหมายเหตุกับรายละเอียด
    ///
    /// เมื่อความกว้างรวมเกินตาราง DataGrid จะขึ้นแถบเลื่อนแนวนอนให้เอง
    /// ซึ่งดีกว่าบีบทุกคอลัมน์จนข้อความหาย
    /// </summary>
    /// <summary>ความกว้างขั้นต่ำ คอลัมน์ที่กำหนดความกว้างมาแล้วห้ามถูกบีบเลย</summary>
    private static double ResolveMinWidth(ReportColumn column, bool isNumeric, bool isSequence)
    {
        if (column.MinWidth is { } explicitMinimum) return explicitMinimum;
        if (column.Width is { } width) return width;

        if (isSequence) return 70;
        if (isNumeric) return NumericWidth;

        return 160;
    }

    private static DataGridLength ResolveWidth(ReportColumn column, bool isNumeric, bool isSequence)
    {
        if (column.Width is { } width) return new DataGridLength(width);

        if (isSequence) return new DataGridLength(70);
        if (isNumeric) return new DataGridLength(NumericWidth);

        return new DataGridLength(1, DataGridLengthUnitType.Star);
    }
}
