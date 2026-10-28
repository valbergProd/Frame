param(
    [Parameter(Mandatory)][string]$Setup,
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$Root
)
# Use only a /DValidationBuild installer: it has a separate AppId and no shortcuts,
# launch action, production mutex or user-data uninstall handler.
$ErrorActionPreference='Stop'
if ($Setup -notlike '*-Setup-Validation.exe') { throw 'A validation installer is required.' }
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\work')) + '\'
$Root=[IO.Path]::GetFullPath($Root)
if (!$Root.StartsWith($workspace,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path $Root)) { throw 'Use a fresh directory under work.' }
$key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E2DC4F6-884E-46C9-A79E-06E67D250468}_is1'
if (Test-Path $key) { throw 'An earlier validation installation still exists.' }
New-Item -ItemType Directory $Root | Out-Null
$dir=Join-Path $Root 'Programs with spaces\FRAME'
$checks=[Collections.Generic.List[object]]::new()
function Check($name,[bool]$ok) {
    $checks.Add(@{name=$name;passed=$ok})
    $checks | ConvertTo-Json | Set-Content (Join-Path $Root 'results.json')
    if (!$ok) { throw "FAIL: $name" }
    "PASS: $name"
}
function Snapshot {
    $settings=Join-Path $env:LOCALAPPDATA 'FRAME\settings.json'
    $production='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{0998192B-07BE-4AF5-B687-50024D3B1493}_is1'
    @{settings=$(if(Test-Path $settings){(Get-FileHash $settings).Hash});
      run=[string](Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue).FRAME;
      install=[string](Get-ItemProperty $production -ErrorAction SilentlyContinue).InstallLocation} | ConvertTo-Json -Compress
}
function Install($name,$extra) {
    $arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="'+$dir+'" /LOG="'+(Join-Path $Root ($name+'.log'))+'" '+$extra
    $p=Start-Process $Setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(!$p.WaitForExit(240000)) {throw "Owned setup process $($p.Id) timed out"}
    return $p.ExitCode
}
$before=Snapshot
$before | Set-Content (Join-Path $Root 'user-state-before.json')
# Missing and corrupted downloads must fail before copying the app or registering it.
$code=Install 'missing' ('/FFMPEGARCHIVE="'+(Join-Path $Root 'missing.zip')+'"')
Check 'Missing offline archive blocks setup without installing FRAME' ($code -ne 0 -and !(Test-Path "$dir\FRAME.exe") -and !(Test-Path $key))
[IO.File]::WriteAllText((Join-Path $Root 'corrupt.zip'),'not a trusted FFmpeg package')
$code=Install 'corrupt' ('/FFMPEGARCHIVE="'+(Join-Path $Root 'corrupt.zip')+'"')
Check 'Bad checksum blocks setup without installing FRAME' ($code -ne 0 -and !(Test-Path "$dir\FRAME.exe") -and !(Test-Path $key))
# Real online first install, with no winget call and no manually copied dependencies.
$code=Install 'online' ''
Check 'Online setup downloads and installs recording tools' ($code -eq 0 -and (Test-Path "$dir\tools\ffmpeg.exe") -and (Test-Path "$dir\tools\ffprobe.exe"))
Check 'Installed FRAME matches the new payload' ((Get-FileHash "$dir\FRAME.exe").Hash -eq (Get-FileHash $Payload).Hash)
$manifest=@{}; Get-Content (Join-Path $PSScriptRoot '..\packaging\ffmpeg.ini') | ForEach-Object {if($_ -match '^([A-Za-z0-9]+)=(.+)$'){$manifest[$matches[1]]=$matches[2]}}
Check 'Installed binaries and license match pinned hashes' (
    (Get-FileHash "$dir\tools\ffmpeg.exe").Hash -eq $manifest.FfmpegSha256 -and
    (Get-FileHash "$dir\tools\ffprobe.exe").Hash -eq $manifest.FfprobeSha256 -and
    (Get-FileHash "$dir\tools\FFMPEG-LICENSE.txt").Hash -eq $manifest.LicenseSha256)
$code=Install 'reuse-offline' ('/FFMPEGARCHIVE="'+(Join-Path $Root 'missing.zip')+'"')
Check 'Reinstall reuses verified tools without any archive or download' ($code -eq 0 -and (Select-String -Path "$Root\reuse-offline.log" -SimpleMatch 'download not needed'))
# Simulate an incomplete old install, then ensure failed preparation preserves the app.
$probe=Join-Path $dir 'tools\ffprobe.exe'
[IO.File]::WriteAllText($probe,'damaged fixture')
$code=Install 'failed-repair' ('/FFMPEGARCHIVE="'+(Join-Path $Root 'corrupt.zip')+'"')
Check 'Failed repair leaves existing FRAME and damaged fixture unchanged' ($code -ne 0 -and (Get-FileHash "$dir\FRAME.exe").Hash -eq (Get-FileHash $Payload).Hash -and [IO.File]::ReadAllText($probe) -eq 'damaged fixture')
$code=Install 'repair' ('/FFMPEGARCHIVE="'+$Archive+'"')
Check 'Verified offline archive repairs missing or changed recording tools' ($code -eq 0 -and (Get-FileHash $probe).Hash -eq $manifest.FfprobeSha256)
# Start the installed payload with isolated settings and no explicitly configured FFmpeg.
$data=Join-Path $Root 'data'; New-Item -ItemType Directory "$data\captures" | Out-Null
@{OutputDirectory="$data\captures";Onboarded=$true;VisualMode='dark';CheckForUpdatesOnStartup=$false} | ConvertTo-Json | Set-Content "$data\settings.json"
$p=Start-Process "$dir\FRAME.exe" -ArgumentList "--data `"$data`" --render `"$Root\installed.png`" --mode dark" -WindowStyle Hidden -PassThru
if(!$p.WaitForExit(55000)){throw 'Installed FRAME render timed out'}
Check 'Installed FRAME starts and renders with isolated settings' ($p.ExitCode -eq 0 -and (Test-Path "$Root\installed.png"))
# A media run without --ffmpeg exercises Backend.Resolve against the private tools folder.
$p=Start-Process "$dir\FRAME.exe" -ArgumentList "--media-test `"$Root\media`"" -WindowStyle Hidden -PassThru
if(!$p.WaitForExit(600000)){throw 'Installed FRAME media tests timed out'}
Check 'Installed FRAME discovers private FFmpeg and passes media tests' ($p.ExitCode -eq 0)
$settingsHash=(Get-FileHash "$data\settings.json").Hash
$uninstaller=(Get-ChildItem $dir -Filter 'unins*.exe' | Select-Object -First 1).FullName
$p=Start-Process $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$Root\uninstall.log`"" -WindowStyle Hidden -PassThru
if(!$p.WaitForExit(55000)){throw 'Validation uninstall timed out'}
for($i=0;$i -lt 20 -and (Test-Path "$dir\FRAME.exe");$i++){Start-Sleep -Milliseconds 250}
Check 'Uninstall removes FRAME and its private FFmpeg pair' (!(Test-Path "$dir\FRAME.exe") -and !(Test-Path "$dir\tools\ffmpeg.exe") -and !(Test-Path "$dir\tools\ffprobe.exe") -and !(Test-Path $key))
Check 'Uninstall preserves isolated settings and media output' ((Get-FileHash "$data\settings.json").Hash -eq $settingsHash -and (Test-Path "$Root\media"))
Check 'Real user settings, startup entry and production install location unchanged' ((Snapshot) -eq $before)
"$($checks.Count)/$($checks.Count) setup checks passed."
