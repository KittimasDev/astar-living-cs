#Requires -Version 5.1
<#
.SYNOPSIS
    สำรองฐานข้อมูลก่อนถอนการติดตั้ง

.DESCRIPTION
    เรียกจาก [UninstallRun] ของตัวติดตั้ง เป็นกันชนสุดท้ายไม่ให้ข้อมูลลูกค้าหาย

    เหตุผลที่ต้องมีสคริปต์นี้แทนการเรียก pg_dump ตรง ๆ จากตัวติดตั้ง
    ฐานข้อมูลตั้ง scram-sha-256 ไว้ pg_dump จึงต้องการรหัสผ่าน
    แต่ตอนถอนการติดตั้งไม่มีหน้าจอให้กรอก (รันแบบ runhidden)
    ถ้าเรียกตรง ๆ มันจะล้มเหลวเงียบ ๆ แล้วลูกค้าจะเสียข้อมูลโดยไม่มีใครรู้

    รหัสผ่านถูกเก็บด้วย DPAPI ที่ db.secret อ่านได้ด้วย AstarLiving.Setup read-secret
    ซึ่งต้องรันด้วยสิทธิ์ผู้ดูแล — ตัวถอนการติดตั้งรันด้วยสิทธิ์นั้นอยู่แล้ว

    ไฟล์สำรองไปอยู่ใน ProgramData ไม่ใช่ Documents ของผู้ใช้
    เพราะตอนถอนการติดตั้งโปรแกรมทำงานในชื่อผู้ดูแล ไม่ใช่ผู้ใช้จริง
    ไฟล์จึงจะไปโผล่ในโปรไฟล์ที่ลูกค้าหาไม่เจอ
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InstallRoot,

    [int] $Port = 54329,
    [string] $Database = 'astarliving',
    [string] $AppUser = 'asliving_app'
)

# ห้ามหยุดการถอนการติดตั้งเพราะสำรองข้อมูลไม่ได้
# ถ้าฐานข้อมูลถูกถอนไปก่อนแล้ว หรือบริการหยุดอยู่ การถอนต้องเดินต่อได้
$ErrorActionPreference = 'Continue'

$configDir = Join-Path $env:ProgramData 'AstarLiving\config'
$backupDir = Join-Path $env:ProgramData 'AstarLiving\backups'
$secretPath = Join-Path $configDir 'db.secret'
$setupExe = Join-Path $InstallRoot 'setup\AstarLiving.Setup.exe'
$pgDump = Join-Path $InstallRoot 'pgsql\bin\pg_dump.exe'

foreach ($required in @($setupExe, $pgDump, $secretPath)) {
    if (-not (Test-Path $required)) {
        Write-Warning "ไม่พบ $required จึงข้ามการสำรองข้อมูล"
        exit 0
    }
}

if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path $backupDir "uninstall-$stamp.dump"

try {
    $password = & $setupExe read-secret --file $secretPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($password)) {
        Write-Warning 'อ่านรหัสผ่านฐานข้อมูลไม่ได้ จึงข้ามการสำรองข้อมูล'
        exit 0
    }

    $env:PGPASSWORD = $password

    & $pgDump -h 127.0.0.1 -p $Port -U $AppUser -d $Database -Fc -f $target

    if ($LASTEXITCODE -eq 0 -and (Test-Path $target)) {
        Write-Host "สำรองข้อมูลไว้ที่ $target"
    }
    else {
        Write-Warning "สำรองข้อมูลไม่สำเร็จ (รหัส $LASTEXITCODE)"
    }
}
finally {
    # ห้ามทิ้งรหัสผ่านไว้ในตัวแปรสภาพแวดล้อมของโปรเซสที่ยังทำงานต่อ
    $env:PGPASSWORD = $null
}

exit 0
