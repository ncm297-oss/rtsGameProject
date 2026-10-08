# BUG-0142: M4-1 nits: CanPlace vs Build with an Attacking worker, slot scan phase decides in-reach duels

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
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
