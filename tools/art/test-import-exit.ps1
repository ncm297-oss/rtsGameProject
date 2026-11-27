# M6-VH10 (BUG-1831): proves in seconds that tools/import-assets.ps1 fails (non-zero exit, no IMPORT ASSETS PASS line)
# when its Godot import does not finish, without Blender, the packs or a real Godot. The script runs with -Only set to an id
# that matches nothing (no Blender run, nothing written), a stand-in file for Blender, a stand-in source folder holding
# the manifest's licence files, and $env:GODOT pointing at a stand-in .cmd for each case:
#   ok      exits 0, no ERROR line          -> the script passes (the harness itself can pass)
#   exit1   exits 1                         -> the script fails
#   error0  exits 0 with an ERROR line      -> the script fails (the check it always had)
#   killed  sleeps until this test ends its cmd.exe tree (taskkill /T /F, as QA did in BUG-1831) -> the script fails
# Only processes this test started are ended, by the ids it recorded (the import script's, and the stand-in's cmd.exe,
# which writes its own id to a file).
#
# Usage: powershell -File tools/art/test-import-exit.ps1     prints IMPORT EXIT TEST PASS and exits 0, else FAIL lines, exit 1.
$ErrorActionPreference = 'Stop'
$script = (Resolve-Path (Join-Path $PSScriptRoot '..\import-assets.ps1')).Path
$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot '..\asset-manifest.json') | ConvertFrom-Json
$work = Join-Path ([IO.Path]::GetTempPath()) "rts-import-exit-test-$PID"
if (Test-Path -LiteralPath $work) { Remove-Item -Recurse -Force -LiteralPath $work }
New-Item -ItemType Directory -Force $work | Out-Null
$failures = New-Object System.Collections.Generic.List[string]

# The stand-ins.
$blender = Join-Path $work 'blender-stand-in.exe'
Set-Content -LiteralPath $blender -Value 'not run: -Only matches nothing' -Encoding ascii
$sources = Join-Path $work 'sources'
foreach ($rel in $manifest.licences) {
    $file = Join-Path $sources $rel
    New-Item -ItemType Directory -Force (Split-Path $file -Parent) | Out-Null
    Set-Content -LiteralPath $file -Value 'Stand-in licence: CC0 1.0' -Encoding ascii
}
$pidFile = Join-Path $work 'killed.pid'
$standIns = @{
    ok     = "@echo off`r`necho stand-in import`r`nexit /b 0`r`n"
    exit1  = "@echo off`r`necho stand-in import failing`r`nexit /b 1`r`n"
    error0 = "@echo off`r`necho ERROR: stand-in import`r`nexit /b 0`r`n"
    # The cmd.exe running this batch writes its id (the parent of the powershell it starts), then waits up to 60 s. A second
    # call (the script's second import, reached only when it ignored the kill, as before the fix) returns at once.
    killed = "@echo off`r`nif exist `"$pidFile`" exit /b 0`r`necho stand-in import hanging`r`npowershell -NoProfile -Command `"(Get-CimInstance Win32_Process -Filter ('ProcessId=' + `$PID)).ParentProcessId | Set-Content -Encoding ascii '$pidFile'`"`r`nping -n 60 127.0.0.1 > nul`r`nexit /b 0`r`n"
}
foreach ($name in $standIns.Keys) { [IO.File]::WriteAllText((Join-Path $work "godot-$name.cmd"), $standIns[$name]) }

function Invoke-Case([string]$name, [bool]$wantPass) {
    $env:GODOT = Join-Path $work "godot-$name.cmd"
    $out = Join-Path $work "$name.out"
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath 'powershell.exe' -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError "$out.err" `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"", '-Only', 'vh10-no-such-id', '-Blender', "`"$blender`"", '-SourceRoot', "`"$sources`"")
    $null = $p.Handle # keeps the exit code readable after the process ends
    if ($name -eq 'killed') {
        while (-not (Test-Path -LiteralPath $pidFile) -and $clock.Elapsed.TotalSeconds -lt 30 -and -not $p.HasExited) { Start-Sleep -Milliseconds 100 }
        $standIn = if (Test-Path -LiteralPath $pidFile) { [int]((Get-Content -LiteralPath $pidFile -Raw).Trim()) } else { 0 }
        if ($standIn -gt 0) { & taskkill.exe /T /F /PID $standIn | Out-Null } else { $failures.Add("killed: the stand-in never wrote its process id") }
    }
    if (-not $p.WaitForExit(60000)) {
        & taskkill.exe /T /F /PID $p.Id | Out-Null
        $failures.Add("${name}: the import script did not end in 60 s")
        return
    }
    $code = $p.ExitCode
    # The import script keeps its work folder (named by its process id) after a FAIL, for its log: this one is ours.
    Remove-Item -Recurse -Force -LiteralPath (Join-Path ([IO.Path]::GetTempPath()) "rts-import-assets-$($p.Id)") -ErrorAction SilentlyContinue
    $lines = @(Get-Content -LiteralPath $out)
    $passed = @($lines | Where-Object { $_ -eq 'IMPORT ASSETS PASS' }).Count -gt 0
    $fail = $lines | Where-Object { $_ -like 'FAIL*' } | Select-Object -First 1
    Write-Host ("{0,-7} exit {1}, PASS line {2}, {3:0.0} s{4}" -f $name, $code, $(if ($passed) { 'yes' } else { 'no' }), $clock.Elapsed.TotalSeconds, $(if ($fail) { ": $fail" } else { '' }))
    if ($wantPass -and ($code -ne 0 -or -not $passed)) { $failures.Add("${name}: want exit 0 and the PASS line, got exit $code, PASS line $passed") }
    if (-not $wantPass -and ($code -eq 0 -or $passed)) { $failures.Add("${name}: want a non-zero exit and no PASS line, got exit $code, PASS line $passed (BUG-1831)") }
}

$keep = $env:GODOT
try {
    Invoke-Case 'ok' $true
    Invoke-Case 'exit1' $false
    Invoke-Case 'error0' $false
    Invoke-Case 'killed' $false
} finally {
    $env:GODOT = $keep
    Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
}
foreach ($f in $failures) { Write-Host "IMPORT EXIT TEST FAIL: $f" }
if ($failures.Count -gt 0) { exit 1 }
Write-Host 'IMPORT EXIT TEST PASS'
exit 0
