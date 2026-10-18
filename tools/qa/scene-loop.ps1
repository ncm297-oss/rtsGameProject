# QA scene loop: runs every headless game/tests/*Test.tscn (the playables M3PlayableTest and M4PlayableTest last, or skipped with -SkipPlayable), and
# fails a scene on a non-zero exit, a missing "PASS" line, or any ERROR / SCRIPT ERROR / Unhandled / Exception line.
# Godot's exit code alone hides script load failures, hence the log checks. Does not build: run smoke.ps1 (or
# dotnet build RtsGame.sln) first, since headless Godot loads whatever RtsGame.dll is on disk.
# A scene still running after -TimeoutSec seconds (default 600: the slowest scene, M3PlayableTest, takes about 2 minutes
# under load) is killed with its process tree and fails with "timeout" in its row, so one hung scene (BUG-0860: an LFS
# model not pulled) can't hang the loop. LoopTimeoutProbe.tscn never quits: -Filter LoopTimeoutProbe -TimeoutSec 5 shows the row.
# Usage: powershell -File tools/qa/scene-loop.ps1 [-SkipPlayable] [-Filter <wildcard>] [-LogDir <dir>] [-TimeoutSec <s>]
param(
    [switch]$SkipPlayable,
    [string]$Filter = '*Test',
    [string]$LogDir = '',
    [int]$TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$godot = $env:GODOT
if (-not $godot) { $godot = [Environment]::GetEnvironmentVariable('GODOT', 'User') }
if (-not $godot -or -not (Test-Path $godot)) { Write-Host "FAIL: GODOT env var not set or path missing"; exit 2 }
$gamePath = Join-Path $root 'game'
if ($LogDir -and -not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force $LogDir | Out-Null }

$playables = @('M3PlayableTest', 'M4PlayableTest')
$scenes = Get-ChildItem (Join-Path $gamePath 'tests') -Filter "$Filter.tscn" | Sort-Object Name | Where-Object { $playables -notcontains $_.BaseName }
foreach ($p in $playables) {
    $path = Join-Path $gamePath "tests\$p.tscn"
    if (-not $SkipPlayable -and ($p -like $Filter) -and (Test-Path $path)) { $scenes += Get-Item $path }
}

$failed = @()
$tmpLog = Join-Path ([IO.Path]::GetTempPath()) "scene-loop-$PID.log"
foreach ($s in $scenes) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    # cmd /c (as before) keeps PowerShell 5.1 from turning Godot's stderr into ErrorRecords; it writes to a file so the
    # process can be waited on with a limit. cmd strips the outer quotes of /c "..." and keeps the inner ones.
    $cmdLine = "/c `"`"$godot`" --headless --path `"$gamePath`" res://tests/$($s.Name) > `"$tmpLog`" 2>&1`""
    $proc = Start-Process -FilePath cmd.exe -ArgumentList $cmdLine -NoNewWindow -PassThru
    $null = $proc.Handle # keeps the exit code readable after the process ends (a Start-Process quirk)
    $timedOut = -not $proc.WaitForExit($TimeoutSec * 1000)
    if ($timedOut) {
        # The console exe is a wrapper around the editor exe: kill the whole tree, not just cmd.
        cmd /c "taskkill /T /F /PID $($proc.Id) >nul 2>&1"
        $proc.WaitForExit(10000) | Out-Null
        $exit = 'timeout'
    } else {
        $proc.WaitForExit()
        $exit = $proc.ExitCode
    }
    $watch.Stop()
    $log = @()
    if (Test-Path $tmpLog) { $log = @(Get-Content -Encoding UTF8 $tmpLog); Remove-Item -Force $tmpLog -ErrorAction SilentlyContinue }
    if ($LogDir) { $log | Set-Content -Encoding utf8 (Join-Path $LogDir "$($s.BaseName).log") }
    # Case-sensitive, and backtrace frames ("    [3] ...") skipped: a warning's frames contain names like godot_variant_call_error.
    $bad = @($log | Where-Object { $_ -cmatch 'ERROR|Unhandled|Exception' -and $_ -notmatch '^\s+\[\d+\]' })
    $pass = @($log | Where-Object { $_ -match 'PASS' -and $_ -notmatch 'FAIL' })
    $ok = (-not $timedOut) -and $exit -eq 0 -and $bad.Count -eq 0 -and $pass.Count -gt 0
    $status = if ($ok) { 'PASS' } else { 'FAIL' }
    $note = if ($timedOut) { "timeout after $TimeoutSec s (killed)" } else { $pass | Select-Object -Last 1 }
    Write-Host ("{0,-24} {1}  exit {2}  {3,6:0.0} s  {4}" -f $s.BaseName, $status, $exit, $watch.Elapsed.TotalSeconds, $note)
    if (-not $ok) {
        $failed += $s.BaseName
        $log | Where-Object { $_ -cmatch 'FAIL|ERROR|Unhandled|Exception' -and $_ -notmatch '^\s+\[\d+\]' } | Select-Object -First 15 | ForEach-Object { Write-Host "    | $_" }
    }
}
Write-Host ""
if ($failed.Count -gt 0) { Write-Host "SCENE LOOP FAIL: $($failed.Count) of $($scenes.Count): $($failed -join ', ')"; exit 1 }
Write-Host "SCENE LOOP PASS: $($scenes.Count) scenes"
exit 0
