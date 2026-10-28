param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$NotesFile,
    [switch]$Publish,
    [switch]$Preview
)
# Upload a reviewed installer as a draft; -Publish explicitly makes it visible to FRAME's updater.
# Does not build, replace an existing release, or push source changes. Commit/push and tag the matching source first.
$ErrorActionPreference = 'Stop'
$repo='valbergProd/Frame'
$root=Split-Path -Parent $PSScriptRoot
if($Version -notmatch '^\d+\.\d+\.\d+(-beta(\.\d+)?)?$') {throw 'Use a release version such as 0.6.6-beta or 1.0.0.'}
$numeric=$Version -replace '-.*$',''
$beta=$Version.Contains('-beta')
$expected="FRAME-$numeric" + $(if($beta){'-Beta'}else{''}) + '-Setup.exe'
$file=Get-Item -LiteralPath $Installer
$notes=Get-Item -LiteralPath $NotesFile
if($file.Name -cne $expected -or $file.Length -le 0 -or $file.Length -gt 512MB) {throw "Expected $expected, no larger than 512 MB."}
if($file.VersionInfo.ProductName.Trim() -ne 'FRAME' -or "$($file.VersionInfo.FileMajorPart).$($file.VersionInfo.FileMinorPart).$($file.VersionInfo.FileBuildPart)" -ne $numeric) {throw 'Installer version metadata does not match.'}
$hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$tag="v$Version"
if($Preview) { [pscustomobject]@{Repository=$repo;Tag=$tag;Installer=$file.FullName;Size=$file.Length;Sha256=$hash;Notes=$notes.FullName;Publish=[bool]$Publish}; return }
Push-Location $root
try {
    $status=git status --porcelain
    if($LASTEXITCODE -ne 0 -or $status) {throw 'Commit and push the source before releasing.'}
    $head=git rev-parse HEAD
    $tagCommit=git rev-list -n 1 $tag
    if($LASTEXITCODE -ne 0 -or $tagCommit -ne $head) {throw "Tag $tag must point to the checked-out, reviewed commit."}
    $remote=git ls-remote origin "refs/tags/$tag" "refs/tags/$tag^{}"
    if($LASTEXITCODE -ne 0 -or !$remote -or !($remote | Where-Object {$_ -like "$head*"})) {throw "Push tag $tag before releasing."}
    $releaseArgs=@('release','create',$tag,'--repo',$repo,'--verify-tag','--draft','--title',"FRAME $Version",'--notes-file',$notes.FullName,$file.FullName)
    if($beta) {$releaseArgs+='--prerelease'}
    & gh @releaseArgs
    if($LASTEXITCODE -ne 0) {throw 'Release creation failed. Inspect GitHub before retrying; an existing release is never overwritten.'}
    $json=gh api "repos/$repo/releases/tags/$tag"
    if($LASTEXITCODE -ne 0) {throw 'Release is still a draft: metadata could not be verified.'}
    $release=$json | ConvertFrom-Json
    $asset=@($release.assets | Where-Object {$_.name -ceq $expected})
    if($asset.Count -ne 1 -or $asset[0].size -ne $file.Length -or $asset[0].digest -cne "sha256:$hash") {throw 'Release is still a draft: uploaded installer size/checksum verification failed.'}
    if($Publish) {
        gh release edit $tag --repo $repo --draft=false
        if($LASTEXITCODE -ne 0) {throw 'Publication failed; inspect the draft on GitHub.'}
        Write-Host "Published: $($release.html_url)"
    } else {Write-Host "Verified draft: $($release.html_url). Review it before publishing."}
} finally {Pop-Location}
