# BUG-1048: A seat whose scout dies before it looks near home sends its wood gatherers 300-455 m away (BUG-1045 again)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-05-0805, task M6-S5 (QA) |
| System | AI economy / scout (`AiPlayer.InGatherReach` fallback at `ScoutPhase` 2), M6-S5 BUG-1045 fix |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherReachQaTests.TheScoutDiesBeforeTheSweepEnds"`
   (`TheScoutDiesBeforeTheSweepEnds_TheSeatStillGathersWood_By4000` is skipped with this bug's id; remove the `Skip`).
2. The scene: BUG-1045's repro, `AiTeamTests.MirrorAllies(1)` (mirror-start 2v2, seed 1). Seat 3's scout is killed on
   the first tick it is out (tick 301, `ScoutPhase` 1), through the normal death path.

## Expected
BUG-1045's bar: the seat gathers wood near home (some wood by tick 4,000). Its own forest is 17.5 m from its hall.

## Actual
The scout's death sets `ScoutPhase` 2, so `InGatherReach` lets every known node through (the documented fallback,
docs/02 "AI opponent": "a seat whose scout dies early falls back on any node it knows"). Seat 3 knows only its ally's
forests: at tick 4,000 its nearest *known* forest is 298 m away, its 5 wood gatherers are on forests up to 455 m from
its hall, and it has gathered **0 wood** (730 gold). That is BUG-1045's symptom exactly. The seat does not idle (the
fallback works as written), so `TheScoutDiesBeforeTheSweepEnds_EveryKnownNodeIsInReach_AndTheSeatSendsWorkersToWood`
passes and pins the documented behaviour.

## Notes
- S3, not S2: docs/02 and the brief call the fallback intended, and the fix's criteria hold. But in a team game a
  scout lost early (an enemy raider at home, an early rush) is ordinary, and with an ally's vision the fallback walks
  the seat's wood economy across the map for the rest of the game; the reach table shows every start has a forest
  within 21 m that it simply never looked at.
- A possible fix (Producer's call): with the scout dead, keep the reach while any node of the kind is known within it,
  and send a gatherer (or the next free worker) to look at the head ring when none is; or replace a scout that dies
  before the head ring is done.
- Related: BUG-1045, BUG-1047.

## Producer triage (2026-11-05-0805 ACCEPT)
S3 stands; the fallback is as documented, but in a 2v2 an early-lost scout is ordinary. **Decision:** the same rule as BUG-1047's: a
dead scout does not lift the reach by itself; the reach lifts when the head ring is visited (by any own unit's sight), and until then a
kind with no node within reach takes the nearest known node of that kind. A seat whose scout died before the head ring was seen keeps
gathering near home from what its hall sees and its gatherers' walks reveal. Fix with BUG-1047 (M6-SH1 or the map-format task,
whichever comes first); un-skip `TheScoutDiesBeforeTheSweepEnds_TheSeatStillGathersWood_By4000` with it.
