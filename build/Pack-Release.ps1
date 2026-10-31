#Requires -Version 5.1
<#
.SYNOPSIS
    ประกอบชุดปล่อยเวอร์ชันขึ้นเซิร์ฟเวอร์อัปเดต

.DESCRIPTION
    ผลลัพธ์คือโฟลเดอร์ที่พร้อม rsync ขึ้น nginx ประกอบด้วย
      - manifest.json       รายการสิ่งที่ปล่อย พร้อมลายเซ็น
      - api/*.zip           บริการเบื้องหลัง
      - *.nupkg             ชุดของโปรแกรมหน้าจอที่ Velopack สร้าง
      - releases.win.json   รายการเวอร์ชันที่ Velopack อ่าน

    ลำดับที่ห้ามสลับ: ต้องเรียก vpk download ก่อน vpk pack เสมอ
    ไม่อย่างนั้นจะไม่มีไฟล์ส่วนต่าง ลูกค้าจะต้องโหลดเต็มก้อนทุกครั้ง (35 MB แทน 3 MB)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/Pack-Release.ps1 -Version 1.0.1 -FeedUrl https://updates.example.com/astarliving
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $Channel = 'stable',
    [string] $Configuration = 'Release',

    # ที่อยู่ของ feed เดิมบนเซิร์ฟเวอร์ ใช้ดึงเวอร์ชันก่อนหน้ามาคำนวณไฟล์ส่วนต่าง
    [string] $FeedUrl,

    # ไฟล์กุญแจส่วนตัวสำหรับเซ็น manifest (PKCS#8 แบบ PEM)
    # ไม่ระบุ = ยังไม่เซ็น ซึ่งเครื่องแม่จะปฏิเสธ จึงใช้ได้เฉพาะตอนทดสอบในเครื่อง
    [string] $SigningKeyPath,

    [string] $Notes
)

$ErrorActionPreference = 'Stop'

$buildRoot = $PSScriptRoot
$repoRoot = Split-Path $buildRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
$releaseDir = Join-Path $artifacts "release/$Channel"
$packDir = Join-Path $artifacts 'pack'

Write-Host "ประกอบชุดปล่อยเวอร์ชัน $Version ช่องทาง $Channel"

# --- 1. ตรวจว่าชุดทดสอบผ่านก่อนเสมอ ---------------------------------------
Write-Host '[1/6] รันชุดทดสอบ'
& dotnet test (Join-Path $repoRoot 'AstarLiving.sln') -c $Configuration --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'ชุดทดสอบไม่ผ่าน จึงหยุดการประกอบ' }

# --- 2. build ทุกส่วน -------------------------------------------------------
Write-Host '[2/6] build บริการเบื้องหลังและโปรแกรมหน้าจอ'

foreach ($dir in @($releaseDir, $packDir)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

New-Item -ItemType Directory -Path (Join-Path $releaseDir 'api') -Force | Out-Null

$apiOut = Join-Path $artifacts 'api'
$clientOut = Join-Path $artifacts 'client-bin'
$updaterOut = Join-Path $artifacts 'updater'

foreach ($target in @(
        @{ Project = 'src/AstarLiving.Api'; Out = $apiOut },
        @{ Project = 'src/AstarLiving.Client'; Out = $clientOut },
        @{ Project = 'src/AstarLiving.ServerUpdater'; Out = $updaterOut })) {

    if (Test-Path $target.Out) { Remove-Item $target.Out -Recurse -Force }

    & dotnet publish (Join-Path $repoRoot $target.Project) `
        -c $Configuration -r win-x64 --self-contained false `
        -p:Version=$Version -o $target.Out --nologo
    if ($LASTEXITCODE -ne 0) { throw "build $($target.Project) ไม่สำเร็จ" }
}

# --- 3. บีบบริการเบื้องหลังเป็น zip พร้อมค่าแฮช ------------------------------
Write-Host '[3/6] บีบบริการเบื้องหลัง'

$apiZipName = "AstarLiving.Api-$Version-win-x64.zip"
$apiZipPath = Join-Path $releaseDir "api/$apiZipName"

Compress-Archive -Path (Join-Path $apiOut '*') -DestinationPath $apiZipPath -CompressionLevel Optimal
$apiHash = (Get-FileHash $apiZipPath -Algorithm SHA256).Hash

# --- 4. แพ็กโปรแกรมหน้าจอด้วย Velopack --------------------------------------
Write-Host '[4/6] แพ็กโปรแกรมหน้าจอ'

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    throw 'ไม่พบคำสั่ง vpk ติดตั้งด้วย: dotnet tool install -g vpk'
}

# ดึงเวอร์ชันก่อนหน้ามาก่อน เพื่อให้คำนวณไฟล์ส่วนต่างได้
if ($FeedUrl) {
    Write-Host '      ดึงเวอร์ชันก่อนหน้ามาคำนวณส่วนต่าง'
    & vpk download http --url "$FeedUrl/$Channel" --outputDir $packDir
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'ดึงเวอร์ชันก่อนหน้าไม่ได้ รอบนี้จะได้เฉพาะไฟล์เต็มก้อน'
    }
}
else {
    Write-Warning 'ไม่ได้ระบุ FeedUrl จึงไม่มีไฟล์ส่วนต่าง ลูกค้าจะโหลดเต็มก้อน'
}

& vpk pack `
    --packId AstarLiving `
    --packVersion $Version `
    --packDir $clientOut `
    --mainExe AstarLiving.exe `
    --packTitle 'A star Living' `
    --packAuthors 'A STAR TECHNOLOGY CO., LTD.' `
    --outputDir $packDir `
    --delta BestSize
if ($LASTEXITCODE -ne 0) { throw 'แพ็กโปรแกรมหน้าจอไม่สำเร็จ' }

Copy-Item (Join-Path $packDir '*') $releaseDir -Force -Exclude '*.pdb'

# --- 5. สร้าง manifest แล้วเซ็น ---------------------------------------------
Write-Host '[5/6] สร้างรายการปล่อยเวอร์ชันและเซ็น'

$clientFiles = Get-ChildItem $releaseDir -File |
    Where-Object { $_.Extension -in '.nupkg', '.json' -and $_.Name -ne 'manifest.json' } |
    ForEach-Object { $_.Name }

$manifest = [ordered]@{
    version         = $Version
    channel         = $Channel
    releasedAt      = (Get-Date).ToUniversalTime().ToString('o')
    apiPackage      = "api/$apiZipName"
    apiSha256       = $apiHash
    clientFiles     = @($clientFiles)
    contractVersion = 1
    notes           = $Notes
}

$payload = $manifest | ConvertTo-Json -Depth 5 -Compress

$signature = ''
if ($SigningKeyPath) {
    if (-not (Test-Path $SigningKeyPath)) { throw "ไม่พบไฟล์กุญแจที่ $SigningKeyPath" }

    Add-Type -AssemblyName System.Security
    $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
    $ecdsa.ImportFromPem((Get-Content $SigningKeyPath -Raw))

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $signature = [Convert]::ToBase64String(
        $ecdsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256))

    $publicKey = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
    Write-Host "      กุญแจสาธารณะที่ต้องตั้งใน machine.json: $publicKey"
}
else {
    Write-Warning 'ไม่ได้เซ็น manifest เครื่องแม่ของลูกค้าจะปฏิเสธชุดนี้'
}

$signed = [ordered]@{ payload = $payload; signature = $signature }

# เขียนแบบไม่มี BOM เพราะฝั่งที่อ่านตรวจลายเซ็นทีละไบต์
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText(
    (Join-Path $releaseDir 'manifest.json'),
    ($signed | ConvertTo-Json -Depth 5),
    $utf8NoBom)

# --- 6. สรุป ----------------------------------------------------------------
Write-Host ''
Write-Host "[6/6] เสร็จแล้ว ผลลัพธ์อยู่ที่ $releaseDir"

Get-ChildItem $releaseDir -Recurse -File | ForEach-Object {
    '  {0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB)
}

Write-Host ''
Write-Host 'ขั้นตอนถัดไป: คัดลอกทั้งโฟลเดอร์ขึ้นเซิร์ฟเวอร์'
Write-Host '  ไฟล์ manifest.json ต้องเป็นไฟล์สุดท้ายที่คัดลอกเสมอ'
Write-Host '  เพราะเครื่องแม่ของลูกค้าเริ่มอัปเดตทันทีที่เห็นไฟล์นี้'
