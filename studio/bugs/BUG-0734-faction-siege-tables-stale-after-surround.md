# BUG-0734: Faction pages' siege tables are stale after the surround rule (CounterTriangleMarginsTests 8 rows red)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-15-2325, task M5-P1 |
| System | content docs (`docs/factions/malazan.md`, `docs/factions/whirlwind.md` "Balance baseline" siege tables), `Content/CounterTriangleMarginsTests` |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CounterTriangleMarginsTests"` on `studio/2026-10-15-2325-sim`
(ce7e26b). 8 rows fail: `EveryPrintedRow_EqualsItsPageRow` (both factions), 4 `AMutatedPageCell_*` rows and the two
`BUG0290_*` rows (each expects exactly its own planted problem and also gets the stale siege cells). Green on 53e69d7.

## Expected
docs/02 / CLAUDE.md "Docs updated in the same commit when behavior changed": the pages' printed tables equal the harness.

## Actual
Only the siege tables differ (no group-table cell moved):
```
malazan.md   1 Catapult on a Tent:      Line time 192.2 s -> 155.0 s, Siege / line 9 % -> 11 %
malazan.md   1 Battering Ram on a Billet: Siege time 13.8 s -> 13.7 s, Line time 254.5 s -> 191.8 s, Siege / line 5 % -> 7 %
whirlwind.md 1 Battering Ram on a Billet: Siege time 13.8 s -> 13.7 s, Line time 254.5 s -> 191.8 s, Siege / line 5 % -> 7 %
```
The line units now raze the 2x2 faster because more of them strike at once (the slice's goal). The Ram (melee, 0.5 m) also
goes through the rule, hence its 0.1 s.

## Notes
- Not a sim defect, and the sim track may not edit `docs/factions/**` or `Content/**` (brief "Hands off"). The fix is a
  re-print of both pages' siege tables from `AllEightPairs_BothSeats_PrintTheMarginTable_AndTheDocWinnerWins` on the merged
  tree by the data track (D19 edits the same pages this session on the old movement, so the re-print must come after both
  merges). Producer's sequencing call; until then `main` would be red on these 8 rows.
- The dev disclosed this in docs/03 "Implementation (pathing slice 1)" with the same numbers.
- The siege-to-line ratio still holds the design's direction (siege far faster), but the "Siege / line" percentages move up.
