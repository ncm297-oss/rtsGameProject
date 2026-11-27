# QA M6-VH10 (BUG-1831), beside tools/art/test-import-exit.ps1: more stand-ins for the Godot import, run against a copy of
# tools/import-assets.ps1 placed in a temp root (so the stand-ins' game/.godot/imported is the temp root's, never the
# repo's). Cases:
#   crash    exits with an access-violation code       -> the script fails
#   tmp0     exits 0, writes an empty *.tmp now          -> the script fails (a cut-off texture compression)
#   tmpold   exits 0, an empty *.tmp from an hour ago    -> the script passes (not this import's)
#   tmpfull  exits 0, writes a non-empty *.tmp now       -> the script passes (only size 0 counts)
#   nolog    exits 0 and deletes nothing, prints nothing -> the script passes
# Usage: powershell -File tools/qa/import-exit-extra.ps1   prints IMPORT EXIT EXTRA PASS and exits 0, else FAIL lines, exit 1.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$work = Join-Path ([IO.Path]::GetTempPath()) "rts-import-exit-extra-$PID"
if (Test-Path -LiteralPath $work) { Remove-Item -Recurse -Force -LiteralPath $work }
$root = Join-Path $work 'root'
New-Item -ItemType Directory -Force (Join-Path $root 'tools\art'), (Join-Path $root 'game\.godot\imported') | Out-Null
Copy-Item (Join-Path $repo 'tools\import-assets.ps1') (Join-Path $root 'tools\import-assets.ps1')
Copy-Item (Join-Path $repo 'tools\asset-manifest.json') (Join-Path $root 'tools\asset-manifest.json')
$script = Join-Path $root 'tools\import-assets.ps1'
$imported = Join-Path $root 'game\.godot\imported'
$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'tools\asset-manifest.json') | ConvertFrom-Json
$blender = Join-Path $work 'blender-stand-in.exe'
Set-Content -LiteralPath $blender -Value 'not run' -Encoding ascii
$sources = Join-Path $work 'sources'
foreach ($rel in $manifest.licences) {
    $file = Join-Path $sources $rel
    New-Item -ItemType Directory -Force (Split-Path $file -Parent) | Out-Null
    Set-Content -LiteralPath $file -Value 'Stand-in licence: CC0 1.0' -Encoding ascii
}
$failures = New-Object System.Collections.Generic.List[string]
$standIns = [ordered]@{
    crash   = "@echo off`r`necho stand-in crashing`r`nexit /b -1073741819`r`n"
    tmp0    = "@echo off`r`necho stand-in`r`ntype nul > `"$imported\cut.ctex.tmp`"`r`nexit /b 0`r`n"
    tmpold  = "@echo off`r`necho stand-in`r`nexit /b 0`r`n"
    tmpfull = "@echo off`r`necho stand-in`r`necho data> `"$imported\full.ctex.tmp`"`r`nexit /b 0`r`n"
    nolog   = "@exit /b 0`r`n"
}
$want = @{ crash = $false; tmp0 = $false; tmpold = $true; tmpfull = $true; nolog = $true }
$keep = $env:GODOT
try {
    foreach ($name in $standIns.Keys) {
        Get-ChildItem -LiteralPath $imported -File | Remove-Item -Force
        if ($name -eq 'tmpold') {
            $old = Join-Path $imported 'old.ctex.tmp'
            [IO.File]::WriteAllBytes($old, [byte[]]@())
            (Get-Item -LiteralPath $old).LastWriteTime = (Get-Date).AddHours(-1)
        }
        $cmd = Join-Path $work "godot-$name.cmd"
        [IO.File]::WriteAllText($cmd, $standIns[$name])
        $env:GODOT = $cmd
        $out = Join-Path $work "$name.out"
        $p = Start-Process -FilePath 'powershell.exe' -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError "$out.err" `
            -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"", '-Only', 'qa-no-such-id', '-Blender', "`"$blender`"", '-SourceRoot', "`"$sources`"")
        $null = $p.Handle
        if (-not $p.WaitForExit(60000)) { & taskkill.exe /T /F /PID $p.Id | Out-Null; $failures.Add("${name}: no end in 60 s"); continue }
        $code = $p.ExitCode
        Remove-Item -Recurse -Force -LiteralPath (Join-Path ([IO.Path]::GetTempPath()) "rts-import-assets-$($p.Id)") -ErrorAction SilentlyContinue
        $lines = @(Get-Content -LiteralPath $out)
        $passed = @($lines | Where-Object { $_ -eq 'IMPORT ASSETS PASS' }).Count -gt 0
        $fail = $lines | Where-Object { $_ -like 'FAIL*' } | Select-Object -First 1
        Write-Host ("{0,-8} exit {1}, PASS line {2}{3}" -f $name, $code, $passed, $(if ($fail) { ": $fail" } else { '' }))
        if ($want[$name] -and ($code -ne 0 -or -not $passed)) { $failures.Add("${name}: want a pass, got exit $code, PASS $passed") }
        if (-not $want[$name] -and ($code -eq 0 -or $passed)) { $failures.Add("${name}: want a failure, got exit $code, PASS $passed") }
    }
} finally {
    $env:GODOT = $keep
    Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
}
foreach ($f in $failures) { Write-Host "IMPORT EXIT EXTRA FAIL: $f" }
if ($failures.Count -gt 0) { exit 1 }
Write-Host 'IMPORT EXIT EXTRA PASS'
exit 0
