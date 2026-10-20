#ifndef AppVersion
#error AppVersion is required
#endif
#ifndef SourceDir
#error SourceDir is required
#endif
#ifndef ReleaseDir
#error ReleaseDir is required
#endif

[Setup]
AppId={{FA10C4BE-FD51-4855-AD88-0B1EC6CF9704}
AppName=硬链接自动备份
AppVersion={#AppVersion}
AppPublisher=123456789-code
AppPublisherURL=https://github.com/123456789-code/HardLinkAutoBackup
DefaultDirName={localappdata}\Programs\HardLinkAutoBackup
DefaultGroupName=硬链接自动备份
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
WizardStyle=modern
SetupIconFile=..\Assets\App.ico
UninstallDisplayIcon={app}\HardLinkAutoBackup.exe
LicenseFile=..\LICENSE.txt
OutputDir={#ReleaseDir}
OutputBaseFilename=HardLinkAutoBackup-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\硬链接自动备份"; Filename: "{app}\HardLinkAutoBackup.exe"
Name: "{userdesktop}\硬链接自动备份"; Filename: "{app}\HardLinkAutoBackup.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HardLinkAutoBackup.exe"; Description: "打开硬链接自动备份"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
	result_code: Integer;
	version_high, version_low: Cardinal;
begin
	Result := '';
	if GetVersionNumbers(ExpandConstant('{app}\HardLinkAutoBackup.exe'), version_high, version_low) and
		((version_high > $00010001) or ((version_high = $00010001) and (version_low >= $00010000))) then
		if not Exec(ExpandConstant('{app}\HardLinkAutoBackup.exe'), '--check-install', '', SW_HIDE, ewWaitUntilTerminated, result_code) or (result_code <> 0) then
			Result := '请先从托盘退出旧程序，再重试安装。';
end;

function InitializeUninstall: Boolean;
var
	result_code: Integer;
begin
	Result := True;
	if FileExists(ExpandConstant('{app}\HardLinkAutoBackup.exe')) then
		Result := Exec(ExpandConstant('{app}\HardLinkAutoBackup.exe'), '--check-uninstall', '', SW_HIDE, ewWaitUntilTerminated, result_code) and (result_code = 0);
end;
