# BUG-0513: M4-H3 nits: test helper counts a Dropped event as a cast start, a loosened QA row, memory item off by 4.5 KB

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-2334, task M4-H3 (QA) |
| System | sim tests (`Abilities/AbilityScenes`, `QA/TowerGhostQaTests`, `QA/FieldBuildFairnessQaTests`) |
| Fixed by | |

## Repro / items
1. **`AbilityScenes.Had(sim, caster, resolved: false)` / `TickOf(..., resolved: false)` match a Dropped event as a start.**
   M4-H3 records a drop as `Resolved == false, Dropped == true` and docs/03 says "skip it where only starts are wanted", but
   the shared helper used by ~20 rows (`AbilitySystemTests`, `SelfAuraTests:174 Assert.False(Had(..., false))`,
   `AbilityQaTests:43`, `SummonTests`, `CusserQaTests`, ...) was not updated. No row fails today (none of them drops a cast),
   but a future row that drops one would read it as a start. Fix: `e.Resolved == resolved && !e.Dropped`.
2. **`TowerGhostQaTests.ARangedOrderOnARememberedBuildingUpACliff_EndsWithin2Minutes(gone: false)` was loosened to
   "order ended OR the Tent took a hit".** With the Tent alive and the ramp reaching it, BUG-0401's rule says the order must
   be kept and the Tent hit; the `||` also passes the old give-up behaviour, so the row no longer guards BUG-0401 in this
   layout. Fix: `gone` true -> the order ends; `gone` false -> the Tent takes a hit and the target is still held until then.
3. **Memory itemisation.** The new comment in `World_1024Map_CacheStays32_MemoryBounded` says "M4-H3, +16,408 + rounding
   (231,908,912 on its head)". Measured here, 3 pairs: base 3584a3a 231,896,872 / 231,896,896 / 231,897,088, head 231,908,912
   x 3: **+11,824 to +12,040 B**, i.e. the rounding is about -4.4 KB, not positive. The margin statement (11,088 B under
   231,920,000) is right. Fix: state the measured delta.

## Expected
Helpers and QA rows that keep guarding the rule they were written for; itemised numbers that match a measurement.

## Notes
None of these changes behaviour; grouped as one S4.
