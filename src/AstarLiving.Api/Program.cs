using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Api.Configuration;
using AstarLiving.Shared.Api;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Common;
using AstarLiving.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// รันเป็น Windows Service บนเครื่องแม่ และรันเป็นโปรเซสธรรมดาตอนพัฒนา
builder.Host.UseWindowsService(options => options.ServiceName = "AstarLiving API");

// ─── ค่าตั้ง ─────────────────────────────────────────────────────────────────
// ลำดับ: appsettings (ค่าเริ่มต้นที่มาพร้อมแพ็กเกจ) -> machine.json ของเครื่องนั้น -> ตัวแปรสภาพแวดล้อม
// machine.json อยู่นอกโฟลเดอร์ติดตั้ง จึงรอดจากการอัปเดตที่เขียนทับโฟลเดอร์ทั้งก้อน
// ปกติอยู่ที่ %ProgramData%\AstarLiving\config
// ตัวแปร ASTARLIVING_CONFIG_DIR มีไว้ทดสอบการติดตั้งลงโฟลเดอร์ชั่วคราวโดยไม่แตะเครื่องจริง
var machineConfigDir = Environment.GetEnvironmentVariable("ASTARLIVING_CONFIG_DIR")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AstarLiving", "config");

var machineConfigPath = Path.Combine(machineConfigDir, "machine.json");

builder.Configuration
    .AddJsonFile(machineConfigPath, optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("ASTARLIVING_");

var logRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "AstarLiving", "logs", "api");
Directory.CreateDirectory(logRoot);

builder.Host.UseSerilog((context, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logRoot, "api-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true)
    .ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddOptions<AstarLivingOptions>()
    .Bind(builder.Configuration.GetSection(AstarLivingOptions.SectionName))
    // กุญแจ JWT บนเครื่องลูกค้าอยู่ในไฟล์ที่ป้องกันด้วย DPAPI ไม่ได้อยู่ในไฟล์ตั้งค่า
    // จึงต้องเติมให้ก่อนตรวจความครบถ้วน
    .PostConfigure(ResolveJwtSecrets)
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.Jwt.AccessSecret) && !string.IsNullOrWhiteSpace(o.Jwt.RefreshSecret),
        "ไม่พบกุญแจสำหรับเซ็น token กรุณาตรวจไฟล์ jwt-access.secret และ jwt-refresh.secret ในโฟลเดอร์ config")
    .ValidateDataAnnotations()
    .ValidateOnStart();

var options = builder.Configuration
    .GetSection(AstarLivingOptions.SectionName)
    .Get<AstarLivingOptions>() ?? new AstarLivingOptions();

// ค่าชุดนี้อ่านตรงจาก configuration จึงไม่ผ่าน PostConfigure ต้องเติมกุญแจเองอีกที
ResolveJwtSecrets(options);

// ─── ฐานข้อมูล ───────────────────────────────────────────────────────────────
// รหัสผ่านฐานข้อมูลบนเครื่องลูกค้าถูกเก็บแยกไว้ในไฟล์ที่ป้องกันด้วย DPAPI
// ช่อง Password ใน appsettings มีไว้สำหรับเครื่องนักพัฒนาเท่านั้น
var databasePassword = ResolveDatabasePassword(options.Database);

var connectionString = options.Database.ConnectionString
    ?? AstarLivingDataSource.BuildConnectionString(
        options.Database.Host,
        options.Database.Port,
        options.Database.Database,
        options.Database.Username,
        databasePassword);

var dataSource = AstarLivingDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<AstarLivingDbContext>(db => db.UseNpgsql(dataSource));

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

// ─── บริการของโดเมน ──────────────────────────────────────────────────────────
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ThaiCalendar>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<AstarLiving.Infrastructure.Auditing.AuditStamp>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddScoped<AuthService>();

// ที่เก็บไฟล์ภาพ: URL ฐานต้องลงท้ายด้วย /api เพื่อให้ประกอบเป็น {base}/images/... เหมือนของเดิม
//
// ค่าเริ่มต้นเป็นที่อยู่แบบสัมพัทธ์โดยตั้งใจ ห้ามใส่ชื่อเครื่องหรือพอร์ตตายตัว
// เพราะเครื่องแม่ไม่รู้ว่าเครื่องลูกเรียกมันด้วยที่อยู่ไหนในเครือข่ายของลูกค้า
// เครื่องลูกเป็นฝ่ายเติมที่อยู่ที่ตัวเองใช้อยู่แล้วเอง
builder.Services.AddSingleton(new AstarLiving.Infrastructure.Storage.ImageStorageSettings(
    Root: options.Storage.UploadRoot,
    PublicBaseUrl: string.IsNullOrWhiteSpace(options.Storage.PublicBaseUrl)
        ? "/api"
        : options.Storage.PublicBaseUrl));
builder.Services.AddSingleton<AstarLiving.Infrastructure.Storage.ImageStorage>();

// บริการของแต่ละโมดูล
builder.Services.AddScoped<AstarLiving.Api.Modules.Master.ProjectService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Master.SettingService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Master.PaymentMethodService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Master.NoticeTemplateService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Master.UserService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Master.ActivityLogService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Internal.ResidentService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Internal.PaymentQueries>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Internal.PaymentService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Internal.InvoiceIssuingService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Internal.ReceiptService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Internal.CollectionService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Internal.RevenueTransactionService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.External.DashboardService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.External.ReportService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.External.SummaryExpensesService>();

// การพิมพ์เอกสาร: ต้องเรียก Initialize ก่อนสร้าง PDF ใบแรก
// ไม่อย่างนั้น QuestPDF จะโยน exception เรื่องสัญญาอนุญาตและหาฟอนต์ไทยไม่เจอ
AstarLiving.Pdf.PdfEnvironment.Initialize();
builder.Services.AddSingleton<AstarLiving.Pdf.LogoLoader>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Printing.DocumentDataBuilder>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Printing.PrintService>();

// สำรองฐานข้อมูล — ที่เก็บไฟล์ที่รอดาวน์โหลดต้องเป็น singleton
// เพราะมันถือทั้งตารางบัตรผ่านและตัวกันไม่ให้สำรองซ้อนกันของทั้งเครื่อง
builder.Services.AddSingleton<AstarLiving.Api.Modules.Maintenance.BackupArtifactStore>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Maintenance.BackupService>();
builder.Services.AddScoped<AstarLiving.Api.Modules.Maintenance.SystemStatusService>();

// ─── การยืนยันตัวตนและสิทธิ์ ─────────────────────────────────────────────────
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        // ไม่ให้แปลงชื่อ claim เป็น URI ยาวของ ClaimTypes เพื่อให้ claim "sub" / "role" / "project_id"
        // ยังชื่อเดิมตอนอ่านฝั่ง CurrentUserAccessor
        jwt.MapInboundClaims = false;

        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtTokenService.SigningKey(options.Jwt.AccessSecret),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),

            // ให้ [Authorize(Roles = ...)] และ policy อ่านสิทธิ์จาก claim "role" ที่เราใส่เอง
            RoleClaimType = AstarClaims.Role,
            NameClaimType = AstarClaims.Username,
        };

        jwt.Events = new JwtBearerEvents
        {
            // ปฏิเสธ refresh token ที่ถูกนำมาใช้แทน access token
            // (ช่องโหว่ของระบบเดิมที่เซ็น token ทั้งสองชนิดด้วยกุญแจและ payload เดียวกัน)
            OnTokenValidated = context =>
            {
                var tokenType = context.Principal?.FindFirst(AstarClaims.TokenType)?.Value;
                if (tokenType != AstarClaims.AccessTokenType)
                {
                    context.Fail("token ชนิดนี้ใช้เรียก API ไม่ได้");
                }

                return Task.CompletedTask;
            },

            // ให้ 401/403 ออกมาเป็น body รูปแบบเดียวกับ NestJS
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await WriteErrorAsync(context.Response, 401, "Unauthorized");
            },

            OnForbidden = async context => await WriteErrorAsync(context.Response, 403, "Forbidden"),
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.AddAstarLivingPolicies();

    // ทุก endpoint ต้องล็อกอิน เว้นแต่ทำเครื่องหมาย [AllowAnonymous]
    o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ─── MVC ─────────────────────────────────────────────────────────────────────
builder.Services
    .AddControllers(mvc =>
    {
        mvc.Filters.Add<NestStyleExceptionFilter>();

        // บันทึกทุกคำสั่งที่เปลี่ยนแปลงข้อมูลลงตารางประวัติการใช้งาน
        mvc.Filters.Add<ActivityLogFilter>();
        mvc.Conventions.Add(new GlobalRoutePrefixConvention("api"));
    })
    // ตั้งค่า JSON อยู่ที่ AstarJson เพื่อให้ client ใช้ชุดเดียวกันเป๊ะ
    // ถ้าสองฝั่งตั้งไม่ตรงกัน ฟิลด์จะกลายเป็นค่าว่างเงียบ ๆ โดยไม่มี error ให้เห็น
    .AddJsonOptions(json => AstarJson.Configure(json.JsonSerializerOptions));

