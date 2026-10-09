# QA scene loop: runs every headless game/tests/*Test.tscn (M3PlayableTest last, or skipped with -SkipPlayable), and
# fails a scene on a non-zero exit, a missing "PASS" line, or any ERROR / SCRIPT ERROR / Unhandled / Exception line.
# Godot's exit code alone hides script load failures, hence the log checks. Does not build: run smoke.ps1 (or
# dotnet build RtsGame.sln) first, since headless Godot loads whatever RtsGame.dll is on disk.
# Usage: powershell -File tools/qa/scene-loop.ps1 [-SkipPlayable] [-Filter <wildcard>] [-LogDir <dir>]
param(
    [switch]$SkipPlayable,
    [string]$Filter = '*Test',
    [string]$LogDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$godot = $env:GODOT
if (-not $godot) { $godot = [Environment]::GetEnvironmentVariable('GODOT', 'User') }
if (-not $godot -or -not (Test-Path $godot)) { Write-Host "FAIL: GODOT env var not set or path missing"; exit 2 }
$gamePath = Join-Path $root 'game'
if ($LogDir -and -not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force $LogDir | Out-Null }

$scenes = Get-ChildItem (Join-Path $gamePath 'tests') -Filter "$Filter.tscn" | Sort-Object Name | Where-Object { $_.BaseName -ne 'M3PlayableTest' }
if (-not $SkipPlayable -and ('M3PlayableTest' -like $Filter)) { $scenes += Get-Item (Join-Path $gamePath 'tests\M3PlayableTest.tscn') }

$failed = @()
foreach ($s in $scenes) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $log = cmd /c "`"$godot`" --headless --path `"$gamePath`" res://tests/$($s.Name) 2>&1"
    $exit = $LASTEXITCODE
    $watch.Stop()
    if ($LogDir) { $log | Set-Content -Encoding utf8 (Join-Path $LogDir "$($s.BaseName).log") }
    # Case-sensitive, and backtrace frames ("    [3] ...") skipped: a warning's frames contain names like godot_variant_call_error.
    $bad = @($log | Where-Object { $_ -cmatch 'ERROR|Unhandled|Exception' -and $_ -notmatch '^\s+\[\d+\]' })
    $pass = @($log | Where-Object { $_ -match 'PASS' -and $_ -notmatch 'FAIL' })
    $ok = $exit -eq 0 -and $bad.Count -eq 0 -and $pass.Count -gt 0
    $status = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0,-24} {1}  exit {2}  {3,6:0.0} s  {4}" -f $s.BaseName, $status, $exit, $watch.Elapsed.TotalSeconds, ($pass | Select-Object -Last 1))
    if (-not $ok) {
        $failed += $s.BaseName
        $log | Where-Object { $_ -cmatch 'FAIL|ERROR|Unhandled|Exception' -and $_ -notmatch '^\s+\[\d+\]' } | Select-Object -First 15 | ForEach-Object { Write-Host "    | $_" }
    }
}
Write-Host ""
if ($failed.Count -gt 0) { Write-Host "SCENE LOOP FAIL: $($failed.Count) of $($scenes.Count): $($failed -join ', ')"; exit 1 }
Write-Host "SCENE LOOP PASS: $($scenes.Count) scenes"
exit 0
