#define AppName "DllCompare"
#define AppVersion GetEnv("APP_VERSION")
#define AppPublisher "greatbody"
#define AppUrl "https://github.com/greatbody/DllCompare"
#define SourceDir GetEnv("PUBLISH_DIR")
#define OutputDir GetEnv("INSTALLER_OUTPUT_DIR")
#define RuntimeIdentifier GetEnv("RUNTIME_IDENTIFIER")

#if AppVersion == ""
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{9A73D53B-7977-43E2-9206-65282AF79440}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#RuntimeIdentifier}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\DllCompare.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\DllCompare.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\DllCompare.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DllCompare.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
