param([string]$Destination = 'C:\dev\Frame')
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($Destination)
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
if ($target.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a destination outside this source folder.' }
if (Test-Path -LiteralPath $target) {
    if ((Get-ChildItem -LiteralPath $target -Force | Measure-Object).Count -gt 0) { throw "Destination is not empty: $target. Existing files will not be overwritten." }
} else { New-Item -ItemType Directory -Path $target | Out-Null }
foreach ($name in @('src','tests','assets','docs','Frame.sln','Directory.Build.props','README.md','Build.ps1','Publish.ps1','Run.ps1','Recover-Session.ps1','.gitignore','AGENTS.md','CLAUDE.md','Handover_files')) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $name)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $target -Recurse }
}
Write-Host "Project copied to $target. Run Build.ps1 and Publish.ps1 there."
