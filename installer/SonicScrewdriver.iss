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

; The wizard follows the Windows display language when it is one of these, and asks otherwise.
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

; Only the texts of this script: the standard wizard pages come with Inno Setup.
[CustomMessages]
en.StartWithWindows=Start {#AppName} with Windows
en.LaunchApp=Launch {#AppName}
en.NeedDotNet=%1 needs the .NET 8 Desktop Runtime, which is not installed.%n%nOpen the Microsoft download page now? (Install "Desktop Runtime x64", then run this setup again.)
fr.StartWithWindows=Lancer {#AppName} au démarrage de Windows
fr.LaunchApp=Lancer {#AppName}
fr.NeedDotNet=%1 a besoin du .NET 8 Desktop Runtime, qui n'est pas installé.%n%nOuvrir la page de téléchargement de Microsoft maintenant ? (Installez « Desktop Runtime x64 », puis relancez cette installation.)
de.StartWithWindows={#AppName} mit Windows starten
de.LaunchApp={#AppName} starten
de.NeedDotNet=%1 benötigt die .NET 8 Desktop Runtime, die nicht installiert ist.%n%nJetzt die Download-Seite von Microsoft öffnen? (Installieren Sie „Desktop Runtime x64“ und starten Sie dieses Setup danach erneut.)
es.StartWithWindows=Iniciar {#AppName} con Windows
es.LaunchApp=Iniciar {#AppName}
es.NeedDotNet=%1 necesita .NET 8 Desktop Runtime, que no está instalado.%n%n¿Abrir ahora la página de descarga de Microsoft? (Instale «Desktop Runtime x64» y vuelva a ejecutar este instalador.)
it.StartWithWindows=Avvia {#AppName} con Windows
it.LaunchApp=Avvia {#AppName}
it.NeedDotNet=%1 richiede .NET 8 Desktop Runtime, che non è installato.%n%nAprire ora la pagina di download di Microsoft? (Installa "Desktop Runtime x64", quindi riavvia questo programma di installazione.)
pt.StartWithWindows=Iniciar o {#AppName} com o Windows
pt.LaunchApp=Iniciar o {#AppName}
pt.NeedDotNet=O %1 precisa do .NET 8 Desktop Runtime, que não está instalado.%n%nAbrir agora a página de download da Microsoft? (Instale "Desktop Runtime x64" e execute este instalador novamente.)

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Same value the app itself manages from its settings window.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SonicScrewdriver"; \
  ValueData: """{app}\{#AppExe}"""; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0';

{ A .NET 8 Desktop Runtime inside the given dotnet folder (the one with shared\Microsoft.WindowsDesktop.App). }
function HasRuntimeIn(DotNetDir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if DotNetDir = '' then Exit;
  Result := FindFirst(AddBackslash(DotNetDir) + 'shared\Microsoft.WindowsDesktop.App\8.*', FindRec);
  if Result then
    FindClose(FindRec);
end;

{ The x64 runtime registers each installed version in the 64-bit registry, wherever it was installed. }
function HasRuntimeInRegistry(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then
      begin
        Result := True;
        Exit;
      end;
end;

{ True when a .NET 8 Desktop Runtime (x64) is installed: registry, Program Files (also the x64 folder of Windows on Arm),
  a per-user install (dotnet-install) or the folder DOTNET_ROOT points to. }
function HasDesktopRuntime8(): Boolean;
begin
  Result := HasRuntimeInRegistry()
    or HasRuntimeIn(ExpandConstant('{commonpf64}\dotnet'))
    or HasRuntimeIn(ExpandConstant('{commonpf64}\dotnet\x64'))
    or HasRuntimeIn(ExpandConstant('{localappdata}\Microsoft\dotnet'))
    or HasRuntimeIn(GetEnv('DOTNET_ROOT'));
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not HasDesktopRuntime8() then
  begin
    if MsgBox(FmtMessage(CustomMessage('NeedDotNet'), ['{#AppName}']), mbConfirmation, MB_YESNO) = IDYES then
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
