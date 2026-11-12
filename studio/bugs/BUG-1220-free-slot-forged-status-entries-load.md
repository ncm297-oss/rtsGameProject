# BUG-1220: A forged save whose free unit slot carries status entries (a regenerating one included) loads

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-11-1317, task M7-H1 (QA full) |
| System | save format (status part of the unit store, `StatusStore.ReadFrom`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M7H1QaTests.AFreeSlot_CarryingAConsistentForgedRegeneratingStatus_IsRefused"`
   (skipped with this id; remove the `Skip` to run it).
2. The row saves `M7H1QaTests.Scene()` at tick 40, then on a never-used free slot writes a whole, self-consistent
   regenerating status: the status part's `Count` 1, `StatusId` = `regenerating`, magnitude 100, 10,000 ticks left,
   `UnitsWithStatuses` + 1, and the `REGN` part's slot count 1 and total + 1, then recomputes the checksum.

## Expected
`SaveError.Malformed`. Freeing a slot clears its statuses (`UnitStore.Free` → `StatusStore.Clear`), so no build writes a free
slot with entries; M7-H1's `REGN` checks ("a free slot's is -1 with an accumulator of 0") stop at the part's own fields.

## Actual
`SaveError.None`. After 600 ticks: the slot is still free, still holds 1 status entry, and `RegeneratingUnits` stays 1 after
every honest status has expired, so `RegenSystem`'s O(1) skip never fires again (the very symptom BUG-1165 named for a forged
total). The entry is cleared when a unit is next spawned into the slot (`regen False, statuses 0, total 0`), so nothing heals
from it; no crash.

## Notes
- The `REGN` checks (BUG-1165) are right as far as they go: each slot's count equals its entries and the total the slots with
  one. The gap is in the status part, which checks each entry's id but not that a free slot has `Count == 0` (the same
  family as the stealth counts the M7-H1 developer noted in docs/03 as "not checked yet"). One fix covers both: in
  `UnitStore.ReadFrom`'s free-slot branch, also require `Statuses.Count[i] == 0`, and check `_stealth` / `_blind` /
  `StealthedUnits` / `BlindedUnits` / `UnitsWithStatuses` against the entries as `ReadRegenFrom` now does.
- Forged-save only; the save is a local file the player owns. S4.
