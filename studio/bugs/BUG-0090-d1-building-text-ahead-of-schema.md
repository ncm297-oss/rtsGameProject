# BUG-0090: D1 building descriptions promise rules the data can't express yet; stale BuildingSlot comment

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-06-1744, task D1 |
| System | data (buildings.json), Rts.Sim.Data comments |
| Fixed by | |

## Repro
1. Read `game/data/factions/*/buildings.json` (D1) and `sim/Rts.Sim/Data/BuildingSlot.cs:4`.

## Expected
Player-facing text matches what the data defines, or the gap is tracked; code comments describe the shipped data.

## Actual
Three small related items, none playable yet (buildings can't be placed until M3-3):
- `malazan_watchtower` / `whirlwind_lookout_tower` say the tower "spots hidden enemies and shoots at intruders"
  but the schema has no attack, sight or detector fields, so a placed tower would do neither.
- Requirements live only in text ("needs a Legion Barracks", "needs Age II"); there is no `requires` field, so
  nothing enforces them, and the text must be kept in sync by hand once M3-5 / M3-6 add `requires`.
- The Forge descriptions (`malazan_armory`, `whirlwind_smithy`) place the faction upgrade (Moranth Supply /
  Dryjhna's Prophecy) at the Forge. docs/02 lists the faction upgrade in the Forge table, so this is consistent,
  but the research location is still a schema question for M3-5.
- `sim/Rts.Sim/Data/BuildingSlot.cs:4` remarks "M3-2 ships the Town Hall only", stale after D1 (sim-owned file).

## Notes
Recheck the three description groups when M3-5 (techs / `requires`) and the tower attack fields land; fix the
comment in the next sim-track change touching `Rts.Sim/Data`.
