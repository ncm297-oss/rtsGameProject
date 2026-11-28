# QA (M6-V31): more bad building kits for tools/art/build_building.py than tools/art/test-kit-errors.ps1 tries. Each case is
# one manifest entry in a scratch manifest (written as raw JSON text, so a case can hold what ConvertTo-Json would not, such
# as NaN), built by Blender into a scratch repo root holding a copy of the Whirlwind's buildings.json; nothing under the
# real repo is written. A "FAIL" case wants a FAIL line holding its text, a non-zero exit and no model written; a "RESULT"
# case wants a model. Cases:
#   nocolours   a kit of team parts only and no "colours" key at all          -> FAIL ... has no "colours" map (BUG-1911), no model
#   badhex      a colour "#zz0000"                                             -> FAIL, no model
#   nan         a part whose size holds NaN (json.load reads it)               -> FAIL ... finite (BUG-1911), no model
#   inf         a part whose position holds Infinity                           -> FAIL ... finite (BUG-1911), no model
#   noat        a part with no "at"                                            -> FAIL ... at
#   sides2      a cylinder with 2 sides                                        -> FAIL ... at least 3 sides
#   turned      a part inside the footprint unturned, outside when turned 45   -> FAIL ... outside its
#   notbuilding an id the data does not hold                                    -> FAIL ... not a building of
#   unusedcol   a colour no part uses (a kit with no source)                   -> FAIL ... no kit part has role brass (BUG-1911)
#   typo        a part with "turn" (not "rotate") and "role" right             -> FAIL ... unknown key turn (BUG-1911; was ignored)
#   boolsize    a size [true, 1, 1]                                            -> FAIL ... finite sizes (BUG-1911; was read as 1)
# Needs Blender 5.2 (as tools/import-assets.ps1 finds it).
# Usage: powershell -File tools/qa/kit-errors-extra.ps1 [-Work <dir>]   prints KIT ERRORS EXTRA PASS / FAIL lines; exit 0 / 1.
param([string]$Blender = '', [string]$Work = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$builder = Join-Path $repo 'tools\art\build_building.py'
if (-not $Blender) { $Blender = $env:BLENDER }
if (-not $Blender) { $Blender = [Environment]::GetEnvironmentVariable('BLENDER', 'User') }
if (-not $Blender) { $Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' }
if (-not (Test-Path -LiteralPath $Blender -PathType Leaf)) { Write-Host "FAIL: Blender not found at '$Blender'"; exit 2 }

if (-not $Work) { $Work = Join-Path ([IO.Path]::GetTempPath()) "rts-kit-errors-extra-$PID" }
if (Test-Path -LiteralPath $Work) { Remove-Item -Recurse -Force -LiteralPath $Work }
$root = Join-Path $Work 'root'
$data = Join-Path $root 'game\data\factions\whirlwind'
New-Item -ItemType Directory -Force $data | Out-Null
Copy-Item (Join-Path $repo 'game\data\factions\whirlwind\buildings.json') $data

$head = '"maxTriangles": 5000, "tileSize": 32, "viewElevationDegrees": 15, "team": ["cloth"], "teamShade": 0.8'
$cloth = '{ "shape": "ridge", "size": [2.5, 2.0, 2.5], "at": [0, 0, 0], "role": "cloth" }'
$dest = '"destination": "game/assets/models/whirlwind/whirlwind_tent/whirlwind_tent.glb"'
function Entry([string]$id, [string]$colours, [string]$parts, [string]$dst = $dest) {
    $c = if ($colours) { ", `"colours`": $colours" } else { '' }
    "{ `"buildings`": [ { `"id`": `"$id`", $dst, $head$c, `"kit`": [ $parts ] } ] }"
}
$pole = '"#5a4330"'
$cases = [ordered]@{
    nocolours   = @{ want = 'FAIL'; text = 'whirlwind_tent: the entry has no "colours" map'; json = (Entry 'whirlwind_tent' '' $cloth) }
    badhex      = @{ want = 'FAIL'; text = ''; json = (Entry 'whirlwind_tent' '{ "pole": "#zz0000" }' "$cloth, { `"shape`": `"pole`", `"size`": [0.1, 1, 0.1], `"at`": [0, 0, 0], `"role`": `"pole`" }") }
    nan         = @{ want = 'FAIL'; text = 'whirlwind_tent: kit part 1 (box, role pole): size * is not three finite sizes over 0'; json = (Entry 'whirlwind_tent' "{ `"pole`": $pole }" "$cloth, { `"shape`": `"box`", `"size`": [NaN, 1, 0.2], `"at`": [0, 0, 0], `"role`": `"pole`" }") }
    inf         = @{ want = 'FAIL'; text = 'whirlwind_tent: kit part 1 (box, role pole): at * is not three finite numbers'; json = (Entry 'whirlwind_tent' "{ `"pole`": $pole }" "$cloth, { `"shape`": `"box`", `"size`": [0.2, 1, 0.2], `"at`": [Infinity, 0, 0], `"role`": `"pole`" }") }
    noat        = @{ want = 'FAIL'; text = 'at'; json = (Entry 'whirlwind_tent' "{ `"pole`": $pole }" "$cloth, { `"shape`": `"box`", `"size`": [0.2, 1, 0.2], `"role`": `"pole`" }") }
    sides2      = @{ want = 'FAIL'; text = 'at least 3 sides'; json = (Entry 'whirlwind_tent' "{ `"pole`": $pole }" "$cloth, { `"shape`": `"cylinder`", `"sides`": 2, `"size`": [0.2, 1, 0.2], `"at`": [0, 0, 0], `"role`": `"pole`" }") }
    turned      = @{ want = 'FAIL'; text = 'outside its'; json = (Entry 'whirlwind_tent' "{ `"pole`": $pole }" "{ `"shape`": `"box`", `"size`": [3.4, 1, 3.4], `"at`": [0, 0, 0], `"rotate`": [0, 45, 0], `"role`": `"cloth`" }, { `"shape`": `"pole`", `"size`": [0.1, 1, 0.1], `"at`": [0, 0, 0], `"role`": `"pole`" }") }
    notbuilding = @{ want = 'FAIL'; text = 'not a building of'; json = (Entry 'whirlwind_bazaar' "{ `"pole`": $pole }" "$cloth, { `"shape`": `"pole`", `"size`": [0.1, 1, 0.1], `"at`": [0, 0, 0], `"role`": `"pole`" }" '"destination": "game/assets/models/whirlwind/whirlwind_bazaar/whirlwind_bazaar.glb"') }
    unusedcol   = @{ want = 'FAIL'; text = 'whirlwind_tent: colour brass: no kit part has role brass'; json = (Entry 'whirlwind_tent' "{ `"pole`": $pole, `"brass`": `"#b08d57`" }" "$cloth, { `"shape`": `"pole`", `"size`": [0.1, 1, 0.1], `"at`": [0, 0, 0], `"role`": `"pole`" }") }
    typo        = @{ want = 'FAIL'; text = 'whirlwind_tent: kit part 1 (box, role pole): unknown key turn'; json = (Entry 'whirlwind_tent' "{ `"pole`": $pole }" "$cloth, { `"shape`": `"box`", `"size`": [0.2, 1, 0.6], `"at`": [0, 0, 0], `"turn`": [0, 90, 0], `"role`": `"pole`" }") }
    boolsize    = @{ want = 'FAIL'; text = 'whirlwind_tent: kit part 1 (box, role pole): size * is not three finite sizes over 0'; json = (Entry 'whirlwind_tent' "{ `"pole`": $pole }" "$cloth, { `"shape`": `"box`", `"size`": [true, 1, 0.2], `"at`": [0, 0, 0], `"role`": `"pole`" }") }
}
$failures = New-Object System.Collections.Generic.List[string]
foreach ($name in $cases.Keys) {
    $c = $cases[$name]
    $manifest = Join-Path $Work "$name.json"
    [IO.File]::WriteAllText($manifest, $c.json)
    $id = if ($name -eq 'notbuilding') { 'whirlwind_bazaar' } else { 'whirlwind_tent' }
    $glb = Join-Path $root "game\assets\models\whirlwind\$id\$id.glb"
    Remove-Item -LiteralPath $glb -ErrorAction SilentlyContinue
    $log = Join-Path $Work "$name.log"
    $argList = @('-b', '--factory-startup', '-P', "`"$builder`"", '--', '--manifest', "`"$manifest`"", '--source-root', "`"$Work`"",
        '--repo', "`"$root`"", '--work', "`"$(Join-Path $Work "w-$name")`"", '--building', $id)
    $p = Start-Process -FilePath $Blender -ArgumentList ($argList -join ' ') -NoNewWindow -Wait -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $lines = @(Get-Content -LiteralPath $log)
    $fail = $lines | Where-Object { $_ -like 'FAIL *' } | Select-Object -First 1
    $result = $lines | Where-Object { $_ -like 'RESULT *' } | Select-Object -First 1
    $written = Test-Path -LiteralPath $glb
    $said = if ($fail) { $fail } elseif ($result) { "RESULT (exit $($p.ExitCode), model written: $written) size " + (($result.Substring(7) | ConvertFrom-Json).size -join ' x ') } else { "no FAIL or RESULT line (exit $($p.ExitCode))" }
    if ($c.want -eq 'MEASURE') { Write-Host ("{0,-11} info  {1}" -f $name, $said); continue }
    $ok = $fail -and ($c.text -eq '' -or $fail -like "*$($c.text)*") -and -not $result -and $p.ExitCode -ne 0 -and -not $written
    Write-Host ("{0,-11} {1}  {2}" -f $name, $(if ($ok) { 'ok  ' } else { 'FAIL' }), $said)
    if (-not $ok) { $failures.Add("${name}: want a FAIL line$(if ($c.text) { " holding '$($c.text)'" }) and no model, got: $said") }
}
foreach ($f in $failures) { Write-Host "KIT ERRORS EXTRA FAIL: $f" }
if ($failures.Count -eq 0) { Write-Host 'KIT ERRORS EXTRA PASS'; exit 0 }
exit 1
