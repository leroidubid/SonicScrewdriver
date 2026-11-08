; Sonic Screwdriver installer (Inno Setup 6).
; Build: publish the exe first (see README), then:  iscc installer\SonicScrewdriver.iss /DAppVersion=1.0.0
; Per-user install, no administrator rights needed.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Sonic Screwdriver"
#define AppExe "SonicScrewdriver.exe"

[Setup]
AppId={{7C1F2E4A-5B3D-4E8F-9A2C-6D0B1E3F5A47}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Remi Chanoine
DefaultDirName={autopf}\SonicScrewdriver
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\publish
OutputBaseFilename=SonicScrewdriver-Setup-{#AppVersion}
SetupIconFile=..\src\SonicScrewdriver\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Close a running copy (Restart Manager) before replacing or removing its files.
CloseApplications=force
RestartApplications=no
MinVersion=10.0.17763

[Tasks]
Name: "startup"; Description: "Start {#AppName} with Windows"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Same value the app itself manages from its settings window.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SonicScrewdriver"; \
  ValueData: """{app}\{#AppExe}"""; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0';

{ True when a .NET 8 Desktop Runtime (x64) is installed. }
function HasDesktopRuntime8(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not HasDesktopRuntime8() then
  begin
    if MsgBox('{#AppName} needs the .NET 8 Desktop Runtime, which is not installed.' + #13#10 + #13#10 +
              'Open the Microsoft download page now? (Install "Desktop Runtime x64", then run this setup again.)',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', DotNetUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

{ The app can also enable "Start with Windows" itself: always remove that entry on uninstall.
  Settings in %APPDATA%\SonicScrewdriver are kept. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SonicScrewdriver');
end;
