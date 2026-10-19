#define AppName "SwissTechKnife"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#define AppPublisher "luingry"
#define AppExeName "SwissTechKnife.exe"
#define Bin "..\src\SwissTechKnife\bin\Release\net48"

[Setup]
AppId={{6E7C0F3A-41B2-4C7E-9A0B-2B1E3F5D8C11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\SwissTechKnife
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=SwissTechKnife-{#AppVersion}-setup
SetupIconFile=..\assets\brand\swisstechknife.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
WizardImageFile=..\assets\brand\installer\wizard-164x314.bmp,..\assets\brand\installer\wizard-328x628.bmp
WizardSmallImageFile=..\assets\brand\installer\wizard-small-55x58.bmp,..\assets\brand\installer\wizard-small-110x116.bmp
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: unchecked

[Files]
Source: "{#Bin}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bin}\{#AppExeName}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bin}\SwissTechKnife.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bin}\SwissTechKnife.Sdk.dll"; DestDir: "{app}"; Flags: ignoreversion
; Bundled module catalog and packages (scripts/pack-modules.ps1). Remote catalogs can replace it via settings.
Source: "{#Bin}\feed\*"; DestDir: "{app}\feed"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[InstallDelete]
Type: filesandordirs; Name: "{app}\feed"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Native images: a cold start does not JIT the host. Best effort; the app runs with JIT if ngen is missing.
Filename: "{code:NgenPath}"; Parameters: "install ""{app}\{#AppExeName}"" /nologo /silent"; Flags: runhidden waituntilterminated; StatusMsg: "Otimizando a inicialização..."; Check: NgenAvailable
Filename: "{app}\{#AppExeName}"; Description: "Abrir o SwissTechKnife"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{code:NgenPath}"; Parameters: "uninstall ""{app}\{#AppExeName}"" /nologo /silent"; Flags: runhidden waituntilterminated; Check: NgenAvailable; RunOnceId: "RemoveNativeImages"

[Code]
// AnyCPU runs as a 64-bit process on a 64-bit OS, so its native images come from Framework64.
function NgenPath(Param: String): String;
begin
  if IsWin64 and FileExists(ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\ngen.exe')) then
    Result := ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\ngen.exe')
  else
    Result := ExpandConstant('{win}\Microsoft.NET\Framework\v4.0.30319\ngen.exe');
end;

function NgenAvailable(): Boolean;
begin
  Result := FileExists(NgenPath(''));
end;
