; PCL OpenClaw Launcher — per-version installer template.
; Generated placeholders: 0.6.0 E://openclaw//ocl//app-v0.6.0 E://openclaw//ocl//installers//0.6.0 8E1C3A72-0F5B-4D31-9C10-0A1F2B3C4D60
[Setup]
AppId={{8E1C3A72-0F5B-4D31-9C10-0A1F2B3C4D60}
AppName=PCL OpenClaw Launcher
AppVersion=0.6.0
AppVerName=PCL OpenClaw Launcher 0.6.0
AppPublisher=Twinight_Miner
AppPublisherURL=https://github.com/twinightminer-arch/PCL-OpenClaw-Launcher
DefaultDirName={localappdata}\Programs\OCL-0.6.0
DefaultGroupName=OCL
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=E://openclaw//ocl//installers//0.6.0
OutputBaseFilename=OCL-0.6.0-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
SetupIconFile=E://openclaw//ocl//app-v0.6.0\Assets\ocl.ico
UninstallDisplayIcon={app}\PCL-OpenClaw-Launcher.exe
DisableWelcomePage=no

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
Source: "E://openclaw//ocl//app-v0.6.0\PCL-OpenClaw-Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "E://openclaw//ocl//app-v0.6.0\PCL-OpenClaw-Launcher.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "E://openclaw//ocl//app-v0.6.0\PCL-OpenClaw-Launcher.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "E://openclaw//ocl//app-v0.6.0\PCL-OpenClaw-Launcher.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "E://openclaw//ocl//app-v0.6.0\PclControls.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "E://openclaw//ocl//app-v0.6.0\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\PCL OpenClaw Launcher 0.6.0"; Filename: "{app}\PCL-OpenClaw-Launcher.exe"
Name: "{userdesktop}\OCL 0.6.0"; Filename: "{app}\PCL-OpenClaw-Launcher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PCL-OpenClaw-Launcher.exe"; Description: "启动 OCL"; Flags: nowait postinstall skipifsilent
