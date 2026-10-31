using System.Text.Json;
using System.Text.Json.Nodes;
using AstarLiving.Shared.Security;

// เครื่องมือบรรทัดคำสั่งที่ตัวติดตั้งเรียกใช้
// แยกออกมาจากตัว API เพราะต้องทำงานตอนที่ API ยังไม่ถูกติดตั้งเป็นบริการ
//
//   AstarLiving.Setup generate-password
//   AstarLiving.Setup write-config --config-dir <dir> --port <port> --database <db> --username <user> --password <pw> [--api-port <port>]
//   AstarLiving.Setup protect-secret --file <path> --value <secret>
//   AstarLiving.Setup read-secret --file <path>
//   AstarLiving.Setup set-admin --config-dir <dir> --username <user> --password <pw> [--project <name>]
//   AstarLiving.Setup enable-demo-data --config-dir <dir>

return args.FirstOrDefault() switch
{
    "generate-password" => GeneratePassword(),
    "write-config" => WriteConfig(args),
    "protect-secret" => ProtectSecret(args),
    "read-secret" => ReadSecret(args),
    "set-admin" => SetAdmin(args),
    "enable-demo-data" => EnableDemoData(args),
    _ => Usage(),
};

static int GeneratePassword()
{
    Console.WriteLine(MachineSecret.GeneratePassword());
    return 0;
}

/// <summary>
/// เขียนไฟล์ตั้งค่าของเครื่องแม่
///
/// machine.json เก็บค่าที่ไม่เป็นความลับและถูกอ่านทุกครั้งที่ API เริ่มทำงาน
/// ส่วนรหัสผ่านถูกเก็บแยกในไฟล์ db.secret ที่ป้องกันด้วย DPAPI
///
/// ถ้ามี machine.json อยู่แล้วจะรวมค่าใหม่เข้าไปโดยไม่ทับค่าที่ผู้ดูแลเคยแก้ไว้
/// ข้อนี้สำคัญมากตอนอัปเดตเวอร์ชัน
/// </summary>
static int WriteConfig(string[] args)
{
    var options = ParseOptions(args);

    var configDir = Require(options, "config-dir");
    var port = Require(options, "port");
    var database = Require(options, "database");
    var username = Require(options, "username");
    var password = Require(options, "password");
    var host = options.GetValueOrDefault("host", "127.0.0.1");
    var siteName = options.GetValueOrDefault("site-name", "A star Living");
    var apiPort = options.GetValueOrDefault("api-port", "5080");

    Directory.CreateDirectory(configDir);

    var secretPath = Path.Combine(configDir, "db.secret");
    MachineSecret.Write(secretPath, password);

    var configPath = Path.Combine(configDir, "machine.json");

    var root = File.Exists(configPath)
        ? JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? []
        : [];

    var section = root["AstarLiving"]?.AsObject();
    if (section is null)
    {
        section = [];
        root["AstarLiving"] = section;
    }

    var db = section["Database"]?.AsObject();
    if (db is null)
    {
        db = [];
        section["Database"] = db;
    }

    db["Host"] = host;
    db["Port"] = int.Parse(port);
    db["Database"] = database;
    db["Username"] = username;
    db["SecretFilePath"] = secretPath;

    // ไม่เขียนรหัสผ่านลง machine.json เด็ดขาด
    db.Remove("Password");

    // กุญแจสำหรับเซ็น token
    // ถ้าไม่สร้างตรงนี้ เครื่องที่ติดตั้งจริงจะได้กุญแจเป็นค่าว่าง
    // แล้วทุกคำขอที่ต้องยืนยันตัวตนจะพังทันที โดยที่ตอนพัฒนาไม่เห็นอาการ
    // เพราะ appsettings.Development.json มีกุญแจสำหรับทดสอบอยู่แล้ว
    var jwt = section["Jwt"]?.AsObject();
    if (jwt is null)
    {
        jwt = [];
        section["Jwt"] = jwt;
    }

    jwt["AccessSecretFilePath"] = EnsureSecretFile(Path.Combine(configDir, "jwt-access.secret"));
    jwt["RefreshSecretFilePath"] = EnsureSecretFile(Path.Combine(configDir, "jwt-refresh.secret"));

    // กุญแจต้องไม่อยู่ในไฟล์ตั้งค่าที่อ่านได้ด้วยตาเปล่า
    jwt.Remove("AccessSecret");
    jwt.Remove("RefreshSecret");

    var site = section["Site"]?.AsObject();
    if (site is null)
    {
        site = [];
        section["Site"] = site;
    }

    // ชื่อไซต์ผู้ดูแลแก้เองได้ จึงตั้งให้เฉพาะตอนที่ยังไม่มีค่า
    site["Name"] ??= siteName;

    // พอร์ตที่บริการเปิดต้องมาจากตัวติดตั้งที่เดียว
    //
    // ถ้าปล่อยให้ appsettings.json ในแพ็กเกจเป็นคนกำหนด มันจะไม่มีวันตรงกับ
    // กฎไฟร์วอลล์และที่อยู่ใน client.json ที่ตัวติดตั้งเขียนไว้
    // ผลคือบริการขึ้นปกติแต่โปรแกรมหน้าจอหาไม่เจอ ซึ่งวินิจฉัยยากมากที่หน้างาน
    var kestrel = root["Kestrel"]?.AsObject();
    if (kestrel is null)
    {
        kestrel = [];
        root["Kestrel"] = kestrel;
    }

    var endpoints = kestrel["Endpoints"]?.AsObject();
    if (endpoints is null)
    {
        endpoints = [];
        kestrel["Endpoints"] = endpoints;
    }

    endpoints["Lan"] = new JsonObject { ["Url"] = $"http://0.0.0.0:{apiPort}" };

    File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"เขียนไฟล์ตั้งค่าแล้ว: {configPath}");
    Console.WriteLine($"เก็บรหัสผ่านฐานข้อมูลแบบป้องกันแล้วที่: {secretPath}");
    Console.WriteLine($"บริการจะเปิดที่พอร์ต: {apiPort}");
    return 0;
}

