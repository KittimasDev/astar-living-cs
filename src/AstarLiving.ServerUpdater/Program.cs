using AstarLiving.ServerUpdater;
using AstarLiving.Shared.Versioning;
using Serilog;

// บริการอัปเดตบนเครื่องแม่
//
// หน้าที่สองอย่าง
// 1. ดึงเวอร์ชันใหม่จากเซิร์ฟเวอร์ปล่อยเวอร์ชันมาอัปเดตเครื่องแม่เอง
//    แล้วค่อยปล่อย feed ให้เครื่องลูกตามทีหลัง
// 2. เฝ้าดูว่า API ยังทำงานอยู่ ถ้าล้มให้สั่งเริ่มใหม่
//
// ไซต์ที่ไม่มีอินเทอร์เน็ตจะทำงานข้อ 2 อย่างเดียว และไม่มีอะไรพังเพราะข้อ 1 ล้มเหลว

var builder = Host.CreateApplicationBuilder(args);

var configDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AstarLiving", "config");

// อ่านค่าตั้งจากไฟล์เดียวกับที่ API ใช้ ซึ่งอยู่นอกโฟลเดอร์ติดตั้งจึงรอดจากการอัปเดต
builder.Configuration.AddJsonFile(Path.Combine(configDir, "machine.json"), optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables("ASTARLIVING_");

var options = builder.Configuration.GetSection(UpdaterOptions.SectionName).Get<UpdaterOptions>() ?? new UpdaterOptions();

// กุญแจที่ใช้ตรวจลายเซ็นของรายการปล่อยเวอร์ชัน
// ถ้าไม่ได้ตั้งไว้ ตัวตรวจจะปฏิเสธทุก manifest ซึ่งเป็นพฤติกรรมที่ต้องการ
ManifestVerifier.PublicKeyBase64 = options.ReleasePublicKey ?? string.Empty;

var logRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "AstarLiving", "logs", "updater");

Directory.CreateDirectory(logRoot);

builder.Services.AddSerilog(config => config
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logRoot, "updater-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<WindowsServiceControl>();
builder.Services.AddSingleton<UpdateRunner>();

builder.Services.AddHttpClient("release-feed", client => client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddHttpClient("health", client => client.Timeout = TimeSpan.FromSeconds(10));

builder.Services.AddHostedService<UpdateWorker>();
builder.Services.AddHostedService<ApiWatchdog>();

builder.Services.AddWindowsService(service => service.ServiceName = "AstarLiving ServerUpdater");

await builder.Build().RunAsync();
