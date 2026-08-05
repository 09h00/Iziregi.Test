#define MyAppName "Iziregi"
#define MyAppVersion "1.0.59"
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
; ✅ Le flag "skipifsilent" a été retiré : avant, l'application ne se relançait pas
; automatiquement après une installation silencieuse (/VERYSILENT), il fallait recliquer
; sur l'icône du bureau. Le paramètre "--updated" permet à l'application de savoir
; qu'elle vient d'être relancée juste après une mise à jour, pour afficher un message de
; confirmation au démarrage (voir MainWindow.xaml.cs).
Filename: "{app}\{#MyAppExeName}"; Parameters: "--updated"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall
