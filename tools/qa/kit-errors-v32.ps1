# QA (M6-V32): bad building kits the developer did not list for BUG-1911, built by tools/art/build_building.py into a scratch
# repo root (same harness as tools/qa/kit-errors-extra.ps1; nothing under the real repo is written). Each case wants a FAIL
# line holding its text (and, where it says so, naming the entry and the part), a non-zero exit and no model. Cases:
#   negsize     a size [-0.2, 1, 0.2]                               -> FAIL ... kit part 1 ... not three finite sizes over 0
#   strsize     a size ["0.2", 1, 0.2]                              -> FAIL ... kit part 1 ... not three finite sizes over 0
#   strat       an at [0, "0", 0]                                   -> FAIL ... kit part 1 ... not three finite numbers
#   rotnan      a rotate [0, NaN, 0]                                -> FAIL ... kit part 1 ... rotate ... not three finite numbers
#   rotstr      a rotate "90"                                       -> FAIL ... kit part 1 ... rotate
#   twobad0     part 0 bad (NaN size), part 1 good                  -> FAIL ... kit part 0
#   twobad2     parts 0 and 1 good, part 2 bad (Infinity at)        -> FAIL ... kit part 2
#   size4       a size of four numbers                              -> FAIL ... kit part 1 ... not three finite sizes
#   topneg      a cone with top -1                                  -> FAIL ... kit part 1 ... top
#   topnan      a cone with top NaN                                 -> FAIL ... kit part 1 ... top
#   sidesfloat  a cylinder with sides 4.5                           -> FAIL ... kit part 1 ... sides
#   sidesbool   a cylinder with sides true                          -> FAIL ... kit part 1 ... sides
#   axisbox     a box with "axis": "x"                              -> FAIL ... kit part 1 ... axis
#   rolelist    a part whose role is a list                         -> FAIL ... whirlwind_tent (a FAIL line, not only a traceback)
#   partstr     a part that is a string, not an object              -> FAIL ... whirlwind_tent
#   colourslist "colours" is a list, not a map                      -> FAIL ... whirlwind_tent
#   huge        a size of 1e308 (finite) on a source-less kit       -> FAIL ... outside its
# Needs Blender 5.2 (as tools/import-assets.ps1 finds it).
# Usage: powershell -File tools/qa/kit-errors-v32.ps1 [-Work <dir>]   prints KIT ERRORS V32 PASS / FAIL lines; exit 0 / 1.
param([string]$Blender = '', [string]$Work = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$builder = Join-Path $repo 'tools\art\build_building.py'
if (-not $Blender) { $Blender = $env:BLENDER }
if (-not $Blender) { $Blender = [Environment]::GetEnvironmentVariable('BLENDER', 'User') }
if (-not $Blender) { $Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' }
if (-not (Test-Path -LiteralPath $Blender -PathType Leaf)) { Write-Host "FAIL: Blender not found at '$Blender'"; exit 2 }

if (-not $Work) { $Work = Join-Path ([IO.Path]::GetTempPath()) "rts-kit-errors-v32-$PID" }
if (Test-Path -LiteralPath $Work) { Remove-Item -Recurse -Force -LiteralPath $Work }
$root = Join-Path $Work 'root'
$data = Join-Path $root 'game\data\factions\whirlwind'
New-Item -ItemType Directory -Force $data | Out-Null
Copy-Item (Join-Path $repo 'game\data\factions\whirlwind\buildings.json') $data

$head = '"maxTriangles": 5000, "tileSize": 32, "viewElevationDegrees": 15, "team": ["cloth"], "teamShade": 0.8'
$cloth = '{ "shape": "ridge", "size": [2.5, 2.0, 2.5], "at": [0, 0, 0], "role": "cloth" }'
$dest = '"destination": "game/assets/models/whirlwind/whirlwind_tent/whirlwind_tent.glb"'
$pc = '{ "pole": "#5a4330" }'
function Entry([string]$parts, [string]$colours = $pc) {
    "{ `"buildings`": [ { `"id`": `"whirlwind_tent`", $dest, $head, `"colours`": $colours, `"kit`": [ $parts ] } ] }"
}
function Part([string]$extra) { "{ `"shape`": `"box`", `"role`": `"pole`", $extra }" }
$goodPole = '{ "shape": "pole", "size": [0.1, 1, 0.1], "at": [0, 0, 0], "role": "pole" }'
$p1 = 'whirlwind_tent: kit part 1'
$cases = [ordered]@{
    negsize     = @{ text = "$p1 (box, role pole): size"; also = 'not three finite sizes over 0'; json = (Entry "$cloth, $(Part '"size": [-0.2, 1, 0.2], "at": [0, 0, 0]')") }
    strsize     = @{ text = "$p1 (box, role pole): size"; also = 'not three finite sizes over 0'; json = (Entry "$cloth, $(Part '"size": ["0.2", 1, 0.2], "at": [0, 0, 0]')") }
    strat       = @{ text = "$p1 (box, role pole): at"; also = 'not three finite numbers'; json = (Entry "$cloth, $(Part '"size": [0.2, 1, 0.2], "at": [0, "0", 0]')") }
    rotnan      = @{ text = "$p1 (box, role pole): rotate"; also = 'not three finite numbers'; json = (Entry "$cloth, $(Part '"size": [0.2, 1, 0.2], "at": [0, 0, 0], "rotate": [0, NaN, 0]')") }
    rotstr      = @{ text = "$p1 (box, role pole): rotate"; also = ''; json = (Entry "$cloth, $(Part '"size": [0.2, 1, 0.2], "at": [0, 0, 0], "rotate": "90"')") }
    twobad0     = @{ text = 'whirlwind_tent: kit part 0 (box, role pole): size'; also = ''; json = (Entry "$(Part '"size": [NaN, 1, 0.2], "at": [0, 0, 0]'), $cloth") }
    twobad2     = @{ text = 'whirlwind_tent: kit part 2 (box, role pole): at'; also = ''; json = (Entry "$cloth, $goodPole, $(Part '"size": [0.2, 1, 0.2], "at": [0, Infinity, 0]')") }
    size4       = @{ text = "$p1 (box, role pole): size"; also = ''; json = (Entry "$cloth, $(Part '"size": [0.2, 1, 0.2, 1], "at": [0, 0, 0]')") }
    topneg      = @{ text = "$p1 (cone, role pole): top"; also = ''; json = (Entry "$cloth, { `"shape`": `"cone`", `"role`": `"pole`", `"size`": [0.4, 1, 0.4], `"at`": [0, 0, 0], `"top`": -1 }") }
    topnan      = @{ text = "$p1 (cone, role pole): top"; also = ''; json = (Entry "$cloth, { `"shape`": `"cone`", `"role`": `"pole`", `"size`": [0.4, 1, 0.4], `"at`": [0, 0, 0], `"top`": NaN }") }
    sidesfloat  = @{ text = "$p1 (cylinder, role pole): sides"; also = ''; json = (Entry "$cloth, { `"shape`": `"cylinder`", `"role`": `"pole`", `"size`": [0.4, 1, 0.4], `"at`": [0, 0, 0], `"sides`": 4.5 }") }
    sidesbool   = @{ text = "$p1 (cylinder, role pole): sides"; also = ''; json = (Entry "$cloth, { `"shape`": `"cylinder`", `"role`": `"pole`", `"size`": [0.4, 1, 0.4], `"at`": [0, 0, 0], `"sides`": true }") }
    axisbox     = @{ text = "$p1 (box, role pole): axis"; also = ''; json = (Entry "$cloth, $(Part '"size": [0.2, 1, 0.2], "at": [0, 0, 0], "axis": "x"')") }
    rolelist    = @{ text = 'whirlwind_tent'; also = ''; json = (Entry "$cloth, { `"shape`": `"box`", `"role`": [`"pole`"], `"size`": [0.2, 1, 0.2], `"at`": [0, 0, 0] }") }
    partstr     = @{ text = 'whirlwind_tent'; also = ''; json = (Entry "$cloth, `"pole`"") }
    colourslist = @{ text = 'whirlwind_tent'; also = ''; json = (Entry "$cloth, $goodPole" '["#5a4330"]') }
    huge        = @{ text = "$p1 (box, role pole)"; also = 'outside its'; json = (Entry "$cloth, $(Part '"size": [1e308, 1, 0.2], "at": [0, 0, 0]')") }
}
$glb = Join-Path $root 'game\assets\models\whirlwind\whirlwind_tent\whirlwind_tent.glb'
$failures = New-Object System.Collections.Generic.List[string]
foreach ($name in $cases.Keys) {
    $c = $cases[$name]
    $manifest = Join-Path $Work "$name.json"
    [IO.File]::WriteAllText($manifest, $c.json)
    Remove-Item -LiteralPath $glb -ErrorAction SilentlyContinue
    $log = Join-Path $Work "$name.log"
    $argList = @('-b', '--factory-startup', '-P', "`"$builder`"", '--', '--manifest', "`"$manifest`"", '--source-root', "`"$Work`"",
        '--repo', "`"$root`"", '--work', "`"$(Join-Path $Work "w-$name")`"", '--building', 'whirlwind_tent')
    $p = Start-Process -FilePath $Blender -ArgumentList ($argList -join ' ') -NoNewWindow -Wait -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $lines = @(Get-Content -LiteralPath $log)
    $fail = $lines | Where-Object { $_ -like 'FAIL *' } | Select-Object -First 1
    $result = $lines | Where-Object { $_ -like 'RESULT *' } | Select-Object -First 1
    $written = Test-Path -LiteralPath $glb
    $said = if ($fail) { $fail } elseif ($result) { "RESULT (exit $($p.ExitCode), model written: $written)" } else { "no FAIL or RESULT line (exit $($p.ExitCode))" }
    $ok = $fail -and $fail.Contains($c.text) -and ($c.also -eq '' -or $fail.Contains($c.also)) -and -not $result -and $p.ExitCode -ne 0 -and -not $written
    Write-Host ("{0,-11} {1}  {2}" -f $name, $(if ($ok) { 'ok  ' } else { 'FAIL' }), $said)
    if (-not $ok) { $failures.Add("${name}: want a FAIL line holding '$($c.text)' $($c.also) and no model, got: $said") }
}
foreach ($f in $failures) { Write-Host "KIT ERRORS V32 FAIL: $f" }
if ($failures.Count -eq 0) { Write-Host 'KIT ERRORS V32 PASS'; exit 0 }
exit 1
