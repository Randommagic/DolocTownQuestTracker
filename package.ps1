# DolocTownQuestTracker workshop packaging
#
# Builds, then assembles dist\workshop\ in the layout the game's workshop items use
# (info.json + workshop.json + icon.png + preview.png + Content\), with the DTMAPI CodeMod
# payload under Content\DTMAPI\ where DTMAPI looks for it.
#
#   .\package.ps1            build + assemble
#   .\package.ps1 -SkipBuild use whatever is already in bin\

param(
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$modName = 'DolocTownQuestTracker'

if (-not $SkipBuild) {
    & (Join-Path $here 'build.ps1') -NoDeploy
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
}

$dll = Join-Path $here ('bin\' + $modName + '.dll')
if (-not (Test-Path $dll)) { throw "not built: $dll" }

# ModIdentity is the single source of truth for the version. The DLL carries no metadata to
# read it from -- there is no generated AssemblyInfo when building with csc directly.
$identity = Get-Content (Join-Path $here 'src\ModIdentity.cs') -Raw -Encoding UTF8
$m = [regex]::Match($identity, 'Version\s*=\s*"([^"]+)"')
if (-not $m.Success) { throw 'could not read Version from src\ModIdentity.cs' }
$version = $m.Groups[1].Value

$uid = [regex]::Match($identity, 'UniqueID\s*=\s*"([^"]+)"')
if (-not $uid.Success) { throw 'could not read UniqueID from src\ModIdentity.cs' }
$uniqueId = $uid.Groups[1].Value

$dist = Join-Path $here 'dist'
$out = Join-Path $dist 'workshop'
$outDtm = Join-Path $out 'Content\DTMAPI'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $outDtm | Out-Null

Copy-Item (Join-Path $here 'workshop\info.json') $out
Copy-Item (Join-Path $here 'workshop\Content\DTMAPI\*.json') $outDtm
Copy-Item $dll $outDtm

# workshop.json carries the id Steam hands out on first upload. Keep whatever is already
# there; only write a placeholder when there is none, so re-packaging never orphans the item.
$wsSrc = Join-Path $here 'workshop\workshop.json'
if (Test-Path $wsSrc) {
    Copy-Item $wsSrc $out
} else {
    # Written through .NET rather than Out-File: Out-File -Encoding utf8 adds a BOM on Windows
    # PowerShell, and this file is parsed by the game.
    $noBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $out 'workshop.json'), '{ "workshop_id": 0 }', $noBom)
    Write-Output 'NOTE  workshop.json had no id -- wrote a placeholder (0).'
}

# Four files repeat identity for four different readers: the store page, DTMAPI's manifest
# scanner, its package marker, and the code. Drift between them either mislabels the item or
# stops it loading, so it is checked here rather than discovered in a log.
# Every read here is explicitly UTF-8. Windows PowerShell decodes a BOM-less file with the
# ANSI codepage by default, which turns the Korean text in info.json into invalid JSON and
# fails ConvertFrom-Json -- so the encoding is not optional.
$info = Get-Content (Join-Path $out 'info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$manifest = Get-Content (Join-Path $outDtm 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$marker = Get-Content (Join-Path $outDtm 'dtmapi-package.json') -Raw -Encoding UTF8 | ConvertFrom-Json

$problems = @()
if ($info.version -ne $version) { $problems += "info.json version is $($info.version), PluginInfo says $version" }
if ($manifest.Version -ne $version) { $problems += "manifest.json Version is $($manifest.Version), PluginInfo says $version" }
if ($marker.version -ne $version) { $problems += "dtmapi-package.json version is $($marker.version), PluginInfo says $version" }
if ($manifest.UniqueID -ne $uniqueId) { $problems += "manifest.json UniqueID is $($manifest.UniqueID), PluginInfo says $uniqueId" }
if ($marker.uniqueId -ne $uniqueId) { $problems += "dtmapi-package.json uniqueId is $($marker.uniqueId), PluginInfo says $uniqueId" }
if ($manifest.Name -ne $info.name) { $problems += "manifest.json Name is '$($manifest.Name)' but info.json name is '$($info.name)'" }
if ($manifest.Description -ne $info.description) { $problems += "manifest.json Description and info.json description have drifted apart" }
if ($manifest.Author -ne $info.author) { $problems += "manifest.json Author is '$($manifest.Author)' but info.json author is '$($info.author)'" }
if ($manifest.Type -ne 'CodeMod') { $problems += "manifest.json Type must be exactly CodeMod, got $($manifest.Type)" }
if ($manifest.EntryDll -ne ('Content/DTMAPI/' + $modName + '.dll')) { $problems += "manifest.json EntryDll does not point at the packaged DLL: $($manifest.EntryDll)" }
if ($problems.Count -gt 0) {
    Write-Output ''
    foreach ($p in $problems) { Write-Output ("MISMATCH  " + $p) }
    throw 'package identity does not line up; fix the files above'
}

foreach ($art in @('icon.png', 'preview.png')) {
    $src = Join-Path $here ('workshop\' + $art)
    if (Test-Path $src) { Copy-Item $src $out } else { Write-Output ("TODO  missing " + $art) }
}

$zip = Join-Path $dist ($modName + '-' + $version + '.zip')
Compress-Archive -Path (Join-Path $out 'Content') -DestinationPath $zip

Write-Output ''
Write-Output ("PACKAGED  " + $version)
Write-Output ("  upload folder : " + $out)
Write-Output ("  content zip   : " + $zip)
Get-ChildItem $out -Recurse -File | ForEach-Object {
    Write-Output ("    " + $_.FullName.Replace($out, '').TrimStart('\'))
}
