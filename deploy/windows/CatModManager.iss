; Cat Mod Manager — Inno Setup 6 Script
; Build: ISCC /DAppVersion=1.2.0 CatModManager.iss
;        (or use pack.cs which sets the version automatically)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName      "Cat Mod Manager"
#define AppPublisher "Cat Mod Manager Team"
#define AppURL       "https://github.com/rael-g/CatModManager"
#define AppExeName   "CatModManager.exe"
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
[Components]
Name: "main"; Description: "Cat Mod Manager (Core Application)"; Types: full custom; Flags: fixed

; Include auto-generated plugin components
#include "plugins_generated.iss"

; ─────────────────────────────────────────────────────────────────────────────
[Tasks]

; Desktop shortcut — unchecked by default (user opts in)
Name: "desktopicon"; \
  Description: "Create a &desktop shortcut"; \
  GroupDescription: "Additional shortcuts:"; \
  Flags: unchecked

; ─────────────────────────────────────────────────────────────────────────────
[Files]

; Application binaries
Source: "publish\*"; \
  DestDir: "{app}"; \
  Components: main; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; Game definitions — all samples via wildcard. 
; Installed once, never overwritten on upgrade if user customized them.
Source: "..\..\samples\game_definitions\*"; \
  DestDir: "{localappdata}\catmodmanager\game_definitions"; \
  Flags: onlyifdoesntexist uninsneveruninstall

; ─────────────────────────────────────────────────────────────────────────────
[Icons]

; Start Menu
Name: "{autoprograms}\{#AppName}"; \
  Filename: "{app}\{#AppExeName}"; \
  IconFilename: "{app}\{#AppExeName}"; \
  Components: main

; Desktop (optional task)
Name: "{autodesktop}\{#AppName}"; \
  Filename: "{app}\{#AppExeName}"; \
  IconFilename: "{app}\{#AppExeName}"; \
  Tasks: desktopicon

; ─────────────────────────────────────────────────────────────────────────────
[Run]

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