builder.Services.Configure<ApiBehaviorOptions>(o =>
    o.InvalidModelStateResponseFactory = ValidationResponseFactory.Create);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(swagger =>
{
    swagger.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "API Documentation Living Mini",
        Version = "1.0",
        Description = "ระบบบริหารค่าส่วนกลาง A star Living",
    });

    swagger.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
    });

    swagger.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearerAuth" },
        }] = [],
    });

    var xml = Path.Combine(AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) swagger.IncludeXmlComments(xml);
});

// ระหว่างพัฒนาให้หน้าเว็บ Next.js เดิมเรียก API ใหม่ได้ เพื่อเทียบผลทีละหน้าจอ
// บนเครื่องลูกค้าจริงไม่มีเบราว์เซอร์เรียก API จึงไม่ต้องเปิด CORS
const string DevCorsPolicy = "dev-frontend";
builder.Services.AddCors(cors => cors.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddScoped<AstarLiving.Infrastructure.Persistence.SchemaBootstrapper>();
builder.Services.AddScoped<AstarLiving.Infrastructure.Persistence.DemoDataSeeder>();

var app = builder.Build();

// เตรียมฐานข้อมูลก่อนเปิดรับคำขอ
// เครื่องใหม่จะได้ตารางครบพร้อมข้อมูลตั้งต้น ส่วนเครื่องที่มีข้อมูลเดิมอยู่แล้วจะถูกผูกเข้าระบบ
// migration โดยไม่แตะข้อมูล ระหว่างนี้ /api/v1/handshake จะรายงานสถานะ Migrating ให้เครื่องลูกรอ
await using (var scope = app.Services.CreateAsyncScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        AstarLiving.Api.Controllers.MigrationStateAccessor.Current =
            AstarLiving.Shared.Versioning.MigrationState.Migrating;

        var bootstrapper = scope.ServiceProvider
            .GetRequiredService<AstarLiving.Infrastructure.Persistence.SchemaBootstrapper>();

        var outcome = await bootstrapper.EnsureReadyAsync(CancellationToken.None);
        startupLogger.LogInformation("สถานะฐานข้อมูลตอนเริ่มทำงาน: {Outcome}", outcome);

        var seeder = scope.ServiceProvider
            .GetRequiredService<AstarLiving.Infrastructure.Persistence.DemoDataSeeder>();

        // ค่าเริ่มต้นคือไม่ใส่ข้อมูลสาธิต ไซต์จริงจึงปลอดภัยแม้ลืมตั้งค่า
        var includeDemoData = builder.Configuration.GetValue(
            $"{AstarLivingOptions.SectionName}:Seed:DemoData", false);

        var seedPlan = AstarLiving.Infrastructure.Persistence.SeedPlanLoader.Load(
            machineConfigDir, includeDemoData, startupLogger);

        if (await seeder.SeedIfEmptyAsync(seedPlan, CancellationToken.None))
        {
            // รหัสผ่านต้องไม่ถูกเขียนลงบันทึกการทำงานเด็ดขาด
            startupLogger.LogInformation(
                seedPlan.IncludeDemoData
                    ? "สร้างข้อมูลสาธิตแล้ว เข้าสู่ระบบด้วยผู้ใช้ admin หรือ manager"
                    : "สร้างข้อมูลตั้งต้นแล้ว เข้าสู่ระบบด้วยผู้ใช้ {Username}",
                seedPlan.AdminUsername ?? string.Empty);

            AstarLiving.Infrastructure.Persistence.SeedPlanLoader.Consume(
                machineConfigDir, startupLogger);
        }

        AstarLiving.Api.Controllers.MigrationStateAccessor.Current =
            AstarLiving.Shared.Versioning.MigrationState.Ready;
    }
    catch (Exception ex)
    {
        AstarLiving.Api.Controllers.MigrationStateAccessor.Current =
            AstarLiving.Shared.Versioning.MigrationState.Failed;

        // ไม่ปิดโปรแกรม เพื่อให้ /api/v1/handshake ยังตอบได้ว่าเกิดอะไรขึ้น
        startupLogger.LogError(ex, "เตรียมฐานข้อมูลไม่สำเร็จ");
    }
}

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}

// ไฟล์ที่ผู้ใช้อัปโหลด (โลโก้โครงการ) เสิร์ฟที่ /api/images เหมือนระบบเดิม
Directory.CreateDirectory(options.Storage.UploadRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(options.Storage.UploadRoot),
    RequestPath = "/api/images",
});

