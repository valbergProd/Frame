$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'dist\FRAME.exe'
if (-not (Test-Path -LiteralPath $exe)) { & (Join-Path $PSScriptRoot 'Publish.ps1') }
Start-Process -FilePath $exe
