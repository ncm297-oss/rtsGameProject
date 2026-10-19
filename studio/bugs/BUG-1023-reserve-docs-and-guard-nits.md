# BUG-1023: M5-4g nits: the docs explain the 4 remaining flags wrongly; a guarded step keeps the other kind banked; expand reserved for a dropped mine; criterion 2's twin run missing

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (items 1, 2, 5 fixed in fix round 1; items 3, 4 open) |
| Found | 2026-10-18-1814, task M5-4g (QA) |
| System | AI reserve (`BuildOrderExecutor.Reservable`, `AiPlayer.SetReserve`), docs/03 "Implementation (M5-4g)" |
| Fixed by | 57dfa22 (items 1, 2, 5) |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReservePairingsStressTests" --logger "console;verbosity=detailed"`:
each `ROW` prints BUG-0981's flags with, for an `expand`, `freeMine` (the executor's `ExpansionMine` finds one) and
`ungatedGold` (a seen gold node not marked dropped), and the reserve's longest hold.

## Items
1. **docs/03 "Implementation (M5-4g)", Measured:** "three on sides that know no gold node by then (the guard leaves them
   unreserved ...)". On the three (seed 5 and seed 9 `crossbow_push`, seed 13 Whirlwind `standard`) the output is
   `freeMine=False ungatedGold=False`: what leaves the expand unreserved is `Reservable`'s "free mine known" check
   (`ExpansionMine` < 0), not the no-node guard (the guard drops a kind from the reserve; it never un-reserves a step).
   "one (Whirlwind `standard`, seed 9) behind the house steps above it": a trace of that side shows steps 23 and 25 (after
   the expand in file order) reserved at 11:30-13:00, which only happens when the expand is not `Reservable` (no free mine
   then); the expand itself is reserved from 18:30 (275 / 275, short of wood), the houses only at 14:30 and 17:30-18:00.
   The counts (11 -> 4, all `expand`) are right; the reasons need rewording.
2. **A guarded-out kind leaves the step reserved on the other kind for good.** Hard v Hard seed 2, Malazan: step 22
   (a 150 / 200 Build) reserved as **150 / 0** from 9:20 to the defeat at 17:27 (wood guarded out: no gatherable wood),
   833 gold / 15 wood at the end. The step can't be paid without wood, so its 150 gold is banked "for nothing", the case
   `Reservable` exists to avoid. Small (150 gold), but it lasts minutes.
3. **An expand is reserved for a remembered mine its gatherers dropped.** `ExpansionMine` (and so `Reservable`) ignores
   `Board.NodeDropped`, while `KnowsNode` honours it: pair (1,0) seed 37 `crossbow_push` ends `freeMine=True
   ungatedGold=False` with 5 gold / 3,530 wood, the expand reserved as 0 / 275 for a mine the workers found gone. Pre-existing
   for the expand's target choice; the reserve now also banks wood for it.
4. **Criterion 2's twin run is absent.** The brief asks for "`reserveShift` 0 reproduces today's split exactly (a twin
   run hash-equal to the pre-change behaviour on the AI scenes in `AiSaveScenes`)". `ReserveTests.AShiftOfZero_IsExactlyGoldShare`
   proves the function (bit-equal share); no twin run exists, and as written none can pass, since the reserve changes
   production whatever the shift. The Producer should restate the item (e.g. shift 0 and a scene with no reservable step
   hash-equal to the base).

5. **docs/02 wording:** "`ai.reserveShift`, 0.25 of the workers' gold share" reads as a quarter of the share (0.6 x 0.25 =
   0.15); the rule adds or takes 0.25 from the share itself (0.6 -> 0.85 / 0.35, docs/03 and `AiPlayer.WantedGoldShare`).
   Suggest "0.25 more (or less) of the workers on gold".

## Notes
Group of small findings; none blocks on its own.

## Re-check (2026-10-18-1814, fix round 1, 57dfa22)
- Item 1 **fixed**: docs/03 "Measured" now gives the reasons correctly. Over the 27 runs, 12 of the 17 flags are `expand`
  steps: 7 with `freeMine=False` and 5 with `freeMine=True`, which matches the docs.
- Item 2 **fixed**: `BuildOrderExecutor.Fillable` does not reserve a step that is short of a kind no gatherer can reach.
  This is covered by the developer's 3-row no-node test, QA's `NoKnownWood_AStepShortOfWoodIsNotReserved_ProductionTrains`
  (renamed from `..._WoodDropsOutOfTheReserve_ProductionTrainsAboveTheGoldPart` because its expectation now says this),
  and the new `KnowsNodeAnswer_LastsOneThink_*`. Hard seed 2 Malazan is no longer reserved 150 / 0. It now holds
  150 / 200 (short of wood, wood reachable) from 13:16 to 20:00 instead.
- Item 3 **open**: `ExpansionMine` still ignores `Board.NodeDropped`.
- Item 4 **open**: this is the Producer's restatement of criterion 2. The code is unchanged.
- Item 5 **fixed**: docs/02 now reads "0.25 more, or less, of the workers on gold".
