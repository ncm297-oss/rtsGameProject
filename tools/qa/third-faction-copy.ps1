# Adds a copy of Malazan's faction files to a SCRATCH CLONE's game/data as a further faction, appended to the order.
# For scratch clones only (a `git clone` or `git archive` in a temp folder): it refuses a studio worktree, any linked git
# worktree and the main checkout, because a faction on disk there moves the golden header and every default-seat row.
#
# What it writes (the same replacements as Data/FactionOrderTests.AddTeblor, with the names derived from -Id so any id
# works): game/data/factions/<Id>/ = factions/malazan/* with `malazan_` -> `<Id>_`, `moranth_supply` -> `<Id>_upgrade`,
# `"telas_fire"` -> `"<Id>_fire"`, `"cusser"` -> `"<Id>_cusser"`; faction.json's id set to <Id> and its displayName to
# "<Id> (Malazan copy)"; <Id> appended last to common/factions.json's order. Appended, so no shipped id moves (M7-3 / M7-3b).
#
# Usage: powershell -File tools/qa/third-faction-copy.ps1 -Repo C:\path\to\scratch-clone [-Id teblor]
param(
    [Parameter(Mandatory = $true)][string]$Repo,
    [string]$Id = 'teblor'
)

$ErrorActionPreference = 'Stop'

function Refuse([string]$why) { Write-Host "REFUSED: $why"; exit 2 }

if ($Id -cnotmatch '^[a-z][a-z0-9]*(_[a-z0-9]+)*$') { Refuse "id '$Id' is not snake_case" }
if (-not (Test-Path -LiteralPath $Repo -PathType Container)) { Refuse "no folder $Repo" }
$root = (Resolve-Path -LiteralPath $Repo).Path.TrimEnd('\')
$data = Join-Path $root 'game\data'

# --- scratch clones only -----------------------------------------------------------------------------------------
if ($root -match '\\\.claude\\worktrees(\\|$)') { Refuse "$root is under .claude\worktrees (a studio worktree); use a scratch clone" }
$dotGit = Join-Path $root '.git'
if (Test-Path -LiteralPath $dotGit -PathType Leaf) { Refuse "$root is a linked git worktree (.git is a file); use a scratch clone" }
if (Test-Path -LiteralPath (Join-Path $root '.claude\worktrees') -PathType Container) {
    Refuse "$root hosts .claude\worktrees (the main checkout); use a scratch clone"
}
if (Test-Path -LiteralPath $dotGit -PathType Container) {
    $list = @(cmd /c "git -C `"$root`" worktree list --porcelain 2>nul" | Where-Object { $_ -like 'worktree *' })
    if ($list.Count -gt 1) { Refuse "$root has $($list.Count) git worktrees (the main checkout); use a scratch clone" }
}
# The checkout this script runs from, when it runs from a worktree: its main checkout is never a target.
$common = cmd /c "git -C `"$PSScriptRoot`" rev-parse --path-format=absolute --git-common-dir 2>nul"
$own = cmd /c "git -C `"$PSScriptRoot`" rev-parse --path-format=absolute --git-dir 2>nul"
if ($LASTEXITCODE -eq 0 -and $common -and $own -and ($common -ne $own)) {
    $main = (Split-Path -Parent ($common -replace '/', '\')).TrimEnd('\')
    if ($root -ieq $main) { Refuse "$root is the main checkout; use a scratch clone" }
}

# --- the copy ----------------------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath (Join-Path $data 'common\factions.json'))) { Refuse "$root has no game\data\common\factions.json" }
$from = Join-Path $data 'factions\malazan'
$to = Join-Path $data "factions\$Id"
if (Test-Path -LiteralPath $to) { Refuse "factions\$Id already exists in $data" }
$orderFile = Join-Path $data 'common\factions.json'
$order = @((Get-Content -LiteralPath $orderFile -Raw | ConvertFrom-Json).order)
if ($order -contains $Id) { Refuse "'$Id' is already in common/factions.json's order" }

$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Path $to | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $from -File) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $text = $text.Replace('malazan_', "${Id}_").Replace('moranth_supply', "${Id}_upgrade")
    $text = $text.Replace('"telas_fire"', "`"${Id}_fire`"").Replace('"cusser"', "`"${Id}_cusser`"")
    if ($file.Name -eq 'faction.json') {
        $text = [regex]::Replace($text, '"id"\s*:\s*"malazan"', "`"id`": `"$Id`"")
        $first = New-Object System.Text.RegularExpressions.Regex('"displayName"\s*:\s*"[^"]*"')
        $text = $first.Replace($text, "`"displayName`": `"$Id (Malazan copy)`"", 1) # the faction's own name, the first one
    }
    [IO.File]::WriteAllText((Join-Path $to $file.Name), $text, $utf8)
}
$quoted = ($order + $Id | ForEach-Object { "`"$_`"" }) -join ', '
[IO.File]::WriteAllText($orderFile, "{`n  `"order`": [$quoted]`n}`n", $utf8)
Write-Host "OK: added factions\$Id (a Malazan copy) to $data; order is now [$quoted]"
exit 0
