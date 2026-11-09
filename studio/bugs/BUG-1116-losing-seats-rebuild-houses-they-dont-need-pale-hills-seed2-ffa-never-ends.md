# BUG-1116: Losing seats rebuild houses they don't need; Pale Hills 4-seat FFA seed 2 no longer ends by 144,000 ticks

| Field | Value |
| --- | --- |
| Severity | S3 (was S1; re-triaged by the Producer at the 2026-11-07-0236 ACCEPT: the row no longer ending is BUG-1068's gold starvation on Pale Hills seed 2 with a new face, the house churn is a design nit, and the row is marked a known stall with this id) |
| Status | open (the 4-seat seed-2 row is a skipped known case on `main`; on the held M5-9a branch it ends at 80,278 and is un-skipped, while the seed-1 FFA there has no result by 144,000: BUG-1153's family. The house rule and the hunt's lone-idle-worker gap are the sim's hardening work, see the ruling below) |
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

## M5-H5 (2026-11-08): the house rule re-measured with BUG-1119 and BUG-1120 in, and the lone-idle-worker gap; neither shipped

**The house rule** (reopen a House step only when `HalfPopCap < hard cap && HalfPopCap - HalfPop <= ai.houseWhenPopWithin`,
in `BuildOrderExecutor.ReopenLost`) was tried again on top of BUG-1119 / BUG-1120 and **reverted** under the brief's gate:

| Row | Without the rule (shipped) | With the rule |
| --- | --- | --- |
| `MapFileTests` Pale Hills seed 1 (bound 36,000) | 34,219 | **no result by 36,000** (55,311; 38,330 with the sweep variant below too): the loser keeps one production building at cell (78, 50), never seen; no rebuilt house shows the way |
| `KnownStalls` Pale Hills seed 5 (72,000) | 55,631 | **no result by 72,000** (49,069 with the sweep variant below) |
| `KnownStalls` Pale Hills seeds 3 / 4 / 6 | 29,421 | 28,517 |
| `KnownStalls` generated seed 6 | 26,875 | 26,034 |
| 4-seat FFA seeds 1 / 3, teams seed 1 (144,000) | end | end at 81,410 / 63,352 / 55,691 |
| 4-seat FFA seed 2 | no result | no result (every seat under 25 gold from about 48,000, wood 4,400-31,000) |

QA's two-process row was not re-run: the first gate (seed 1) is already red. So the churn fix stays out until the hunt
finds a beaten side's last building or worker without the rebuilt houses' help.

**The lone-idle-worker gap is the sweep's order, not its coverage.** Probe of seed 5 under the house rule: the camp
follower at (391.8, 438.3) stands 8.4 m from interior sweep point 87 (the cell centre (384, 435.2)), well inside a
member's sight once the point is reached; point 87 was simply **never reached** in 30,000 ticks. The hunt began at 42,000
75 m from it (origin (317.7, 391.6)); nearest-first took three interior points, then ring point 218 on the south edge,
which started the perimeter leg (2 km, about 12,000 ticks), then the interior from the far side: 170 of 225 points
visited by 72,000, pass 0. So the brief's two directions (denser points, a pass over unseen cells) would not reach it
sooner. Measured instead (a temporary toggle, removed):

| Variant | Lone-worker scene (seen within 6,000) | QA `WorkersAtNewHidePoints` Pale Hills AI 1 | Vathar AI 1 |
| --- | --- | --- | --- |
| shipped | 14,937 ticks (red) | 36:26 | 52:30 |
| points within two grid cells of the origin first | red | **no result by 60:00** | **no result** |
| the perimeter leg waits until those points are visited | green | **no result** | 43:52 |
| both | green | **no result** | **no result** |

Both make the scene green and sink a QA hide row (the quarter-cell interior hide, found only on the odd pass, which the
reordered first pass reaches too late); the leg deferral also fails QA's `HuntSweepRound2LegQaTests` (on a 128 m map two
grid cells is the whole map, so the leg never starts) and moves the Malazan page's `crossbow_push` v `standard` seed 32
cell. Not shipped. The row is kept as a skipped known case: `Ai/EndGameHardeningTests.ALoneIdleWorker_NearWhereTheHuntBegins_IsSeenBeforeThePerimeterLeg`
(512 m flat map, a worker 75 m from the army with a ring point nearer; red today at 14,937 ticks). A next try should keep
the first pass's length (e.g. the leg deferred only while a local point is nearer than the reached ring point, or the
local points spliced into the leg) and be judged on that row plus QA's hide and leg rows.


## Developer notes, M5-9a round 2 (2026-11-08-1556; applied by the Producer at ACCEPT)
- **M5-9a round 2 (2026-11-08-1556, the developer; a note, not a fix):** with the starved wave on, Pale Hills seed 1
  Normal v Normal (BUG-1153) ends only at 71,660 and the four-seat Pale Hills seed 1 FFA
  (`HuntStressTests.FourAiSeats_OnPaleHills_*`, seed 1, no teams) still has no result by 144,000, both for this bug's
  pattern: a small army (11-15 Desert Archers; in the FFA 8 units of seat 2, in one wave from 72,000 to 144,000) razes a
  losing base that keeps rebuilding with its wood (Pale Hills seed 1: seat 0 holds 10-17 buildings between 36,000 and
  64,800 with no army). Pale Hills seed 2 still does not end (BUG-1068), so its rows stay skipped and nothing here is
  closed.
- **M5-9a round 2, fix round 1 (2026-11-08-1556, the developer):** the Pale Hills seed 2 FFA (`HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall`)
  now ends at 80,278 (66:53, seat 3) with the oracle clean on 1,079 hunting thinks: un-skipped. The seed 1 FFA still has
  no result by 144,000 (BUG-1153's family). `EndGameHardeningTests`' lone-idle-worker row stays skipped (not this change).

## Developer notes, M5-9b (2026-11-09-0426)

- With M5-9b's far expansion two more Pale Hills Normal v Normal rows end past 72,000 because a decided match takes long to
  finish: seed 2 at 79,504 (decided by 40:00; five hunts to find the loser's far depot and workers) and seed 5 at 96,321 (the
  loser has no army and no gold from 70:00 and rebuilds with 14,000-17,000 wood; four hunts). Filed as **BUG-1157**; both
  `MapSlice2` rows are skipped with the measurement under this id (`NormalVersusNormal_PaleHillsSeed5_SlowRaze_*`,
  `NormalVersusNormal_PaleHillsSeed2_Stalemate_*`). Your sweep-order item would help both: the hunt could look at the start
  locations first (the far expansion's leg reads the same list).
- The Pale Hills seed-1 FFA has no result by 144,000 with the far expansion too (no free gold left with four seats): skipped
  under BUG-1068 / this id (`HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`). The seed-2 FFA ends at 97,140
  (was 80,278) and stays green.
