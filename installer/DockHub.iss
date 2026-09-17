; DockHub installer (Inno Setup 6)
; Build with installer\build.ps1, which publishes the app and passes the version and source folder.

#define MyAppName "DockHub"
#define MyAppExe "DockHub.exe"
#define MyAppPublisher "DockHub"
#define MyAppURL "https://github.com/sametgurtuna/DockHub"

#ifndef MyAppVersion
  #define MyAppVersion "2.2.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{6F2B7E14-3C1A-4D5B-9E27-8A41C0D5B3F2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; DockHub never needs admin rights, so it installs per user by default.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=DockHub-Setup-{#MyAppVersion}-x64
SetupIconFile=..\src\CustomDock\Assets\DockHub.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.StartupGroup=Startup:
turkish.StartupGroup=Başlangıç:
english.StartupTask=Start DockHub automatically when I sign in to Windows
turkish.StartupTask=Windows'a giriş yaptığımda DockHub'ı otomatik başlat
english.LaunchApp=Launch DockHub now
turkish.LaunchApp=DockHub'ı şimdi başlat

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:StartupGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's own "Start with Windows" setting writes, so both stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExe}"" --startup"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#MyAppName}"; Tasks: not startup; Flags: deletevalue uninsdeletevalue
; Pre-rename autostart entry.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "CustomDock"; Flags: deletevalue
; The app reads this on its first run to pick the default for its own setting.
Root: HKCU; Subkey: "Software\DockHub"; ValueType: dword; ValueName: "StartWithWindows"; ValueData: 1; Tasks: startup; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\DockHub"; ValueType: dword; ValueName: "StartWithWindows"; ValueData: 0; Tasks: not startup; Flags: uninsdeletekey
; Explorer "Pin to DockHub" verbs the app registers at runtime.
Root: HKCU; Subkey: "Software\Classes\exefile\shell\DockHub.Pin"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\lnkfile\shell\DockHub.Pin"; Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
procedure RunDockHub(const Args: String);
var
  Exe: String;
  ResultCode: Integer;
begin
  Exe := ExpandConstant('{app}\{#MyAppExe}');
  if FileExists(Exe) then
    Exec(Exe, Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { An upgrade must not replace files while the dock is running; --exit also brings the Windows taskbar back. }
  RunDockHub('--exit');
  Sleep(1500);
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  RunDockHub('--exit');
  Sleep(1500);
  RunDockHub('--restore-taskbar');
  Result := True;
end;
