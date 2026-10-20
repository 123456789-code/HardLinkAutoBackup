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
function GetTempFileNameW(directory, prefix: String; unique: Cardinal; file_name: String): Cardinal;
	external 'GetTempFileNameW@kernel32.dll stdcall';

function WriteAccessError(const path: String): String;
begin
	Log('HLAB_WRITE_CHECK_FAILED: ' + path);
	Result := '安装程序无法写入以下位置：' + #13#10 + path + #13#10#13#10;
	if not IsAdmin then
		Result := Result + '如果选择了 Program Files 等受保护目录，请关闭安装程序，右键安装包选择“以管理员身份运行”（使用同一 Windows 账户），再安装到原目录。也可返回并选择默认用户目录。' + #13#10#13#10;
	Result := Result + '请从托盘退出旧程序，并检查上述文件是否只读或被占用。检查通过前不会开始复制程序文件；请勿跳过失败文件。';
end;

function CheckInstallWriteAccess: String;
var
	directory, parent_directory, probe, file_path: String;
	stream: TFileStream;
	found: TFindRec;
begin
	Result := '';
	directory := ExpandConstant('{app}');
	{ 目标尚不存在时检查最近的已有父目录，不留下新建目录。 }
	while not DirExists(directory) do begin
		parent_directory := ExtractFileDir(directory);
		if FileExists(directory) or (parent_directory = directory) or (parent_directory = '') then begin
			Result := WriteAccessError(directory);
			exit;
		end;
		directory := parent_directory;
	end;
	probe := StringOfChar(#0, 260);
	if GetTempFileNameW(directory, 'hlb', 0, probe) = 0 then begin
		Result := WriteAccessError(directory);
		exit;
	end;
	probe := Copy(probe, 1, Pos(#0, probe) - 1);
	if not DeleteFile(probe) then begin
		Result := WriteAccessError(probe);
		exit;
	end;
	{ 创建新文件成功不代表可以覆盖已有卸载器；只打开检查，不改动其内容。 }
	if FindFirst(ExpandConstant('{app}\unins*.*'), found) then begin
		try
			repeat
				if (CompareText(ExtractFileExt(found.Name), '.exe') = 0) or
					(CompareText(ExtractFileExt(found.Name), '.dat') = 0) then begin
					file_path := ExpandConstant('{app}\') + found.Name;
					try
						stream := TFileStream.Create(file_path, fmOpenReadWrite or fmShareDenyNone);
						stream.Free;
					except
						Result := WriteAccessError(file_path);
						exit;
					end;
				end;
			until not FindNext(found);
		finally
			FindClose(found);
		end;
	end;
end;

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
	if Result = '' then
		Result := CheckInstallWriteAccess;
end;

function InitializeUninstall: Boolean;
var
	result_code: Integer;
begin
	Result := True;
	if FileExists(ExpandConstant('{app}\HardLinkAutoBackup.exe')) then
		Result := Exec(ExpandConstant('{app}\HardLinkAutoBackup.exe'), '--check-uninstall', '', SW_HIDE, ewWaitUntilTerminated, result_code) and (result_code = 0);
end;
