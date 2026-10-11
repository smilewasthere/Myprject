; ============================================================================
;  MPLC — Windows installer (Inno Setup 6)
;
;  Produces a single MPLC-Setup-<version>.exe. The user double-clicks it,
;  it installs MPLC (appears in Add/Remove Programs with a working
;  uninstaller), and creates a Start Menu + optional Desktop shortcut.
;
;  Compile from the repo root:
;     "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" ^
;         /DSourceDir=%CD% ^
;         /DPublishDir=%CD%\dist\win-x64\publish ^
;         /DAppVersion=1.0.0 ^
;         installer\mplc.iss
;
;  Output: dist\MPLC-Setup-<version>.exe
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

; SourceDir = repo root (icon). PublishDir = dotnet publish output (files).
#ifndef SourceDir
  #define SourceDir "..\"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\win-x64\publish"
#endif

#define MyAppName     "MPLC"
#define MyAppExeName  "MPLC.exe"
; Fixed AppId GUID — NEVER change this once released, or existing installs
; won't be upgraded/uninstalled cleanly.
#define MyAppId      "{{7B3A9E2D-4C1F-4A6B-9E8D-2F5A1C3B7E90}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher=personal
AppComments=Personal mpv-powered, VLC-style video player
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Setup
VersionInfoVersion={#AppVersion}
VersionInfoCompany=personal

; Install to Program Files (needs elevation) — 64-bit location.
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin

; Add/Remove Programs entry + clean uninstall.
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#AppVersion}
CreateUninstallRegKey=yes

; 64-bit only (the bundled libmpv engine is x64; see README).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir={#SourceDir}\dist
OutputBaseFilename=MPLC-Setup-{#AppVersion}
SetupIconFile={#SourceDir}\src\MyPlayer\MPLC.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Everything from the self-contained publish output (MPLC.exe, libmpv-2.dll,
; the .NET runtime DLLs, and the embedded mpv.conf/input.conf).
Source: "{#PublishDir}\*"; DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; \
    Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent
