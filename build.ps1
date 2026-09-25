# DolocTownQuestTracker build + deploy
#
# This machine has no .NET SDK (dotnet runtimes only, no `dotnet build`) and no Roslyn,
# so we drive the in-box .NET Framework compiler directly. That compiler is C# 5 only --
# no ?., no $"", no expression-bodied members. The source is kept C# 5 clean so it also
# compiles unchanged on any modern toolchain if an SDK is installed later.
#
# The mod is a DTMAPI CodeMod, so the build needs DTMAPI installed in the game: its
# 1_install_dtmapi.bat puts DTMAPI.Abstractions.dll under BepInEx\plugins\DTMAPI.
#
#   .\build.ps1              build + deploy to the local MODS folder
#   .\build.ps1 -NoDeploy    build only

param(
    [string] $GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Doloc Town",
    [switch] $NoDeploy
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$modName = 'DolocTownQuestTracker'

$managed = Join-Path $GameDir 'DolocTown_Data\Managed'
$bepCore = Join-Path $GameDir 'BepInEx\core'
$dtmapi = Join-Path $GameDir 'BepInEx\plugins\DTMAPI'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

foreach ($p in @($managed, $bepCore, $dtmapi, $csc)) {
    if (-not (Test-Path $p)) { throw "not found: $p" }
}

# UnityEngine.dll is the legacy monolithic facade. Assembly-CSharp's own references reach it,
# so it stays on the list.
#
# netstandard.dll is required and produces one unavoidable warning: DTMAPI.Abstractions was
# built against netstandard 2.0 while the game ships 2.1, so csc reports CS1701 and unifies
# them. It resolves the same way at runtime under Unity Mono -- the reference CodeMod on the
# workshop (Quality of Doloc) is itself built against netstandard 2.1.
$refNames = @(
    'mscorlib', 'System', 'System.Core', 'netstandard',
    'UnityEngine',
    'UnityEngine.CoreModule', 'UnityEngine.UI', 'UnityEngine.UIModule',
    'UnityEngine.TextRenderingModule', 'Unity.InputSystem',
    # MissionTip's fields are TMP_Text, so the overlay needs TextMeshPro.
    'Unity.TextMeshPro',
    # firstpass holds Bright.Config.BeanBase, the base of every Luban-generated *Info type
    # (FactionMissionInfo, SeedUnlockInfo, ...), so it is required even though we never
    # name it directly.
    'Assembly-CSharp', 'Assembly-CSharp-firstpass'
)
$refs = @()
foreach ($n in $refNames) {
    $path = Join-Path $managed ($n + '.dll')
    if (-not (Test-Path $path)) { throw "reference missing: $path" }
    $refs += "/r:$path"
}

# Harmony comes from BepInEx's core, which DTMAPI's installer put there. DTMAPI loads our DLL
# into the same AppDomain, so both resolve at runtime without being shipped.
$harmony = Join-Path $bepCore '0Harmony.dll'
if (-not (Test-Path $harmony)) { throw "reference missing: $harmony" }
$refs += "/r:$harmony"

$abstractions = Join-Path $dtmapi 'DTMAPI.Abstractions.dll'
if (-not (Test-Path $abstractions)) { throw "reference missing: $abstractions" }
$refs += "/r:$abstractions"

$sources = @(Get-ChildItem (Join-Path $here 'src') -Filter *.cs -Recurse | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'no sources under src\' }

$outDir = Join-Path $here 'bin'
New-Item -ItemType Directory -Force $outDir | Out-Null
$outDll = Join-Path $outDir ($modName + '.dll')

Write-Output "sources : $($sources.Count) file(s)"
Write-Output "refs    : $($refs.Count)"
Write-Output "out     : $outDll"
Write-Output ''

# /nostdlib+ /noconfig so the game's own mscorlib is used rather than the machine's.
# /preferreduilang so diagnostics come back in ASCII English -- the localized ones arrive in
# the OEM codepage and are unreadable in most terminals and CI logs.
& $csc /nologo /target:library /nostdlib+ /noconfig /optimize+ /debug- /warn:4 `
    /preferreduilang:en-US `
    "/out:$outDll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }

Write-Output ''
Write-Output "BUILD OK  $((Get-Item $outDll).Length) bytes"

if ($NoDeploy) { return }

# A DTMAPI CodeMod is not a BepInEx plugin: it is discovered as a mod package, either under
# the game's local MODS folder or in a workshop subscription. Deploy to the local folder --
# it is user-writable, and it is the same layout the workshop item ships.
$dest = Join-Path $env:USERPROFILE ("AppData\LocalLow\RedSawGames\DolocTown\MODS\" + $modName)
$destContent = Join-Path $dest 'Content'
$destDtm = Join-Path $destContent 'DTMAPI'
try {
    # Wipe Content first: DTMAPI walks every file in the package before loading, and a DLL or
    # manifest left over from an earlier layout would sit there being inspected forever.
    if (Test-Path $destContent) { Remove-Item $destContent -Recurse -Force }
    New-Item -ItemType Directory -Force $destDtm | Out-Null
    Copy-Item $outDll -Destination $destDtm -Force
    Copy-Item (Join-Path $here 'workshop\info.json') -Destination $dest -Force
    Copy-Item (Join-Path $here 'workshop\Content\DTMAPI\*.json') -Destination $destDtm -Force
    Write-Output "DEPLOYED  $dest"
    Write-Output '  enable it once in the title screen Mods page, then restart the game'
} catch {
    # Much the most likely cause: the game is open and has the DLL loaded, so it cannot be
    # replaced. Say so rather than making the reader guess from an access-denied message.
    Write-Output "DEPLOY FAILED: $($_.Exception.Message)"
    Write-Output '  close Doloc Town and run this again -- a loaded DLL cannot be overwritten'
    Write-Output "  or copy it by hand: $outDll  ->  $destDtm"
}
