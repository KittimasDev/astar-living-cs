using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using AstarLiving.Client.Services;
using AstarLiving.Client.ViewModels;
using AstarLiving.Client.ViewModels.Pages;
using AstarLiving.Client.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Polly;
using Polly.Extensions.Http;

namespace AstarLiving.Client;

public partial class App : Application
{
    private IHost? _host;
    private ShellWindow? _shell;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ปิดโปรแกรมเฉพาะตอนที่สั่งเอง ไม่ใช่ตอนที่หน้าต่างสุดท้ายถูกปิด
        // เพราะระหว่างสลับจากหน้าเข้าสู่ระบบไปหน้าหลักจะมีจังหวะที่ไม่มีหน้าต่างเปิดอยู่เลย
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnUnhandledException;

        ApplyThaiCulture();

        AppPaths.EnsureUserDirectories();

        _host = BuildHost();
        await _host.StartAsync();

        _host.Services.GetRequiredService<ClientSettings>().Load();

        var session = _host.Services.GetRequiredService<AuthSession>();
        session.SignedOut += OnSessionExpired;
        session.UpgradeRequired += OnUpgradeRequired;

        await StartUserFlowAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    // ── ลำดับการเปิดโปรแกรม ────────────────────────────────────────────────

    /// <summary>
    /// ถ้ายังมีโทเคนที่ใช้ได้อยู่ให้ข้ามหน้าเข้าสู่ระบบไปเลย
    /// ถ้าติดต่อเครื่องแม่ข่ายไม่ได้ ให้ไปหน้าเข้าสู่ระบบเพื่อให้ผู้ใช้เห็นที่อยู่เครื่องแม่และแก้ได้
    /// </summary>
    private async Task StartUserFlowAsync()
    {
        var auth = _host!.Services.GetRequiredService<AuthApi>();

        try
        {
            var resumed = await auth.TryResumeAsync();
            if (resumed is not null)
            {
                await ShowShellAsync();
                return;
            }
        }
        catch (ApiException)
        {
            // ต่อเครื่องแม่ข่ายไม่ได้ตอนเปิดโปรแกรม ให้ไปหน้าเข้าสู่ระบบตามปกติ
        }

        ShowLogin();
    }

    private void ShowLogin()
    {
        var window = _host!.Services.GetRequiredService<LoginWindow>();

        window.Closed += async (_, _) =>
        {
            if (window.SignedIn)
            {
                await ShowShellAsync();
            }
            else
            {
                Shutdown();
            }
        };

        MainWindow = window;
        window.Show();
    }

    private async Task ShowShellAsync()
    {
        _shell = _host!.Services.GetRequiredService<ShellWindow>();
        _shell.ViewModel.SignedOut += OnShellSignedOut;

        MainWindow = _shell;
        _shell.Show();

        await _shell.ViewModel.InitializeAsync();
    }

    private void OnShellSignedOut(object? sender, EventArgs e) => ReturnToLogin();

