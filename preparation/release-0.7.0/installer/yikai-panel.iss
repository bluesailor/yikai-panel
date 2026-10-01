; 易开面板完整环境包安装脚本（Inno Setup 6；版本号由 rebuild 脚本用 /DAppVersion 传入）
;
; 设计要点：
;   · 安装器只负责铺文件。数据库初始化、配置生成、hosts 同步都由面板首次启动时完成，
;     这样失败原因能显示在面板里（带端口占用者、服务日志），而不是消失在安装日志中。
;   · 随包的 php.ini / 数据库页面 php.ini 里写的是开发机根目录 D:\yikai，安装时按实际
;     安装位置改写。其余配置（nginx、apache、mysql、panel.json）都由面板运行时生成。
;   · 网站（wwwroot）与数据库（data）、备份（backups）标记为 uninsneveruninstall：
;     卸载默认保留，用户明确选择“全部删除”时才由代码移除。
;   · 默认不用管理员权限安装（面板自己会在需要时请求提权完成 hosts 与防火墙操作）。

#define AppName "易开面板"
#ifndef AppVersion
  #define AppVersion "0.7.4"
#endif
#define AppPublisher "易开面板"
#define AppExeName "YikaiLocal.exe"
; 负载目录、输出位置可由 ISCC /D 覆盖：#define 包在 #ifndef 里，便于用同一份脚本编译
; “装到随包路径本身”的测试安装器（见 verify-noop-rewrite.ps1），不会覆盖正式产物。
#ifndef Payload
  #define Payload "D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\YikaiPanel"
#endif
#ifndef OutputDir
  #define OutputDir "D:\yikai\packages"
#endif
#ifndef OutputBase
  #define OutputBase "YikaiPanel-" + AppVersion + "-setup-x64"
#endif

[Setup]
AppId={{8F1C0A64-7B2E-4E6B-9C3A-5D1E7F2A9B44}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} {#AppVersion} 安装程序
DefaultDirName={code:GetDefaultDir}
DisableDirPage=no
DisableProgramGroupPage=yes
; 不弹“选择安装语言”：按 Windows 界面语言自动选中文 / 日语，其它系统语言用 [Languages] 里的第一项（英文）
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
DefaultGroupName={#AppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupLogging=yes
Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBase}
SetupIconFile=D:\yikai-soft\dev\yikai-panel\src\Assets\app.ico
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\soft\panel\{#AppExeName}
CloseApplications=no
UsePreviousAppDir=yes

[Languages]
; 顺序有意义：系统语言匹配不到时用第一项。中文系统 → 中文，日文系统 → 日语，其它 → 英文。
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
chinese.CreateDesktopIcon=创建桌面快捷方式
chinese.LaunchApp=启动易开面板
english.CreateDesktopIcon=Create a desktop shortcut
english.LaunchApp=Start Yikai Panel
japanese.CreateDesktopIcon=デスクトップにショートカットを作成
japanese.LaunchApp=易开面板を起動
; “默认网站”页：网站名称与 YikaiCMS 后台账号（面板首次启动时按这里的信息自动装好默认网站）
chinese.SitePageTitle=默认网站
chinese.SitePageSubtitle=填写网站名称和后台管理员账号
chinese.SitePageIntroFull=面板第一次启动时，会用这些信息自动装好随包的 YikaiCMS 网站（yikaicms.localhost）。忘记密码时可以在面板里一键重置。
chinese.SitePageIntroMin=面板第一次启动时，会联网下载最新版 YikaiCMS，并用这些信息装好默认网站（yikaicms.localhost）。忘记密码时可以在面板里一键重置。
chinese.SiteNamePrompt=网站名称：
chinese.AdminUserPrompt=管理员用户名（4-20 位字母或数字）：
chinese.AdminPasswordPrompt=管理员密码（至少 6 位）：
chinese.DefaultSiteName=我的网站
chinese.SiteNameError=请填写网站名称（不超过 100 个字）。
chinese.AdminUserError=管理员用户名只能是 4-20 位英文字母或数字。
chinese.AdminPasswordError=管理员密码至少 6 位（最多 64 位），首尾不能有空格。
english.SitePageTitle=Default site
english.SitePageSubtitle=Site name and admin account
english.SitePageIntroFull=When the panel starts for the first time, it installs the bundled YikaiCMS site (yikaicms.localhost) with these details. If you forget the password, you can reset it from the panel.
english.SitePageIntroMin=When the panel starts for the first time, it downloads the latest YikaiCMS and installs the default site (yikaicms.localhost) with these details. If you forget the password, you can reset it from the panel.
english.SiteNamePrompt=Site name:
english.AdminUserPrompt=Admin user name (4-20 letters or digits):
english.AdminPasswordPrompt=Admin password (at least 6 characters):
english.DefaultSiteName=My Website
english.SiteNameError=Enter a site name (up to 100 characters).
english.AdminUserError=The admin user name must be 4-20 letters or digits.
english.AdminPasswordError=The admin password needs 6-64 characters and cannot start or end with a space.
japanese.SitePageTitle=既定サイト
japanese.SitePageSubtitle=サイト名と管理者アカウント
japanese.SitePageIntroFull=パネルの初回起動時に、同梱の YikaiCMS サイト（yikaicms.localhost）をこの内容で自動インストールします。パスワードを忘れた場合はパネルからリセットできます。
japanese.SitePageIntroMin=パネルの初回起動時に最新の YikaiCMS をダウンロードし、この内容で既定サイト（yikaicms.localhost）をインストールします。パスワードを忘れた場合はパネルからリセットできます。
japanese.SiteNamePrompt=サイト名：
japanese.AdminUserPrompt=管理者ユーザー名（英数字 4-20 文字）：
japanese.AdminPasswordPrompt=管理者パスワード（6 文字以上）：
japanese.DefaultSiteName=マイサイト
japanese.SiteNameError=サイト名を入力してください（100 文字以内）。
japanese.AdminUserError=管理者ユーザー名は英数字 4-20 文字にしてください。
japanese.AdminPasswordError=管理者パスワードは 6-64 文字で、先頭と末尾に空白は使えません。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; 软件组件（含 nginx、三种 PHP、两种 MySQL、Apache、数据库页面、CMS 模板与运行库安装包）
Source: "{#Payload}\soft\*"; DestDir: "{app}\soft"; Flags: ignoreversion recursesubdirs createallsubdirs
; 随包配置（cacert、mime.types、fastcgi_params、伪静态规则、面板默认值/架构说明）
Source: "{#Payload}\config\*"; DestDir: "{app}\config"; Flags: ignoreversion recursesubdirs createallsubdirs
; 默认站点：只补缺失的文件，不动用户已经安装好的 CMS 内容；卸载时不删除
#ifndef NoDefaultSite
Source: "{#Payload}\wwwroot\*"; DestDir: "{app}\wwwroot"; Flags: onlyifdoesntexist recursesubdirs createallsubdirs uninsneveruninstall
#endif

[Dirs]
; nginx 不会自建这两个目录，缺失时会直接启动失败；面板运行期也会写它们
Name: "{app}\soft\nginx\logs"
Name: "{app}\soft\nginx\temp"
Name: "{app}\config\ssl"
Name: "{app}\logs"
Name: "{app}\temp"
; 运行期数据：卸载时保留
#ifdef NoDefaultSite
; 最小包不带默认站点：只建空目录，项目由面板新建时创建
Name: "{app}\wwwroot"
#endif
Name: "{app}\data\mysql57"; Flags: uninsneveruninstall
Name: "{app}\data\mysql80"; Flags: uninsneveruninstall
Name: "{app}\backups"; Flags: uninsneveruninstall

[UninstallDelete]
; 面板运行期会在这些目录里写文件（nginx 的 logs/temp、数据库页面脚本、运行日志、锁与临时文件），
; 它们不在安装清单里，Inno 默认不会删除，卸载后会留下整个 soft 目录。
; 用 [UninstallDelete] 交给 Inno 自己的删除流程（含失败重试），而不是写在 [Code] 里手删。
; config 不在这里：panel.json 与本地根证书要留给重新安装。
Type: filesandordirs; Name: "{app}\soft"
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\temp"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\soft\panel\{#AppExeName}"; IconFilename: "{app}\soft\panel\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\soft\panel\{#AppExeName}"; IconFilename: "{app}\soft\panel\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\soft\panel\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteUserData: Boolean;
  SitePage: TInputQueryWizardPage;

function CreateFileW(lpFileName: String; dwDesiredAccess, dwShareMode: DWORD; lpSecurityAttributes: DWORD;
  dwCreationDisposition, dwFlagsAndAttributes: DWORD; hTemplateFile: THandle): DWORD;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(hObject: DWORD): BOOL; external 'CloseHandle@kernel32.dll stdcall';

const
  GENERIC_READ = $80000000;
  GENERIC_WRITE = $40000000;
  OPEN_EXISTING = 3;
  INVALID_HANDLE = $FFFFFFFF;

function GetDefaultDir(Param: String): String;
begin
  // 没有 D 盘时退到安装盘下的 yikai 目录；客户可以自己改。
  if DirExists('D:\') then Result := 'D:\yikai' else Result := ExpandConstant('{sd}\yikai');
end;

// 面板正在运行时它自己的 EXE 被锁住，安装会失败。用独占方式试打开判断。
function PanelFileLocked(): Boolean;
var
  Handle: DWORD;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\soft\panel\{#AppExeName}');
  if not FileExists(Exe) then begin Result := False; Exit; end;
  Handle := CreateFileW(Exe, GENERIC_READ or GENERIC_WRITE, 0, 0, OPEN_EXISTING, 0, 0);
  if Handle = INVALID_HANDLE then Result := True
  else begin CloseHandle(Handle); Result := False; end;
end;

// 先从托盘之外正常停止环境（面板自己会停 nginx/PHP/MySQL），再结束面板进程。
procedure StopPanel();
var
  Code: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\soft\panel\{#AppExeName}');
  Exec(Exe, '--root "' + ExpandConstant('{app}') + '" --stop', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Answer: Integer;
begin
  Result := '';
  if PanelFileLocked() then
  begin
    // 静默安装（自动化验收、批量部署）不弹窗：直接按“关闭面板并继续”处理。
    if WizardSilent() then begin StopPanel(); Exit; end;
    Answer := MsgBox('易开面板正在运行。安装需要先关闭它，当前运行的环境（网站、数据库）也会一起停止。' + #13#10 + #13#10 +
      '是否现在关闭并继续安装？', mbConfirmation, MB_YESNO);
    if Answer = IDYES then StopPanel()
    else Result := '请先从面板托盘菜单选择“退出并停止环境”，然后重新运行安装程序。';
  end;
end;

function ForwardSlashes(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '/', True);
end;

// 随包 php.ini 里写的是开发机路径 D:/yikai/（正斜杠、带尾部分隔符），改成实际安装位置。
// 路径不对时 PHP 找不到扩展目录和日志目录，网站会直接起不来。
// 三个要点：
//   1) 替换串带尾部分隔符：D:/yikai 是 D:/yikai-test 这类安装目录的前缀，不加边界会把已改好的路径再写坏；
//   2) 没有需要改的内容就不写文件（否则会给配置加上 BOM、改掉时间戳）；
//   3) 装到默认目录 D:\yikai 时，随包路径本身就是正确答案，不能把它当成“残留”。
function RewriteIniPaths(): Boolean;
var
  Files: TArrayOfString;
  Lines: TArrayOfString;
  Index, Line, Leftover: Integer;
  NewRoot, NativeRoot, Text: String;
  Changed: Boolean;
begin
  Result := True;
  SetArrayLength(Files, 4);
  Files[0] := ExpandConstant('{app}\config\phpmyadmin-php.ini');
  Files[1] := ExpandConstant('{app}\soft\php\8.0\php.ini');
  Files[2] := ExpandConstant('{app}\soft\php\8.2\php.ini');
  Files[3] := ExpandConstant('{app}\soft\php\8.5\php.ini');
  NewRoot := ForwardSlashes(ExpandConstant('{app}'));
  NativeRoot := ExpandConstant('{app}');
  for Index := 0 to GetArrayLength(Files) - 1 do
  begin
    if not FileExists(Files[Index]) then Continue;
    if not LoadStringsFromFile(Files[Index], Lines) then begin Result := False; Continue; end;
    Changed := False;
    for Line := 0 to GetArrayLength(Lines) - 1 do
    begin
      if StringChangeEx(Lines[Line], 'D:/yikai/', NewRoot + '/', True) > 0 then Changed := True;
      if StringChangeEx(Lines[Line], 'D:\yikai\', NativeRoot + '\', True) > 0 then Changed := True;
    end;
    if Changed and (not SaveStringsToUTF8FileWithoutBOM(Files[Index], Lines, False)) then
    begin
      Result := False;
      Continue;
    end;
    // 读回核对：路径必须指向本次安装位置；装到非默认目录时还不能残留开发机路径
    if LoadStringsFromFile(Files[Index], Lines) then
    begin
      Text := '';
      for Line := 0 to GetArrayLength(Lines) - 1 do Text := Text + Lines[Line] + #10;
      if NewRoot = 'D:/yikai' then Leftover := 0
      else Leftover := StringChangeEx(Text, 'D:/yikai/', '', False) + StringChangeEx(Text, 'D:\yikai\', '', False);
      if (Leftover > 0) or (StringChangeEx(Text, NewRoot + '/', '', False) = 0) then Result := False;
    end
    else Result := False;
  end;
end;

// ---- 默认网站页 ----------------------------------------------------------------
// 只在全新安装时出现：目标目录里已有 panel.json（升级、保留数据后重装）说明网站已经在了，不再询问。
// 静默安装（自动化验收、批量部署）不显示页面：命令行给了 /SITENAME= /ADMINUSER= /ADMINPASS= 才写设置。
function ExistingPanelConfig(const Dir: String): Boolean;
begin
  Result := FileExists(AddBackslash(Dir) + 'config\panel.json');
end;

function ValidAdminUser(const Value: String): Boolean;
var
  I: Integer;
  C: Char;
begin
  Result := (Length(Value) >= 4) and (Length(Value) <= 20);
  for I := 1 to Length(Value) do
  begin
    C := Value[I];
    if not (((C >= 'a') and (C <= 'z')) or ((C >= 'A') and (C <= 'Z')) or ((C >= '0') and (C <= '9'))) then Result := False;
  end;
end;

// 与 CMS 登录规则一致：登录时会 trim，首尾带空格的密码装进去就登不上
function ValidAdminPassword(const Value: String): Boolean;
begin
  Result := (Length(Value) >= 6) and (Length(Value) <= 64) and (Trim(Value) = Value);
end;

function ValidSiteName(const Value: String): Boolean;
begin
  Result := (Length(Trim(Value)) > 0) and (Length(Trim(Value)) <= 100);
end;

procedure InitializeWizard();
begin
  SitePage := CreateInputQueryPage(wpSelectDir, CustomMessage('SitePageTitle'), CustomMessage('SitePageSubtitle'),
#ifdef NoDefaultSite
    CustomMessage('SitePageIntroMin'));
#else
    CustomMessage('SitePageIntroFull'));
#endif
  // 密码明文显示并预填 admin888：本地开发环境，面板里也会显示可恢复的账号
  SitePage.Add(CustomMessage('SiteNamePrompt'), False);
  SitePage.Add(CustomMessage('AdminUserPrompt'), False);
  SitePage.Add(CustomMessage('AdminPasswordPrompt'), False);
  SitePage.Values[0] := ExpandConstant('{param:SITENAME|' + CustomMessage('DefaultSiteName') + '}');
  SitePage.Values[1] := ExpandConstant('{param:ADMINUSER|admin}');
  SitePage.Values[2] := ExpandConstant('{param:ADMINPASS|admin888}');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (SitePage <> nil) and (PageID = SitePage.ID) and ExistingPanelConfig(WizardDirValue());
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (SitePage = nil) or (CurPageID <> SitePage.ID) then Exit;
  if not ValidSiteName(SitePage.Values[0]) then
  begin
    MsgBox(CustomMessage('SiteNameError'), mbError, MB_OK); Result := False; Exit;
  end;
  if not ValidAdminUser(SitePage.Values[1]) then
  begin
    MsgBox(CustomMessage('AdminUserError'), mbError, MB_OK); Result := False; Exit;
  end;
  if not ValidAdminPassword(SitePage.Values[2]) then
  begin
    MsgBox(CustomMessage('AdminPasswordError'), mbError, MB_OK); Result := False; Exit;
  end;
end;

// 写 config\setup-site.txt（UTF-8，每行 key=value）：面板首次启动时读入、装好默认网站，然后删掉这个文件
procedure WriteSetupSite();
var
  Lines: TArrayOfString;
  Target: String;
begin
  if ExistingPanelConfig(ExpandConstant('{app}')) then Exit;
  if WizardSilent() and (ExpandConstant('{param:SITENAME|}') = '') and (ExpandConstant('{param:ADMINUSER|}') = '') and (ExpandConstant('{param:ADMINPASS|}') = '') then Exit;
  if not (ValidSiteName(SitePage.Values[0]) and ValidAdminUser(SitePage.Values[1]) and ValidAdminPassword(SitePage.Values[2])) then
  begin
    Log('默认网站设置不合规，未写入（面板首次启动时不自动安装默认网站）');
    Exit;
  end;
  SetArrayLength(Lines, 3);
  Lines[0] := 'siteName=' + Trim(SitePage.Values[0]);
  Lines[1] := 'adminUser=' + SitePage.Values[1];
  Lines[2] := 'adminPassword=' + SitePage.Values[2];
  Target := ExpandConstant('{app}\config\setup-site.txt');
  if SaveStringsToUTF8File(Target, Lines, False) then Log('默认网站设置已写入：' + Target)
  else Log('默认网站设置写入失败：' + Target);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
  WriteSetupSite();
  // 自检结果写进安装日志：静默安装（自动化验收）不弹窗，但可以从日志核对
  if not RewriteIniPaths() then
  begin
    Log('PHP 配置路径自检未通过：' + ExpandConstant('{app}') + '\soft\php');
    if not WizardSilent() then
      MsgBox('安装已完成，但 PHP 配置里的路径没有改写成功。' + #13#10 + #13#10 +
        'PHP 可能找不到扩展目录或日志目录，网站会起不来。请检查：' + #13#10 +
        ExpandConstant('{app}') + '\soft\php\<版本>\php.ini' + #13#10 + #13#10 +
        '其中 extension_dir 和 error_log 应当指向 ' + ExpandConstant('{app}') + '。', mbError, MB_OK);
    Exit;
  end;
  Log('PHP 配置路径自检通过：' + ExpandConstant('{app}'));
end;

function InitializeUninstall(): Boolean;
var
  Answer: Integer;
begin
  Result := True;
  DeleteUserData := False;
  if PanelFileLocked() then
  begin
    if UninstallSilent() then StopPanel()
    else
    begin
      Answer := MsgBox('易开面板正在运行。卸载需要先关闭它，当前运行的环境（网站、数据库）也会一起停止。' + #13#10 + #13#10 +
        '是否现在关闭并继续卸载？', mbConfirmation, MB_YESNO);
      if Answer = IDYES then StopPanel()
      else begin Result := False; Exit; end;
    end;
  end;
  // 静默卸载不弹窗：默认保留网站与数据库（删除是不可恢复的操作，只应在用户明确选择时执行）。
  if UninstallSilent() then Exit;
  Answer := MsgBox('是否保留网站文件和数据库？' + #13#10 + #13#10 +
    '是：只移除易开面板程序，保留 wwwroot（网站）、data（数据库）和 backups（备份）。' + #13#10 +
    '否：连同网站、数据库和备份一起删除，无法恢复。', mbConfirmation, MB_YESNO);
  DeleteUserData := (Answer = IDNO);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    if DeleteUserData then
    begin
      DelTree(ExpandConstant('{app}\wwwroot'), True, True, True);
      DelTree(ExpandConstant('{app}\data'), True, True, True);
      DelTree(ExpandConstant('{app}\backups'), True, True, True);
      DelTree(ExpandConstant('{app}\config'), True, True, True);
    end;
  end;
end;
