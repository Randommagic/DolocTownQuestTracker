# DolocQuestTracker build + deploy
#
# This machine has no .NET SDK (dotnet runtimes only, no `dotnet build`) and no Roslyn,
# so we drive the in-box .NET Framework compiler directly. That compiler is C# 5 only --
# no ?., no $"", no expression-bodied members. The source is kept C# 5 clean so it also
# compiles unchanged on any modern toolchain if an SDK is installed later.
#
#   .\build.ps1              build + deploy to the game's BepInEx\plugins
#   .\build.ps1 -NoDeploy    build only

param(
    [string] $GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Doloc Town",
    [switch] $NoDeploy
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$modName = 'DolocQuestTracker'

$managed = Join-Path $GameDir 'DolocTown_Data\Managed'
$bepCore = Join-Path $GameDir 'BepInEx\core'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

foreach ($p in @($managed, $bepCore, $csc)) {
    if (-not (Test-Path $p)) { throw "not found: $p" }
}

# UnityEngine.dll is the legacy monolithic facade. BepInEx.dll references it (not the
# split *Module assemblies), so it has to be on the reference list or BaseUnityPlugin
# fails to resolve MonoBehaviour.
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
foreach ($n in @('BepInEx', '0Harmony')) {
    $path = Join-Path $bepCore ($n + '.dll')
    if (-not (Test-Path $path)) { throw "reference missing: $path" }
    $refs += "/r:$path"
}

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
& $csc /nologo /target:library /nostdlib+ /noconfig /optimize+ /debug- /warn:4 `
    "/out:$outDll" $refs $sources
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }

Write-Output ''
Write-Output "BUILD OK  $((Get-Item $outDll).Length) bytes"

if ($NoDeploy) { return }

$dest = Join-Path $GameDir ("BepInEx\plugins\" + $modName)
try {
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item $outDll -Destination $dest -Force
    Write-Output "DEPLOYED  $dest"
} catch {
    Write-Output "DEPLOY FAILED (권한?): $($_.Exception.Message)"
    Write-Output "  수동 복사: $outDll  ->  $dest"
}
