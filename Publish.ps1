param([switch]$SelfContained, [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$env:MSBuildEnableWorkloadResolver = 'false'
Push-Location $PSScriptRoot
try {
    if ($SelfContained) {
        foreach ($project in @('src/Frame.App')) {
            dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputDirectory -m:1
            if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
        }
    } else {
        foreach ($project in @('src/Frame.App')) {
            dotnet publish $project -c Release --self-contained false -p:RestoreIgnoreFailedSources=true -o $OutputDirectory -m:1 -nodeReuse:false
            if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
        }
    }
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
    $quickStart = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'docs/QUICKSTART.md')).Replace('(VALIDATION.md)', '(docs/VALIDATION.md)').Replace('(LIMITATIONS.md)', '(docs/LIMITATIONS.md)')
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'README.md'), $quickStart)
    $docsDirectory = Join-Path $OutputDirectory 'docs'
    New-Item -ItemType Directory -Force -Path $docsDirectory | Out-Null
    foreach ($document in @('ARCHITECTURE.md','LIMITATIONS.md','VALIDATION.md','ACCEPTANCE.md','CHANGELOG.md','UPDATES.md','FRAME_068_VALIDATION.md','FRAME_067_VALIDATION.md','FRAME_066_VALIDATION.md','FRAME_065_VALIDATION.md','core-tests.txt','media-validation.json','idle-benchmark.json','interface-dark.png','interface-light.png','interface-hotkeys.png','hotkey-dialog-tests.json')) {
        $documentPath = Join-Path 'docs' $document
        if (Test-Path -LiteralPath $documentPath) { Copy-Item -LiteralPath $documentPath -Destination $docsDirectory }
    }
    Write-Host "Ready: $OutputDirectory\FRAME.exe. FFmpeg and ffprobe are separate runtime dependencies. (FRAME Advanced Capture is a separate repository, C:\dev\AdvancedCapture, with its own Package.ps1.)"
} finally { Pop-Location }
