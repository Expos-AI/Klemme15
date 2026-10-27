# Fetch EdiabasLib (GPLv3, https://github.com/uholeschak/ediabaslib) into external\ediabaslib.
# Only needed for EDIABAS mode in the Windows build. Pinned to a tested commit.
$ErrorActionPreference = 'Stop'
$commit = '3ef551e8ad425ee362868ff4bef1b55f15330039'
$dir = Join-Path (Split-Path $PSScriptRoot -Parent) 'external\ediabaslib'
if (-not (Test-Path (Join-Path $dir '.git'))) {
    git init -q $dir
    git -C $dir remote add origin https://github.com/uholeschak/ediabaslib.git
}
git -C $dir fetch -q --depth 1 origin $commit
git -C $dir checkout -q FETCH_HEAD
Write-Host "EdiabasLib ready at $dir ($commit)"
