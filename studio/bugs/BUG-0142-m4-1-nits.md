# BUG-0142: M4-1 nits: CanPlace vs Build with an Attacking worker, slot scan phase decides in-reach duels

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open: items 3-4 fixed in eace378 (`SimConfig.Combat` summary rewritten; retaliation compares `Ignored` only when it is a unit, `CombatTests.HitByAUnitWithTheHandleOfAGivenUpBuilding_StillRetaliates`); items 1-2 open |
| Found | 2026-10-07-2014, task M4-1 |
| System | construction placement rule, combat scan stagger |
| Fixed by | |

## Repro
1. `CanPlace_AgreesWithBuild_WhenTheIssuingWorkerIsAttackingInsideTheFootprint` (`QA/CombatQaTests`): the player's
   own Laborer stands `Attacking` inside a Billet footprint; `World.CanPlace` answers `UnitInTheWay` (the ghost reads
   red), the Build given to that same worker places the site. Same shape as BUG-0092's holding-worker case: `Check`
   skips the issuing worker, `CanPlace` cannot know which worker will build. The developer noted it too.
2. `Report_IdleDuelInReach_SlotScanPhaseDecidesTheWinner` (`QA/CombatQaTests`, report only): two equal Heavy
   Infantry spawned already in reach, Idle. In slots 0 v 1, 0 v 2, 0 v 3 the first to scan wins with 4 hp left; in
   slots 0 v 4 (same scan phase) both die. Attack-moved duels are symmetric in every slot order (asserted in
   `AttackMoveDuel_AnySlotOrder_BothDieOnTheSameTick_TheSameTickInEveryArrangement`), because they engage on reach in
   phase 10, not on a scan.

## Expected
1. The ghost and the order agree (docs/03: `CanPlace` is "the rule `Command.Build` applies").
2. A fair fight is decided by the units, not their slot numbers (item 2 is a consequence of the brief's staggered scan;
   recorded for the Producer, not necessarily a fix).

## Actual
See repro.

## Notes
Item 2 fix idea if wanted: let a unit that is hit while Idle start its swing on the same tick its attacker's hit
lands, or let an in-reach enemy be picked up by the phase-10 reach check rather than waiting for a scan.

## Re-check 2026-10-07-2315 (QA, fix round 1)
Item 1: still open (the developer documented it as the BUG-0092 shape); row stays skipped. Item 2 unchanged. Two more
nits from the fix round:

3. `SimConfig.Combat`'s `///` summary is stale: it says "pending the Producer's call" (decided 2026-10-07, docs/03
   "Combat switch") and "a replay always plays back with combat on", while `ReplayPlayer.Run(replay, data, combat)` now
   lets a caller play one back with combat off.
4. `CombatSystem` retaliation compares `u.Ignored[v] != hit.Attacker` without `IgnoredIsBuilding`: when the ignored
   target is a building whose (index, generation) equals the attacking unit's handle, the hit starts no retaliation.
   Harmless (a melee attacker is in reach and the next scan takes it, at most 4 ticks later), but the scan code
   checks the building flag and this does not.

