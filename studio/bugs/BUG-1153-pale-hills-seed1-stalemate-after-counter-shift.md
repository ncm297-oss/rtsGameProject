# BUG-1153: Pale Hills seed 1 Normal v Normal no longer ends once Normal shifts its mix (gold runs out, an archer army below its wave threshold)

| Field | Value |
| --- | --- |
| Severity | S2 (a pinned criterion row stops ending; QA focus 6 of M5-9a) |
| Status | open |
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
