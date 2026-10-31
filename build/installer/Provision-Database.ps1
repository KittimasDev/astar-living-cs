<#
.SYNOPSIS
    สร้างและตั้งค่าฐานข้อมูล PostgreSQL บนเครื่องแม่ของลูกค้า

.DESCRIPTION
    ตัวติดตั้งเรียกสคริปต์นี้หนึ่งครั้งตอนติดตั้งโหมดเครื่องแม่
    ทำตั้งแต่สร้างคลัสเตอร์ ตั้งค่าความปลอดภัย ลงทะเบียนบริการ Windows
    สร้างบัญชีที่โปรแกรมใช้ และเขียนไฟล์ตั้งค่าให้ API อ่าน

    ข้อตัดสินใจด้านความปลอดภัยที่สำคัญ
    - PostgreSQL ฟังเฉพาะ 127.0.0.1 ไม่เปิดออก LAN เลย
      เครื่องลูกข่ายคุยกับ API ผ่าน HTTPS เท่านั้น ไม่ได้ต่อฐานข้อมูลตรง
      ตัดความเสี่ยงจากการสแกนพอร์ต 5432 ซึ่งเป็นเป้าหมายยอดนิยมของมัลแวร์เรียกค่าไถ่
    - บัญชีที่โปรแกรมใช้ไม่ใช่ superuser
    - ใช้พอร์ตที่ไม่ใช่ค่ามาตรฐาน เพื่อไม่ชนกับ PostgreSQL ตัวอื่นที่อาจมีอยู่

    สคริปต์นี้รันซ้ำได้ ถ้าพบคลัสเตอร์เดิมอยู่แล้วจะไม่สร้างทับ

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Provision-Database.ps1 -InstallRoot "C:\Program Files\AstarLiving"
#>
[CmdletBinding()]
param(
    [string] $InstallRoot = "$env:ProgramFiles\AstarLiving",
    [string] $DataRoot = "$env:ProgramData\AstarLiving",
    [int]    $Port = 54329,
    [string] $AppDatabase = 'astarliving',
    [string] $AppUser = 'asliving_app',
    [string] $SuperUser = 'asliving_super',
    [string] $ServiceName = 'astarliving-postgres',

    # ใช้ตอนทดสอบบนเครื่องนักพัฒนา: ไม่ลงทะเบียนเป็นบริการ Windows
    [switch] $NoService,

    # พอร์ตที่บริการเบื้องหลังจะเปิด ต้องตรงกับกฎไฟร์วอลล์และ client.json
    [int] $ApiPort = 5080
)

$ErrorActionPreference = 'Stop'

# PowerShell 5.1 ใส่ BOM ให้ทุกไฟล์ที่เขียนด้วย -Encoding utf8
# แต่ตัวอ่านไฟล์ตั้งค่าของ PostgreSQL ไม่รองรับ BOM และจะปฏิเสธทั้งไฟล์
# จึงต้องเขียนเองด้วยตัวเข้ารหัสที่ไม่ใส่ BOM
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-PlainText {
    param([string] $Path, [string] $Content)
    [IO.File]::WriteAllText($Path, $Content, $Utf8NoBom)
}

function Add-PlainText {
    param([string] $Path, [string] $Content)
    $existing = if (Test-Path $Path) { [IO.File]::ReadAllText($Path) } else { '' }
    [IO.File]::WriteAllText($Path, $existing + $Content, $Utf8NoBom)
}

$pgRoot = Join-Path $InstallRoot 'pgsql'
$pgBin = Join-Path $pgRoot 'bin'
$dataDir = Join-Path $DataRoot 'pgdata'
$configDir = Join-Path $DataRoot 'config'
$logDir = Join-Path $DataRoot 'logs\pg'
$setupExe = Join-Path $InstallRoot 'setup\AstarLiving.Setup.exe'

foreach ($required in @('initdb.exe', 'pg_ctl.exe', 'psql.exe', 'pg_isready.exe')) {
    if (-not (Test-Path (Join-Path $pgBin $required))) {
        throw "ไม่พบ $required ใน $pgBin"
    }
}

New-Item -ItemType Directory -Path $DataRoot, $configDir, $logDir -Force | Out-Null

# ─── 1. สร้างคลัสเตอร์ ───────────────────────────────────────────────────────
$clusterExists = Test-Path (Join-Path $dataDir 'PG_VERSION')

