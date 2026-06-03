#define SourceDir GetEnv("AGENTGUARD_INSTALLER_SOURCE")
#define OutputDir GetEnv("AGENTGUARD_INSTALLER_OUTPUT")
#define AppVersion GetEnv("AGENTGUARD_INSTALLER_VERSION")
#define IconFile GetEnv("AGENTGUARD_INSTALLER_ICON")

[Setup]
AppId={{6B8A963B-B5C3-4B32-BEE8-6E7C351C04C7}
AppName=AgentGuard
AppVersion={#AppVersion}
AppPublisher=AgentGuard
DefaultDirName={localappdata}\Programs\AgentGuard
DefaultGroupName=AgentGuard
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=AgentGuardSetup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\AgentGuard.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AgentGuard"; Filename: "{app}\AgentGuard.exe"; IconFilename: "{app}\AgentGuard.exe"
Name: "{autodesktop}\AgentGuard"; Filename: "{app}\AgentGuard.exe"; IconFilename: "{app}\AgentGuard.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AgentGuard.exe"; Description: "{cm:LaunchProgram,AgentGuard}"; Flags: nowait postinstall skipifsilent