/// <summary>
/// เปิดโหมดข้อมูลสาธิตใน machine.json
///
/// ค่าเริ่มต้นของระบบคือปิด ไซต์จริงจึงไม่มีทางได้ข้อมูลปลอมแม้ลืมตั้งค่า
/// การเปิดต้องเป็นการกระทำที่ตั้งใจและเห็นได้ชัดเสมอ
/// </summary>
static int EnableDemoData(string[] args)
{
    var options = ParseOptions(args);
    var configDir = Require(options, "config-dir");
    var configPath = Path.Combine(configDir, "machine.json");

    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine($"ไม่พบไฟล์ตั้งค่าที่ {configPath}");
        return 1;
    }

    var root = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? [];

    var section = root["AstarLiving"]?.AsObject();
    if (section is null)
    {
        section = [];
        root["AstarLiving"] = section;
    }

    var seed = section["Seed"]?.AsObject();
    if (seed is null)
    {
        seed = [];
        section["Seed"] = seed;
    }

    seed["DemoData"] = true;

    File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine("เปิดโหมดข้อมูลสาธิตแล้ว");
    return 0;
}

/// <summary>
/// เก็บบัญชีผู้ดูแลระบบที่ช่างผู้ติดตั้งกำหนด ไว้ให้บริการหลักอ่านตอนเริ่มทำงานครั้งแรก
///
/// เก็บแบบป้องกันด้วย DPAPI ไม่ใช่ข้อความธรรมดา และบริการหลักจะลบไฟล์นี้ทิ้ง
/// ทันทีที่สร้างบัญชีเสร็จ รหัสผ่านจึงไม่ค้างอยู่บนดิสก์ของลูกค้า
///
/// ที่ต้องทำแบบนี้แทนการฝังรหัสผ่านไว้ในโค้ด เพราะไซต์จริงทุกไซต์
/// ต้องมีรหัสผ่านของตัวเอง ไม่ใช่รหัสเดียวกันทั้งประเทศ
/// </summary>
static int SetAdmin(string[] args)
{
    var options = ParseOptions(args);

    var configDir = Require(options, "config-dir");
    var username = Require(options, "username");
    var password = Require(options, "password");
    var project = options.GetValueOrDefault("project", string.Empty);

    if (password.Length < 8)
    {
        Console.Error.WriteLine("รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร");
        return 1;
    }

    Directory.CreateDirectory(configDir);

    var payload = JsonSerializer.Serialize(new
    {
        username,
        password,
        project = string.IsNullOrWhiteSpace(project) ? null : project,
    });

    var path = Path.Combine(configDir, "admin-bootstrap.secret");
    MachineSecret.Write(path, payload);

    Console.WriteLine($"เก็บบัญชีผู้ดูแลระบบแบบป้องกันแล้วที่: {path}");
    return 0;
}

/// <summary>
/// สร้างไฟล์กุญแจให้ถ้ายังไม่มี แล้วคืนที่อยู่ของไฟล์
///
/// ต้องไม่สร้างทับของเดิมเมื่อรันซ้ำ ไม่อย่างนั้นการติดตั้งซ้ำหรือการซ่อมระบบ
/// จะทำให้ผู้ใช้ทุกคนที่กำลังทำงานค้างอยู่หลุดจากระบบพร้อมกัน
/// </summary>
static string EnsureSecretFile(string path)
{
    if (!File.Exists(path))
    {
        // 64 ตัวอักษรคือ 512 บิต มากพอสำหรับ HMAC-SHA256 ที่ใช้เซ็น token
        MachineSecret.Write(path, MachineSecret.GeneratePassword(64));
    }

    return path;
}

/// <summary>
/// เก็บค่าลับหนึ่งค่าลงไฟล์
/// ใช้กับรหัสผ่าน superuser ของ PostgreSQL ซึ่งต้องใช้ตอนสร้างบัญชีและตอนสำรองข้อมูล
/// </summary>
static int ProtectSecret(string[] args)
{
    var options = ParseOptions(args);

    MachineSecret.Write(Require(options, "file"), Require(options, "value"));
    return 0;
}

/// <summary>ใช้ตอนแก้ปัญหาหน้างาน เพื่อยืนยันว่าไฟล์ลับอ่านกลับได้บนเครื่องนั้น</summary>
static int ReadSecret(string[] args)
{
    var options = ParseOptions(args);
    var file = Require(options, "file");

    Console.WriteLine(MachineSecret.Read(file));
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("""
        วิธีใช้:
          AstarLiving.Setup generate-password
          AstarLiving.Setup write-config --config-dir <dir> --port <port> --database <db> --username <user> --password <pw> [--host <host>] [--site-name <name>]
          AstarLiving.Setup protect-secret --file <path> --value <secret>
          AstarLiving.Setup read-secret --file <path>
        """);
    return 1;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 1; i < args.Length - 1; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;

        result[args[i][2..]] = args[i + 1];
        i++;
    }

    return result;
}

static string Require(Dictionary<string, string> options, string key) =>
    options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"ต้องระบุพารามิเตอร์ --{key}");
