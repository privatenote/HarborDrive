#ifndef PayloadDir
  #error PayloadDir must point to the published x64 folder
#endif
#ifndef WinFspMsi
  #error WinFspMsi must point to the verified official MSI
#endif
#ifndef AppVersion
  #define AppVersion "0.1.3"
#endif

[Setup]
AppId={{7BFE6434-7206-4B51-BCC4-E0D9511D13B4}
AppName=HarborDrive
AppVersion={#AppVersion}
AppPublisher=HarborDrive
AppComments=S3 network drive for Windows x64 (x86-64)
DefaultDirName={autopf}\HarborDrive
DefaultGroupName=HarborDrive
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\HarborDrive.exe
SetupIconFile=..\src\HarborDrive.App\Assets\HarborDrive.ico
OutputDir=..\artifacts
OutputBaseFilename=HarborDrive-{#AppVersion}-x64-Setup
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
SetupMutex=HarborDrive.Setup
CloseApplications=no
RestartApplications=no
AllowNoIcons=yes
InfoBeforeFile=install-notes.txt
VersionInfoDescription=HarborDrive integrated x64 installer
VersionInfoVersion={#AppVersion}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WinFspMsi}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\HarborDrive"; Filename: "{app}\HarborDrive.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\HarborDrive"; Filename: "{app}\HarborDrive.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\HarborDrive.exe"; Description: "HarborDrive 실행 / Launch HarborDrive"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: CanLaunchApp

[Code]
var
  PrerequisiteNeedsRestart: Boolean;

function RunningProcessError: String;
var
  Locator, Service, Processes, Item: Variant;
  I: Integer;
  Name, Path, InstallPrefix: String;
begin
  Result := '';
  InstallPrefix := LowerCase(AddBackslash(ExpandConstant('{app}')));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT Name, ExecutablePath FROM Win32_Process WHERE Name = ''HarborDrive.exe'' OR Name = ''rclone.exe''');
    for I := 0 to Processes.Count - 1 do begin
      Item := Processes.ItemIndex(I);
      Name := LowerCase(Item.Name);
      Path := '';
      if not VarIsNull(Item.ExecutablePath) then Path := LowerCase(Item.ExecutablePath);
      if (Name = 'harbordrive.exe') or
         ((Name = 'rclone.exe') and ((Path = '') or (Pos(InstallPrefix, Path) = 1))) then begin
        Result := 'HarborDrive 또는 이 설치 폴더의 전송 엔진이 실행 중입니다.' + #13#10 +
          '앱에서 모든 연결 해제 후 종료를 선택한 다음 다시 시도하세요.' + #13#10 +
          'Close HarborDrive safely before installing or uninstalling. Processes will not be terminated.';
        Exit;
      end;
    end;
  except
    Log('Process inspection failed: ' + GetExceptionMessage);
    Result := '실행 중인 전송 엔진을 확인하지 못했습니다. 앱과 전송 엔진을 안전하게 종료한 후 다시 시도하세요. / Could not verify running processes.';
  end;
end;

function WinFspReady: Boolean;
var
  Directory: String;
  VersionMS, VersionLS: Cardinal;
begin
  Result := False;
  if not RegQueryStringValue(HKLM32, 'SOFTWARE\WinFsp', 'InstallDir', Directory) then
    if not RegQueryStringValue(HKLM64, 'SOFTWARE\WinFsp', 'InstallDir', Directory) then Exit;
  if GetVersionNumbers(AddBackslash(Directory) + 'bin\winfsp-x64.dll', VersionMS, VersionLS) then
    Result := VersionMS >= ((2 shl 16) or 1);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Installer, InstallLog, Parameters: String;
  ExitCode: Integer;
begin
  Result := RunningProcessError;
  if Result <> '' then Exit;
  if WinFspReady then begin
    Log('Compatible WinFsp already installed; shared driver is reused.');
    Exit;
  end;
  try
    ExtractTemporaryFile('winfsp-2.1.25156.msi');
    Installer := ExpandConstant('{tmp}\winfsp-2.1.25156.msi');
    if not SameText(GetSHA256OfFile(Installer), '073a70e00f77423e34bed98b86e600def93393ba5822204fac57a29324db9f7a') then begin
      Result := 'WinFsp 설치 파일 검증에 실패했습니다. / WinFsp checksum mismatch.';
      Exit;
    end;
    InstallLog := ExpandConstant('{tmp}\HarborDrive-WinFsp-install.log');
    Parameters := '/i ' + AddQuotes(Installer) + ' /qn /norestart REBOOT=ReallySuppress ADDLOCAL=ALL /L*v ' + AddQuotes(InstallLog);
    Log('Installing bundled official WinFsp MSI. Log: ' + InstallLog);
    if not Exec(ExpandConstant('{sys}\msiexec.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
      Result := 'WinFsp 설치를 시작하지 못했습니다. / Could not start WinFsp installation.';
      Exit;
    end;
    if (ExitCode = 3010) or (ExitCode = 1641) then PrerequisiteNeedsRestart := True
    else if ExitCode <> 0 then begin
      Result := 'WinFsp 설치 실패 / WinFsp installation failed (' + IntToStr(ExitCode) + '). ' + InstallLog;
      Exit;
    end;
    if not WinFspReady then
      Result := 'WinFsp 설치 상태를 확인하지 못했습니다. / WinFsp installation could not be verified. ' + InstallLog;
  except
    Result := 'WinFsp 설치 중 오류 / WinFsp prerequisite error: ' + GetExceptionMessage;
  end;
end;

function NeedRestart: Boolean;
begin
  Result := PrerequisiteNeedsRestart;
end;

function CanLaunchApp: Boolean;
begin
  Result := not PrerequisiteNeedsRestart;
end;

function InitializeUninstall: Boolean;
var
  Error: String;
begin
  Error := RunningProcessError;
  Result := Error = '';
  if not Result then begin
    Log(Error);
    if not UninstallSilent then MsgBox(Error, mbError, MB_OK);
  end;
end;

procedure RemoveMatchingStartupValue(Root: HKEY; const Key: String);
var
  Value, Expected: String;
begin
  { The app always quotes its executable, even for paths without spaces. AddQuotes only quotes when necessary for a command line. }
  Expected := '"' + ExpandConstant('{app}\HarborDrive.exe') + '" --background';
  if RegQueryStringValue(Root, Key, 'HarborDrive', Value) then begin
    if SameText(Value, Expected) then begin
      if RegDeleteValue(Root, Key, 'HarborDrive') then Log('Removed matching HarborDrive startup value: ' + Key)
      else Log('Could not remove matching HarborDrive startup value: ' + Key);
    end else Log('Preserved HarborDrive startup value pointing to a different installation.');
  end;
end;

procedure RemoveMatchingUserStartupEntries(Root: HKEY);
var
  UserKeys: TArrayOfString;
  I: Integer;
begin
  if not RegGetSubkeyNames(Root, '', UserKeys) then begin
    Log('Could not enumerate loaded user startup keys.');
    Exit;
  end;
  for I := 0 to GetArrayLength(UserKeys) - 1 do begin
    RemoveMatchingStartupValue(Root, UserKeys[I] + '\Software\Microsoft\Windows\CurrentVersion\Run');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    { Explicit views also cover x64 emulation on ARM64. Never delete the Run key. }
    RemoveMatchingStartupValue(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Run');
    RemoveMatchingStartupValue(HKCU32, 'Software\Microsoft\Windows\CurrentVersion\Run');
    RemoveMatchingUserStartupEntries(HKU64);
    RemoveMatchingUserStartupEntries(HKU32);
  end;
  { WinFsp and per-user settings, credentials, logs and VFS caches are shared or user data. Never remove them here. }
end;
