# M6-V31: proves that tools/art/build_building.py refuses a bad building kit with a clear FAIL line (and writes no model),
# and builds a good one. Each case is one manifest entry in a scratch manifest, built by Blender into a scratch repo root
# that holds a copy of the Whirlwind's buildings.json (a kit with no source is checked against the data's footprint), so
# nothing under the real repo is written. Cases:
#   ok          a two-part tent                                    -> RESULT, a .glb written
#   outside     a part 0.1 m past the 2 x 2 footprint less 0.25 m  -> FAIL ... outside its ... footprint
#   zero        a part with a size of 0                            -> FAIL ... is not three sizes over 0
#   nocolour    a part whose role has no colour and is not team    -> FAIL ... role has no colour
#   neither     no source and no kit                               -> FAIL ... neither a source nor a kit
#   shape       an unknown shape                                   -> FAIL ... shape not one of
#   thick       an awning 0.5 m thick                              -> FAIL ... an awning
#   under       a part reaching under the ground                   -> FAIL ... under the ground
#   empty       a kit with no parts                                -> FAIL ... kit has no parts
#   keepnone    a pack source cropped by a box that holds no face  -> FAIL ... holds no face
#   keepalone   a keep box and a kit but no source                 -> FAIL ... crops a source
# Needs Blender 5.2 (as tools/import-assets.ps1 finds it) and, for keepnone, the building pack under asset-sources/.
# Usage: powershell -File tools/art/test-kit-errors.ps1   prints KIT ERRORS TEST PASS and exits 0, else FAIL lines, exit 1.
param([string]$Blender = '', [string]$SourceRoot = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$builder = Join-Path $PSScriptRoot 'build_building.py'
if (-not $Blender) { $Blender = $env:BLENDER }
if (-not $Blender) { $Blender = [Environment]::GetEnvironmentVariable('BLENDER', 'User') }
if (-not $Blender) { $Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' }
if (-not (Test-Path -LiteralPath $Blender -PathType Leaf)) { Write-Host "FAIL: Blender not found at '$Blender'"; exit 2 }
if (-not $SourceRoot) {
    $SourceRoot = Join-Path $repo 'asset-sources'
    if (-not (Test-Path -LiteralPath $SourceRoot)) {
        $common = (& git -C $repo rev-parse --path-format=absolute --git-common-dir 2>$null)
        if ($common) { $SourceRoot = Join-Path (Split-Path $common -Parent) 'asset-sources' }
    }
}

$work = Join-Path ([IO.Path]::GetTempPath()) "rts-kit-errors-test-$PID"
if (Test-Path -LiteralPath $work) { Remove-Item -Recurse -Force -LiteralPath $work }
$root = Join-Path $work 'root'
$data = Join-Path $root 'game\data\factions\whirlwind'
New-Item -ItemType Directory -Force $data | Out-Null
Copy-Item (Join-Path $repo 'game\data\factions\whirlwind\buildings.json') $data

$pole = @{ shape = 'pole'; size = @(0.1, 1.0, 0.1); at = @(0, 0, 0); role = 'pole' }
$cloth = @{ shape = 'ridge'; size = @(2.5, 2.0, 2.5); at = @(0, 0, 0); role = 'cloth' }
$cases = [ordered]@{
    ok       = @{ want = 'RESULT'; kit = @($cloth, $pole) }
    outside  = @{ want = 'outside its'; kit = @($cloth, @{ shape = 'box'; size = @(0.2, 0.5, 0.2); at = @(1.75, 0, 0); role = 'pole' }) }
    zero     = @{ want = 'is not three sizes over 0'; kit = @($cloth, @{ shape = 'box'; size = @(0.2, 0, 0.2); at = @(0, 0, 0); role = 'pole' }) }
    nocolour = @{ want = 'role has no colour'; kit = @($cloth, @{ shape = 'box'; size = @(0.2, 0.5, 0.2); at = @(0, 0, 0); role = 'brass' }) }
    neither  = @{ want = 'neither a source nor a kit'; kit = $null }
    shape    = @{ want = 'shape not one of'; kit = @($cloth, @{ shape = 'sphere'; size = @(0.2, 0.5, 0.2); at = @(0, 0, 0); role = 'pole' }) }
    thick    = @{ want = 'an awning'; kit = @($cloth, @{ shape = 'awning'; size = @(1.0, 0.5, 1.0); at = @(0, 0, 0); role = 'pole' }) }
    under    = @{ want = 'under the ground'; kit = @($cloth, @{ shape = 'box'; size = @(0.2, 0.5, 0.2); at = @(0, -0.1, 0); role = 'pole' }) }
    empty    = @{ want = 'kit has no parts'; kit = @() }
    keepnone = @{ want = 'holds no face'; kit = $null; source = 'quaternius-ultimate-fantasy-rts-2022-08/Ultimate Fantasy RTS - Aug 2022/glTF/Market_FirstAge_Level1.gltf'; keep = @{ min = @(5, 5, 5); max = @(6, 6, 6) } }
    keepalone = @{ want = 'crops a source'; kit = @($cloth, $pole); keepOnly = @{ min = @(-1, 0, -1); max = @(1, 2, 1) } }
}
$failures = New-Object System.Collections.Generic.List[string]
foreach ($name in $cases.Keys) {
    $c = $cases[$name]
    $entry = [ordered]@{
        id = 'whirlwind_tent'; destination = 'game/assets/models/whirlwind/whirlwind_tent/whirlwind_tent.glb'
        maxTriangles = 5000; tileSize = 32; viewElevationDegrees = 15; team = @('cloth'); teamShade = 0.8
        colours = [ordered]@{ pole = '#5a4330' }
    }
    if ($null -ne $c.kit) { $entry.kit = @($c.kit) }
    if ($c.keepOnly) { $entry.keep = $c.keepOnly }
    if ($c.source) {
        $entry.source = $c.source; $entry.keep = $c.keep; $entry.team = @('Green', 'Red')
        $entry.colours = [ordered]@{ Wood = '#5b4632'; Wood_Light = '#6e3f2c'; Yellow = '#b59a62' }
    }
    $manifest = Join-Path $work "$name.json"
    [IO.File]::WriteAllText($manifest, (@{ buildings = @($entry) } | ConvertTo-Json -Depth 8))
    $glb = Join-Path $root 'game\assets\models\whirlwind\whirlwind_tent\whirlwind_tent.glb'
    Remove-Item -LiteralPath $glb -ErrorAction SilentlyContinue
    $log = Join-Path $work "$name.log"
    $argList = @('-b', '--factory-startup', '-P', "`"$builder`"", '--', '--manifest', "`"$manifest`"", '--source-root', "`"$SourceRoot`"",
        '--repo', "`"$root`"", '--work', "`"$(Join-Path $work "w-$name")`"", '--building', 'whirlwind_tent')
    $p = Start-Process -FilePath $Blender -ArgumentList ($argList -join ' ') -NoNewWindow -Wait -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $lines = @(Get-Content -LiteralPath $log)
    $fail = $lines | Where-Object { $_ -like 'FAIL *' } | Select-Object -First 1
    $result = $lines | Where-Object { $_ -like 'RESULT *' } | Select-Object -First 1
    if ($c.want -eq 'RESULT') {
        $ok = $result -and -not $fail -and $p.ExitCode -eq 0 -and (Test-Path -LiteralPath $glb)
        $said = if ($fail) { $fail } else { "exit $($p.ExitCode), model written: $(Test-Path -LiteralPath $glb)" }
    } else {
        $ok = $fail -and $fail -like "*$($c.want)*" -and -not $result -and $p.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $glb)
        $said = if ($fail) { $fail } else { "no FAIL line (exit $($p.ExitCode))" }
    }
    Write-Host ("{0,-10} {1}  {2}" -f $name, $(if ($ok) { 'ok  ' } else { 'FAIL' }), $said)
    if (-not $ok) { $failures.Add("${name}: want '$($c.want)', got: $said") }
}
Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
foreach ($f in $failures) { Write-Host "KIT ERRORS TEST FAIL: $f" }
if ($failures.Count -eq 0) { Write-Host 'KIT ERRORS TEST PASS'; exit 0 }
exit 1
