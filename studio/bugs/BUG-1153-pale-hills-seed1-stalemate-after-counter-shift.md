# BUG-1153: Pale Hills seed 1 Normal v Normal no longer ends once Normal shifts its mix (gold runs out, an archer army below its wave threshold)

| Field | Value |
| --- | --- |
| Severity | S2 (a pinned criterion row stops ending; QA focus 6 of M5-9a). **Since round 2 the row ends (68,664) but outside its 36,000 bound: S2 while it is red on the branch; the Producer's rule below says when it becomes S3** |
| Status | open (M5-9a round 2 ESCALATED 2026-11-08-1556: the branch `studio/2026-11-08-1556-sim` is pushed, nothing on `main`; the next sim session's rule is below) |
| Found | 2026-11-08-1316, task M5-9a (developer, rule 43 re-measure) |
| System | AI economy and waves (BUG-1068's gold exhaustion), surfaced by the counter shift |
| Fixed by | |

## Repro
1. On the M5-9a branch: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MatchCriterionTests.EachOtherMap_NormalVersusNormal_EndsWithAWinnerInsideItsBound_WithoutErrors&DisplayName~pale_hills"`
   (Stress): "no result in 36000 ticks". Also `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks(pale_hills)`,
   `MatchCriterionTests.EveryRow_InTwoProcesses_*(pale_hills)` and QA's sweep row `pale_hills` seed 1 (no end at 72,000).
   Same family, also failing on the branch: `Stress/HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt(seed 1,
   FFA)` ("no result by 144,000 ticks") and QA's `Stress/RebuildLookMatchQaStressTests.FourAiSeats_*(pale_hills, 1)` ("nothing
   looked or rebuilt: the row checks nothing", its coverage precondition: the four-seat match no longer makes a look or a
   hall rebuild).
2. With both factions' Normal `counterStrength` at 0 the same match ends at 34,219, seat 1 winning, as before M5-9a
   (`CounterCompositionTests.TheNonHardPins_WithNormalStrengthZero_PlayAsBeforeThisTask`, the `pale_hills` 1 Normal row).

## Expected
docs/05 M5 criterion 5's rows (docs/03 "Implementation (M5-5)"): Pale Hills seed 1, two Normal AI seats, ends with a winner
within 36,000 ticks.

## Actual
Probed every 2:00 (both seats `standard`):
- Up to about 22:00 the two runs (strength 0.5 and 0) track closely; both sides' gold is gone by about 24:00 (Malazan 46-66,
  Whirlwind 2-16 from then on; wood piles up to thousands): the start mines are spent and no expansion is taken (BUG-1068).
- With the shift the Whirlwind side's late mix leans to Horse Raiders / priests (against Malazan Crossbowmen, Line and
  casters seen); after the 24:00-28:00 fights it is left with **10 Desert Archers and 1-3 priests**, under its `standard`
  wave threshold (14 pop / 861 gold + wood), and with 16 gold it can train nothing: no wave after 28:00, no hunt (it still
  knows Malazan buildings). The Malazan side keeps 5 laborers and its buildings, 10 gold: it can train nothing either.
- At 60:00: no result. Without the shift the Whirlwind side kept melee Raiders and Zealots and finished the Malazans at 34,219.
- Normal strength 0.1: ends at 34,545; 0.25: ends at 51,696 (outside 36,000). Not a smooth function of the strength: the
  shift only reshuffles a fragile, gold-starved end game.

## Notes
- Root cause is the existing end-game fragility, not the counter memory: a gold-starved side whose army is below its wave
  threshold never attacks again (BUG-1068's economy: the `expand` step looks 40 m only, so the map's far mines are never
  taken). Related: BUG-1152 (wood-starved silent seats; Raraku seed 5 shows it newly on this branch), BUG-1116.
- Candidate levers (each an AI change, own task): a wave from a side that can no longer grow (no income for its next unit)
  with whatever it has; BUG-1068's expansion reach; or D26 tuning Normal's strength (data).
