param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Root)
# Beta diagnostics validation on isolated --data folders under $Root (never the user's %LOCALAPPDATA%\FRAME).
# 1 real crash (--crash-test) -> 2 diagnostics harness on the same folder (note, export, controlled failures)
# 3 FFmpeg missing (--ffmpeg-only-settings) -> panel + log   4 unreadable settings -> defaults + kept copy + log
$ErrorActionPreference = 'Stop'
$ffmpeg = [string]@((Get-Item "$env:LOCALAPPDATA\Microsoft\WinGet\Links\ffmpeg.exe").Target)[0]
New-Item -ItemType Directory -Force $Root | Out-Null
function New-TestData($name, $settings) { $d = Join-Path $Root $name; New-Item -ItemType Directory -Force "$d\captures" | Out-Null; $settings.OutputDirectory = "$d\captures"; $settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$d\settings.json" -Encoding UTF8; $d }
function Lines($d) { Get-ChildItem "$d\Logs" -Filter 'FRAME-*.log' -ErrorAction SilentlyContinue | Get-Content }
$out = [ordered]@{}

# 1 + 2
$d = New-TestData 'crash-then-harness' @{ FfmpegPath = $ffmpeg; Onboarded = $true; ReduceMotion = $true; DesktopAudio = $false; Microphone = $false; Sound = $false; Toolbar = $false }
$p = Start-Process -FilePath $Exe -ArgumentList @('--data', $d, '--crash-test') -PassThru; $exited = $p.WaitForExit(60000)
$crashLine = Lines $d | Where-Object { $_ -match '"Level":"Critical"' -and $_ -match 'Simulated fatal error' } | Select-Object -Last 1
$out.crash = [ordered]@{ exited = $exited; exitCode = $(if ($exited) { $p.ExitCode } else { $null }); criticalLogged = [bool]$crashLine; crashJson = (Test-Path "$d\Logs\crash.json"); sessionMarkerLeft = (Test-Path "$d\Logs\session.json") }
$p = Start-Process -FilePath $Exe -ArgumentList @('--data', $d, '--render', "$d\after-crash.png", '--diagnostics-test', "$d\diagnostics.json") -PassThru; if (-not $p.WaitForExit(300000)) { $p.Kill(); throw 'harness timeout' }
$h = Get-Content "$d\diagnostics.json" -Raw | ConvertFrom-Json
$out.harness = [ordered]@{ exit = $p.ExitCode; passed = $h.passed; total = $h.total; failed = @($h.results | Where-Object { -not $_.passed } | ForEach-Object { $_.name }) }
$out.cleanExitRemovedMarker = -not (Test-Path "$d\Logs\session.json")

# 3 FFmpeg missing
$d3 = New-TestData 'ffmpeg-missing' @{ FfmpegPath = ''; Onboarded = $true; ReduceMotion = $true; DesktopAudio = $false; Microphone = $false }
$p = Start-Process -FilePath $Exe -ArgumentList @('--data', $d3, '--ffmpeg-only-settings', '--render', "$d3\ffmpeg-missing.png") -PassThru; $p.WaitForExit(120000) | Out-Null
$out.ffmpegMissing = [ordered]@{ exit = $p.ExitCode; logged = [bool](Lines $d3 | Where-Object { $_ -match '"Component":"engine-failed"' -and $_ -match '"Level":"Error"' -and $_ -match 'FFmpeg 8.1' }); render = "$d3\ffmpeg-missing.png" }

# 4 unreadable settings (invalid frame rate)
$d4 = Join-Path $Root 'bad-settings'; New-Item -ItemType Directory -Force $d4 | Out-Null
'{ "OutputDirectory": "' + ($d4 -replace '\\', '\\') + '", "Fps": 45, "Onboarded": true }' | Set-Content -LiteralPath "$d4\settings.json" -Encoding UTF8
$p = Start-Process -FilePath $Exe -ArgumentList @('--data', $d4, '--render', "$d4\bad-settings.png") -PassThru; $p.WaitForExit(120000) | Out-Null
$out.badSettings = [ordered]@{ exit = $p.ExitCode; logged = [bool](Lines $d4 | Where-Object { $_ -match '"Component":"settings"' -and $_ -match '"Level":"Error"' }); keptCopy = [bool](Get-ChildItem $d4 -Filter 'settings.json.unreadable-*') }

$out | ConvertTo-Json -Depth 6 | Tee-Object -FilePath (Join-Path $Root 'validate-diagnostics.json')
