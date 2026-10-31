; ตัวติดตั้ง A star Living
;
; คอมไพล์ด้วย Inno Setup 6 ขึ้นไป
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 AstarLiving.iss
;
; ตัวติดตั้งนี้ทำทุกอย่างให้จบในครั้งเดียว ไม่ต้องลง Docker และไม่ต้องตั้งค่าอะไรเพิ่ม
;   - ติดตั้ง .NET 8 runtime แบบออฟไลน์
;   - ติดตั้ง PostgreSQL ที่มากับตัวติดตั้ง และสร้างฐานข้อมูลให้
;   - ลงทะเบียน API เป็นบริการ Windows
;   - เปิดพอร์ตใน Windows Firewall ให้เครื่องลูกข่ายเข้าถึงได้

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "A star Living"
#define Publisher "A STAR TECHNOLOGY CO., LTD."
#define ServiceName "AstarLiving API"
#define UpdaterServiceName "AstarLiving ServerUpdater"
#define PgServiceName "astarliving-postgres"
#define ApiPort 5080
#define PgPort 54329

[Setup]
AppId={{9E2C7A41-5D63-4F0B-9E1A-3C8B5D2F7A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\AstarLiving
DefaultGroupName={#AppName}
OutputBaseFilename=AstarLivingSetup-{#AppVersion}
OutputDir=..\..\artifacts
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
DisableDirPage=no
UninstallDisplayName={#AppName} {#AppVersion}

[Languages]
Name: "thai"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "server"; Description: "เครื่องแม่ข่าย (ติดตั้งฐานข้อมูลและบริการ)"
Name: "client"; Description: "เครื่องลูกข่าย (ใช้งานอย่างเดียว)"

[Components]
Name: "runtime"; Description: "ตัวรันโปรแกรม .NET"; Types: server client; Flags: fixed
Name: "app";     Description: "โปรแกรมหน้าจอ";        Types: server client; Flags: fixed
Name: "pg";      Description: "ฐานข้อมูล PostgreSQL";  Types: server
Name: "api";     Description: "บริการเบื้องหลัง";       Types: server

[Tasks]
; ไม่ติ๊กมาให้โดยตั้งใจ ไซต์จริงต้องไม่มีข้อมูลปลอมปนอยู่ในฐานข้อมูล
Name: "demodata"; Description: "ใส่ข้อมูลตัวอย่างสำหรับทดลองใช้ (ห้ามเลือกที่ไซต์ลูกค้าจริง)"; \
    GroupDescription: "ข้อมูลตั้งต้น"; Components: api; Flags: unchecked

[Files]
; ตัวรันโปรแกรมแบบออฟไลน์ ลูกค้าจึงไม่ต้องมีอินเทอร์เน็ตตอนติดตั้ง
Source: "..\vendor\runtime\*";  DestDir: "{tmp}\runtime"; Flags: deleteafterinstall; Components: runtime

; ฐานข้อมูลที่ตัดไฟล์ไม่ใช้ออกแล้ว ราว 75 MB
Source: "..\vendor\pgsql\*";    DestDir: "{app}\pgsql";   Flags: recursesubdirs createallsubdirs; Components: pg

; บริการเบื้องหลังและเครื่องมือตั้งค่า
Source: "..\..\artifacts\api\*";   DestDir: "{app}\api\current"; Flags: recursesubdirs createallsubdirs; Components: api

; บริการอัปเดตของเครื่องแม่ ดึงเวอร์ชันใหม่มาติดตั้งเองและเฝ้าดูว่าบริการหลักยังทำงานอยู่
Source: "..\..\artifacts\updater\*"; DestDir: "{app}\updater"; Flags: recursesubdirs createallsubdirs; Components: api
Source: "..\..\artifacts\setup\*"; DestDir: "{app}\setup";       Flags: recursesubdirs createallsubdirs; Components: pg api

; สคริปต์ติดตั้งฐานข้อมูล
Source: "Provision-Database.ps1"; DestDir: "{app}\setup"; Components: pg
Source: "Backup-Database.ps1";    DestDir: "{app}\setup"; Components: pg

; โปรแกรมหน้าจอที่ติดตั้งแบบรายผู้ใช้ผ่าน Velopack
; vpk ตั้งชื่อไฟล์ผลลัพธ์ตามรหัสแพ็กเกจ ไม่ใช่ Setup.exe เฉย ๆ
Source: "..\..\artifacts\client\AstarLiving-win-Setup.exe"; DestDir: "{app}\bootstrap"; Components: app

[Dirs]
; ข้อมูลทั้งหมดอยู่นอกโฟลเดอร์ติดตั้ง เพราะตัวอัปเดตจะแทนที่โฟลเดอร์ติดตั้งทั้งก้อน
Name: "{commonappdata}\AstarLiving";          Permissions: users-readexec
Name: "{commonappdata}\AstarLiving\config"
Name: "{commonappdata}\AstarLiving\logs"
Name: "{commonappdata}\AstarLiving\backups"
Name: "{commonappdata}\AstarLiving\uploads"
; โฟลเดอร์ที่บริการอัปเดตวางไฟล์ไว้ให้เครื่องลูกดึง
Name: "{commonappdata}\AstarLiving\updates"

[Run]
; ลำดับสำคัญ: ตัวรันโปรแกรมต้องมาก่อน ไม่อย่างนั้น initdb และ API จะเริ่มไม่ได้
Filename: "{tmp}\runtime\windowsdesktop-runtime-8.0-win-x64.exe"; \
    Parameters: "/install /quiet /norestart"; \
    StatusMsg: "กำลังติดตั้งตัวรันโปรแกรม..."; \
    Components: runtime; Flags: waituntilterminated

Filename: "{tmp}\runtime\aspnetcore-runtime-8.0-win-x64.exe"; \
    Parameters: "/install /quiet /norestart"; \
    StatusMsg: "กำลังติดตั้งส่วนประกอบของบริการ..."; \
    Components: api; Flags: waituntilterminated

Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup\Provision-Database.ps1"" -InstallRoot ""{app}"" -Port {#PgPort} -ApiPort {#ApiPort}"; \
    StatusMsg: "กำลังสร้างฐานข้อมูล อาจใช้เวลาสักครู่..."; \
    Components: pg; Flags: waituntilterminated runhidden

; โหมดสาธิตต้องถูกเปิดอย่างชัดเจน ค่าเริ่มต้นของระบบคือปิด
Filename: "{app}\setup\AstarLiving.Setup.exe";     Parameters: "enable-demo-data --config-dir ""{commonappdata}\AstarLiving\config""";     StatusMsg: "กำลังเตรียมข้อมูลตัวอย่าง...";     Components: api; Tasks: demodata; Flags: runhidden waituntilterminated

; ต้องเก็บบัญชีผู้ดูแลก่อนบริการเริ่มทำงานครั้งแรกเสมอ
; เพราะบริการอ่านไฟล์นี้ตอนสร้างฐานข้อมูลเปล่า ถ้าไม่มีจะไม่มีใครล็อกอินได้เลย
Filename: "{app}\setup\AstarLiving.Setup.exe"; \
    Parameters: "set-admin --config-dir ""{commonappdata}\AstarLiving\config"" --username ""{code:GetAdminUser}"" --password ""{code:GetAdminPassword}"" --project ""{code:GetProjectName}"""; \
    StatusMsg: "กำลังตั้งบัญชีผู้ดูแลระบบ..."; \
    Components: api; Tasks: not demodata; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; \
    Parameters: "create ""{#ServiceName}"" binPath= ""{app}\api\current\AstarLiving.Api.exe"" start= auto"; \
    Components: api; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; \
    Parameters: "config ""{#ServiceName}"" depend= {#PgServiceName}"; \
    Components: api; Flags: runhidden waituntilterminated

; ให้ Windows เริ่มบริการใหม่เองเมื่อหยุดผิดปกติ
Filename: "{sys}\sc.exe"; \
    Parameters: "failure ""{#ServiceName}"" reset= 86400 actions= restart/5000/restart/15000/restart/60000"; \
    Components: api; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; Parameters: "start ""{#ServiceName}"""; \
    Components: api; Flags: runhidden waituntilterminated

; บริการอัปเดต ต้องเริ่มหลังบริการหลักเสมอ เพราะมันเฝ้าดูบริการหลักอยู่
Filename: "{sys}\sc.exe"; \
    Parameters: "create ""{#UpdaterServiceName}"" binPath= ""{app}\updater\AstarLiving.ServerUpdater.exe"" start= auto"; \
    Components: api; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; \
    Parameters: "failure ""{#UpdaterServiceName}"" reset= 86400 actions= restart/30000/restart/60000/restart/120000"; \
    Components: api; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; Parameters: "start ""{#UpdaterServiceName}"""; \
    Components: api; Flags: runhidden waituntilterminated

; เปิดพอร์ตให้เครื่องลูกข่ายเฉพาะเครือข่ายส่วนตัวและโดเมน
Filename: "netsh.exe"; \
    Parameters: "advfirewall firewall add rule name=""A star Living API"" dir=in action=allow protocol=TCP localport={#ApiPort} profile=private,domain"; \
    Components: api; Flags: runhidden waituntilterminated

; ติดตั้งโปรแกรมหน้าจอในชื่อผู้ใช้ที่กำลังล็อกอินอยู่ ไม่ใช่ผู้ดูแลที่รันตัวติดตั้ง
; ถ้าไม่ใส่ runasoriginaluser โปรแกรมจะไปอยู่ในโปรไฟล์ของผู้ดูแลและผู้ใช้จริงจะไม่เห็น
Filename: "{app}\bootstrap\AstarLiving-win-Setup.exe"; Parameters: "--silent"; \
    StatusMsg: "กำลังติดตั้งโปรแกรมหน้าจอ..."; \
    Components: app; Flags: runasoriginaluser waituntilterminated

[UninstallRun]
; สำรองข้อมูลก่อนถอนการติดตั้งเสมอ ถือเป็นกันชนสุดท้ายไม่ให้ข้อมูลลูกค้าหาย
;
; ต้องผ่านสคริปต์ ห้ามเรียก pg_dump ตรง ๆ
; เพราะฐานข้อมูลตั้ง scram-sha-256 ไว้ pg_dump จึงต้องการรหัสผ่าน
; ตอนถอนการติดตั้งไม่มีหน้าจอให้กรอก การเรียกตรง ๆ จะล้มเหลวเงียบ ๆ
; แล้วลูกค้าจะเสียข้อมูลโดยไม่มีใครรู้ว่ากันชนนี้ไม่เคยทำงาน
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup\Backup-Database.ps1"" -InstallRoot ""{app}"" -Port {#PgPort}"; \
    RunOnceId: "BackupBeforeUninstall"; Flags: runhidden waituntilterminated; Components: pg

; หยุดบริการอัปเดตก่อนเสมอ ไม่อย่างนั้นมันจะสั่งเริ่มบริการหลักกลับมาระหว่างถอนการติดตั้ง
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#UpdaterServiceName}"""; RunOnceId: "StopUpdater"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#UpdaterServiceName}"""; RunOnceId: "DeleteUpdater"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#ServiceName}"""; RunOnceId: "StopApi"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#ServiceName}"""; RunOnceId: "DeleteApi"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "stop {#PgServiceName}"; RunOnceId: "StopPg"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#PgServiceName}"; RunOnceId: "DeletePg"; Flags: runhidden
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""A star Living API"""; RunOnceId: "RemoveFirewall"; Flags: runhidden

[UninstallDelete]
; ลบเฉพาะโฟลเดอร์ติดตั้งเท่านั้น
;
; ห้ามอ้างถึง {commonappdata}\AstarLiving ที่นี่เด็ดขาด
; เพราะเป็นที่เก็บฐานข้อมูล ไฟล์สำรอง และการตั้งค่าทั้งหมดของลูกค้า
; รายการนี้ถูกตรวจทุกครั้งก่อนปล่อยเวอร์ชันใหม่
Type: filesandordirs; Name: "{app}\api"
Type: filesandordirs; Name: "{app}\updater"
Type: filesandordirs; Name: "{app}\bootstrap"

[Code]
// หน้าจอให้กรอกที่อยู่เครื่องแม่ตอนติดตั้งโหมดเครื่องลูกข่าย
var
  ServerPage: TInputQueryWizardPage;
  AdminPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ServerPage := CreateInputQueryPage(wpSelectComponents,
    'ที่อยู่เครื่องแม่ข่าย',
    'ระบุเครื่องที่ติดตั้งฐานข้อมูลไว้',
    'กรอกชื่อเครื่องหรือหมายเลขไอพีของเครื่องแม่ข่าย เช่น 192.168.1.10');

  ServerPage.Add('ที่อยู่เครื่องแม่ข่าย:', False);
  ServerPage.Values[0] := '192.168.1.10';

  // บัญชีผู้ดูแลของแต่ละไซต์ต้องต่างกัน จึงถามตอนติดตั้งแทนการฝังไว้ในโปรแกรม
  AdminPage := CreateInputQueryPage(ServerPage.ID,
    'บัญชีผู้ดูแลระบบ',
    'ตั้งบัญชีสำหรับเข้าใช้งานครั้งแรก',
    'บัญชีนี้ใช้เข้าระบบครั้งแรกเพื่อสร้างโครงการและผู้ใช้งานคนอื่น' + #13#10 +
    'รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร และควรเปลี่ยนหลังส่งมอบ');

  AdminPage.Add('ชื่อโครงการ:', False);
  AdminPage.Add('ชื่อผู้ใช้:', False);
  AdminPage.Add('รหัสผ่าน:', True);
  AdminPage.Add('ยืนยันรหัสผ่าน:', True);

  // รับค่าจากบรรทัดคำสั่งได้ เพื่อให้ติดตั้งแบบไม่มีหน้าจอได้ เช่น
  //   AstarLivingSetup-1.0.1.exe /VERYSILENT /PROJECT="นิติบุคคล..." /ADMINUSER=admin /ADMINPASS=...
  AdminPage.Values[0] := ExpandConstant('{param:PROJECT|โครงการของฉัน}');
  AdminPage.Values[1] := ExpandConstant('{param:ADMINUSER|admin}');
  AdminPage.Values[2] := ExpandConstant('{param:ADMINPASS|}');
  AdminPage.Values[3] := AdminPage.Values[2];
end;

// หยุดบริการที่ล็อกไฟล์อยู่ ก่อนตัวติดตั้งเริ่มคัดลอกไฟล์
//
// ใช้ net stop ไม่ใช่ sc stop เพราะ sc คืนค่าทันทีโดยไม่รอให้บริการหยุดจริง
// ส่วน net รอจนกว่าจะหยุดเสร็จ ซึ่งเป็นสิ่งที่ต้องการตรงนี้
// รหัสที่คืนมาไม่ต้องสนใจ บริการที่ยังไม่มีหรือหยุดอยู่แล้วก็คืนค่าไม่เป็นศูนย์เหมือนกัน
procedure StopServiceAndWait(const ServiceId: string);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\net.exe'), 'stop "' + ServiceId + '" /y',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// การติดตั้งแบบไม่มีหน้าจอต้องไม่เงียบเมื่อข้อมูลไม่ครบ
// ถ้าปล่อยผ่าน จะได้ระบบที่ติดตั้งจบแต่ไม่มีใครล็อกอินได้ ซึ่งหาสาเหตุยากที่หน้างาน
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  // ตอนติดตั้งครั้งแรกไม่มีอะไรให้หยุด แต่ตอนลงทับของเดิมจำเป็นมาก
  //
  // ถ้าไม่หยุดก่อน ตัวติดตั้งจะเขียนทับ AstarLiving.Api.exe กับ postgres.exe ไม่ได้
  // แล้วขึ้นกล่อง "DeleteFile failed; code 5" ให้ผู้ใช้เลือกเองกลางคัน
  // ซึ่งถ้าเผลอกด Skip จะได้ระบบที่ไฟล์ปนกันระหว่างสองเวอร์ชัน
  //
  // ลำดับสำคัญ: หยุดตัวอัปเดตก่อน ไม่งั้นมันจะเห็นบริการหลักหยุดแล้วสั่งเริ่มใหม่
  // ส่วนทั้งสามตัวถูกเริ่มคืนให้เองในขั้นตอนหลังคัดลอกไฟล์
  if WizardIsComponentSelected('api') then
  begin
    StopServiceAndWait('{#UpdaterServiceName}');
    StopServiceAndWait('{#ServiceName}');
  end;

  if WizardIsComponentSelected('pg') then
    StopServiceAndWait('{#PgServiceName}');

  if not WizardSilent then
    Exit;

  if not WizardIsComponentSelected('api') then
    Exit;

  if WizardIsTaskSelected('demodata') then
    Exit;

  if Length(AdminPage.Values[2]) < 8 then
    Result := 'การติดตั้งแบบไม่มีหน้าจอต้องระบุ /ADMINPASS ที่ยาวอย่างน้อย 8 ตัวอักษร';
end;

function GetAdminUser(Param: string): string;
begin
  Result := AdminPage.Values[1];
end;

function GetAdminPassword(Param: string): string;
begin
  Result := AdminPage.Values[2];
end;

function GetProjectName(Param: string): string;
begin
  Result := AdminPage.Values[0];
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  // หน้าที่อยู่เครื่องแม่จำเป็นเฉพาะเครื่องลูกข่าย เครื่องแม่ใช้ 127.0.0.1 อยู่แล้ว
  if PageID = ServerPage.ID then
    Result := WizardIsComponentSelected('api');

  // หน้าบัญชีผู้ดูแลจำเป็นเฉพาะเครื่องแม่ที่ไม่ได้เลือกข้อมูลสาธิต
  // โหมดสาธิตมีบัญชีสำเร็จรูปมาให้อยู่แล้ว
  if PageID = AdminPage.ID then
    Result := (not WizardIsComponentSelected('api')) or WizardIsTaskSelected('demodata');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID <> AdminPage.ID then
    Exit;

  if Trim(AdminPage.Values[0]) = '' then
  begin
    MsgBox('กรุณากรอกชื่อโครงการ', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if Length(Trim(AdminPage.Values[1])) < 3 then
  begin
    MsgBox('ชื่อผู้ใช้ต้องมีอย่างน้อย 3 ตัวอักษร', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if Length(AdminPage.Values[2]) < 8 then
  begin
    MsgBox('รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if AdminPage.Values[2] <> AdminPage.Values[3] then
  begin
    MsgBox('รหัสผ่านทั้งสองช่องไม่ตรงกัน', mbError, MB_OK);
    Result := False;
    Exit;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigDir: string;
  ClientConfig: string;
begin
  if CurStep = ssPostInstall then
  begin
    ConfigDir := ExpandConstant('{commonappdata}\AstarLiving\config');
    ForceDirectories(ConfigDir);

    if WizardIsComponentSelected('api') then
      ClientConfig := '{"ServerUrl":"http://127.0.0.1:' + IntToStr({#ApiPort}) + '"}'
    else
      ClientConfig := '{"ServerUrl":"http://' + ServerPage.Values[0] + ':' + IntToStr({#ApiPort}) + '"}';

    SaveStringToFile(ConfigDir + '\client.json', ClientConfig, False);
  end;
end;
