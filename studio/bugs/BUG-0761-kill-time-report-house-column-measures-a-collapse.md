# BUG-0761: Since the collapse rule, `BuildingKillTimeReportTests`' House column measures a collapse (0.1 s, nobody struck) and stays green

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (e8a622e, D-H5 round 1, verified by QA) |
| Found | 2026-10-16-0847, task M5-H2 (QA) |
| System | data-track measured report `sim/Rts.Sim.Tests/Content/BuildingKillTimeReportTests.cs` vs the collapse rule (BUG-0703) |
| Fixed by | D-H5 fix round 1 (data track): `TestSim.KeepIn(sim, 1)` after the attacker loop in `Raze()`, plus a guard that fails any fall not caused by player 0's attackers (a `Collapsed` death or another killer, the building at full hp on its last tick, or no attacker struck) |

## Repro
1. On the M5-H2 branch (47ca4a2): `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~BuildingKillTimeReportTests" --logger "console;verbosity=detailed"`.
2. Run the same on its base 8c39c08 and compare the printed tables.

## Expected
Each cell is the time 20 line units / 10 Raiders / 2 siege units / 20 Crossbowmen take to raze a lone enemy House, infantry
hall and Town Hall, as the faction pages and STATE quote it (e.g. 20 Heavy Infantry on a House 11.8 s, struck 19 / 20).
Standing rule 25: a sim change that can move a measured cell names the test, and the integration re-runs it.

## Actual
Every House cell on head reads **0.1 s with [0 / n] struck** (base: 6.3-54.0 s); the hall and Town Hall columns are
unchanged. The "before D19" variant's band count goes from 6 / 12 to **8 / 12** because a 0.1-s collapse meets the House
band. The test stays green (12 / 12 on today's data), so nothing flags it.

```
base: | 20 malazan_heavy_infantry (1480) | 11.8 s [19 / 20] ok | 23.1 s [20 / 20] ok | 45.0 s [18 / 20] ok |
head: | 20 malazan_heavy_infantry (1480) | 0.1 s [0 / 20] ok  | 23.1 s [20 / 20] ok | 45.0 s [18 / 20] ok |
base: | 2 malazan_catapult (700) | 6.3 s [2 / 2] 3.9x HI per cost ok | ...
head: | 2 malazan_catapult (700) | 0.1 s [0 / 2] 2.1x HI per cost ok | ...
```

`Raze()` spawns player 1's building straight into the store and gives player 1 no unit. A House trains nothing, so since
M5-H2 player 1 is defeated on tick 1 and its House collapses (a `Collapsed` event, no attacker involved); the loop ends
on `!IsAlive(b)` and reports the collapse as a razing. Halls train units, so their columns are unaffected. This is standing
rule 24's case (a building staged before any unit of that player), which the dev fixed in ~40 sim-track scenes, but this
file is in the data track's `Content/` folder, which the M5-H2 brief put out of the sim's reach, and its row stays green, so
the full-suite run didn't show it.

## Notes
- The fix is one line in a data-track file: `TestSim.KeepIn(sim, 1)` in `Raze()` **after the attacker loop** (before the
  first tick). QA tried both placements on head: after the attackers, the whole table equals base 8c39c08's except one cell
  (Raiders on an infantry hall 19.4 -> 19.5 s, which unpatched head shows too: the M5-H2 movement / surround change);
  placed before the attackers, the keeper takes unit slot 0, every attacker's slot shifts, and cells all over the table move
  (20 HI on a House 11.8 s [19 / 20] -> 12.6 s [17 / 20], on a Town Hall 45.0 -> 41.0 s). The Producer routes it (data
  track, or a sim fix round with an explicit exception to the folder rule).
- **Integration hazard:** the merged-tree check re-runs this report (rule 25). Run as it stands, it prints 0.1-s House cells
  that a reprint would copy onto the faction pages.
- Severity S2: a measured guard the integration relies on silently stops measuring (as if its House band were set to
  zero). Its asserts can no longer fail for the House column.
- Checked the rest of `Content/` + `QA/Content/` base vs head (895 / 895 green both): `CounterTriangleMarginsTests`,
  `LineShockSweep` and the siege rows print identically; `AiSpendingReportTests` moved (expected from the BUG-0650 /
  BUG-0702 AI changes; a report the data track re-runs at D-H5 anyway).

## Verification (QA, D-H5 fix round 1, e8a622e)
- Table on head vs a fresh `git archive` of base 8c39c08 (both run with `--logger "console;verbosity=detailed"`): every
  seconds / struck cell of all three tables is equal except 20 Raiders on the infantry hall 19.4 -> 19.5 s (today and the
  "today / before" table). The House column is back: 20 HI 11.8 s [19 / 20], 20 Raiders 9.8 s, 20 Crossbowmen 27.4 s,
  10 Raiders 13.8 s, 2 Catapults 6.3 s, 2 Sappers 13.7 s (before-D19: 44.6 / 42.4 / 54.0 / 78.2 / 6.3 / 13.7 s). The only
  other diff lines are D-H5's own "/ n.nx Raider per cost" annotations. 12 / 12 band cells; the Raider-miss pin
  (`malazan_catapult Infantry hall`, `malazan_catapult Town Hall`) is unchanged in source and green.
- The 19.5 s cell comes from M5-H2, not the keeper: with the keeper and all three guards removed, head prints 19.5 s on the
  hall too (and 0.1 s [0 / n] on every House).
- Mutants (scratch copies): keeper removed -> red, "malazan_heavy_infantry v whirlwind_tent: the building fell on tick 1
  but not to player 0's attackers"; keeper and the killer assert removed -> red, "... fell on tick 1 at full hp, never
  struck". The guard bites on its own and is layered.
- No other collapse trap in `Content/` + `QA/Content/`: with `World.Collapse` instrumented to log a stack trace, the 895
  tests there trigger 0 collapses (control: `QA/CollapseRuleQaTests` logs 27). By source: the kill-time report is the only
  file there that spawns a building directly; the other ticking scenes are units-only (never built, so never defeated) or
  `AiScenes.Flat` / generated maps with a Town Hall.
