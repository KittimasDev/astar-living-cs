<#
.SYNOPSIS
    แตกไฟล์ PostgreSQL แบบไม่มีตัวติดตั้ง แล้วตัดส่วนที่ไม่ได้ใช้ออก

.DESCRIPTION
    ไฟล์ต้นฉบับจาก EDB มีขนาดราว 300 MB แต่ระบบนี้ใช้จริงไม่ถึงครึ่ง
    การตัดช่วยให้ตัวติดตั้งเล็กลงมาก ซึ่งเป็นโจทย์หลักของการย้ายออกจาก Docker

    สิ่งที่ตัดออก: เอกสาร, ไฟล์สำหรับนักพัฒนา (include, *.lib, *.pdb),
    ชุดภาษาที่ไม่ใช่ไทยและอังกฤษ และโปรแกรมบรรทัดคำสั่งที่ระบบไม่ได้เรียกใช้

    สิ่งที่ห้ามตัด: ไฟล์ทั้งหมดใน lib และ share/extension เพราะ PostgreSQL
    โหลดส่วนขยายตามชื่อไฟล์ ตัดผิดตัวเดียวฐานข้อมูลจะสตาร์ทไม่ขึ้น

.EXAMPLE
    pwsh ./Trim-Postgres.ps1 -ZipPath ../vendor/postgresql-16.10-windows-x64-binaries.zip -OutputPath ../vendor/pgsql
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ZipPath,
    [Parameter(Mandatory)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ZipPath)) {
    throw "ไม่พบไฟล์ $ZipPath"
}

# โปรแกรมที่ระบบเรียกใช้จริง นอกเหนือจากนี้ตัดได้ทั้งหมด
$keepExecutables = @(
    'postgres.exe'        # ตัวฐานข้อมูลเอง
    'pg_ctl.exe'          # ลงทะเบียนและควบคุมบริการ
    'initdb.exe'          # สร้างคลัสเตอร์ครั้งแรก
    'psql.exe'            # ใช้ตอนแก้ปัญหาหน้างาน
    'pg_dump.exe'         # สำรองข้อมูล
    'pg_dumpall.exe'
    'pg_restore.exe'      # กู้คืน
    'pg_isready.exe'      # ตรวจว่าพร้อมรับการเชื่อมต่อหรือยัง
    'pg_config.exe'
    'pg_controldata.exe'
    'createdb.exe'
    'vacuumdb.exe'
    'reindexdb.exe'
)

$suffix = [guid]::NewGuid().ToString('N')
$stagingPath = Join-Path ([IO.Path]::GetTempPath()) "astar-pg-extract-$suffix"

Write-Host "กำลังแตกไฟล์ไปที่ $stagingPath"
Expand-Archive -Path $ZipPath -DestinationPath $stagingPath -Force

$source = Join-Path $stagingPath 'pgsql'
if (-not (Test-Path $source)) {
    throw "ไม่พบโฟลเดอร์ pgsql ในไฟล์บีบอัด"
}

$sizeBefore = (Get-ChildItem $source -Recurse -File | Measure-Object Length -Sum).Sum

# ─── ตัดโฟลเดอร์ที่ไม่ใช้ทั้งก้อน ─────────────────────────────────────────────
foreach ($folder in @('doc', 'include', 'symbols', 'pgAdmin 4', 'StackBuilder')) {
    $path = Join-Path $source $folder
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "  ตัดโฟลเดอร์ $folder"
    }
}

# ─── ตัดไฟล์สำหรับนักพัฒนา ──────────────────────────────────────────────────
Get-ChildItem (Join-Path $source 'lib') -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in @('.lib', '.pdb') } |
    Remove-Item -Force

# ─── เก็บเฉพาะภาษาไทยและอังกฤษ ──────────────────────────────────────────────
$localePath = Join-Path $source 'share/locale'
if (Test-Path $localePath) {
    Get-ChildItem $localePath -Directory |
        Where-Object { $_.Name -notin @('th', 'en', 'en_US') } |
        Remove-Item -Recurse -Force
}

# ─── เก็บเฉพาะโปรแกรมที่ใช้จริง (ไฟล์ dll ต้องเก็บไว้ทั้งหมด) ─────────────────
$binPath = Join-Path $source 'bin'
Get-ChildItem $binPath -File -Filter '*.exe' |
    Where-Object { $_.Name -notin $keepExecutables } |
    ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Verbose "  ตัดโปรแกรม $($_.Name)"
    }

$sizeAfter = (Get-ChildItem $source -Recurse -File | Measure-Object Length -Sum).Sum

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Recurse -Force
}

$parent = Split-Path $OutputPath -Parent
if ($parent -and -not (Test-Path $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

Move-Item $source $OutputPath
Remove-Item $stagingPath -Recurse -Force -ErrorAction SilentlyContinue

$before = [math]::Round($sizeBefore / 1MB, 1)
$after = [math]::Round($sizeAfter / 1MB, 1)

Write-Host ""
Write-Host "เสร็จแล้ว: $OutputPath"
Write-Host "  ก่อนตัด $before MB  ->  หลังตัด $after MB"
