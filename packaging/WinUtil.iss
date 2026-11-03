#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif
#ifndef PublishDirectory
  #define PublishDirectory "..\artifacts\publish\win-x64"
#endif
#ifndef InstallerDirectory
  #define InstallerDirectory "..\artifacts\installer"
#endif

[Setup]
AppId={{B64F112A-0F4F-47E4-9065-E0C4C9D118BB}
AppName=WinUtil
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\WinUtil
DefaultGroupName=WinUtil
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerDirectory}
OutputBaseFilename=WinUtil-Setup-{#AppVersion}-win-x64
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\WinUtil.exe
AppMutex=Local\WinUtil
CloseApplications=no
RestartApplications=no

[Files]
Source: "{#PublishDirectory}\WinUtil.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\WinUtil"; Filename: "{app}\WinUtil.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "WinUtil"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\WinUtil.exe"; WorkingDir: "{app}"; Description: "Launch WinUtil"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if not Exec(ExpandConstant('{app}\WinUtil.exe'), '--configure-startup',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Windows startup could not be configured: ' + SysErrorMessage(ResultCode));
    if ResultCode <> 0 then
      RaiseException('Windows startup could not be configured. See %APPDATA%\WinUtil\logs\crash.log for details.');
  end;
end;
