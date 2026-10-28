param([Parameter(Mandatory)][string]$OldSetup, [Parameter(Mandatory)][string]$NewSetup, [Parameter(Mandatory)][string]$NewPayload, [Parameter(Mandatory)][string]$Root)
# Installer validation on this PC (no VM available): per-user install into a throw-away folder ($Root\Programs\FRAME),
# upgrade, running-instance check and uninstall. The installed FRAME is only started with --data folders under $Root.
# The user's own %LOCALAPPDATA%\FRAME, Videos\FRAME and HKCU Run "FRAME" value are fingerprinted (read-only) before and
# after and must be unchanged. Start-menu/desktop shortcuts are created by the test install and removed by its uninstall.
$ErrorActionPreference = 'Stop'
$appId = '{0998192B-07BE-4AF5-B687-50024D3B1493}_is1'
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId"
$installDir = Join-Path $Root 'Programs\FRAME'
$startLnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\FRAME.lnk"
$deskLnk = [Environment]::GetFolderPath('Desktop') + '\FRAME.lnk'
$results = [System.Collections.Generic.List[object]]::new()
function Check($name, [bool]$ok, $detail = $null) { $results.Add([ordered]@{ name = $name; passed = $ok; detail = $detail }); Write-Host ("{0} {1}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name) }
function Fingerprint {
    $real = "$env:LOCALAPPDATA\FRAME"; $vid = [Environment]::GetFolderPath('MyVideos') + '\FRAME'
    $files = @(Get-ChildItem $real -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notlike "$real\Logs\*" -and $_.FullName -notlike "$real\Thumbnails\*" })
    [ordered]@{
        settings = $(if (Test-Path "$real\settings.json") { (Get-FileHash "$real\settings.json").Hash } else { 'none' })
        dataFiles = ($files | ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }) -join ';'
        videos = (@(Get-ChildItem $vid -Recurse -File -ErrorAction SilentlyContinue) | ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }) -join ';'
        run = [string](Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue).FRAME
    }
}
function Shortcut($path) { if (-not (Test-Path $path)) { return $null }; $s = (New-Object -ComObject WScript.Shell).CreateShortcut($path); [ordered]@{ target = $s.TargetPath; args = $s.Arguments; workDir = $s.WorkingDirectory; icon = $s.IconLocation } }
function RunSetup($exe, [string[]]$extra, $log) { $p = Start-Process -FilePath $exe -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$log") + $extra) -PassThru; if (-not $p.WaitForExit(300000)) { $p.Kill(); throw "Setup timed out: $exe" }; $p.ExitCode }
function TestData($name, $hotkeys) {
    $d = Join-Path $Root $name; New-Item -ItemType Directory -Force "$d\captures" | Out-Null
    $ff = [string]@((Get-Item "$env:LOCALAPPDATA\Microsoft\WinGet\Links\ffmpeg.exe").Target)[0]
    @{ OutputDirectory = "$d\captures"; FfmpegPath = $ff; Onboarded = $true; ReduceMotion = $true; DesktopAudio = $false; Microphone = $false; ReplaySeconds = 120; StartWithWindows = $true; Hotkeys = $hotkeys } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$d\settings.json"
    'not a real video (installer test)' | Set-Content -LiteralPath "$d\captures\FRAME 2026-10-16 test recording.mp4"
    $d
}
if (Test-Path $uninstallKey) { throw 'FRAME is already installed for this user; the test does not touch an existing installation.' }
if (Get-Process FRAME -ErrorAction SilentlyContinue) { throw 'A FRAME process is running; quit it before the installer test.' }
New-Item -ItemType Directory -Force $Root | Out-Null
$before = Fingerprint

# 1. Fresh install of the older test installer (with desktop shortcut)
$code = RunSetup $OldSetup @("/DIR=$installDir", '/TASKS=desktopicon') "$Root\setup-old.log"
$k = Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue
Check 'Fresh install: Setup exit 0, files present' ($code -eq 0 -and (Test-Path "$installDir\FRAME.exe") -and (Test-Path "$installDir\licenses\FFMPEG-NOTICE.txt") -and (Test-Path "$installDir\BETA_README.md") -and (Test-Path "$installDir\tools\README.txt")) @{ code = $code }
Check 'Fresh install: registered in Installed Apps (per-user key, name, version, location, icon)' ($k -and $k.DisplayName -eq 'FRAME 0.5.3 Beta (upgrade test)' -and $k.DisplayVersion -eq '0.5.3' -and $k.InstallLocation.TrimEnd('\') -eq $installDir -and $k.DisplayIcon -like "$installDir\FRAME.exe*" -and $k.UninstallString) ($k | Select-Object DisplayName, DisplayVersion, InstallLocation, DisplayIcon, UninstallString, Publisher)
$sm = Shortcut $startLnk; $dk = Shortcut $deskLnk
Check 'Fresh install: Start-menu and desktop shortcuts point to the installed FRAME.exe' ($sm -and $sm.target -eq "$installDir\FRAME.exe" -and $sm.workDir -eq $installDir -and $dk -and $dk.target -eq "$installDir\FRAME.exe") @{ start = $sm; desktop = $dk }
$data = TestData 'data' @(@{ Action = 'Record'; Modifiers = 6; Key = 0x52 }, @{ Action = 'Pause'; Modifiers = 6; Key = 0x50 }, @{ Action = 'Replay'; Modifiers = 1; Key = 0x5A }, @{ Action = 'Monitors'; Modifiers = 6; Key = 0x4D }, @{ Action = 'Open'; Modifiers = 3; Key = 0x46 })
$p = Start-Process -FilePath "$installDir\FRAME.exe" -ArgumentList @('--data', $data, '--render', "$Root\installed-old.png") -PassThru; $p.WaitForExit(180000) | Out-Null
Check 'Installed (older) FRAME starts and renders from its install folder' ($p.ExitCode -eq 0 -and (Test-Path "$Root\installed-old.png"))
$dataBefore = (Get-ChildItem $data -Recurse -File | Where-Object { $_.FullName -notlike "*\Logs\*" -and $_.FullName -notlike "*\Thumbnails\*" -and $_.Name -ne 'recording-status.json' } | ForEach-Object { "$($_.FullName)|$((Get-FileHash $_.FullName).Hash)" }) -join ';'

# 2. A running FRAME (main instance mutex) blocks Setup
$runningData = TestData 'running-data' @(@{ Action = 'Replay'; Modifiers = 6; Key = 0x53 })
$running = Start-Process -FilePath "$installDir\FRAME.exe" -ArgumentList @('--data', $runningData) -PassThru; Start-Sleep -Seconds 8
$oldHash = (Get-FileHash "$installDir\FRAME.exe").Hash
$code = RunSetup $NewSetup @() "$Root\setup-while-running.log"
Check 'Upgrade while FRAME runs: Setup stops (AppMutex), files unchanged' ($code -ne 0 -and (Get-FileHash "$installDir\FRAME.exe").Hash -eq $oldHash) @{ code = $code }
Stop-Process -Id $running.Id -Force; Start-Sleep -Seconds 2

# 3. Upgrade (no /DIR: Setup must find the existing installation)
$code = RunSetup $NewSetup @() "$Root\setup-upgrade.log"
$k = Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue
Check 'Upgrade: exit 0, same folder, FRAME.exe replaced by the 0.5.4 payload' ($code -eq 0 -and (Get-FileHash "$installDir\FRAME.exe").Hash -eq (Get-FileHash $NewPayload).Hash -and (Get-Item "$installDir\FRAME.exe").VersionInfo.ProductVersion -eq '0.5.4-beta') @{ code = $code }
Check 'Upgrade: Installed Apps entry updated (same location)' ($k -and $k.DisplayName -eq 'FRAME 0.5.4 Beta' -and $k.DisplayVersion -eq '0.5.4' -and $k.InstallLocation.TrimEnd('\') -eq $installDir) ($k | Select-Object DisplayName, DisplayVersion, InstallLocation)
$sm = Shortcut $startLnk; $dk = Shortcut $deskLnk
Check 'Upgrade: shortcuts still valid (desktop choice remembered)' ($sm -and $sm.target -eq "$installDir\FRAME.exe" -and $dk -and $dk.target -eq "$installDir\FRAME.exe") @{ start = $sm; desktop = $dk }
$dataAfter = (Get-ChildItem $data -Recurse -File | Where-Object { $_.FullName -notlike "*\Logs\*" -and $_.FullName -notlike "*\Thumbnails\*" -and $_.Name -ne 'recording-status.json' } | ForEach-Object { "$($_.FullName)|$((Get-FileHash $_.FullName).Hash)" }) -join ';'
Check 'Upgrade: settings (hotkeys incl. Alt+Z, capture folder, start-up) and recordings in the data folder unchanged' ($dataBefore -eq $dataAfter)
$p = Start-Process -FilePath "$installDir\FRAME.exe" -ArgumentList @('--data', $data, '--render', "$Root\installed-new.png", '--render-settings') -PassThru; $p.WaitForExit(180000) | Out-Null
$settingsAfter = Get-Content "$data\settings.json" -Raw | ConvertFrom-Json
Check 'Upgraded FRAME reads the existing settings (Alt+Z, Start with Windows, capture folder; Shadow hotkey added unset)' ($p.ExitCode -eq 0 -and ($settingsAfter.Hotkeys | Where-Object Action -eq 'Replay').Key -eq 0x5A -and $settingsAfter.StartWithWindows -and $settingsAfter.OutputDirectory -eq "$data\captures" -and -not (Test-Path "$data\settings.json.unreadable-*")) @{ replay = ($settingsAfter.Hotkeys | Where-Object Action -eq 'Replay') }
Check 'Upgraded FRAME writes logs to <data>\Logs (not the install folder)' ((Test-Path "$data\Logs") -and -not (Get-ChildItem $installDir -Recurse -Filter '*.log' -ErrorAction SilentlyContinue))
Check 'No developer files in the installation' (-not (Get-ChildItem $installDir -Recurse -File | Where-Object { $_.Extension -in '.pdb', '.cs', '.json', '.ps1', '.iss', '.mp4', '.mkv' -or $_.Name -like '*settings*' }))

# 4. Uninstall
$uninstaller = (Get-ChildItem $installDir -Filter 'unins*.exe' | Select-Object -First 1).FullName
$p = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$Root\uninstall.log") -PassThru; $p.WaitForExit(120000) | Out-Null
for ($i = 0; $i -lt 60 -and (Test-Path $uninstallKey); $i++) { Start-Sleep -Milliseconds 500 }
Start-Sleep -Seconds 2
Check 'Uninstall: program files, shortcuts and Installed Apps entry removed' (-not (Test-Path "$installDir\FRAME.exe") -and -not (Test-Path $installDir) -and -not (Test-Path $startLnk) -and -not (Test-Path $deskLnk) -and -not (Test-Path $uninstallKey)) @{ left = @(Get-ChildItem $installDir -Recurse -ErrorAction SilentlyContinue | ForEach-Object FullName) }
Check 'Uninstall: data folder and recordings kept (default when not asked)' ((Test-Path "$data\settings.json") -and (Test-Path "$data\captures\FRAME 2026-10-16 test recording.mp4"))
$after = Fingerprint
Check "User's real FRAME data, Videos\FRAME and Start-with-Windows entry untouched by install, upgrade and uninstall" (($before | ConvertTo-Json) -eq ($after | ConvertTo-Json)) @{ run = $after.run }
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Root 'installer-results.json')
"{0}/{1} passed" -f @($results | Where-Object passed).Count, $results.Count
