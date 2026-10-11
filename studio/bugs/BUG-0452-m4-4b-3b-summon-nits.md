# BUG-0452: M4-4b-3b summon nits: meaningless fields required or accepted on a spawn-only summon; a dropped death event is silent

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (items 1-3, M4-5; set by the Producer at the 1954 ACCEPT) |
| Found | 2026-10-10-1314, task M4-4b-3b |
| System | data loader (abilities), `World.RecordDeath` |
| Fixed by | 6223f19 (M4-5, sim branch): `DataLoader.Abilities.cs` `OnlySpawns` (`affects` optional, `own_units`), the radius refusal, `World.Deaths` doc + `Debug.Assert`; proof `Data/StealthLoaderTests.Bug0452_*`, QA-verified below |

## Repro
1. A `summon` whose only effects are `spawn` must still give `affects` (the dev report's deviation; docs/03 "For the data
   track (M4-4b-3b)": "`affects` ... as for every ability"), although nothing it does reads it.
2. The same summon with `"radius": 4` loads clean (`QA/SummonQaTests.EdgeSummons_Load`): the radius is ignored for
   placement (always 3 rings round the caster's cell), so a data author may think it widens the search.
3. `World.RecordDeath` drops an event silently once `DeathCount` reaches `UnitCapacity + BuildingCapacity`; the kill /
   loss still counts but a view never hears of the death (its node would linger). Reaching it needs one slot to die, be
   re-summoned and die again many times in one tick, so it is theoretical; no test reaches it.

## Expected
A field that has no effect is either optional (1) or refused with a message saying why (2), as the loader does for every
other unused field. A dropped event (3) is documented where a view reads it (`World.Deaths`), not only in
`RecordDeath`'s remarks, or the list is sized so it can't happen.

## Actual
As above.

## Notes
None of these is reachable in shipped data (no summon ships before Shadow, M8).

## Producer triage (2026-10-10-1314 ACCEPT)
S4, end-of-M4 sim hardening (or with M4-5 if the loader is touched): (1) `affects` optional on a summon whose effects are
all `spawn` (default `own_units`, documented); (2) `radius` refused on such a summon ("a summon with spawn effects only has
no radius"); (3) the dropped event documented at `World.Deaths` (the view's read) and a `Debug.Assert` in `RecordDeath`.
docs/03 "For the data track (M4-4b-3b)" updated with the fix.

## QA verification (2026-10-10-1954, M4-5, commit 6223f19)
Items 1-3 done as triaged; status left for the Producer to set at ACCEPT.
1. `affects` optional on a spawn-only summon (`own_units` when absent): `DataLoader.Abilities.cs` `OnlySpawns`;
   `Data/StealthLoaderTests.Bug0452_ASpawnOnlySummon_NeedsNoAffects_AndDefaultsToOwnUnits` green; a summon with another
   effect still needs it (`Bug0452_ASummonWithAnotherEffect_StillNeedsAffectsAndRadius`).
2. `radius` refused on a spawn-only summon at `abilities[k].radius`: `Bug0452_ASpawnOnlySummon_RefusesARadius` green;
   `AbilityLoaderTests` line 197 and `QA/SummonQaTests.EdgeSummons_Load` changed to match (the radius line removed, the
   rest of the edge row kept).
3. Documented at `World.Deaths` and a `Debug.Assert` in `RecordDeath` (docs/03 updated).
