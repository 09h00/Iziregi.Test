#define MyAppName "Iziregi"
#define MyAppVersion "1.0.9"
#define MyAppPublisher "Iziregi"
#define MyAppExeName "Iziregi.Test.exe"
#define MySourceDir "..\publish-installer"

[Setup]
AppId={{7E2B7B7A-9C2E-4B7B-9B7A-2B7A9C2E4B7B}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Iziregi
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=IziregiSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
FinishedLabel=Installation terminee avec succes !%n%nCliquez sur "Terminer" pour lancer Iziregi. Le premier demarrage peut prendre une dizaine de secondes, c'est normal - merci de patienter.
FinishedLabelNoIcons=Installation terminee avec succes !%n%nCliquez sur "Terminer" pour lancer Iziregi. Le premier demarrage peut prendre une dizaine de secondes, c'est normal - merci de patienter.

[Tasks]
Name: "desktopicon"; Description: "Creer un raccourci sur le Bureau"; GroupDescription: "Raccourcis supplementaires:"

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent
