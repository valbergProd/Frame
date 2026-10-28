param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$ResultFile
)
# Setup stages only these four known entries before changing any installed files.
# No PATH/registry/settings changes, archive-wide extraction, or package-manager dependency.
$ErrorActionPreference = 'Stop'
# Setup can inherit PowerShell 7's PSModulePath through its launching app/shell.
# This helper runs in Windows PowerShell 5.1 and needs only its inbox modules.
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')
try {
    $package = @{}
    foreach ($line in [IO.File]::ReadAllLines($Manifest)) {
        if ($line -match '^([A-Za-z0-9]+)=(.+)$') { $package[$matches[1]] = $matches[2] }
    }
    if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $package.Sha256) {
        throw 'The FFmpeg download checksum does not match. Download it again.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $entries = @(
        @{Source='bin/ffmpeg.exe'; Target='ffmpeg.exe'; Hash=$package.FfmpegSha256},
        @{Source='bin/ffprobe.exe'; Target='ffprobe.exe'; Hash=$package.FfprobeSha256},
        @{Source='LICENSE'; Target='FFMPEG-LICENSE.txt'; Hash=$package.LicenseSha256},
        @{Source='README.txt'; Target='FFMPEG-README.txt'; Hash=$package.ReadmeSha256}
    )
    [void][IO.Directory]::CreateDirectory($Destination)
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($item in $entries) {
            $entry = $zip.GetEntry($package.Root + '/' + $item.Source)
            if (!$entry -or $entry.Length -gt 300MB) { throw "Missing or oversized FFmpeg component: $($item.Source)" }
            $target = Join-Path $Destination $item.Target
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $item.Hash) {
                throw "FFmpeg component checksum failed: $($item.Source)"
            }
        }
    } finally { $zip.Dispose() }
    # Bounded probes of the verified binaries: no capture, device access or installation.
    foreach ($probe in @(
        @{Exe='ffmpeg.exe'; Args='-hide_banner -h filter=gfxcapture'; Text='hmonitor'},
        @{Exe='ffprobe.exe'; Args='-version'; Text='ffprobe version ' + $package.Version}
    )) {
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = Join-Path $Destination $probe.Exe
        $start.Arguments = $probe.Args
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (!$process.WaitForExit(15000)) { $process.Kill(); throw "FFmpeg verification timed out: $($probe.Exe)" }
            $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0 -or !$output.Contains($probe.Text)) { throw "FFmpeg verification failed: $($probe.Exe)" }
        } finally { $process.Dispose() }
    }
    [IO.File]::WriteAllText($ResultFile, 'FFmpeg is ready.')
    exit 0
} catch {
    [IO.File]::WriteAllText($ResultFile, $_.Exception.Message)
    exit 1
}
