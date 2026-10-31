#Requires -Version 5.1
<#
.SYNOPSIS
    ประกอบตัวติดตั้งไซต์ (AstarLivingSetup-<ver>.exe) ด้วย Inno Setup 6

.DESCRIPTION
    จัดของให้ครบตามที่ AstarLiving.iss ต้องการ แล้วเรียก ISCC คอมไพล์

    สิ่งที่ตัวติดตั้งต้องมี
      artifacts\api\          บริการเบื้องหลัง
      artifacts\updater\      บริการอัปเดตของเครื่องแม่
      artifacts\setup\        เครื่องมือเขียนค่าตั้งและสร้างกุญแจ
      artifacts\client\       ตัวติดตั้งโปรแกรมหน้าจอที่ Velopack สร้าง
      build\vendor\pgsql\     ฐานข้อมูลที่ตัดไฟล์ไม่ใช้ออกแล้ว
      build\vendor\runtime\   ตัวรันโปรแกรม .NET แบบออฟไลน์

    ของสองอย่างสุดท้ายดาวน์โหลดมาวางไว้ครั้งเดียว ไม่ได้สร้างใหม่ทุกรอบ
    สคริปต์นี้จึงตรวจว่ามีอยู่ แล้วบอกวิธีหามาถ้าไม่มี

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/Build-Installer.ps1 -Version 1.0.1
#>
[CmdletBinding()]
param(
    [string] $Version = '1.0.0',
    [string] $Configuration = 'Release',

    # ข้ามการ build ใหม่ ใช้ของที่อยู่ใน artifacts อยู่แล้ว
    [switch] $SkipBuild,

    # ข้ามการแพ็กโปรแกรมหน้าจอด้วย vpk (ขั้นตอนที่ช้าที่สุด)
    [switch] $SkipClient
)

$ErrorActionPreference = 'Stop'

$buildRoot = $PSScriptRoot
$repoRoot = Split-Path $buildRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
$vendor = Join-Path $buildRoot 'vendor'
$packDir = Join-Path $artifacts 'pack'

function Find-Iscc {
    # ตัวติดตั้งของ Inno Setup รุ่นใหม่ลงในโปรไฟล์ผู้ใช้ ไม่ใช่ Program Files เสมอไป
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    throw "ไม่พบ ISCC.exe ติดตั้งด้วย: winget install --id JRSoftware.InnoSetup --exact"
}

$iscc = Find-Iscc
Write-Host "ใช้ตัวคอมไพล์ที่ $iscc"

# --- 1. ตรวจของที่ต้องเตรียมไว้ล่วงหน้า -------------------------------------
Write-Host '[1/5] ตรวจส่วนประกอบที่ต้องเตรียมไว้ล่วงหน้า'

$pgsql = Join-Path $vendor 'pgsql'
if (-not (Test-Path (Join-Path $pgsql 'bin\postgres.exe'))) {
    throw @"
ไม่พบฐานข้อมูลที่ $pgsql
ดาวน์โหลด PostgreSQL 16 แบบ binaries zip แล้วแตกไว้ที่นั่น
"@
}

$runtimeDir = Join-Path $vendor 'runtime'
$runtimeFiles = @(
    'windowsdesktop-runtime-8.0-win-x64.exe',
    'aspnetcore-runtime-8.0-win-x64.exe'
)

foreach ($file in $runtimeFiles) {
    $path = Join-Path $runtimeDir $file
    if (-not (Test-Path $path)) {
        throw @"
ไม่พบตัวรันโปรแกรมที่ $path
ดาวน์โหลดด้วย
  curl -sSL -o "$path" https://aka.ms/dotnet/8.0/<ชื่อชุด>-win-x64.exe
"@
    }

    # ไฟล์ที่โหลดไม่สำเร็จมักเป็นหน้า error ขนาดไม่กี่กิโล ซึ่งคอมไพล์ผ่านแต่ติดตั้งพัง
    $size = (Get-Item $path).Length
    if ($size -lt 10MB) {
        throw "ไฟล์ $file มีขนาดเพียง $([math]::Round($size/1MB,2)) MB ซึ่งเล็กผิดปกติ น่าจะโหลดมาไม่ครบ"
    }
}

