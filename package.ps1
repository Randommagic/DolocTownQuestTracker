# DolocTownQuestTracker workshop packaging
#
# Builds, then assembles dist\workshop\ in the layout the game's workshop items use
# (info.json + workshop.json + icon.png + preview.png + Content\), and zips the DLL on
# its own for anyone who just wants the plugin.
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

# PluginInfo is the single source of truth for the version. The DLL carries no metadata to
# read it from -- there is no generated AssemblyInfo when building with csc directly.
$pluginInfo = Get-Content (Join-Path $here 'src\PluginInfo.cs') -Raw
$m = [regex]::Match($pluginInfo, 'Version\s*=\s*"([^"]+)"')
if (-not $m.Success) { throw 'could not read Version from src\PluginInfo.cs' }
$version = $m.Groups[1].Value

$dist = Join-Path $here 'dist'
$out = Join-Path $dist 'workshop'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $out 'Content') | Out-Null

Copy-Item (Join-Path $here 'workshop\info.json') $out
Copy-Item (Join-Path $here 'workshop\Content\*') (Join-Path $out 'Content')
Copy-Item $dll (Join-Path $out 'Content')

# workshop.json carries the id Steam hands out on first upload. Keep whatever is already
# there; only write a placeholder when there is none, so re-packaging never orphans the item.
$wsSrc = Join-Path $here 'workshop\workshop.json'
if (Test-Path $wsSrc) {
    Copy-Item $wsSrc $out
} else {
    '{ "workshop_id": 0 }' | Out-File (Join-Path $out 'workshop.json') -Encoding utf8
    Write-Output 'NOTE  workshop.json had no id -- wrote a placeholder (0).'
}

# info.json repeats the version for the store page; keep the two from drifting apart.
$info = Get-Content (Join-Path $out 'info.json') -Raw | ConvertFrom-Json
if ($info.version -ne $version) {
    Write-Output ("NOTE  info.json says " + $info.version + " but PluginInfo says " + $version + ".")
    Write-Output '      Update workshop\info.json so they match.'
}

foreach ($art in @('icon.png', 'preview.png')) {
    $src = Join-Path $here ('workshop\' + $art)
    if (Test-Path $src) { Copy-Item $src $out } else { Write-Output ("TODO  missing " + $art) }
}

$zip = Join-Path $dist ($modName + '-' + $version + '.zip')
Compress-Archive -Path $dll, (Join-Path $here 'workshop\Content\INSTALL.txt') -DestinationPath $zip

Write-Output ''
Write-Output ("PACKAGED  " + $version)
Write-Output ("  upload folder : " + $out)
Write-Output ("  dll only zip  : " + $zip)
Get-ChildItem $out -Recurse -File | ForEach-Object {
    Write-Output ("    " + $_.FullName.Replace($out, '').TrimStart('\'))
}
