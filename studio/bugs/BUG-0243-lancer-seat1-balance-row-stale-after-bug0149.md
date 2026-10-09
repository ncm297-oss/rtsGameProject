# BUG-0243: After BUG-0149's fix one counter-triangle row prints new numbers; the Malazan page's balance baseline still has the old time

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-08-2144, task M4-H1 |
| System | combat (BUG-0149 switch rule) vs `docs/factions/malazan.md` "Balance baseline" (data track) |
| Fixed by | |

## Repro
1. Run `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Scenario.CounterTriangleTests" --logger "console;verbosity=detailed"`
   on 4c1f168 and on 267c719, then diff the printed lines.
2. Run the same with the base's `CounterTriangleTests.cs` (the old private harness) on 267c719. It prints the same line
   as the new `CounterTriangleScene`, so the harness move is faithful and the difference comes from the combat rule.

## Expected
The M4-H1 brief, criterion 4: `CounterTriangleTests` uses the shared harness "with unchanged printed numbers". The
dev's report says the 14 lines are byte-identical. docs/factions pages match the sim they describe.

## Actual
13 of 14 lines are identical. One changed:
```
base: Shock beats Ranged, malazan_wickan_lancer as player 1: 8/10 left (912 hp, cost 960 of 1200) ... after 429 ticks (21.5 s)
HEAD: Shock beats Ranged, malazan_wickan_lancer as player 1: 8/10 left (888 hp, cost 960 of 1200) ... after 440 ticks (22.0 s)
```
`docs/factions/malazan.md:168` still says `| Shock beats Ranged | Wickan Lancer v Desert Archer | 1 | ... | 8 / 10 | 960 / 1200 (80 %) | 21.5 s |`.
The winner, survivors and cost kept are unchanged.

## Notes
- The scratchpad's `ct_before.txt` / `ct_after.txt` (22:13, both 912 hp / 429 ticks) look like the dev compared the
  harness before the BUG-0149 change went in at 22:24. The harness is faithful; the "unchanged numbers" claim is not.
- The sim track may not edit `docs/factions/`, so the data track has to update the time (21.5 s -> 22.0 s). The data
  branch's `CounterTriangleMarginsTests` is green on the scratch merge, so it doesn't pin the time column.
