# Rebuilds every unit model and the shared animation library in tools/asset-manifest.json from the raw packs
# (docs/04 "Import process"). Blender (background mode) does the model work (tools/art/build_unit.py); Godot then imports
# the results with the settings below. The outputs are committed, so a clone without Blender or asset-sources/ still
# builds and runs; this script only regenerates them.
#
# M6-V26: and every static building model in the manifest's "buildings" (tools/art/build_building.py), imported like a unit
# model (no animations, its atlas extracted beside it with the same texture settings).
#
# Usage: powershell -File tools/import-assets.ps1 [-SourceRoot <dir>] [-Blender <blender.exe>] [-Only <model or building id>|library] [-NoGodot]
#   Blender: -Blender, else $env:BLENDER (process, then user), else C:\Program Files\Blender Foundation\Blender 5.2\blender.exe.
#   Sources: -SourceRoot, else <repo>/asset-sources, else (in a git worktree) the main checkout's asset-sources.
#   Godot import: $env:GODOT (process, then user); without it the Blender outputs are written and the import is left to you.
# Godot import settings written here (each .import keeps its uid, so a re-run gives the same files):
#   a model .glb: no animations, no tangents (no normal maps), its texture extracted beside it; its clip map and (M6-V24)
#   atlas layout ride in it as glTF extras on the armature node, which Godot imports as metadata (M6-V21);
#   an extracted texture: VRAM compressed with mipmaps (docs/04 "Look test (M6-V10)"), alpha border fix off (M6-V24: the
#   atlas alpha is the team mask); a texture of an earlier build that the model no longer holds is deleted;
#   the library .glb: imported as an AnimationLibrary, every clip looping (root motion is off: in-place sources).
# Exit 0 on success; 1 when a build or import fails; 2 when Blender, the manifest or the sources are missing.
param(
    [string]$SourceRoot = '',
    [string]$Blender = '',
    [string]$Only = '',
    [switch]$NoGodot
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $PSScriptRoot 'asset-manifest.json'
$builder = Join-Path $PSScriptRoot 'art\build_unit.py'
$buildingBuilder = Join-Path $PSScriptRoot 'art\build_building.py'

if (-not $Blender) { $Blender = $env:BLENDER }
if (-not $Blender) { $Blender = [Environment]::GetEnvironmentVariable('BLENDER', 'User') }
if (-not $Blender) { $Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' }
if (-not (Test-Path -LiteralPath $Blender -PathType Leaf)) {
    Write-Host "FAIL: Blender not found at '$Blender' (install Blender 5.2, or set `$env:BLENDER / -Blender to blender.exe)"
    exit 2
}
if (-not (Test-Path -LiteralPath $manifestPath)) { Write-Host "FAIL: no manifest at $manifestPath"; exit 2 }
$m = Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json

if (-not $SourceRoot) {
    $SourceRoot = Join-Path $root $m.sourceRoot
    # asset-sources/ is gitignored, so a git worktree (the studio's) has none: use the main checkout's.
    if (-not (Test-Path -LiteralPath $SourceRoot)) {
        $common = (& git -C $root rev-parse --path-format=absolute --git-common-dir 2>$null)
        if ($common) {
            $alt = Join-Path (Split-Path $common -Parent) $m.sourceRoot
            if (Test-Path -LiteralPath $alt) { $SourceRoot = $alt }
        }
    }
}
if (-not (Test-Path -LiteralPath $SourceRoot)) { Write-Host "FAIL: sources not found at '$SourceRoot' (pass -SourceRoot)"; exit 2 }
Write-Host "blender: $Blender"
Write-Host "sources: $SourceRoot"

# Every pack's licence is read at import time: a pack whose licence file is gone or no longer says CC0 stops the run.
foreach ($rel in $m.licences) {
    $file = Join-Path $SourceRoot $rel
    if (-not (Test-Path -LiteralPath $file)) { Write-Host "FAIL: licence file missing: $rel"; exit 2 }
    $line = Get-Content -LiteralPath $file | Where-Object { $_ -match 'CC0' } | Select-Object -First 1
    if (-not $line) { Write-Host "FAIL: licence file no longer says CC0: $rel"; exit 1 }
    Write-Host ("licence: {0}: {1}" -f (Split-Path (Split-Path $rel -Parent) -Leaf), $line.Trim())
}

$work = Join-Path ([IO.Path]::GetTempPath()) "rts-import-assets-$PID"
New-Item -ItemType Directory -Force $work | Out-Null

# One Blender run; returns the parsed RESULT object, or stops the script on a FAIL line or a missing RESULT.
function Invoke-Build([string]$what, [string[]]$extra, [string]$script = $builder) {
    $log = Join-Path $work "$what.log"
    $argList = @('-b', '--factory-startup', '-P', "`"$script`"", '--', '--manifest', "`"$manifestPath`"",
        '--source-root', "`"$SourceRoot`"", '--repo', "`"$root`"", '--work', "`"$work`"") + $extra
    $p = Start-Process -FilePath $Blender -ArgumentList ($argList -join ' ') -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $lines = @(Get-Content -LiteralPath $log)
    $failLine = $lines | Where-Object { $_ -like 'FAIL *' } | Select-Object -First 1
    $result = $lines | Where-Object { $_ -like 'RESULT *' } | Select-Object -Last 1
    if ($failLine -or -not $result -or $p.ExitCode -ne 0) {
        if (-not $failLine) { $failLine = "Blender exited $($p.ExitCode) without a result (log: $log)" }
        Write-Host "FAIL: $what`: $($failLine -replace '^FAIL ', '')"
        exit 1
    }
    return ($result.Substring(7) | ConvertFrom-Json)
}

# Writes a minimal .import (importer, the uid Godot gave the file before, the given params); Godot fills in the rest.
function Set-Import([string]$file, [string]$importer, [string]$type, [string[]]$params) {
    $import = "$file.import"
    $uid = $null
    if (Test-Path -LiteralPath $import) {
        $hit = Select-String -LiteralPath $import -Pattern '^uid="([^"]+)"' | Select-Object -First 1
        if ($hit) { $uid = $hit.Matches[0].Groups[1].Value }
    }
    $text = "[remap]`n`nimporter=`"$importer`"`ntype=`"$type`"`n"
    if ($uid) { $text += "uid=`"$uid`"`n" }
    $text += "`n[params]`n`n" + ($params -join "`n") + "`n"
    [IO.File]::WriteAllText($import, $text)
    # Godot notices an edited .import by its mtime, in whole seconds: one rewritten in the second of the last import is
    # missed. Its .md5 gone, the next import redoes the file whatever the clock says.
    $imported = Join-Path $root 'game\.godot\imported'
    if (Test-Path -LiteralPath $imported) {
        Get-ChildItem -LiteralPath $imported -Filter "$(Split-Path $file -Leaf)-*.md5" | Remove-Item -Force
    }
}

# A built model's .glb import settings, and (M6-V24) the textures Godot extracted from an earlier build that the model no
# longer holds (the role textures the atlas replaced) removed with their .import and imported copy.
function Set-ModelImport($r) {
    Set-Import (Join-Path $root $r.destination) 'scene' 'PackedScene' @(
        'animation/import=false', 'meshes/ensure_tangents=false', 'gltf/embedded_image_handling=1')
    $dir = Split-Path (Join-Path $root $r.destination) -Parent
    $keep = @($r.textures.PSObject.Properties | ForEach-Object { "$($r.id)_$($_.Name).png" })
    foreach ($old in @(Get-ChildItem -LiteralPath $dir -Filter "$($r.id)_*.png" -File)) {
        if ($keep -contains $old.Name) { continue }
        Write-Host "    removing $($old.Name) (no longer in the model)"
        Remove-Item -LiteralPath $old.FullName, "$($old.FullName).import" -Force -ErrorAction SilentlyContinue
        $imported = Join-Path $root 'game\.godot\imported'
        if (Test-Path -LiteralPath $imported) { Get-ChildItem -LiteralPath $imported -Filter "$($old.Name)-*" | Remove-Item -Force }
    }
}

$results = @()
foreach ($model in $m.models) {
    if ($Only -and $Only -ne $model.id) { continue }
    Write-Host "building $($model.id) ..."
    $r = Invoke-Build $model.id @('--model', $model.id)
    $results += $r
    Set-ModelImport $r
}
# M6-V26: the static building models.
$buildingResults = @()
foreach ($b in @($m.buildings)) {
    if (-not $b -or ($Only -and $Only -ne $b.id)) { continue }
    Write-Host "building $($b.id) ..."
    $r = Invoke-Build $b.id @('--building', $b.id) $buildingBuilder
    $buildingResults += $r
    Set-ModelImport $r
}
$library = $null
if (-not $Only -or $Only -eq 'library') {
    Write-Host "building the animation library ..."
    $library = Invoke-Build 'library' @('--library')
    # Every clip loops: the builder names each glTF animation <name>_loop, which Godot's importer reads as the loop flag
    # and drops from the name (per-clip _subresources would write every default slice, a 400 KB .import).
    Set-Import (Join-Path $root $m.library.destination) 'animation_library' 'AnimationLibrary' @('animation/fps=30')
}

$godot = $env:GODOT
if (-not $godot) { $godot = [Environment]::GetEnvironmentVariable('GODOT', 'User') }
function Invoke-GodotImport {
    $log = Join-Path $work 'godot-import.log'
    cmd /c "`"$godot`" --headless --path `"$(Join-Path $root 'game')`" --import > `"$log`" 2>&1"
    $bad = @(Get-Content -LiteralPath $log | Where-Object { $_ -cmatch 'ERROR' -and $_ -notmatch '^\s+\[\d+\]' })
    if ($bad.Count -gt 0) { $bad | Select-Object -First 10 | ForEach-Object { Write-Host "    | $_" }; Write-Host "FAIL: Godot import"; exit 1 }
}
if ($NoGodot -or -not $godot -or -not (Test-Path -LiteralPath $godot)) {
    Write-Host "note: Godot import skipped; run: & `$env:GODOT --headless --path game --import, then this script again"
} else {
    Write-Host "importing in Godot ..."
    # M7-V5: Godot's glTF import overwrites a texture it extracted only when the .png.import names the md5 it wrote
    # (generator_parameters); Set-Import below writes the file without it, so a texture changed in the manifest (a tint)
    # never reached the game. Each rebuilt model's texture imports get a placeholder md5: the import below extracts every
    # texture again (the same bytes when nothing changed) and keeps each uid; Set-Import then writes them as before.
    foreach ($r in @($results) + @($buildingResults)) {
        $dir = Split-Path (Join-Path $root $r.destination) -Parent
        foreach ($imp in Get-ChildItem -LiteralPath $dir -Filter "$($r.id)_*.png.import") {
            $text = [IO.File]::ReadAllText($imp.FullName)
            if ($text -notmatch 'generator_parameters=') {
                $text = $text -replace '(?m)^(uid="[^"]*")$', "`$1`ngenerator_parameters={`"md5`": `"stale`"}"
                [IO.File]::WriteAllText($imp.FullName, $text)
            }
        }
    }
    Invoke-GodotImport
    # The textures Godot extracted from each model: VRAM compressed with mipmaps, then imported again. M6-V24: the atlas
    # alpha is the team mask, so the importer must leave the colours under it alone (fix_alpha_border off; no texel is
    # transparent anyway).
    foreach ($r in @($results) + @($buildingResults)) {
        $dir = Split-Path (Join-Path $root $r.destination) -Parent
        foreach ($png in Get-ChildItem -LiteralPath $dir -Filter '*.png') {
            Set-Import $png.FullName 'texture' 'CompressedTexture2D' @('compress/mode=2', 'mipmaps/generate=true', 'detect_3d/compress_to=0', 'process/fix_alpha_border=false')
        }
    }
    Invoke-GodotImport
}

# Summary: every generated file with its size, the triangles and textures of each model, the library's clips.
$total = 0
foreach ($r in @($results) + @($buildingResults)) {
    if ($r.sourceTriangles) {
        # M6-V26: a building: its triangles from the source's through the hidden faces and the decimation, its size as
        # built (width x depth x height in the pack's units; the view fits it to the footprint).
        Write-Host ("{0}: {1} triangles (source {2}, {3} after the hidden faces), {4} x {5} x {6} as built, materials [{7}]" -f $r.id, $r.triangles, $r.sourceTriangles, $r.afterHiddenFaces, $r.size[0], $r.size[1], $r.size[2], ($r.materials -join ' '))
        foreach ($d in $r.decimated.PSObject.Properties) { Write-Host ("    decimated {0}: {1} -> {2} triangles" -f $d.Name, $d.Value[0], $d.Value[1]) }
    } else {
        Write-Host ("{0}: {1} triangles, {2:0.00} m tall, feet at {3} m, materials [{4}]" -f $r.id, $r.triangles, $r.height, $r.feet, ($r.materials -join ' '))
    }
    foreach ($t in $r.textures.PSObject.Properties) { Write-Host ("    texture {0} {1} x {2}" -f $t.Name, $t.Value[0], $t.Value[1]) }
    # M6-V24: the atlas's tiles (column, row from the top-left).
    if ($r.atlas) { Write-Host ("    atlas {0} x {1} tiles of {2}: {3}" -f $r.atlas.grid[0], $r.atlas.grid[1], $r.atlas.tile, (($r.atlas.tiles.PSObject.Properties | ForEach-Object { "$($_.Name) ($($_.Value -join ', '))" }) -join ' ')) }
    # M6-V21: the clip map written into the model (state name -> library clip).
    if ($r.clips) { Write-Host ("    clips " + (($r.clips.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' ')) }
    $dir = Split-Path (Join-Path $root $r.destination) -Parent
    foreach ($f in Get-ChildItem -LiteralPath $dir -File | Sort-Object Name) {
        if ($f.Extension -ne '.import') { $total += $f.Length }
        # M6-V24: each binary's SHA-256 (the first 16 hex digits), so two runs can be compared byte for byte.
        $hash = if ($f.Extension -ne '.import') { '  ' + (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.Substring(0, 16) } else { '' }
        Write-Host ("    {0,10:N0}  {1}{2}" -f $f.Length, $f.FullName.Substring($root.Length + 1).Replace('\', '/'), $hash)
    }
}
if ($library) {
    Write-Host "library: $($library.destination) ($($library.bones) bones animated, $($library.dropped) finger bones dropped)"
    foreach ($c in $library.clips.PSObject.Properties) { Write-Host ("    {0,-12} <- {1} ({2} s)" -f $c.Name, $c.Value, $library.seconds.($c.Name)) }
    $lib = Get-Item (Join-Path $root $library.destination)
    $total += $lib.Length
    Write-Host ("    {0,10:N0}  {1}  {2}" -f $lib.Length, $library.destination, (Get-FileHash -LiteralPath $lib.FullName -Algorithm SHA256).Hash.Substring(0, 16))
}
Write-Host ("binaries written: {0:N0} bytes ({1:0.00} MB)" -f $total, ($total / 1MB))
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
Write-Host "IMPORT ASSETS PASS"
exit 0
