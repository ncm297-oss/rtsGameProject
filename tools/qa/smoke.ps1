# QA smoke gate: build, import, headless boot, then fail on anything the exit code hides.
# Godot's --quit-after exits 0 even when a C# script fails to load, so this script also checks
# the log for the version banner and for ERROR / SCRIPT ERROR / Unhandled lines.
# Usage: powershell -File tools/qa/smoke.ps1 [-Frames 600] [-SkipBuild]
param(
    [int]$Frames = 600,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$godot = $env:GODOT
if (-not $godot) { $godot = [Environment]::GetEnvironmentVariable('GODOT', 'User') }
if (-not $godot -or -not (Test-Path $godot)) { Write-Host "FAIL: GODOT env var not set or path missing"; exit 2 }

$gamePath = Join-Path $root 'game'
$simCs = Get-Content (Join-Path $root 'sim\Rts.Sim\SimInfo.cs') -Raw
if ($simCs -notmatch 'Version\s*=\s*"([^"]+)"') { Write-Host "FAIL: cannot read SimInfo.Version"; exit 2 }
$banner = "Rts.Sim $($Matches[1])"

if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'RtsGame.sln') -nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: dotnet build exit $LASTEXITCODE"; exit 1 }
}

# cmd /c keeps PowerShell 5.1 from turning Godot's stderr into ErrorRecords.
$import = cmd /c "`"$godot`" --headless --path `"$gamePath`" --import 2>&1"
$importExit = $LASTEXITCODE
$smoke = cmd /c "`"$godot`" --headless --path `"$gamePath`" --quit-after $Frames 2>&1"
$smokeExit = $LASTEXITCODE
# Since M6-V1 a no-arg boot stops at the main menu, so a second pass starts the default match (--skirmish) to keep
# the match path (sim start, every view, the HUD) under the gate as it was before the shell existed.
$match = cmd /c "`"$godot`" --headless --path `"$gamePath`" --quit-after $Frames -- --skirmish 2>&1"
$matchExit = $LASTEXITCODE

$problems = @()
if ($importExit -ne 0) { $problems += "import exit $importExit" }
if ($smokeExit -ne 0) { $problems += "smoke exit $smokeExit" }
if ($matchExit -ne 0) { $problems += "match smoke exit $matchExit" }

# BUG-0955 (M6-VH4): matched as scene-loop.ps1 does: case-sensitive (Godot's own error lines are upper-case) and
# backtrace frames ("    [3] ...") skipped, since a warning's frames name functions like godot_variant_call_error.
function Test-Problem([string]$line) { $line -cmatch 'ERROR|Unhandled|Exception' -and $line -notmatch '^\s+\[\d+\]' }

# BUG-0955: a clone without the LFS objects (unsupported: CLAUDE.md puts every binary through LFS) holds pointer text
# files where the .wav clips should be. Godot's wav importer writes no .import result for them, so it fails them again on
# every --import: per clip "Not a WAV file ... found 'vers'", "Can't save empty resource to path
# 'res://.godot/imported/<clip>.wav-<md5>.sample'" and one unnamed "Condition "f.is_null()" is true". Only those import
# lines are skipped, only for clips that are pointers on disk, and the skip is printed; a normal clone has no pointers,
# so nothing changes there. Other pointers (the looktest art) fail the first run only, as before.
$pointerWavs = @(Get-ChildItem (Join-Path $gamePath 'assets') -Recurse -File -Filter '*.wav' -ErrorAction SilentlyContinue |
    Where-Object { $_.Length -lt 1024 -and (Get-Content $_.FullName -TotalCount 1) -like 'version https://git-lfs.github.com/spec/*' } |
    ForEach-Object { $_.Name })
$skipped = 0
$unsavedSamples = 0
$importBad = @()
foreach ($line in @($import)) {
    if (-not (Test-Problem $line)) { continue }
    if ($pointerWavs.Count -gt 0) {
        if ($line -cmatch "^ERROR: Not a WAV file\. File should start with 'RIFF', but found 'vers'") { $skipped++; continue }
        if ($line -cmatch "^ERROR: Can't save empty resource to path 'res://\.godot/imported/([^/']+\.wav)-[0-9a-f]+\.sample'" -and
            $pointerWavs -contains $Matches[1]) { $skipped++; $unsavedSamples++; continue }
    }
    $importBad += $line
}
# The unnamed md5 condition follows each unsaved sample (the import runs threaded, so it can't be paired by position).
$nullMd5 = @($importBad | Where-Object { $_ -cmatch '^ERROR: Condition "f\.is_null\(\)" is true\. Continuing\.' })
$keepNull = [Math]::Max(0, $nullMd5.Count - $unsavedSamples)
if ($unsavedSamples -gt 0) {
    $importBad = @($importBad | Where-Object { $_ -cnotmatch '^ERROR: Condition "f\.is_null\(\)" is true\. Continuing\.' }) + @($nullMd5 | Select-Object -First $keepNull)
    $skipped += $nullMd5.Count - $keepNull
}
if ($skipped -gt 0) { Write-Host "WARN: $($pointerWavs.Count) .wav clips are LFS pointers (no 'git lfs pull'): $skipped of their --import error lines skipped (BUG-0955)" }
$bad = @($importBad) + @(@($smoke) + @($match) | Where-Object { Test-Problem $_ })
foreach ($line in $bad) { $problems += "log: $line" }
if (-not ($smoke | Where-Object { $_ -match [regex]::Escape($banner) })) { $problems += "banner '$banner' missing from smoke log" }
if (-not ($match | Where-Object { $_ -match [regex]::Escape($banner) })) { $problems += "banner '$banner' missing from the match smoke log" }
if (-not ($match | Where-Object { $_ -match '^Match started:' })) { $problems += "'Match started:' missing from the --skirmish smoke log" }

$smoke | ForEach-Object { Write-Host "  | $_" }
Write-Host "  -- match pass (-- --skirmish) --"
$match | ForEach-Object { Write-Host "  | $_" }
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL: $_" }
    exit 1
}
Write-Host "PASS: import + smoke (menu and --skirmish match) exit 0, '$banner' printed, no ERROR lines"
exit 0
