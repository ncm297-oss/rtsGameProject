# BUG-0452: M4-4b-3b summon nits: meaningless fields required or accepted on a spawn-only summon; a dropped death event is silent

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-1314, task M4-4b-3b |
| System | data loader (abilities), `World.RecordDeath` |
| Fixed by | |

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