// feed ของตัวอัปเดตที่เครื่องลูกดึงไป
// เครื่องลูกไม่ต้องมีอินเทอร์เน็ต ดึงจากเครื่องแม่ในวงแลนอย่างเดียว
// ไฟล์ในโฟลเดอร์นี้ถูกวางโดย ServerUpdater หลังเครื่องแม่อัปเดตตัวเองสำเร็จแล้วเท่านั้น
Directory.CreateDirectory(AstarLiving.Api.Controllers.UpdateFeed.Root);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        AstarLiving.Api.Controllers.UpdateFeed.Root),
    RequestPath = "/api/updates",

    // .nupkg ไม่อยู่ในรายการชนิดไฟล์มาตรฐาน ถ้าไม่เปิดตรงนี้จะได้ 404
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
});

// กั้นโปรแกรมที่คุยกันคนละรุ่นสัญญา ต้องอยู่ก่อนชั้นตรวจสิทธิ์
// เพื่อให้ผู้ใช้เห็นข้อความ "ต้องอัปเดตก่อนใช้งาน" แทนข้อความว่าหมดสิทธิ์
app.UseMiddleware<ContractVersionMiddleware>();

// เอกสาร API ต้องอยู่ก่อนชั้นตรวจสิทธิ์
// เพราะ FallbackPolicy มีผลกับทุกคำขอรวมถึงคำขอที่ไม่ได้ผูกกับ endpoint
// ถ้าวางไว้หลัง UseAuthorization หน้า Swagger จะโหลดไฟล์ spec ของตัวเองไม่ได้ (401)
app.UseSwagger(o => o.RouteTemplate = "api/swagger/{documentName}/swagger.json");
app.UseSwaggerUI(o =>
{
    o.RoutePrefix = "api/swagger";
    o.SwaggerEndpoint("/api/swagger/v1/swagger.json", "API Documentation Living Mini");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/api")).ExcludeFromDescription();

await app.RunAsync();

/// <summary>
/// อ่านรหัสผ่านฐานข้อมูลตามลำดับ: ไฟล์ลับของเครื่อง -> ค่าในไฟล์ตั้งค่า
/// ถ้าไฟล์ลับอ่านไม่ได้ จะปล่อยให้ error ขึ้นมาเลย ดีกว่าเงียบแล้วต่อฐานข้อมูลไม่ได้โดยไม่รู้สาเหตุ
/// </summary>
static string ResolveDatabasePassword(DatabaseOptions database) =>
    SecretResolver.ResolveDatabasePassword(database);

/// <summary>
/// เติมกุญแจ JWT จากไฟล์ที่ป้องกันด้วย DPAPI
///
/// ถ้าไม่มีขั้นตอนนี้ เครื่องที่ติดตั้งจริงจะได้กุญแจเป็นค่าว่าง
/// แล้วทุกคำขอที่ต้องยืนยันตัวตนจะพังด้วย IDX10703 โดยที่ตอนพัฒนาไม่เห็นอาการ
/// เพราะ appsettings.Development.json มีกุญแจสำหรับทดสอบอยู่แล้ว
/// </summary>
static void ResolveJwtSecrets(AstarLivingOptions options)
{
    options.Jwt.AccessSecret = ResolveSecret(options.Jwt.AccessSecretFilePath, options.Jwt.AccessSecret);
    options.Jwt.RefreshSecret = ResolveSecret(options.Jwt.RefreshSecretFilePath, options.Jwt.RefreshSecret);
}

/// <summary>
/// อ่านค่าลับตามลำดับ: ไฟล์ลับของเครื่อง -> ค่าในไฟล์ตั้งค่า
/// ถ้าไฟล์ลับมีอยู่แต่อ่านไม่ได้ จะปล่อยให้ error ขึ้นมาเลย
/// ดีกว่าเงียบแล้วใช้ค่าว่างจนพังที่คำขอแรกของผู้ใช้
/// </summary>
static string ResolveSecret(string? secretFilePath, string inlineValue) =>
    SecretResolver.Resolve(secretFilePath, inlineValue);

static async Task WriteErrorAsync(HttpResponse response, int statusCode, string error)
{
    if (response.HasStarted) return;

    response.StatusCode = statusCode;
    response.ContentType = "application/json; charset=utf-8";

    await response.WriteAsync(JsonSerializer.Serialize(new AstarLiving.Shared.Api.ApiError
    {
        StatusCode = statusCode,
        Message = statusCode == 401 ? "กรุณาเข้าสู่ระบบใหม่" : "ไม่มีสิทธิ์เข้าถึงรายการนี้",
        Error = error,
    }, JsonDefaults.ThaiRelaxed));
}

/// <summary>เปิดให้ integration test อ้างถึงคลาส entry point ได้</summary>
public partial class Program;
