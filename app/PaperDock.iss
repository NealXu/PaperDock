#define MyAppName "纸坞"
#define MyAppVersion "0.2.1"
#define MyAppPublisher "Local Workbench"

[Setup]
AppId={{4A7B6F48-95D7-4E66-91C3-21E6DD20B514}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\PaperDock
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\PaperDock.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\release\wpf
OutputBaseFilename=PaperDock-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
Source: "dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\纸坞"; Filename: "{app}\PaperDock.exe"
Name: "{group}\卸载 纸坞"; Filename: "{uninstallexe}"
Name: "{autodesktop}\纸坞"; Filename: "{app}\PaperDock.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PaperDock.exe"; Description: "启动 纸坞"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet48Installed(): Boolean;
var
  Release: Cardinal;
begin
  Result :=
    RegQueryDWordValue(
      HKLM64,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release',
      Release) and (Release >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := IsDotNet48Installed();
  if not Result then
    MsgBox(
      '此应用需要 Microsoft .NET Framework 4.8。请先在 Windows 更新中安装后再重试。',
      mbError,
      MB_OK);
end;
