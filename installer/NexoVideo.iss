#ifndef AppVersion
  #error AppVersion nao definida
#endif
#ifndef PublishDir
  #error PublishDir nao definida
#endif
#ifndef ReleaseDir
  #error ReleaseDir nao definida
#endif

[Setup]
AppId={{DE7D62F1-D2E0-45C1-8355-6DF5D0CB1B72}
AppName=NexoVideo
AppVersion={#AppVersion}
AppVerName=NexoVideo {#AppVersion}
AppPublisher=gustavotoigo-max
AppPublisherURL=https://github.com/gustavotoigo-max/NexoVideo
AppSupportURL=https://github.com/gustavotoigo-max/NexoVideo/issues
AppUpdatesURL=https://github.com/gustavotoigo-max/NexoVideo/releases
DefaultDirName={localappdata}\Programs\NexoVideo
DefaultGroupName=NexoVideo
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir={#ReleaseDir}
OutputBaseFilename=NexoVideo-{#AppVersion}-win-x64-Setup
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\NexoVideo.exe
CloseApplications=yes
CloseApplicationsFilter=NexoVideo.exe
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NexoVideo"; Filename: "{app}\NexoVideo.exe"

[Run]
Filename: "{app}\NexoVideo.exe"; Description: "Abrir NexoVideo"; Flags: nowait postinstall skipifsilent

; Dados e ferramentas em LocalAppData\NexoVideo são preservados na desinstalação.
