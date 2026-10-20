; cTools 安装包脚本（Inno Setup 6）
; 由 GitHub Actions 调用：
;   ISCC.exe /DMyAppVersion=0.1.0 /DPublishDir=...\publish /DOutputDir=...\dist installer\cTools.iss

#define MyAppName "cTools"
#define MyAppExeName "cTools.exe"
#define MyAppPublisher "cTools"
#define MyAppUrl "https://github.com/lx1736767412/cTools"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; AppId 唯一标识此应用，升级安装依赖它保持稳定，切勿修改。
AppId={{6E906C9E-587A-478C-9E9F-432509088460}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={localappdata}\Programs\cTools
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; 按当前用户安装，不需要管理员权限。
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=cTools-{#MyAppVersion}-setup
SetupIconFile=..\assets\cTools.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
{ 卸载时顺手清掉应用自己写入的 HKCU 开机启动项，避免留下失效路径。 }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'cTools');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RTools');
  end;
end;
