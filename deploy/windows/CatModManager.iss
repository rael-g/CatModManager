; Cat Mod Manager — Inno Setup 6 Script
; Build: ISCC /DAppVersion=1.2.0 CatModManager.iss
;        (or use pack.ps1 which sets the version automatically)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef WinFspMsi
  #define WinFspMsi "winfsp-setup.msi"
#endif

#define AppName      "Cat Mod Manager"
#define AppPublisher "Cat Mod Manager Team"
#define AppURL       "https://github.com/rael-g/CatModManager"
#define AppExeName   "CatModManager.Ui.exe"
#define AppId        "{{A7F3C142-9B2E-4D8A-B5C6-1E0F7A3D9C48}"

; ─────────────────────────────────────────────────────────────────────────────
[Setup]

AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}

; Per-user — no UAC prompt for the main install
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline

; Install to %LocalAppData%\Programs\Cat Mod Manager
DefaultDirName={localappdata}\Programs\Cat Mod Manager
DisableDirPage=no

; No program group page (we place shortcuts ourselves)
DisableProgramGroupPage=yes

; Appearance
SetupIconFile=..\..\src\CatModManager.Ui\Assets\icon.ico
WizardStyle=modern
WizardSmallImageFile=..\..\src\CatModManager.Ui\Assets\icon.png

; Output
OutputDir=dist
OutputBaseFilename=CatModManagerSetup-{#AppVersion}

; Language — auto-detect from Windows locale, no dialog
ShowLanguageDialog=no

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; Uninstall
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

; ─────────────────────────────────────────────────────────────────────────────
[Languages]

Name: "english";             MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

; ─────────────────────────────────────────────────────────────────────────────
[Tasks]

; Desktop shortcut — unchecked by default (user opts in)
Name: "desktopicon"; \
  Description: "Create a &desktop shortcut"; \
  GroupDescription: "Additional shortcuts:"; \
  Flags: unchecked

; WinFsp — shown only when not already installed (checked by default when shown)
Name: "winfsp"; \
  Description: "Install &WinFsp driver  (required for VFS mod mounting — launches its own installer)"; \
  GroupDescription: "Prerequisites:"; \
  Check: WinFspNotInstalled

; ─────────────────────────────────────────────────────────────────────────────
[Files]

; Application binaries
Source: "publish\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; Game definitions — installed once, never overwritten on upgrade/reinstall
Source: "..\..\samples\game_definitions\skyrim.toml"; \
  DestDir: "{localappdata}\catmodmanager\game_definitions"; \
  Flags: onlyifdoesntexist uninsneveruninstall

Source: "..\..\samples\game_definitions\cyberpunk.toml"; \
  DestDir: "{localappdata}\catmodmanager\game_definitions"; \
  Flags: onlyifdoesntexist uninsneveruninstall

Source: "..\..\samples\game_definitions\liesofp.toml"; \
  DestDir: "{localappdata}\catmodmanager\game_definitions"; \
  Flags: onlyifdoesntexist uninsneveruninstall

; WinFsp MSI — bundled, extracted to %TEMP% only when the task is selected
Source: "{#WinFspMsi}"; \
  DestDir: "{tmp}"; \
  Flags: deleteafterinstall; \
  Tasks: winfsp

; ─────────────────────────────────────────────────────────────────────────────
[Icons]

; Start Menu
Name: "{autoprograms}\{#AppName}"; \
  Filename: "{app}\{#AppExeName}"; \
  IconFilename: "{app}\{#AppExeName}"

; Desktop (optional task)
Name: "{autodesktop}\{#AppName}"; \
  Filename: "{app}\{#AppExeName}"; \
  IconFilename: "{app}\{#AppExeName}"; \
  Tasks: desktopicon

; ─────────────────────────────────────────────────────────────────────────────
[Run]

; WinFsp: launches the MSI with its own installer UI (handles its own UAC)
Filename: "msiexec.exe"; \
  Parameters: "/i ""{tmp}\winfsp-setup.msi"""; \
  StatusMsg: "Installing WinFsp driver..."; \
  Tasks: winfsp; \
  Flags: waituntilterminated

; Offer to launch CMM after setup completes
Filename: "{app}\{#AppExeName}"; \
  Description: "Launch {#AppName}"; \
  Flags: nowait postinstall skipifsilent

; ─────────────────────────────────────────────────────────────────────────────
[Registry]

; nxm:// protocol handler (HKCU — no admin required)
Root: HKCU; Subkey: "Software\Classes\nxm"; \
  ValueType: string; ValueData: "URL:NXM Protocol"; \
  Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nxm"; \
  ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\nxm\shell\open\command"; \
  ValueType: string; \
  ValueData: """{app}\{#AppExeName}"" --nxm ""%1"""

; .catprofile file association (HKCU — no admin required)
Root: HKCU; Subkey: "Software\Classes\.catprofile"; \
  ValueType: string; ValueData: "CatModManager.Profile"; \
  Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CatModManager.Profile"; \
  ValueType: string; ValueData: "Cat Mod Manager Profile"; \
  Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CatModManager.Profile\DefaultIcon"; \
  ValueType: string; ValueData: "{app}\{#AppExeName},0"
Root: HKCU; Subkey: "Software\Classes\CatModManager.Profile\shell\open\command"; \
  ValueType: string; \
  ValueData: """{app}\{#AppExeName}"" ""%1"""

; ─────────────────────────────────────────────────────────────────────────────
[Code]

{ Returns True when WinFsp is NOT installed — used to show the task checkbox only
  when the driver is actually missing. }
function WinFspNotInstalled: Boolean;
begin
  Result := not (RegKeyExists(HKLM, 'SOFTWARE\WinFsp') or
                 RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\WinFsp'));
end;
