; codeusagemonit setup.exe
;
; Inno Setup 6.3 or newer. ChineseSimplified.isl sits beside this script
; (user-contributed translation; the 6.7 installer does not ship it).
; x64compatible is the 6.3 replacement for the deprecated x64 identifier:
; x64 Windows, and ARM64 Windows running this x64 build under emulation.
; CopyFile replaced FileCopy in 6.4; 6.3 still compiles the old name.
; Build from the repo:  .\source\Windows\build.ps1 -Installer
; Or, after build.ps1 has produced the executables in the repo root:
;   ISCC.exe source\Windows\installer\codeusagemonit.iss
;
#if Ver < EncodeVer(6, 3, 0)
  #error codeusagemonit setup requires Inno Setup 6.3 or newer
#endif

; AppId is stable on purpose: upgrades replace program files and keep
; %LOCALAPPDATA%\codeusagemonit. Do not change it.
;
; Data: this installer writes installed.txt and HKCU\Software\codeusagemonit\InstallPath.
; The app then stores settings in %LOCALAPPDATA%\codeusagemonit (not next to the exe),
; so Program Files works. The zip has neither marker and keeps using <exe>\data.
; portable.txt next to the exe forces that portable layout.

#ifndef MyAppVersion
  #define MyAppVersion "1.3.0"
#endif
#ifndef BuildDir
  #define BuildDir "..\..\.."
#endif
#ifndef RepoRoot
  #define RepoRoot "..\..\.."
#endif

#define MyAppName "codeusagemonit"
#define MyAppPublisher "codeusagemonit"
#define MyAppURL "https://github.com/fanchengliu/codeusagemonit"

