; Screenstop for Windows — Inno Setup script
;
; Per-user install (PrivilegesRequired=lowest): no UAC elevation, installs
; into %LOCALAPPDATA%\Programs\Screenstop — the standard per-user program
; location. Settings live in %APPDATA%\Screenstop and are NEVER touched by
; uninstall.
;
; Build:
;   dotnet publish src/Screenstop.App -c Release -r win-x64 --self-contained false -o installer/dist
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Screenstop.iss
; Output: installer\Output\Screenstop-Setup-<version>.exe

#define MyAppName "Screenstop"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Screenstop"
#define MyAppExeName "Screenstop.exe"
#define MyAppURL "https://github.com/ahmrafi22/Screenstop-Windows"

[Setup]
AppId={{8F1E6C2A-9B4D-4A7E-B5C3-D2E6F8A90123}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={userpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
SetupIconFile=..\src\Screenstop.App\Assets\screenstop.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Close a running instance before installing over it.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only the staging temp folder is disposable; %APPDATA%\Screenstop (settings,
; crash reports) is intentionally preserved across uninstall/reinstall.
Type: filesandordirs; Name: "{tmp}\Screenstop"

[Code]
// Remove the per-user launch-at-login Run key on uninstall. The app writes it
// itself (HKCU, no elevation), so the installer cleans it up here rather than
// owning the value at install time.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'Screenstop');
  end;
end;
