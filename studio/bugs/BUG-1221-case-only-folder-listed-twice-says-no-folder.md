# BUG-1221: A case-only faction folder listed twice: the second entry reads "has no folder", not "listed twice"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-11-1317, task M7-H1 (QA full) |
| System | data loader (`DataLoader.FactionOrder`, BUG-1173 item 2's folder-case message) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M7H1QaTests.ACaseOnlyFolder_ListedTwice_IsNotReportedAsUnlisted"`
   (skipped with this id; remove the `Skip` to run it).
2. A two-faction copy with `factions/whirlwind/` renamed `factions/Whirlwind/` and the order
   `["malazan", "whirlwind", "whirlwind"]`.

## Expected
`order[1]`: the new case message (folder `factions/Whirlwind/`, not `factions/whirlwind/`), and `order[2]`: "faction
'whirlwind' is listed twice", as an exact-case duplicate reads.

## Actual
```
common/factions.json: order[1]: faction 'whirlwind': its folder is factions/Whirlwind/, not factions/whirlwind/ (a folder's name must be its id exactly, case included)
common/factions.json: order[2]: faction 'whirlwind' has no folder factions/whirlwind/
```
The second line is false on Windows (the folder opens case-insensitively), the exact confusion BUG-1173 item 2 removed for
the single entry.

## Notes
The duplicate check is `result.IndexOf(id)`, and after the case-only branch `result` holds the folder's spelling
(`Whirlwind`), not the listed id, so the second `whirlwind` misses it and falls through `FolderByCase` (its
`!result.Contains(folder)` guard is false) to "has no folder". Checking duplicates against the listed ids (or comparing
case-insensitively) fixes it. Cosmetic: both entries still refuse the load. Two case-only folders, each listed once, are
fine (`M7H1QaTests.TwoFoldersDifferingOnlyInCase_EachIsOneOrderError`, green).
