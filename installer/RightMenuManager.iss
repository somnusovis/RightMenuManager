; 右键管家安装脚本（Inno Setup 6）
; 构建命令：
;   "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\RightMenuManager.iss
;
; 产物：publish\installer\右键管家-安装程序.exe

#define MyAppName "右键管家"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "RightMenuManager"
#define MyAppExeName "RightMenuManager.exe"

[Setup]
AppId={{505531C4-89D4-451F-BCF5-C00E882CA228}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\RightMenuManager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; 始终显示安装目录页，允许用户自选安装位置
DisableDirPage=no
; 默认“仅为我安装”，也可以选择“为所有用户安装”
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\publish\installer
OutputBaseFilename=右键管家-安装程序
SetupIconFile=..\src\RightMenuManager\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
VersionInfoVersion=0.1.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\publish\win-x64-self-contained\RightMenuManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动{#MyAppName}"; Flags: nowait postinstall skipifsilent
