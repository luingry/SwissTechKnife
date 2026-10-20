#define AppName "SwissTechKnife"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#define AppPublisher "luingry"
#define AppExeName "SwissTechKnife.exe"
#define Bin "..\src\SwissTechKnife\bin\Release\net48"
; /DTestInstall: per-user build with its own AppId and folder, for exercising the self-update without UAC
; or touching the real install (scripts/test-host-update.ps1).
#ifdef TestInstall
  #define AppIdValue "{{6E7C0F3A-41B2-4C7E-9A0B-2B1E3F5D8C12}"
  #define Privileges "lowest"
  #define InstallDir "{localappdata}\SwissTechKnife-Test"
  #define OutputSuffix "-test"
#else
  #define AppIdValue "{{6E7C0F3A-41B2-4C7E-9A0B-2B1E3F5D8C11}"
  #define Privileges "admin"
  #define InstallDir "{autopf}\SwissTechKnife"
  #define OutputSuffix ""
#endif

[Setup]
AppId={#AppIdValue}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={#InstallDir}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=SwissTechKnife-{#AppVersion}-setup{#OutputSuffix}
SetupIconFile=..\assets\brand\swisstechknife.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired={#Privileges}
WizardStyle=modern
ShowLanguageDialog=auto
WizardImageFile=..\assets\brand\installer\wizard-164x314.bmp,..\assets\brand\installer\wizard-328x628.bmp
WizardSmallImageFile=..\assets\brand\installer\wizard-small-55x58.bmp,..\assets\brand\installer\wizard-small-110x116.bmp
CloseApplications=yes
RestartApplications=no

[Languages]
; English first: it is the default whenever Windows' display language is not Brazilian Portuguese.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.OptimizingStartup=Optimizing startup...
english.LaunchApp=Open SwissTechKnife
brazilianportuguese.OptimizingStartup=Otimizando a inicialização...
brazilianportuguese.LaunchApp=Abrir o SwissTechKnife

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

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
Filename: "{code:NgenPath}"; Parameters: "install ""{app}\{#AppExeName}"" /nologo /silent"; Flags: runhidden waituntilterminated; StatusMsg: "{cm:OptimizingStartup}"; Check: NgenAvailable
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{code:NgenPath}"; Parameters: "uninstall ""{app}\{#AppExeName}"" /nologo /silent"; Flags: runhidden waituntilterminated; Check: NgenAvailable; RunOnceId: "RemoveNativeImages"

[Code]
// Self-update (the running app starts this installer; see src/SwissTechKnife.Core/HostUpdater.cs):
//   /STKUPDATE          unattended update started by the app
//   /STKSIGNAL=<file>   "started" once running (elevated), then "ok" or "failed"
//   /STKMUTEX=<name>    the app's single-instance mutex: wait for its release before replacing files
//   /STKAPP=<exe>       relaunched when Setup ends, after success or failure, so the app always comes back
var
  InstallDone: Boolean;

function HasSwitch(Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

// Only the instance that installs reports: with UAC, a non-elevated instance may start first.
function IsUpdateRun(): Boolean;
begin
#if Privileges == "admin"
  Result := HasSwitch('/STKUPDATE') and IsAdmin;
#else
  Result := HasSwitch('/STKUPDATE');
#endif
end;

procedure WriteSignal(Value: String);
var
  Path: String;
begin
  Path := ExpandConstant('{param:STKSIGNAL|}');
  if (Path <> '') and not SaveStringToFile(Path, Value, False) then
    Log('Could not write the update signal to ' + Path);
end;

function InitializeSetup(): Boolean;
var
  Mutex: String;
  Waited: Integer;
begin
  Result := True;
  if not IsUpdateRun() then
    Exit;
  WriteSignal('started');
  Mutex := ExpandConstant('{param:STKMUTEX|Local\SwissTechKnife.Host}');
  // The app exits once it reads "started"; the mutex goes after its modules stop and settings are saved.
  Waited := 0;
  while CheckForMutexes(Mutex) and (Waited < 60000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  if Waited >= 60000 then
    Log('App still running after 60 s; Restart Manager will close it.')
  else
    Log(Format('App released its mutex after %d ms.', [Waited]));
  // PrepareToInstall then waits for the process itself to be gone.
end;

const
  GENERIC_WRITE = $40000000;
  OPEN_EXISTING = 3;
  INVALID_HANDLE_VALUE = -1;

function CreateFile(lpFileName: String; dwDesiredAccess, dwShareMode: Cardinal; lpSecurityAttributes: Cardinal;
  dwCreationDisposition, dwFlagsAndAttributes: Cardinal; hTemplateFile: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

// True once no process maps the file (a running exe cannot be opened for writing).
function FileIsFree(Path: String): Boolean;
var
  H: THandle;
begin
  Result := True;
  if not FileExists(Path) then
    Exit;
  H := CreateFile(Path, GENERIC_WRITE, 0, 0, OPEN_EXISTING, 0, 0);
  Result := H <> THandle(INVALID_HANDLE_VALUE);
  if Result then
    CloseHandle(H);
end;

// Runs before Restart Manager looks for apps using our files. Releasing the mutex is not the end of the
// process: modules and the runtime still tear down for a moment, and Restart Manager cannot close a process
// without a window, so an unattended update would abort. Wait for the installed exe to be free instead.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Exe: String;
  Waited: Integer;
begin
  Result := '';
  if not IsUpdateRun() then
    Exit;
  Exe := ExpandConstant('{app}\{#AppExeName}');
  Waited := 0;
  while not FileIsFree(Exe) and (Waited < 30000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  if Waited >= 30000 then
    Log('App process still running after 30 s; Restart Manager will try to close it.')
  else
    Log(Format('App process gone after %d ms.', [Waited]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallDone := True;
end;

procedure DeinitializeSetup();
var
  App: String;
  Code: Integer;
begin
  if not IsUpdateRun() then
    Exit;
  if InstallDone then
    WriteSignal('ok')
  else
    WriteSignal('failed');
  App := ExpandConstant('{param:STKAPP|}');
  if (App <> '') and FileExists(App) then
  begin
    if ExecAsOriginalUser(App, '--updated', '', SW_SHOWNORMAL, ewNoWait, Code) then
      Log('Relaunched ' + App)
    else
      Log(Format('Relaunch of %s failed: %d', [App, Code]));
  end;
end;

// AnyCPU runs as a 64-bit process on a 64-bit OS, so its native images come from Framework64.
function NgenPath(Param: String): String;
begin
  if IsWin64 and FileExists(ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\ngen.exe')) then
    Result := ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\ngen.exe')
  else
    Result := ExpandConstant('{win}\Microsoft.NET\Framework\v4.0.30319\ngen.exe');
end;

// ngen needs admin rights: the per-user test build skips it.
function NgenAvailable(): Boolean;
begin
  Result := IsAdminInstallMode and FileExists(NgenPath(''));
end;
