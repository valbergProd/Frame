; FRAME beta installer (Inno Setup 6). Built by scripts\Build-BetaInstaller.ps1, which passes:
;   /DAppVersion=0.5.4  /DFileVersion=0.5.4.0  /DVersionLabel="0.5.4 Beta"
;   /DSourceExe=<published FRAME.exe>  /DOutputDir=<release folder>  /DRepoRoot=<repository>  /DWizardImages=<generated .bmp list>
; Per-user install (no administrator rights): %LOCALAPPDATA%\Programs\FRAME. Settings/logs (%LOCALAPPDATA%\FRAME) and
; recordings (Videos\FRAME or the chosen folder) are never touched by install or upgrade; uninstall asks before it removes
; settings and logs (default: keep) and never touches recordings.

#ifndef AppVersion
  #error AppVersion must be passed by the build script
#endif
#ifndef FileVersion
  #define FileVersion AppVersion + ".0"
#endif
#ifndef VersionLabel
  #define VersionLabel AppVersion + " Beta"
#endif
#ifndef RepoRoot
  #define RepoRoot ".."
#endif
#ifndef OutputBase
  #define OutputBase "FRAME-" + AppVersion + "-Beta-Setup"
#endif

[Setup]
; Stable identity: every FRAME beta/release installer must keep this AppId so upgrades find the existing installation.
AppId={{0998192B-07BE-4AF5-B687-50024D3B1493}
AppName=FRAME
AppVersion={#AppVersion}
AppVerName=FRAME {#VersionLabel}
AppPublisher=FRAME
AppComments=Shadow recording for Windows: save the last minutes of your screen with a hotkey.
VersionInfoVersion={#FileVersion}
VersionInfoProductName=FRAME
VersionInfoProductVersion={#FileVersion}
VersionInfoDescription=FRAME {#VersionLabel} Setup
VersionInfoCopyright=Copyright (c) 2026 the FRAME developer
DefaultDirName={autopf}\FRAME
DefaultGroupName=FRAME
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004 or newer (Windows Graphics Capture for FFmpeg's gfxcapture, capture-excluded notes).
MinVersion=10.0.19041
WizardStyle=modern
SetupIconFile={#RepoRoot}\assets\Frame.ico
#ifdef WizardImages
WizardSmallImageFile={#WizardImages}
#endif
#ifdef WizardLargeImages
WizardImageFile={#WizardLargeImages}
#endif
UninstallDisplayIcon={app}\FRAME.exe
UninstallDisplayName=FRAME {#VersionLabel}
InfoBeforeFile={#RepoRoot}\packaging\INSTALL_NOTES.txt
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBase}
Compression=lzma2/ultra64
SolidCompression=yes
; A running FRAME holds this mutex (App.xaml.cs): Setup and the uninstaller ask the user to quit it first.
AppMutex=Local\FRAME-Desktop-0.1
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppRunningError=FRAME is running.%n%nQuit it first: right-click the FRAME icon in the system tray and choose Quit FRAME. Then click OK to continue, or Cancel to exit.
UninstallAppRunningError=FRAME is running.%n%nQuit it first: right-click the FRAME icon in the system tray and choose Quit FRAME. Then click OK to continue, or Cancel to exit.
FinishedLabel=FRAME is installed.%n%nFRAME needs FFmpeg 8.1 or newer (free, installed separately). If it is missing, FRAME shows how to install it the first time it starts.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "FRAME.exe"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\BETA_README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\RELEASE_NOTES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\tools\README.txt"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\FRAME"; Filename: "{app}\FRAME.exe"; WorkingDir: "{app}"; Comment: "Shadow recording for Windows"
Name: "{autodesktop}\FRAME"; Filename: "{app}\FRAME.exe"; WorkingDir: "{app}"; Comment: "Shadow recording for Windows"; Tasks: desktopicon

[Run]
Filename: "{app}\FRAME.exe"; Description: "Start FRAME"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}\tools"
Type: dirifempty; Name: "{app}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

// "Start FRAME with Windows" writes HKCU\...\Run\FRAME = "<FRAME.exe>" --background. Remove it on uninstall only when it
// points into this installation (a portable copy elsewhere keeps its own entry).
procedure RemoveStartupEntry();
var
  Value: String;
begin
  if RegQueryStringValue(HKCU, RunKey, 'FRAME', Value) then
    if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Value)) > 0 then
      RegDeleteValue(HKCU, RunKey, 'FRAME');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep = usUninstall then
    RemoveStartupEntry();
  if CurUninstallStep = usPostUninstall then
  begin
    Data := ExpandConstant('{localappdata}\FRAME');
    // Default answer (also for /SUPPRESSMSGBOXES): keep. Recordings live elsewhere and are never removed.
    if DirExists(Data) then
      if SuppressibleMsgBox('Also remove FRAME''s settings, diagnostic logs, thumbnails and recovery files?' + #13#10#13#10 +
         Data + #13#10#13#10 +
         'Choose No to keep them (recommended if you may reinstall FRAME). Your saved recordings are not affected either way.',
         mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(Data, True, True, True);
  end;
end;
