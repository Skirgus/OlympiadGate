#define PublishDir "..\publish"
#ifndef AppVersion
  #error Pass the version by running installer/publish.ps1
#endif

[Setup]
AppId={{8F3A1C2E-6B47-4D1A-9E55-7C0A2B6D4F18}
AppName=OlympiadGate
AppVersion={#AppVersion}
AppPublisher=OlympiadGate
DefaultDirName={autopf}\OlympiadGate
DefaultGroupName=OlympiadGate
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=OlympiadGate-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\OlympiadGate.Desktop.exe
SetupLogging=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "OlympiadGate-Setup.exe"

[Dirs]
Name: "{commonappdata}\OlympiadGate"; Permissions: system-full admins-full

[Icons]
Name: "{autoprograms}\OlympiadGate"; Filename: "{app}\OlympiadGate.Desktop.exe"; Parameters: "--admin"; Comment: "Панель администратора OlympiadGate"

[Run]
Filename: "{app}\OlympiadGate.Desktop.exe"; Parameters: "--admin"; Description: "Открыть панель администратора"; Flags: postinstall nowait skipifsilent

[Code]
var
  DeleteBank: Boolean;

function RunSc(const Params: String): Integer;
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\sc.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

function ServiceInstalled: Boolean;
begin
  Result := RunSc('query OlympiadGate') = 0;
end;

procedure KillImage(const ImageName: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ' + ImageName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StopServiceProcess;
begin
  RunSc('stop OlympiadGate');
  KillImage('OlympiadGate.Service.exe');
  Sleep(400);
end;

procedure DeleteService;
var
  Attempt: Integer;
begin
  for Attempt := 1 to 20 do
  begin
    if not ServiceInstalled then
      Exit;
    RunSc('stop OlympiadGate');
    KillImage('OlympiadGate.Service.exe');
    RunSc('delete OlympiadGate');
    Sleep(500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopServiceProcess;
  KillImage('OlympiadGate.Desktop.exe');
  DeleteService;
  if ServiceInstalled then
    Result := 'Не удалось удалить службу OlympiadGate. Перезагрузите компьютер и запустите установку снова.'
  else
    Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'create OlympiadGate binPath= "' + ExpandConstant('{app}\OlympiadGate.Service.exe') + '" start= auto obj= LocalSystem DisplayName= "OlympiadGate"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'description OlympiadGate "Блокировка рабочего стола до решения задач"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'failure OlympiadGate reset= 86400 actions= restart/5000/restart/5000/restart/5000', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'start OlympiadGate', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteBank := MsgBox('Удалить также банк задач и настройки?' + #13#10 + #13#10 + 'Нет — задачи сохранятся для следующей установки.' + #13#10 + 'Да — банк будет удалён.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopServiceProcess;
    Exec(ExpandConstant('{app}\OlympiadGate.Desktop.exe'), '--cleanup', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    KillImage('OlympiadGate.Desktop.exe');
    DeleteService;
    if ServiceInstalled then
      MsgBox('Служба OlympiadGate не удалилась. Перезагрузите компьютер: после перезагрузки Windows снимет службу, помеченную на удаление.', mbError, MB_OK);
  end;
  if (CurUninstallStep = usPostUninstall) and DeleteBank then
    DelTree(ExpandConstant('{commonappdata}\OlympiadGate'), True, True, True);
end;
