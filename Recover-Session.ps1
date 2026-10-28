param([Parameter(Mandatory=$true)][string]$CaptureDirectory, [Parameter(Mandatory=$true)][string]$OutputFile, [string]$Ffmpeg = 'ffmpeg.exe')
$ErrorActionPreference = 'Stop'
$captureRoot = (Resolve-Path -LiteralPath $CaptureDirectory).Path
$output = [IO.Path]::GetFullPath($OutputFile)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output filename; recovery does not overwrite captures.' }
$parts = @(Get-ChildItem -LiteralPath $captureRoot -Filter 'part-*.mkv' | Sort-Object Name)
if ($parts.Count -eq 0) { throw 'No recovery segments found. Choose the Capture-1 or Capture-2 folder inside a recovery session.' }
$list = Join-Path $captureRoot ('recover-' + [guid]::NewGuid().ToString('N') + '.txt')
$lines = $parts | ForEach-Object { if ($_.Name -notmatch '^part-\d+\.mkv$') { throw 'Unexpected segment filename.' }; "file '$($_.Name)'" }
[IO.File]::WriteAllLines($list, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))
$partial = $output + '.partial.mp4'
try {
    & $Ffmpeg -v error -n -f concat -safe 0 -i $list -c copy -movflags +faststart $partial
    if ($LASTEXITCODE -ne 0) { throw 'Recovery could not finalize. Original segments and partial output remain.' }
    & $Ffmpeg -v error -xerror -i $partial -frames:v 1 -f null -
    if ($LASTEXITCODE -ne 0) { throw 'Recovered output did not decode. Original segments remain.' }
    Move-Item -LiteralPath $partial -Destination $output
    Write-Host "Recovered: $output. Review content and timing before removing the original segments."
} finally { Remove-Item -LiteralPath $list -ErrorAction SilentlyContinue }
