# BUG-1165: A forged REGN save part loads: a last damage tick in the future, a regenerating total that contradicts its slots

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-09-2226, task M7-1 |
| System | save / load (`UnitStore.Save.cs`, `StatusStore.Save.cs`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~RegenQaTests.AForgedRegenPart_IsRefused"` with the two
   `Skip`s on its `future-tick` and `total-0` rows removed.
2. The test saves a regen-copy sim (a Heavy Infantry hit, carrying a regenerating status), forges one field of the unit
   store's `REGN` part, recomputes the checksum, and loads.

## Expected
`SaveError.Malformed`, as the accumulator row (`RegenAccum` = 400) already is. No build writes either value:
- `future-tick`: the unit's `LastDamagedTick` set to the save's tick + 5,000. The reader's own comment says
  "a fraction of a hit point, and a tick that has been", but its check is only `LastDamagedTick[i] >= -1`.
- `total-0`: `StatusStore.RegeneratingUnits` set to 0 while a slot's regenerating count is 1 (`ReadRegenFrom` checks each
  slot's count against its status count and the total against the capacity, but never the total against the slots).

## Actual
Both load (`SaveError.None`). The future tick holds that unit's faction regeneration off for 5,000 ticks more than any real
run would. The wrong total makes `RegenSystem`'s O(1) skip fire while a unit carries the status (in a match where no faction
regenerates the status then never heals). When the entry expires, `CountRegen` takes the total to -1, and a save made from
that sim is then refused on load (`ReadInt32In(0, ...)`). A per-slot count of 0 beside a real regenerating entry
underflows the byte to 255 on expiry, so `HasRegen` stays true for good.

## Notes
This needs a hand-edited, re-checksummed file, so it's only a robustness gap. The stealth counts (`_stealth`,
`StealthedUnits`) have had the same unchecked shape since M4-5, which is why this is S4. Fix both the same way: recompute the
derived counts from the entries on load (or check them against the entries), and check `LastDamagedTick <= tick` for live
units (`-1` for free ones).