    /// <summary>โทเคนหมดอายุระหว่างใช้งานและต่ออายุไม่ได้</summary>
    private void OnSessionExpired(object? sender, EventArgs e) =>
        Dispatcher.Invoke(() =>
        {
            if (_shell is null) return;

            MessageBox.Show(
                _shell,
                "เซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่อีกครั้ง",
                "A star Living",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ReturnToLogin();
        });

    /// <summary>
    /// เครื่องแม่ตอบ 426 ว่าโปรแกรมเวอร์ชันนี้ใช้ต่อไม่ได้
    /// ขึ้นกล่องบังคับอัปเดตที่ปิดไม่ได้ ถ้ายังไม่ได้เข้าหน้าหลักก็แจ้งด้วยกล่องของ Windows แทน
    /// </summary>
    private void OnUpgradeRequired(object? sender, string message) =>
        Dispatcher.Invoke(() =>
        {
            if (_shell is not null)
            {
                _ = _shell.ViewModel.ShowForcedUpdateAsync(message);
                return;
            }

            MessageBox.Show(message, "A star Living", MessageBoxButton.OK, MessageBoxImage.Warning);
        });

    private void ReturnToLogin()
    {
        if (_shell is not null)
        {
            _shell.ViewModel.SignedOut -= OnShellSignedOut;
            _shell.Close();
            _shell = null;
        }

        ShowLogin();
    }

    /// <summary>
    /// ตั้งทั้งโปรแกรมให้ใช้ปฏิทินไทย (พ.ศ.)
    ///
    /// ตัวเลือกวันที่ของ WPF อ่านปฏิทินจากวัฒนธรรมของเธรด ไม่ได้เปิดให้ตั้งรายตัว
    /// การตั้งตรงนี้ทำให้ทุกช่องวันที่แสดงปี พ.ศ. เหมือนเว็บเดิมโดยไม่ต้องสร้างคอนโทรลใหม่
    ///
    /// ต้องตั้ง Language ของ FrameworkElement ด้วย เพราะ WPF ใช้ค่านั้นไม่ใช่ CurrentCulture
    /// ตอนจัดรูปแบบสิ่งที่ผูกด้วย binding
    /// </summary>
    private static void ApplyThaiCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("th-TH").Clone();
        culture.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    // ── การประกอบบริการ ────────────────────────────────────────────────────

    private static IHost BuildHost() => Host.CreateDefaultBuilder()
        .UseSerilog((_, logging) => logging
            .MinimumLevel.Information()

            // บันทึกลงไฟล์ในโฟลเดอร์ของผู้ใช้ ไม่ใช่โฟลเดอร์ติดตั้ง
            // เวลาลูกค้าโทรมาแจ้งปัญหา นี่คือสิ่งแรกที่ต้องขอดู
            .WriteTo.File(
                System.IO.Path.Combine(AppPaths.LogDirectory, "client-.log"),
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true))
        .ConfigureServices(services =>
        {
            services.AddSingleton<ClientSettings>();
            services.AddSingleton<TokenStore>();
            services.AddSingleton<AuthSession>();
            services.AddSingleton<NavigationService>();
            services.AddSingleton<DialogService>();
            services.AddSingleton<DocumentService>();
            services.AddSingleton<UpdateService>();
            services.AddTransient<AuthTokenHandler>();

            // ตัวเรียกที่ไม่ผ่าน handler ของโทเคน ใช้เฉพาะตอนต่ออายุโทเคน เพื่อไม่ให้วนซ้ำ
            services.AddHttpClient(AuthTokenHandler.RawClientName, ConfigureClient);

            services.AddHttpClient<ApiClient>(ConfigureClient)
                .AddHttpMessageHandler<AuthTokenHandler>()
                .AddPolicyHandler(RetryPolicy());

            // ตัวเรียกสำหรับงานที่กินเวลานาน จงใจไม่ผูก RetryPolicy
            // เพราะการลองใหม่กับคำสั่งสำรองข้อมูลแปลว่าสั่ง pg_dump ซ้ำ
            services.AddHttpClient(BackupApi.ClientName, ConfigureLongRunningClient)
                .AddHttpMessageHandler<AuthTokenHandler>();

            services.AddSingleton<AuthApi>();
            services.AddSingleton<ServerReadiness>();
            services.AddSingleton<BackupApi>();

            services.AddTransient<LoginViewModel>();
            services.AddTransient<LoginWindow>();
            services.AddTransient<ShellViewModel>();
            services.AddTransient<ShellWindow>();

            RegisterPages(services);
        })
        .Build();

    private static void ConfigureClient(IServiceProvider provider, HttpClient client)
    {
        var settings = provider.GetRequiredService<ClientSettings>();

        // ตั้งค่าตอนสร้างตัวเรียก ถ้าผู้ใช้เปลี่ยนที่อยู่เครื่องแม่ต้องเปิดโปรแกรมใหม่
        // ซึ่งตรงกับที่หน้าตั้งค่าบอกผู้ใช้อยู่แล้ว
        client.BaseAddress = new Uri(settings.ServerUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// ตัวเรียกสำหรับงานที่กินเวลานานอย่างการสำรองฐานข้อมูล
    ///
    /// ปลดเพดานเวลาของตัวเรียก แล้วคุมด้วย CancellationToken รายคำขอแทน
    /// เพื่อให้ปุ่มยกเลิกของผู้ใช้เป็นคนกำหนดว่าจะรอนานแค่ไหน
    /// ไม่ใช่ตัวเลขตายตัวที่เดาขนาดฐานข้อมูลของลูกค้าไม่ได้
    /// </summary>
    private static void ConfigureLongRunningClient(IServiceProvider provider, HttpClient client)
    {
        ConfigureClient(provider, client);
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <summary>
    /// ลองใหม่เฉพาะข้อผิดพลาดชั่วคราวของเครือข่าย
    /// ไม่ลองใหม่กับ 4xx เพราะยิงซ้ำก็ได้ผลเดิม และอาจสร้างเอกสารซ้ำถ้าเป็นคำสั่งเขียนข้อมูล
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> RetryPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(2, attempt => TimeSpan.FromMilliseconds(400 * attempt));

    private static void RegisterPages(IServiceCollection services)
    {
        services.AddTransient<DashboardPageViewModel>();
        services.AddTransient<ResidentPageViewModel>();
        services.AddTransient<InvoicePageViewModel>();
        services.AddTransient<CreditNotePageViewModel>();
        services.AddTransient<OverduePageViewModel>();
        services.AddTransient<CollectionNoticePageViewModel>();
        services.AddTransient<ReceiptPageViewModel>();
        services.AddTransient<ReportPageViewModel>();
        services.AddTransient<ProjectSettingsPageViewModel>();
        services.AddTransient<PaymentMethodSettingsPageViewModel>();
        services.AddTransient<RateSettingsPageViewModel>();
        services.AddTransient<NoticeTemplateSettingsPageViewModel>();
        services.AddTransient<UserSettingsPageViewModel>();
        services.AddTransient<SystemLogsPageViewModel>();
        services.AddTransient<DatabaseBackupPageViewModel>();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // ข้อผิดพลาดที่หลุดมาถึงตรงนี้คือบั๊ก ไม่ใช่เรื่องที่ผู้ใช้แก้ได้
        // เขียนรายละเอียดเต็มลงไฟล์เสมอ เพราะข้อความบรรทัดเดียวบนกล่องแจ้งเตือน
        // แทบไม่เคยพอให้หาสาเหตุได้ตอนลูกค้าโทรมาแจ้ง
        var logPath = WriteCrashLog(e.Exception);

        MessageBox.Show(
            $"เกิดข้อผิดพลาดที่ไม่คาดคิด\n\n{e.Exception.Message}\n\nรายละเอียดถูกบันทึกไว้ที่\n{logPath}",
            "A star Living",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static string WriteCrashLog(Exception exception)
    {
        try
        {
            AppPaths.EnsureUserDirectories();

            var path = System.IO.Path.Combine(AppPaths.LogDirectory, $"crash-{DateTime.Now:yyyyMMdd}.log");

            var where = exception is System.Windows.Markup.XamlParseException xaml
                ? $"{xaml.BaseUri} line {xaml.LineNumber} pos {xaml.LinePosition}{Environment.NewLine}"
                : string.Empty;

            System.IO.File.AppendAllText(
                path,
                $"{Environment.NewLine}===== {DateTimeOffset.Now:O} ====={Environment.NewLine}{where}{exception}{Environment.NewLine}");

            return path;
        }
        catch (Exception writeFailure) when (writeFailure is System.IO.IOException or UnauthorizedAccessException)
        {
            return "(เขียนไฟล์บันทึกไม่ได้)";
        }
    }
}
