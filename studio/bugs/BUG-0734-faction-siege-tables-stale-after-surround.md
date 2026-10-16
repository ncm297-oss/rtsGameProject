# BUG-0734: Faction pages' siege tables are stale after the surround rule (CounterTriangleMarginsTests 8 rows red)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open (fix dispatched: the data track's merged-tree reprint at the 2026-10-15-2325 integration, Producer decision) |
| Found | 2026-10-15-2325, task M5-P1 |
| System | content docs (`docs/factions/malazan.md`, `docs/factions/whirlwind.md` "Balance baseline" siege tables), `Content/CounterTriangleMarginsTests` |
| Fixed by | (the reprint commit on the merged tree sim 94a0c1a + view b6c33e4 + data cc3e170; the dev sets this `fixed` with its hash) |

## Producer decision (2026-10-16, ACCEPT of session 2325)
Sim ACCEPT with no sim change. D19 (the same session) re-printed the same siege cells on the old movement (Catapult on a Tent line
36.2 s / 47 %, Ram on a Billet 40.0 s / 34 %), so neither branch's numbers survive the merge: on the Producer's scratch merge
(sim + view + data) the same 8 rows are red. The conductor merges sim → view → data, then runs a data-track fix-mode dev on the
merged tree that re-prints both pages' siege tables and prose from `AllEightPairs_BothSeats_PrintTheMarginTable_AndTheDocWinnerWins`,
re-runs `Content/BuildingKillTimeReportTests` (the band 12 / 12 must hold; a failing cell is pinned at the measured count with a dated
reason and an S3 filed, never loosened silently), updates docs/03 "Implementation (pathing slice 1)"'s last bullet to the merged-tree
numbers, and pushes only when `CounterTriangleMarginsTests` 9 / 9, `BuildingKillTimeReportTests`, `D19QaTests`, `DataValidationTests`,
`DataContentHashTests` (hash AB811AD47F68D0AE unchanged: docs and tests only), `ReplayGoldenTests` and `GatherWedgeQaTests` are green
and the full non-Perf suite is green on the merged tree. Standing rule 25 (handoff) prevents the repeat.

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