# --- 2. build ส่วนที่ตัวติดตั้งต้องใช้ ---------------------------------------
if (-not $SkipBuild) {
    Write-Host '[2/5] build บริการและเครื่องมือ'

    $targets = @(
        @{ Project = 'src/AstarLiving.Api'; Out = Join-Path $artifacts 'api' },
        @{ Project = 'src/AstarLiving.ServerUpdater'; Out = Join-Path $artifacts 'updater' },
        @{ Project = 'src/AstarLiving.Setup'; Out = Join-Path $artifacts 'setup' },
        @{ Project = 'src/AstarLiving.Client'; Out = Join-Path $artifacts 'client-bin' }
    )

    foreach ($target in $targets) {
        if (Test-Path $target.Out) { Remove-Item $target.Out -Recurse -Force }

        & dotnet publish (Join-Path $repoRoot $target.Project) `
            -c $Configuration -r win-x64 --self-contained false `
            -p:Version=$Version -o $target.Out --nologo --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "build $($target.Project) ไม่สำเร็จ" }
    }
}
else {
    Write-Host '[2/5] ข้ามการ build ตามที่สั่ง'
}

# --- 3. แพ็กโปรแกรมหน้าจอด้วย Velopack --------------------------------------
$clientStage = Join-Path $artifacts 'client'
$clientSetupName = 'AstarLiving-win-Setup.exe'

if (-not $SkipClient) {
    Write-Host '[3/5] แพ็กโปรแกรมหน้าจอ'

    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        throw 'ไม่พบคำสั่ง vpk ติดตั้งด้วย: dotnet tool install -g vpk'
    }

    if (Test-Path $packDir) { Remove-Item $packDir -Recurse -Force }
    New-Item -ItemType Directory -Path $packDir -Force | Out-Null

    & vpk pack `
        --packId AstarLiving `
        --packVersion $Version `
        --packDir (Join-Path $artifacts 'client-bin') `
        --mainExe AstarLiving.exe `
        --packTitle 'A star Living' `
        --packAuthors 'A STAR TECHNOLOGY CO., LTD.' `
        --outputDir $packDir
    if ($LASTEXITCODE -ne 0) { throw 'แพ็กโปรแกรมหน้าจอไม่สำเร็จ' }
}
else {
    Write-Host '[3/5] ข้ามการแพ็กโปรแกรมหน้าจอตามที่สั่ง'
}

# --- 4. จัดของให้ตรงกับที่ .iss มองหา ---------------------------------------
Write-Host '[4/5] จัดของให้ตรงกับที่ตัวติดตั้งมองหา'

# vpk ตั้งชื่อไฟล์ตามรหัสแพ็กเกจ และวางไว้รวมกับ nupkg
# .iss ต้องการไฟล์เดียวในโฟลเดอร์ของตัวเอง จึงต้องคัดมาวางแยก
if (Test-Path $clientStage) { Remove-Item $clientStage -Recurse -Force }
New-Item -ItemType Directory -Path $clientStage -Force | Out-Null

$built = Join-Path $packDir $clientSetupName
if (-not (Test-Path $built)) {
    throw "ไม่พบ $clientSetupName ที่ $packDir — แพ็กโปรแกรมหน้าจอก่อน หรืออย่าใส่ -SkipClient"
}

Copy-Item $built (Join-Path $clientStage $clientSetupName) -Force

# --- 5. คอมไพล์ตัวติดตั้ง ----------------------------------------------------
Write-Host '[5/5] คอมไพล์ตัวติดตั้ง'

$iss = Join-Path $buildRoot 'installer\AstarLiving.iss'

& $iscc "/DAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) { throw 'คอมไพล์ตัวติดตั้งไม่สำเร็จ' }

$output = Join-Path $artifacts "AstarLivingSetup-$Version.exe"

Write-Host ''
Write-Host "เสร็จแล้ว: $output"
Write-Host ('  ขนาด {0:N1} MB' -f ((Get-Item $output).Length / 1MB))
Write-Host ('  SHA-256 {0}' -f (Get-FileHash $output -Algorithm SHA256).Hash)
