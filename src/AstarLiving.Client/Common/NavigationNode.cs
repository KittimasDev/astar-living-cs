using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstarLiving.Client.Common;

/// <summary>หนึ่งรายการในเมนูด้านซ้าย — มีลูกได้หนึ่งชั้นเหมือนเมนูของเว็บเดิม</summary>
public sealed partial class NavigationNode : ObservableObject
{
    public NavigationNode(string key, string label, string iconKey, Type? pageType = null, IEnumerable<NavigationNode>? children = null)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
        PageType = pageType;
        Children = new ObservableCollection<NavigationNode>(children ?? []);
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>คีย์ของ Geometry ใน Themes/Icons.xaml</summary>
    public string IconKey { get; }

    /// <summary>ชนิดของ view model ที่จะเปิด — null เมื่อรายการนี้เป็นแค่หัวข้อที่กางลูกได้</summary>
    public Type? PageType { get; }

    public ObservableCollection<NavigationNode> Children { get; }

    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;
}
