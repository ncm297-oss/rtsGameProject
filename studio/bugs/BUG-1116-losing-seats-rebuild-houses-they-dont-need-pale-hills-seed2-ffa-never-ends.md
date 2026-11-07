# BUG-1116: Losing seats rebuild houses they don't need; Pale Hills 4-seat FFA seed 2 no longer ends by 144,000 ticks

| Field | Value |
| --- | --- |
| Severity | S3 (was S1; re-triaged by the Producer at the 2026-11-07-0236 ACCEPT: the row no longer ending is BUG-1068's gold starvation on Pale Hills seed 2 with a new face, the house churn is a design nit, and the row is marked a known stall with this id) |
| Status | open (the 4-seat seed-2 row is a skipped known case since the ACCEPT fix round; the house rule and the hunt's lone-idle-worker gap are the sim's hardening work, see the ruling below) |
| Found | 2026-11-07-0236, task M5-4d-2 |
| System | AI (BuildOrderExecutor.ReopenLost), match end |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt"`
2. Row `(seed: 2, teams: False)` fails: `seed 2: no result by 144,000 ticks`. It passed at the diff base 052c39e.

## Expected
The row ends within 144,000 ticks as at the base (criterion 1: non-Perf green). The brief: "a row that no longer ends is a
finding, not a bound change". docs/02 D15 C's intent: a house that adds nothing is not built.

## Actual
QA probe (same match, per-seat Build commands and reopened steps; 8155ee4):

| tick | p1 Whirlwind `raider_rush` | p2 Malazan `cadre_rush` |
| --- | --- | --- |
| 24,000 | 26 workers, 39 army, 10 tents; 0 reopens | 30 workers, 28 army, 10 billets; 0 reopens |
| 48,000 | 3 workers, 0 army, gold 4; **91 steps reopened; 96 + 19 tent Builds, 9 more raider camps** | defeated; **75 reopened; 90 billet Builds, 10 crossbow ranges** (hall lost at 38,240) |
| 144,000 | unchanged: 3 workers, 12 tents, 2 raider camps, wood 4,413 | — |

p0 (13 army, gold 6) and p3 (0 army) never reach a wave again; p1's rebuilt buildings stay known, so nobody hunts its 3
workers. About 90 % of the rebuild Builds are **houses** (115 tents, 90 billets) for seats whose population is a small fraction
of their cap. `ReopenLost` reopens a House step whenever the seat holds fewer houses than done House steps, unless it is at
the hard cap. A seat with 3 workers and 10 tents has far more cap than it needs, so a lost tent adds nothing back, yet
each loss draws a new Build (one every ~12 s under siege) and keeps a fresh enemy target on the map.

## Notes
- The dev's report says the same: the BUG-1068 seed-2 stall (gold runs out, wave thresholds can't be reached) moved here
  by rebuilding.
- Proposed cheap fix, the Producer's call (a rule change, a few lines): reopen a House step only when the economy's house
  rule would want a house (`HalfPopCap - HalfPop <= ai.houseHalfPopMargin`, the condition `EconomyManager.House` already
  uses), on top of the hard-cap test. That keeps D15 C's intent and removes most of the churn. Then re-run this row. If it
  still stalls, the remaining cause is BUG-1068's gold starvation, and marking the row a known case is a loosening that needs
  the Producer's (or the owner's) explicit ruling.
- Not the look: 0 thinks on a look point for every seat over the whole match.

## Producer ruling (2026-11-07-0236 ACCEPT)

- **Fix round 1 tried QA's house rule** (reopen a House step only when `HalfPopCap < hard cap && HalfPopCap - HalfPop <=
  ai.houseHalfPopMargin`) and **reverted it**: the churn went (p1 tents 115 → 16, p2 billets 90 → 0) but seed 2 still has no
  result by 144,000 (every seat out of gold by 72,000: p0 16, p1 24, p3 14, wood past 10,000 each; p2 defeated 67:23; p3
  holds 7 army units under its 28 half-pop threshold and still remembers 18 of the defeated p2's buildings, so it neither
  waves nor hunts), **and it turned three green rows red**: `MapFileTests` Pale Hills seed 1 (no result by 36,000),
  `MapSlice2StressTests.KnownStalls` Pale Hills seed 5 (no result by 72,000: the loser's last camp follower stands idle at
  (391.8, 438.3) from 48,000 and the winner's 7-unit hunt never sees it; a rebuilt tent site had been the known target
  that ended the match), and QA's two-process row (no hall lost any more). Rebuilt houses act as a beacon for the hunt.
- **Ruling:** the underlying stall is BUG-1068 (gold starvation on Pale Hills seed 2: by 36,000 every seat's gold is under
  100 and its wood in the thousands); rebuilding is designed behaviour (docs/02 "AI opponent") and the slice ships. The
  4-seat free-for-all seed-2 row becomes a skipped known case under this id and BUG-1068 (the same pattern as
  `MapSlice2StressTests`' seed-2 row), done in the ACCEPT fix round; seeds 1 / 3 and the teams row still run.
- **Left for the sim's hardening (4 / 4), most valuable first:** (1) **BUG-1119**: a seat that is defeated has its buildings
  forgotten by every enemy's memory (the defeat is public: `PlayerView.MatchOver`'s per-seat twin), so a survivor with no
  live enemy building known hunts instead of waiting for a wave; (2) **the hunt finds a lone idle worker**: the seed-5 case
  above (a worker idle 3 m from a sweep point's sight edge for 24,000 ticks); (3) **the house rule**, re-measured with (1)
  and (2) in, since its effect on the pinned rows is what sank it alone; (4) BUG-1068's gold itself (an expansion step
  the losing openings reach, or Pale Hills' far mines) only after the three above.
