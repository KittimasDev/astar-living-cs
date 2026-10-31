using System.Windows;
using AstarLiving.Client.ViewModels;

namespace AstarLiving.Client.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        ViewModel = viewModel;
        viewModel.SignedIn += OnSignedIn;

        // ให้เคอร์เซอร์อยู่ที่ช่องแรกทันที ผู้ใช้จะได้พิมพ์ได้เลยโดยไม่ต้องคลิก
        // แล้วรอเครื่องแม่ข่ายเบื้องหลัง เผื่อผู้ใช้เพิ่งเปิดเครื่องแล้วกดโปรแกรมทันที
        Loaded += (_, _) =>
        {
            UsernameBox.Focus();
            _ = viewModel.WaitForServerAsync(_closing.Token);
        };

        Closed += (_, _) => _closing.Cancel();
    }

    private readonly CancellationTokenSource _closing = new();

    public LoginViewModel ViewModel { get; }

    /// <summary>true เมื่อปิดหน้าต่างเพราะเข้าสู่ระบบสำเร็จ ไม่ใช่เพราะผู้ใช้กดปิด</summary>
    public bool SignedIn { get; private set; }

    private void OnSignedIn(object? sender, EventArgs e)
    {
        // ตั้งธงก่อนปิดเสมอ ตัวจัดการเหตุการณ์ Closed ใช้ค่านี้ตัดสินว่าจะไปหน้าหลักหรือปิดโปรแกรม
        // (ห้ามใช้ DialogResult เพราะหน้าต่างนี้เปิดด้วย Show ไม่ใช่ ShowDialog)
        SignedIn = true;
        Close();
    }
}
