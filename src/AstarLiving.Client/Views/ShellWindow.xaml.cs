using System.Windows;
using AstarLiving.Client.ViewModels;

namespace AstarLiving.Client.Views;

public partial class ShellWindow : Window
{
    public ShellWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
    }

    public ShellViewModel ViewModel { get; }
}
