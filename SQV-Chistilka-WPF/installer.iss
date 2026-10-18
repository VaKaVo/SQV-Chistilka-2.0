; SQV Chistilka 2 — установщик Windows
; Сборка: ISCC.exe /DAppVersion=2.1.0 installer.iss
#ifndef AppVersion
  #define AppVersion "2.1.0"
#endif

[Setup]
AppName=SQV Chistilka 2
AppVersion={#AppVersion}
AppPublisher=SQV
DefaultDirName={autopf}\SQV Chistilka 2
DefaultGroupName=SQV Chistilka 2
OutputDir=installer-out
OutputBaseFilename=SQV-Chistilka-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName=SQV Chistilka 2
WizardStyle=modern
LicenseFile=LICENSE_RU.txt
SetupIconFile=Rh71D.ico
UninstallDisplayIcon={app}\SqvChistilka.exe

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Значки:"; Flags: unchecked
Name: "startmenu"; Description: "Закрепить в меню Пуск"; GroupDescription: "Значки:"; Flags: unchecked

[Files]
Source: "publish-single\SqvChistilka.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SQV Chistilka 2"; Filename: "{app}\SqvChistilka.exe"
Name: "{autodesktop}\SQV Chistilka 2"; Filename: "{app}\SqvChistilka.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SqvChistilka.exe"; Description: "Запустить SQV Chistilka 2"; Flags: nowait postinstall skipifsilent
