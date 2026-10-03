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

$problems = @()
if ($importExit -ne 0) { $problems += "import exit $importExit" }
if ($smokeExit -ne 0) { $problems += "smoke exit $smokeExit" }
$bad = @($import) + @($smoke) | Where-Object { $_ -match 'ERROR|Unhandled|Exception' }
foreach ($line in $bad) { $problems += "log: $line" }
if (-not ($smoke | Where-Object { $_ -match [regex]::Escape($banner) })) { $problems += "banner '$banner' missing from smoke log" }

$smoke | ForEach-Object { Write-Host "  | $_" }
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL: $_" }
    exit 1
}
Write-Host "PASS: import + smoke exit 0, '$banner' printed, no ERROR lines"
exit 0
