param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$env:MSBuildEnableWorkloadResolver = 'false'
Push-Location $PSScriptRoot
try {
    dotnet restore Frame.sln --ignore-failed-sources --disable-parallel -p:RestoreIgnoreFailedSources=true -m:1 -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build Frame.sln -c Release --no-restore -m:1 -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipTests) {
        dotnet run --project tests/Frame.Tests -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    }
} finally { Pop-Location }