if ($clusterExists) {
    Write-Host "พบฐานข้อมูลเดิมอยู่แล้วที่ $dataDir จึงข้ามขั้นตอนสร้างคลัสเตอร์"
}
else {
    Write-Host "กำลังสร้างคลัสเตอร์ฐานข้อมูลที่ $dataDir"

    $superPassword = & $setupExe generate-password
    $passwordFile = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($passwordFile, $superPassword)

    try {
        # locale เป็น C เพื่อให้ผลการเรียงลำดับเหมือนกันทุกเครื่อง
        # ส่วนการเรียงภาษาไทยใช้ collation ของ ICU ที่สร้างเพิ่มด้านล่างแทน
        & (Join-Path $pgBin 'initdb.exe') `
            -D $dataDir `
            -U $SuperUser `
            --pwfile=$passwordFile `
            -E UTF8 `
            --locale=C `
            --auth-host=scram-sha-256 `
            --auth-local=scram-sha-256 | Out-Null

        if ($LASTEXITCODE -ne 0) { throw "initdb ล้มเหลว (รหัส $LASTEXITCODE)" }
    }
    finally {
        Remove-Item $passwordFile -Force -ErrorAction SilentlyContinue
    }

    # เก็บรหัส superuser ไว้ใช้ตอนสร้างบัญชี สำรองข้อมูล และกู้คืน
    # ถ้าไม่เก็บ จะไม่มีทางเข้าถึงคลัสเตอร์นี้ได้อีกเลย
    & $setupExe protect-secret --file (Join-Path $configDir 'pgsuper.secret') --value $superPassword | Out-Null
}

# ─── 2. ตั้งค่าฐานข้อมูล ─────────────────────────────────────────────────────
$logDirForConf = $logDir -replace '\\', '/'

$settings = @"

# ─── ตั้งค่าโดยตัวติดตั้ง A star Living ───
listen_addresses = '127.0.0.1'
port = $Port
timezone = 'Asia/Bangkok'
log_timezone = 'Asia/Bangkok'
lc_messages = 'C'
password_encryption = 'scram-sha-256'

# ปรับให้เหมาะกับเครื่องสำนักงานทั่วไป หน่วยความจำ 8-16 GB
shared_buffers = 512MB
effective_cache_size = 2GB
work_mem = 8MB
maintenance_work_mem = 128MB
max_connections = 50
max_wal_size = 2GB
checkpoint_timeout = 15min

# ปิดการอัปเดตชื่อโปรเซส ช่วยเรื่องประสิทธิภาพบน Windows อย่างชัดเจน
update_process_title = off

logging_collector = on
log_directory = '$logDirForConf'
log_filename = 'pg-%Y-%m-%d.log'
log_rotation_age = 1d
log_truncate_on_rotation = on
log_min_duration_statement = 2000
"@

$confPath = Join-Path $dataDir 'postgresql.conf'
if (-not (Select-String -Path $confPath -Pattern 'ตั้งค่าโดยตัวติดตั้ง A star Living' -Quiet)) {
    Add-PlainText -Path $confPath -Content $settings
    Write-Host "เขียนค่าตั้งลง postgresql.conf แล้ว"
}

# เปิดเฉพาะ loopback เท่านั้น
$hba = @"
# ไฟล์นี้ถูกเขียนโดยตัวติดตั้ง A star Living
# ฐานข้อมูลไม่เปิดออก LAN โดยตั้งใจ เครื่องลูกข่ายให้คุยผ่าน API เท่านั้น
host  all  all  127.0.0.1/32  scram-sha-256
host  all  all  ::1/128       scram-sha-256
"@
Write-PlainText -Path (Join-Path $dataDir 'pg_hba.conf') -Content $hba

# ─── 3. เริ่มฐานข้อมูล ───────────────────────────────────────────────────────
$pgCtl = Join-Path $pgBin 'pg_ctl.exe'

if ($NoService) {
    Write-Host "เริ่มฐานข้อมูลแบบไม่ลงทะเบียนบริการ (โหมดทดสอบ)"

    # ไม่ใช้ pg_ctl start ในโหมดนี้ เพราะมันรอให้เซิร์ฟเวอร์พร้อมแล้วไม่คืนการควบคุม
    # เมื่อถูกเรียกจากสคริปต์ที่เปลี่ยนทางเอาต์พุตไว้ ทำให้ตัวติดตั้งค้าง
    # โหมดใช้งานจริงไม่มีปัญหานี้เพราะใช้ Windows Service
    $postgres = Join-Path $pgBin 'postgres.exe'

    Start-Process -FilePath $postgres `
        -ArgumentList @('-D', $dataDir, '-p', $Port) `
        -WindowStyle Hidden | Out-Null
}
else {
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if (-not $existing) {
        Write-Host "ลงทะเบียนบริการ Windows ชื่อ $ServiceName"

        # ใช้บัญชีบริการเสมือน จึงไม่ต้องจัดการรหัสผ่านของบัญชีผู้ใช้
        & $pgCtl register -N $ServiceName -D $dataDir -S auto -o "-p $Port" | Out-Null

        & sc.exe description $ServiceName "PostgreSQL สำหรับระบบบริหารนิติบุคคล A star Living" | Out-Null

        # ให้ Windows พยายามเริ่มบริการใหม่เองเมื่อหยุดผิดปกติ
        & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
    }

    Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
}

# ─── 4. รอจนกว่าจะรับการเชื่อมต่อได้ ─────────────────────────────────────────
$pgIsReady = Join-Path $pgBin 'pg_isready.exe'
$ready = $false

for ($attempt = 1; $attempt -le 60; $attempt++) {
    & $pgIsReady -h 127.0.0.1 -p $Port | Out-Null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 1
}

if (-not $ready) {
    throw "ฐานข้อมูลไม่ตอบสนองภายในเวลาที่กำหนด ตรวจดู log ที่ $logDir"
}

Write-Host "ฐานข้อมูลพร้อมรับการเชื่อมต่อที่พอร์ต $Port"

# ─── 5. สร้างบัญชีและฐานข้อมูลของโปรแกรม ────────────────────────────────────
$secretPath = Join-Path $configDir 'db.secret'
$configPath = Join-Path $configDir 'machine.json'
$psql = Join-Path $pgBin 'psql.exe'

if (Test-Path $configPath) {
    Write-Host "พบการตั้งค่าเดิมอยู่แล้ว จึงข้ามขั้นตอนสร้างบัญชี"
}
else {
    # การต่อผ่าน TCP ต้องใส่รหัสผ่านเสมอเพราะตั้ง auth เป็น scram-sha-256
    # ส่งผ่านตัวแปรสภาพแวดล้อมเพื่อไม่ให้รหัสไปปรากฏในบรรทัดคำสั่ง
    $env:PGPASSWORD = & $setupExe read-secret --file (Join-Path $configDir 'pgsuper.secret')

    # เก็บรหัสลงไฟล์ก่อน แล้วอ่านกลับมาใช้
    # วิธีนี้ทำให้รหัสในฐานข้อมูลกับในไฟล์เป็นค่าเดียวกันแน่นอน ไม่มีทางหลุดจากกัน
    & $setupExe protect-secret --file $secretPath --value (& $setupExe generate-password) | Out-Null
    $appPassword = & $setupExe read-secret --file $secretPath

    $escaped = $appPassword -replace "'", "''"

    # ใช้ ALTER เมื่อมีบัญชีอยู่แล้ว เพื่อให้รหัสตรงกับไฟล์เสมอ
    # ถ้าการติดตั้งครั้งก่อนล้มกลางคัน การรันซ้ำจะซ่อมให้เอง
    $sql = @"
DO `$`$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '$AppUser') THEN
        ALTER ROLE $AppUser LOGIN PASSWORD '$escaped';
    ELSE
        CREATE ROLE $AppUser LOGIN PASSWORD '$escaped';
    END IF;
END
`$`$;
"@

    $sql | & $psql -w -h 127.0.0.1 -p $Port -U $SuperUser -d postgres -v ON_ERROR_STOP=1 -q
    if ($LASTEXITCODE -ne 0) { throw "สร้างบัญชีผู้ใช้ฐานข้อมูลไม่สำเร็จ" }

    $dbExists = & $psql -w -h 127.0.0.1 -p $Port -U $SuperUser -d postgres -t -A -c "SELECT 1 FROM pg_database WHERE datname = '$AppDatabase'"
    if (-not $dbExists) {
        & $psql -w -h 127.0.0.1 -p $Port -U $SuperUser -d postgres -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE $AppDatabase OWNER $AppUser"
        if ($LASTEXITCODE -ne 0) { throw "สร้างฐานข้อมูลไม่สำเร็จ" }
    }

    # ถอนสิทธิ์ของ PUBLIC ออก เหลือเฉพาะเจ้าของฐานข้อมูล
    & $psql -w -h 127.0.0.1 -p $Port -U $SuperUser -d postgres -q -c "REVOKE ALL ON DATABASE $AppDatabase FROM PUBLIC" | Out-Null

    # collation สำหรับเรียงชื่อภาษาไทย ใช้กับคอลัมน์ข้อความเมื่อจำเป็น
    & $psql -w -h 127.0.0.1 -p $Port -U $SuperUser -d $AppDatabase -q `
        -c "CREATE COLLATION IF NOT EXISTS th_th (provider = icu, locale = 'th-TH')" | Out-Null

    & $setupExe write-config `
        --config-dir $configDir `
        --port $Port `
        --api-port $ApiPort `
        --database $AppDatabase `
        --username $AppUser `
        --password $appPassword | Out-Null

    $env:PGPASSWORD = $null
    Write-Host "สร้างบัญชี $AppUser และฐานข้อมูล $AppDatabase เรียบร้อย"
}

Write-Host ""
Write-Host "ติดตั้งฐานข้อมูลเสร็จสมบูรณ์"
Write-Host "  โปรแกรม : $pgRoot"
Write-Host "  ข้อมูล   : $dataDir"
Write-Host "  ตั้งค่า   : $configDir"
