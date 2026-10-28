param(
    # Optional: package an already published FRAME.exe instead of publishing (e.g. to build a test installer of an older build).
    [string]$SourceExe = '',
    # Optional overrides for test installers; the release uses the csproj version.
    [string]$Version = '',
    [string]$Label = '',
    [string]$OutputRoot = ''
)
# FRAME beta installer build. Usage (from any folder):
#   powershell -ExecutionPolicy Bypass -File C:\dev\Frame\scripts\Build-BetaInstaller.ps1
# Steps: version from src\Frame.App\Frame.App.csproj -> check dotnet + Inno Setup 6 -> self-contained win-x64 single-file
# publish into a NEW release folder (never overwrites) -> verify FRAME.exe (version, size, embedded resources via a quick
# --render of the published exe on a throw-away --data folder) -> wizard images -> ISCC packaging\FRAME.iss -> verify
# Setup.exe (version resource) -> SHA-256 -> release folder with Setup.exe, checksum, BETA_README.md, RELEASE_NOTES.md,
# licenses\, app\FRAME.exe (the exact payload) and build-info.json.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repo 'src\Frame.App\Frame.App.csproj'
$projectVersion = ([xml](Get-Content -LiteralPath $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $projectVersion) { throw "No <Version> in $csproj" }
# .NET runtime bundled into the self-contained FRAME.exe (Frame.App.csproj BundledRuntimeVersion; checked in the smoke test)
$bundledRuntime = ([xml](Get-Content -LiteralPath $csproj -Raw)).Project.PropertyGroup.BundledRuntimeVersion | Where-Object { $_ } | Select-Object -First 1
if (-not $Version) { $Version = $projectVersion }
if (-not $Label) { $Label = "$Version Beta" }
$fileVersion = ($Version -replace '[^0-9.]', '') + '.0'
if (($fileVersion.Split('.')).Count -gt 4) { $fileVersion = ($fileVersion.Split('.')[0..3]) -join '.' }

# Tools
$innoVersion = @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1', 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1') | ForEach-Object { (Get-ItemProperty $_ -ErrorAction SilentlyContinue).DisplayVersion } | Where-Object { $_ } | Select-Object -First 1
if (-not $innoVersion) { $innoVersion = 'Inno Setup 6 (version not registered)' }
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isinfo.php (or: winget install JRSoftware.InnoSetup).' }
if (-not $SourceExe -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET 8 SDK (dotnet) is required to publish FRAME.' }

# Release folder (unique, never reused)
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'builds' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$release = Join-Path $OutputRoot ("FRAME-$($Label -replace '\s+', '-')-$stamp")
if (Test-Path $release) { throw "Release folder already exists (not overwritten): $release" }
New-Item -ItemType Directory -Path $release | Out-Null
$app = Join-Path $release 'app'; New-Item -ItemType Directory -Path $app | Out-Null
Write-Host "FRAME $Label -> $release"

# Publish (or take a given exe)
if ($SourceExe) {
    Copy-Item -LiteralPath $SourceExe -Destination (Join-Path $app 'FRAME.exe')
} else {
    $publish = Join-Path $release '_publish'
    & dotnet publish (Join-Path $repo 'src\Frame.App') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -nologo -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    $unexpected = Get-ChildItem $publish -File | Where-Object { $_.Name -ne 'FRAME.exe' }
    if ($unexpected) { throw "Unexpected files next to the single-file FRAME.exe: $($unexpected.Name -join ', ')" }
    Move-Item (Join-Path $publish 'FRAME.exe') (Join-Path $app 'FRAME.exe'); Remove-Item $publish -Recurse
}
$exe = Join-Path $app 'FRAME.exe'
$info = (Get-Item $exe).VersionInfo
if (-not $SourceExe -and $info.FileVersion -ne $fileVersion) { throw "FRAME.exe file version $($info.FileVersion) does not match $fileVersion" }
if ((Get-Item $exe).Length -lt 50MB) { throw 'FRAME.exe is unexpectedly small for a self-contained build.' }

# Smoke test of the payload: renders the main window from embedded resources on a throw-away data folder (no capture).
$smoke = Join-Path ([IO.Path]::GetTempPath()) ("FRAME-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$smoke\captures" | Out-Null
@{ OutputDirectory = "$smoke\captures"; Onboarded = $true; ReduceMotion = $true } | ConvertTo-Json | Set-Content -LiteralPath "$smoke\settings.json"
$p = Start-Process -FilePath $exe -ArgumentList @('--data', $smoke, '--render', "$smoke\smoke.png") -PassThru
if (-not $p.WaitForExit(180000)) { $p.Kill(); throw 'Smoke render timed out.' }
if ($p.ExitCode -ne 0 -or -not (Test-Path "$smoke\smoke.png") -or (Get-Item "$smoke\smoke.png").Length -lt 20KB) { throw "Smoke render failed (exit $($p.ExitCode))." }
# [string]: Get-Content adds PowerShell metadata (PSPath, …) that ConvertTo-Json would otherwise write out.
$smokeLog = [string](Get-ChildItem "$smoke\Logs" -Filter 'FRAME-*.log' -ErrorAction SilentlyContinue | Get-Content -Encoding UTF8 | Select-Object -First 1)
if (-not $smokeLog) { $smokeLog = 'rendered; no Logs folder (payload older than the 0.5.4 beta logging)' }
if (-not $SourceExe -and $bundledRuntime -and $smokeLog -notlike "*.NET $bundledRuntime *") { throw "The published FRAME.exe does not run on the expected .NET $bundledRuntime runtime: $smokeLog" }
Remove-Item $smoke -Recurse -Force

# Wizard images from the FRAME icon (dark tile, like the app)
# WPF's decoder reads every icon frame (the large ones are PNG-compressed, which GDI+ in Windows PowerShell cannot read).
Add-Type -AssemblyName PresentationCore, WindowsBase
$images = @()
$iconStream = [IO.File]::OpenRead((Join-Path $repo 'assets\Frame.ico'))
try { $frame = [System.Windows.Media.Imaging.BitmapDecoder]::Create($iconStream, 'PreservePixelFormat', 'OnLoad').Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1 } finally { $iconStream.Dispose() }
foreach ($size in 55, 110) {
    $visual = New-Object System.Windows.Media.DrawingVisual; $dc = $visual.RenderOpen()
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(23, 19, 16))), $null, (New-Object System.Windows.Rect 0, 0, $size, $size))
    $pad = [int]($size * 0.14); $dc.DrawImage($frame, (New-Object System.Windows.Rect $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))); $dc.Close()
    $target = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32); $target.Render($visual)
    $rgb = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $target, ([System.Windows.Media.PixelFormats]::Bgr24), $null, 0
    $encoder = New-Object System.Windows.Media.Imaging.BmpBitmapEncoder; $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rgb))
    $path = Join-Path $release "_wizard-$size.bmp"; $out = [IO.File]::Create($path); try { $encoder.Save($out) } finally { $out.Dispose() }; $images += $path
}
# Large welcome/finish image (164 x 314 at 100 %, 328 x 628 at 200 %): the app's desert artwork, darkened, with the mark.
$desert = New-Object System.Windows.Media.Imaging.BitmapImage; $desert.BeginInit(); $desert.UriSource = [Uri](Join-Path $repo 'assets\desert-drift.png'); $desert.CacheOption = 'OnLoad'; $desert.EndInit()
$large = @()
foreach ($scale in 1, 2) {
    $w = 164 * $scale; $h = 314 * $scale
    $visual = New-Object System.Windows.Media.DrawingVisual; $dc = $visual.RenderOpen()
    $cover = [Math]::Max($w / $desert.PixelWidth, $h / $desert.PixelHeight); $iw = $desert.PixelWidth * $cover; $ih = $desert.PixelHeight * $cover
    $dc.DrawImage($desert, (New-Object System.Windows.Rect (($w - $iw) * 0.72), (($h - $ih) / 2), $iw, $ih))
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromArgb(110, 23, 19, 16))), $null, (New-Object System.Windows.Rect 0, 0, $w, $h))
    $mark = 64 * $scale; $dc.DrawImage($frame, (New-Object System.Windows.Rect (($w - $mark) / 2), (88 * $scale), $mark, $mark)); $dc.Close()
    $target = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $w, $h, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32); $target.Render($visual)
    $rgb = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $target, ([System.Windows.Media.PixelFormats]::Bgr24), $null, 0
    $encoder = New-Object System.Windows.Media.Imaging.BmpBitmapEncoder; $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rgb))
    $path = Join-Path $release "_wizard-large-$scale.bmp"; $out = [IO.File]::Create($path); try { $encoder.Save($out) } finally { $out.Dispose() }; $large += $path
}

