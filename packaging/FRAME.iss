; FRAME installer (Inno Setup 6). Built by scripts\Build-BetaInstaller.ps1, which passes:
;   /DAppVersion=1.0.0  /DFileVersion=1.0.0.0  /DVersionLabel="1.0.0"
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
  #define VersionLabel AppVersion
#endif
#ifndef RepoRoot
  #define RepoRoot ".."
#endif
#ifndef OutputBase
  #define OutputBase "FRAME-" + AppVersion + "-Setup"
#endif
#define FfmpegManifest RepoRoot + "\packaging\ffmpeg.ini"
#define FfmpegVersion ReadIni(FfmpegManifest, "Package", "Version", "")
#define FfmpegUrl ReadIni(FfmpegManifest, "Package", "Url", "")
#define FfmpegArchive ReadIni(FfmpegManifest, "Package", "Archive", "")
#define FfmpegSha256 ReadIni(FfmpegManifest, "Package", "Sha256", "")

[Setup]
; Stable identity: every FRAME beta/release installer must keep this AppId so upgrades find the existing installation.
#ifdef ValidationBuild
; Isolated installer tests: different identity, no shortcuts, startup or real user-data cleanup.
AppId={{8E2DC4F6-884E-46C9-A79E-06E67D250468}
AppMutex=Local\FRAME-Installer-Validation
UninstallDisplayName=FRAME Setup Validation
#else
AppId={{0998192B-07BE-4AF5-B687-50024D3B1493}
AppMutex=Local\FRAME-Desktop-0.1
UninstallDisplayName=FRAME {#VersionLabel}
#endif
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
InfoBeforeFile={#RepoRoot}\packaging\INSTALL_NOTES.txt
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBase}
#ifdef ValidationBuild
Compression=none
#else
Compression=lzma2/ultra64
#endif
SolidCompression=yes
; A running FRAME holds this mutex (App.xaml.cs): Setup and the uninstaller ask the user to quit it first.
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppRunningError=FRAME is running.%n%nQuit it first: right-click the FRAME icon in the system tray and choose Quit FRAME. Then click OK to continue, or Cancel to exit.
UninstallAppRunningError=FRAME is running.%n%nQuit it first: right-click the FRAME icon in the system tray and choose Quit FRAME. Then click OK to continue, or Cancel to exit.
FinishedLabel=FRAME and its recording tools are installed.%n%nYou can start recording without installing anything else.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; Small prerequisite helpers first: ExtractTemporaryFile avoids scanning the large app payload.
Source: "{#RepoRoot}\packaging\Prepare-Ffmpeg.ps1"; Flags: dontcopy
Source: "{#FfmpegManifest}"; Flags: dontcopy
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "FRAME.exe"; Flags: ignoreversion
Source: "{tmp}\frame-ffmpeg\ffmpeg.exe"; DestDir: "{app}\tools"; Flags: external ignoreversion; Check: InstallFfmpeg; ExternalSize: 242496512
Source: "{tmp}\frame-ffmpeg\ffprobe.exe"; DestDir: "{app}\tools"; Flags: external ignoreversion; Check: InstallFfmpeg; ExternalSize: 242291712
Source: "{tmp}\frame-ffmpeg\FFMPEG-LICENSE.txt"; DestDir: "{app}\tools"; Flags: external ignoreversion; Check: InstallFfmpeg; ExternalSize: 35147
Source: "{tmp}\frame-ffmpeg\FFMPEG-README.txt"; DestDir: "{app}\tools"; Flags: external ignoreversion; Check: InstallFfmpeg; ExternalSize: 45280
Source: "{#RepoRoot}\packaging\BETA_README.md"; DestDir: "{app}"; DestName: "README.md"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\RELEASE_NOTES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\tools\README.txt"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
#ifndef ValidationBuild
Name: "{autoprograms}\FRAME"; Filename: "{app}\FRAME.exe"; WorkingDir: "{app}"; Comment: "Shadow recording for Windows"
Name: "{autodesktop}\FRAME"; Filename: "{app}\FRAME.exe"; WorkingDir: "{app}"; Comment: "Shadow recording for Windows"; Tasks: desktopicon
#endif

[Run]
#ifndef ValidationBuild
Filename: "{app}\FRAME.exe"; Description: "Start FRAME"; Flags: nowait postinstall skipifsilent
#endif

[UninstallDelete]
Type: dirifempty; Name: "{app}\tools"
Type: dirifempty; Name: "{app}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
var
  FfmpegDownload: TDownloadWizardPage;
  FfmpegProgress: TOutputProgressWizardPage;
  NeedFfmpeg: Boolean;

function InstallFfmpeg: Boolean;
begin
  Result := NeedFfmpeg;
end;

function MatchesHash(const FileName, Hash: String): Boolean;
begin
  Result := False;
  if FileExists(FileName) then
    try
      Result := CompareText(GetSHA256OfFile(FileName), Hash) = 0;
    except
      Log('Could not verify ' + FileName);
    end;
end;

procedure InitializeWizard;
begin
  FfmpegDownload := CreateDownloadPage('Preparing recording tools',
    'Setup is downloading FFmpeg {#FfmpegVersion}. No separate installation is needed.', nil);
  FfmpegDownload.ShowBaseNameInsteadOfUrl := True;
  FfmpegProgress := CreateOutputProgressPage('Preparing recording tools', 'Checking FFmpeg for FRAME...');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Archive, Params: String;
  Detail: AnsiString;
  ExitCode: Integer;
begin
  Result := '';
  FfmpegProgress.Show;
  try
    NeedFfmpeg := not (
      MatchesHash(ExpandConstant('{app}\tools\ffmpeg.exe'), '{#ReadIni(FfmpegManifest, "Package", "FfmpegSha256", "")}') and
      MatchesHash(ExpandConstant('{app}\tools\ffprobe.exe'), '{#ReadIni(FfmpegManifest, "Package", "FfprobeSha256", "")}') and
      MatchesHash(ExpandConstant('{app}\tools\FFMPEG-LICENSE.txt'), '{#ReadIni(FfmpegManifest, "Package", "LicenseSha256", "")}') and
      MatchesHash(ExpandConstant('{app}\tools\FFMPEG-README.txt'), '{#ReadIni(FfmpegManifest, "Package", "ReadmeSha256", "")}'));
  finally
    FfmpegProgress.Hide;
  end;
  if not NeedFfmpeg then begin
    Log('Verified installed FFmpeg {#FfmpegVersion}; download not needed.');
    Exit;
  end;
  try
    // Optional pre-downloaded archive for offline installs; always checked against the same pinned hash.
    Archive := ExpandConstant('{param:FFMPEGARCHIVE|}');
    if Archive = '' then begin
      FfmpegDownload.Clear;
      FfmpegDownload.Add('{#FfmpegUrl}', '{#FfmpegArchive}', '{#FfmpegSha256}');
      FfmpegDownload.Show;
      try
        FfmpegDownload.Download;
      finally
        FfmpegDownload.Hide;
      end;
      Archive := ExpandConstant('{tmp}\{#FfmpegArchive}');
    end;
    if not MatchesHash(Archive, '{#FfmpegSha256}') then
      RaiseException('The FFmpeg archive is missing or its checksum is incorrect.');
    ExtractTemporaryFile('Prepare-Ffmpeg.ps1');
    ExtractTemporaryFile('ffmpeg.ini');
    Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Prepare-Ffmpeg.ps1') +
      '" -Archive "' + Archive + '" -Destination "' + ExpandConstant('{tmp}\frame-ffmpeg') +
      '" -Manifest "' + ExpandConstant('{tmp}\ffmpeg.ini') + '" -ResultFile "' + ExpandConstant('{tmp}\ffmpeg-result.txt') + '"';
    FfmpegProgress.Show;
    try
      if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
        RaiseException('Windows could not start the recording-tools check.');
      if ExitCode <> 0 then begin
        Detail := '';
        LoadStringFromFile(ExpandConstant('{tmp}\ffmpeg-result.txt'), Detail);
        RaiseException('Could not prepare FFmpeg. ' + String(Detail));
      end;
    finally
      FfmpegProgress.Hide;
    end;
    Log('FFmpeg {#FfmpegVersion} downloaded, verified and staged.');
  except
    Result := 'FRAME setup could not prepare its recording tools.' + #13#10#13#10 + GetExceptionMessage + #13#10#13#10 +
      'Check your internet connection and available disk space, then click Install to retry, or Cancel to leave setup. No FRAME files have been changed.';
    Log(Result);
  end;
end;

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
#ifndef ValidationBuild
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
#endif
end;
