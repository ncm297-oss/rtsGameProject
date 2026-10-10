# BUG-0370: Shift-queued casts on walking mages all go to the same mage; its cooldown drops every one after the first

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-0215, task M4-V6b |
| System | view: ability targeting (`AbilityCaster.PickCaster`, `SelectionController.AbilityOrder`) |
| Fixed by | |

## Repro
1. `dotnet build RtsGame.sln`
2. Remove the `Skip` from `ShiftQueuedCastQaTests.TwoShiftClicks_OnWalkingMages_GoToBothMages_BothCastsResolve`
   (`sim/Rts.Sim.Tests/QA/ViewApi/StatusFlashQaTests.cs`) and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TwoShiftClicks"`.
3. In game: select two ready Cadre Mages, give them a Move, then press Q and Shift-click twice on two points.

## Expected
M4-V6b (BUG-0342's fix, docs/02 hotkeys row): with Shift held, the ability stays armed "so several casts can be queued".
Two Shift clicks with two ready mages selected should queue one cast on each mage, and both should resolve.

## Actual
```
first click -> slot 0 (queue 1, cast -1), second click -> slot 0
Assert.NotEqual() Failure: Values are equal
```
A queued `UseAbility` behind a Move sits in the unit's order queue; `UnitStore.CastAbility` is not set until it pops.
`AbilityCaster.PickCaster(queued: true)` only skips a caster whose `CastAbility` is that ability, so the second click
picks the same (nearest) mage again. When the first cast resolves, the 25 s cooldown starts and the second queued
`UseAbility` is dropped at its pop (`OrderSystem`, "popped: dropped if on cooldown now"). The other mage never casts.
The row in `AbilityViewTest.ShiftTwoRow` passes only because its mages are idle (a queued order on an idle unit starts
right away, so `CastAbility` is set before the second click) and it ticks twice between the clicks.

The same root cause covers the gap the developer recorded in docs/03 "Implementation (M4-V6b)": two Shift clicks in the
same tick both pick the same mage.

## Notes
Suggested direction (developer's call): treat a caster as busy for the pick if its order queue already holds a
`UseAbility` of that ability (`QueueKind` / the queued type id), or remember the casters this armed session already sent
until the next tick. Shift-queuing casts while the army is moving is the common case for the feature, so the bug
shows in normal play: a second spell is silently lost while a ready mage idles.