# Installer
$base = "FRAME-$Version-Beta-Setup"
& $iscc "/DAppVersion=$Version" "/DFileVersion=$fileVersion" "/DVersionLabel=$Label" "/DSourceExe=$exe" "/DOutputDir=$release" "/DRepoRoot=$repo" "/DOutputBase=$base" "/DWizardImages=$($images -join ',')" "/DWizardLargeImages=$($large -join ',')" (Join-Path $repo 'packaging\FRAME.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
Remove-Item -LiteralPath ($images + $large)
$setup = Join-Path $release "$base.exe"
if (-not (Test-Path $setup)) { throw "Setup was not created: $setup" }
$setupInfo = (Get-Item $setup).VersionInfo
if ("$($setupInfo.ProductName)".Trim() -ne 'FRAME' -or "$($setupInfo.FileVersion)".Trim() -ne $fileVersion) { throw "Setup version resource mismatch: $($setupInfo.ProductName) $($setupInfo.FileVersion)" }

# Checksums and documents
$hash = (Get-FileHash -Algorithm SHA256 $setup).Hash
"$hash  $base.exe" | Set-Content -LiteralPath "$setup.sha256.txt" -Encoding ASCII
Copy-Item (Join-Path $repo 'packaging\BETA_README.md'), (Join-Path $repo 'packaging\RELEASE_NOTES.md') $release
Copy-Item (Join-Path $repo 'packaging\licenses') (Join-Path $release 'licenses') -Recurse
$buildInfo = [ordered]@{
    product = 'FRAME'; version = $Version; label = $Label; fileVersion = $fileVersion; built = (Get-Date).ToString('o')
    setup = [ordered]@{ file = "$base.exe"; bytes = (Get-Item $setup).Length; sha256 = $hash; signed = $false }
    payload = [ordered]@{ file = 'app\FRAME.exe'; bytes = (Get-Item $exe).Length; sha256 = (Get-FileHash -Algorithm SHA256 $exe).Hash; fileVersion = $info.FileVersion; productVersion = $info.ProductVersion }
    tools = [ordered]@{ dotnet = $(if ($SourceExe) { 'not used (given exe)' } else { (& dotnet --version) }); bundledRuntime = $(if ($SourceExe) { 'see smokeTest' } else { $bundledRuntime }); innoSetup = $innoVersion }
    smokeTest = $smokeLog
}
$buildInfo | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $release 'build-info.json') -Encoding UTF8
Write-Host ''
Write-Host "Setup:    $setup"
Write-Host "Size:     $([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB"
Write-Host "SHA-256:  $hash"
Write-Host "Payload:  $exe ($($info.FileVersion), $($info.ProductVersion))"
Write-Host "Signed:   no (no code-signing certificate configured)"