[Setup]
AppId={{A4E8C1D7-6F2B-4C9A-8E15-3B7D9F0A2C64}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppComments=Windows tray monitor for AI coding tool quotas and usage
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=codeusagemonit setup
VersionInfoCopyright=Copyright (C) 2026 codeusagemonit contributors
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowRootDirectory=yes
UsePreviousAppDir=yes
DirExistsWarning=auto
AlwaysShowDirOnReadyPage=yes
OutputDir="{#BuildDir}"
OutputBaseFilename=codeusagemonit-setup-{#MyAppVersion}
SetupIconFile="{#BuildDir}\app.ico"
UninstallDisplayIcon={app}\codeusagemonit.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile="{#RepoRoot}\LICENSE"
CloseApplications=no
RestartApplications=no
ChangesAssociations=no
ShowLanguageDialog=auto

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimp.Options=其他选项：
english.Options=Other options:
chinesesimp.AutoStart=登录 Windows 后自动启动（只驻留托盘）
english.AutoStart=Start with Windows (tray only)
chinesesimp.AddPath=将 codeusage 加入当前用户的 PATH（新开的终端可直接运行）
english.AddPath=Add codeusage to your user PATH (new terminals can run it)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:Options}"; Flags: unchecked
Name: "addpath"; Description: "{cm:AddPath}"; GroupDescription: "{cm:Options}"; Flags: checkedonce

[Files]
Source: "{#BuildDir}\codeusagemonit.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\codeusage.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Panel.xaml"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\pricing.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\icons\*"; DestDir: "{app}\icons"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\使用说明.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "installed.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "setup-helper.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "setup-helper.ps1"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\codeusagemonit.exe"; WorkingDir: "{app}"; Comment: "{#MyAppName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\codeusagemonit.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Per-user even for an all-users install. installed.txt is what other Windows users see.
Root: HKCU; Subkey: "Software\codeusagemonit"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "codeusagemonit"; ValueData: """{app}\codeusagemonit.exe"" --background"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\codeusagemonit.exe"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  UninstallHelper: String;
  UserDataDir, BesideDir: String;
  UserLinkCode, BesideLinkCode: Integer;

function PowerShellExe: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not FileExists(Result) then Result := 'powershell.exe';
end;

function RunHelper3(const Action, Script, Target: String): Integer;
var
  ResultCode: Integer;
  Params: String;
begin
  Result := 1;
  if (Script = '') or (not FileExists(Script)) then Exit;
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Script + '" -Action ' + Action + ' -Directory "' + RemoveBackslash(ExpandConstant('{app}')) + '"';
  if Target <> '' then Params := Params + ' -Target "' + Target + '"';
  if Exec(PowerShellExe, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode;
end;

function RunHelper(const Action, Script: String): Integer;
begin
  Result := RunHelper3(Action, Script, '');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  ExtractTemporaryFile('setup-helper.ps1');
  if RunHelper('stop', ExpandConstant('{tmp}\setup-helper.ps1')) <> 0 then
    Result := 'codeusagemonit is still running from this folder. Quit it from the tray icon and run setup again.' + #13#10 +
      '这个文件夹里的 codeusagemonit 仍在运行。请在托盘图标上右键退出，然后重新安装。';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
  if not WizardIsTaskSelected('addpath') then Exit;
  if (RunHelper('add-path', ExpandConstant('{app}\setup-helper.ps1')) <> 0) and (not WizardSilent) then
    MsgBox('Could not add codeusage to your user PATH. You can add the install folder yourself.' + #13#10 +
      '未能把 codeusage 加入用户 PATH，可以稍后把安装目录手动加进去。', mbInformation, MB_OK);
end;

function InitializeUninstall: Boolean;
begin
  UninstallHelper := ExpandConstant('{tmp}\setup-helper.ps1');
#if Ver >= EncodeVer(6, 4, 0)
  if not CopyFile(ExpandConstant('{app}\setup-helper.ps1'), UninstallHelper, False) then
#else
  if not FileCopy(ExpandConstant('{app}\setup-helper.ps1'), UninstallHelper, False) then
#endif
    UninstallHelper := ExpandConstant('{app}\setup-helper.ps1');
  Result := True;
end;

procedure RemoveAutoStart;
var
  Value, ExePath: String;
begin
  ExePath := Uppercase(ExpandConstant('{app}\codeusagemonit.exe'));
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'codeusagemonit', Value) then
    if Pos(ExePath, Uppercase(Value)) > 0 then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'codeusagemonit');
end;

function IsUserDataDir(const Dir: String): Boolean;
begin
  Result := (Length(Dir) > 16) and (Uppercase(ExtractFileName(RemoveBackslash(Dir))) = 'CODEUSAGEMONIT') and DirExists(Dir);
end;

function IsBesideDataDir(const Dir: String): Boolean;
var
  AppDir: String;
begin
  AppDir := RemoveBackslash(ExpandConstant('{app}'));
  { A drive root would make this "C:\data", which may be unrelated to the app. }
  Result := (Length(AppDir) > 3) and DirExists(Dir) and (Uppercase(RemoveBackslash(Dir)) = Uppercase(AppDir + '\data'));
end;

function OfferUserData: Boolean;
begin
  { is-link exits 0 for a junction/symlink. Anything else is a real directory we created. }
  Result := IsUserDataDir(UserDataDir) and (UserLinkCode <> 0);
end;

function OfferBesideData: Boolean;
begin
  { Exit 2 means "exists and is not a link". A Scoop persist junction must be left alone,
    and a failed check must not delete a folder we could not identify. }
  Result := IsBesideDataDir(BesideDir) and (BesideLinkCode = 2);
end;

function DataPrompt(const UserData, Beside: String): String;
var
  List: String;
begin
  List := '';
  if OfferUserData then List := List + UserData + #13#10;
  if OfferBesideData then List := List + Beside + #13#10;
  if ActiveLanguage = 'english' then
    Result := 'Also delete settings, saved keys and the usage cache?' + #13#10#13#10 + List + #13#10 +
      'Choose No to keep them. A later install will keep using them.'
  else
    Result := '是否同时删除设置、已保存的密钥和用量缓存？' + #13#10#13#10 + List + #13#10 +
      '选“否”会保留这些文件。以后重新安装会继续使用它们。';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserData, Beside: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    RunHelper('stop', UninstallHelper);
    RunHelper('remove-path', UninstallHelper);
    RemoveAutoStart;
    UserDataDir := ExpandConstant('{localappdata}\codeusagemonit');
    BesideDir := ExpandConstant('{app}\data');
    { Classify before program files (and a helper that failed to copy out) disappear. }
    UserLinkCode := RunHelper3('is-link', UninstallHelper, UserDataDir);
    BesideLinkCode := RunHelper3('is-link', UninstallHelper, BesideDir);
  end;
  if CurUninstallStep <> usPostUninstall then Exit;
  if UninstallSilent then Exit;
  UserData := UserDataDir;
  Beside := BesideDir;
  if (not OfferUserData) and (not OfferBesideData) then Exit;
  if MsgBox(DataPrompt(UserData, Beside), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then Exit;
  if OfferUserData then DelTree(UserData, True, True, True);
  if OfferBesideData then DelTree(Beside, True, True, True);
end;