- On this branch the rows are left failing (not skipped, not re-bound): the Producer decides between accepting with this
  bug open (skip the three rows with this id and add `("pale_hills", 1)` to the sweep's `Known`), lowering Normal's
  strength in data, or fixing the end game first.
- **Producer triage (2026-11-08-1316 ACCEPT): blocks M5-9a; REJECT round 1, fix the end game on the same branch.** Confirmed
  independently (the `EachOtherMap_*` theory in the sim worktree: Raraku and generated pass, Pale Hills "no result"). Not
  accepted with the bug open (a criterion-5 row that stops ending is S2 by M5-9a's own brief) and not tuned away in data
  (0.1 hides the feature; 0.25 ends at 51,696, so the end game is fragile at any strength). Round 2 adds BUG-1068's
  candidate (b) behind `rules.json` `ai.starvedWaveAfter` (0 = off): a seat with no gold income, under its cheapest unit's
  cost, with at least one combat unit, waves with what it has after the window (details in `studio/handoff.md`). Fixed when
  the three criterion rows and QA's sweep row end inside their bounds without a skip or a re-bound.

## Re-check fix round 1 (2026-11-08-1556, QA, fix 65524da)
Still open. Full non-Perf on a fresh clone of 65524da: 8,721 passed / 4 failed / 18 skipped of 8,743 (2 h 51 m); the 4 red
rows are this bug's family: `MatchCriterionTests.EachOtherMap_*(pale_hills)` ("no result in 36000 ticks"),
`MatchCriterionTests.EveryRow_InTwoProcesses_*(pale_hills)`, `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks(pale_hills)`
("no result in 30:00") and `Stress/HuntStressTests.FourAiSeats_OnPaleHills_*(seed 1, FFA)` ("no result by 144,000 ticks").
QA's probe (`Stress/StarvedWaveProbeQaStressTests`): shipped, over at **68,664**, seat 1 (was 71,660); with
`starvedWaveAfter` 0, no result at 72,000. At 36,000 seat 0 has 0 army, 29 workers, gold 46; seat 1 14 army, gold 16, wood
3,992. Seat 0 first stamped 26,560 (first starved wave 30,420), seat 1 26,960 (32,480). QA's sweep row Pale Hills 1 is now
green (ends inside 72,000, silent-seat oracle passes). The developer's fix round 1 measurements match mine.

## Developer notes, M5-9a round 2 (2026-11-08-1556; applied by the Producer at ACCEPT)
- **M5-9a round 2 (2026-11-08-1556, the developer): the starved wave built, the row still does not end inside 36,000.**
  `rules.json` `ai.starvedWaveAfter` (shipped 60 s, 0 = off) / `ai.starvedMinArmy` (1): a seat that can pay for no unit of
  its mix, knows no node of the short kind in reach, has no worker on it and has a free army unit is stamped; after the
  window it waves with every free unit whatever the threshold (docs/03 "Implementation (M5-9a)", round 2). On this row it
  fires as designed and **removes the stalemate, but not the slow end**: the match now ends at **71,660 (59:43), seat 1
  (Whirlwind) the winner**, the same tick at `starvedWaveAfter` 30, 60 and 120 (the first wave only the lever starts comes
  at 47,400, long after every window has run out, so the window does not matter on this row). The end-game shape, probed
  every 600 ticks (60 s window):
  - 27,360: seat 1 first stamped (gold 16, wood 292, 43 army); the wave at 28,800 is a threshold wave (41 members), the
    stamp re-set at 28,740; the 33,000 wave (20 units) too.
  - 34,800: seat 0 (Malazan) has **no army left** (gold 46, 29 workers, 20 buildings, defending from 34,200 to the end);
    seat 1 has 15 Desert Archers / priests, gold 16, and is stamped (never cleared from 28,740).
  - 36,000 (the bound): seat 0 17 buildings, 0 army; seat 1 15 army in a wave on a Malazan building. Decided, not over.
  - 46,200 the long wave ends idle (BUG-0598's rule); 47,400 **the first starved-only wave** (11 units, 24 half-pop, under
    the 14 pop / 861 threshold): it stays out to the end, razing seat 0's buildings one by one while seat 0 rebuilds with
    its wood (17 -> 10 buildings between 36,000 and 64,800; its last worker dies at 61,800); seat 0 defeated at 71,660.
  - So what is left is not this bug's stalemate but a **slow raze**: a ranged army of 11-15 archers against a base that
    keeps rebuilding (BUG-1116's "losing seats rebuild"). Before M5-9a the Whirlwind side kept melee Raiders and Zealots and
    razed the base by 34,219.
- Per the brief: measured at 30 / 60 / 120 and stopped there; **no re-bound, no skip**: `MatchCriterionTests` (both rows),
  `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks(pale_hills)` and QA's sweep row `pale_hills` seed 1 (which
  now ends inside its 72,000 at 71,660 but fails the silent-seat oracle: seat 0, razed with no army, sends nothing
  56,441-61,588, 5,147 ticks over the 4,800 limit) are left failing as measured. Also still failing, same family:
  `HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt(seed 1, FFA)` (no result by 144,000: three
  gold-starved seats, seat 2's 8 units in one wave from 72,000 to 144,000 against a 21-building base). Now green:
  QA's `RebuildLookMatchQaStressTests.FourAiSeats_*(pale_hills, 1)` (round 1's "nothing looked or rebuilt"). The Producer's call between BUG-1068 (a), a
  raze lever (e.g. a decided wave against a side with no army ignoring the rebuild), or data (D26: Normal's strength, a
  Line counter in the Whirlwind late mix).
- **M5-9a round 2, fix round 1 (2026-11-08-1556, the developer): still open. Pale Hills seed 1 Normal v Normal ends at
  68,664 (57:13), seat 1, outside 36,000.** BUG-1154's fix (the check reads production's pick; per-unit obtainability)
  stamps both seats earlier (seat 0 26,560, seat 1 26,960; first starved waves seat 0 30,420, seat 1 32,480) but the
  regular waves (the wave in progress and its cooldown) already limit when seat 1 can go, so the decisive fights land at
  the same time. End-game shape (probe every 1,000-4,000 ticks):
  - 28,000-35,000: seat 1's threshold wave at 28,740 and its starved one at 32,480 kill seat 0's army (12 Heavy Infantry,
    10 Crossbowmen, casters, a catapult) by 35,000; seat 0 sends its own last units as a starved wave at 30,420.
  - **36,000 (the bound), at every window:** seat 0 0 army, 29 laborers, 13-17 buildings (2 garrison keeps, barracks,
    engineers' yard, 10 billets...), gold 46; seat 1 10 Desert Archers, 2-3 priests, 1 zealot, a ram, gold 16, wood
    ~4,000, 26 workers. Every unit of both factions costs gold, and neither side has any gold income left.
  - 36,000-68,664: a slow raze by 7-10 archers of a base that rebuilds billets and the barracks / range with its wood;
    seat 0's last laborer dies at ~61,000, its last producer at 68,664.
  - `starvedWaveAfter` 30: **no result by 72,000** (seat 0 keeps 14+ laborers rebuilding); 60: 68,664; 120: 71,660.
- So no window of the starved wave can end this row inside 36,000: at the bound the side that lost is down to workers and
  a dozen buildings, and the winner's ranged remnant cannot raze them in time. Per the brief: measured and stopped; no
  re-bound, no skip. Still failing: `MatchCriterionTests.EachOtherMap_*(pale_hills)`, `EveryRow_InTwoProcesses_*(pale_hills)`,
  `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks(pale_hills)`, `HuntStressTests.FourAiSeats_OnPaleHills_*(seed 1, FFA)`
  (no result by 144,000). **Now green:** QA's sweep row Pale Hills 1 (ends 68,664, silent max 759 / 1,159: the silent-seat
  oracle passes). Levers left are outside the starved wave's design: BUG-1068 (a) (expansion reach, so gold lasts), a
  raze lever (a decided side's workers / target choice), or data (D26: Normal's strength, a Line counter in the Whirlwind
  late mix). The Producer's call.

## Producer triage, M5-9a round 2 (2026-11-08-1556 ACCEPT step): ESCALATE, with a default for the next sim session

- **Verified** (the Producer's scratch clone of 65524da / f32501b, rule 38): build 0 / 0; the targeted suites green; the Pale
  Hills seed 1 `EachOtherMap_*` row red ("no result in 36000 ticks"); QA's full run 8,721 / 4 / 18, the 4 red rows this
  bug's family. The developer's and QA's numbers agree (68,664; at 36,000 the loser has 0 army, 29 workers, 13-17 buildings;
  the winner 10-14 ranged units razing a base that rebuilds with 4,000 wood).
- **What this bug is now:** not a stalemate (round 2's starved wave ended that: knob 0 still gives no result at 72,000) but a
  **slow raze**, 32,664 ticks past the bound, with the same cause underneath as before: both sides are broke from 24:00
  because nothing in the AI reaches the far mines (candidate (a)). The window has no effect on the end (30 / 60 / 120 s: no
  result / 68,664 / 71,660), so no starved-wave tuning fixes it, as the developer and QA both say.
- **Not accepted with the bug open, not re-bounded in this session:** the brief said so and the rows are red. **Not a third
  round of the same task** ("the same task rejected twice" is a STOP): the remaining lever is a different change to a
  different system (the AI's expansion, BUG-1068's (a)), already on the end-of-M5 hardening list, and the owner was asked at
  M5-H5 (f) which they prefer (rule vs map). **The owner's choice is in STATE "Waiting on you"** (ship with an hour bound now /
  fix the expansion first / more gold on the map / drop the shift).
- **Default (producer_default; applied at the next sim PLAN if the inbox says nothing): M5-9b, candidate (a) on this branch,**
  then one re-measure of every pin, then land both. **Acceptance rule for that session (terminal, so this branch does not
  live a fourth session):** Pale Hills seed 1 Normal v Normal (a) ends inside 36,000: this bug is fixed; (b) ends between
  36,000 and 72,000 with every (a)-(e) check clean: the three 1v1 rows' Pale Hills bound becomes **72,000** (the FFA's hour,
  a Producer decision the owner may revisit, named in the constants' comments and docs/03's M5-5 table) and this bug goes to
  **S3** ("a slow end game on one map", a hardening item: a raze lever or map gold); (c) no result at 72,000: a finding, the
  branch stays held and the owner is asked again. The four-seat Pale Hills seed 1 FFA (`HuntStressTests`, no result by
  144,000) is not a criterion row: if (a) does not end it, it goes to `Known` under BUG-1068 / BUG-1116 with the measurement.