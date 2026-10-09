# BUG-0090: D1 building descriptions promise rules the data can't express yet; stale BuildingSlot comment

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-06-1744, task D1 |
| System | data (buildings.json), Rts.Sim.Data comments |
| Fixed by | comment item: b4b423c (M3-3) rewrote `BuildingSlot.cs:4`. Requirements item: 8bcca04 (D3) sets `requires` to match every "needs X" in the building descriptions, pinned by `BuildingContentTests.I_BUG0090_...` (QA 2026-10-07-1415 checked: dropping a `requires` or adding a "needs" sentence fails it); gating itself lands with M3-6. Forge item: consistent (techs `researchedAt: forge`). Tower sight item: M4-3a gave both towers `sight` 24 (every other building `rules.json` `buildingSight` 12); D7 put a Sight column on both faction pages pinned both ways to `BuildingDef.Sight` (`BuildingContentTests.J*`). Tower attack / detector item: M4-3b gave both towers `attack` and `detector` 16; D-H1 pins every description's "shoots" / "spots hidden enemies" to `BuildingDef.Attack` / `Detector` both ways (`BuildingContentTests.K*`); no text edit was needed |

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

## D-H1 (2026-10-09): last item closed
- `BuildingContentTests.K_BUG0090_ATowerThatSaysItShootsAndSpots_HasAttackAndDetector_BothWays`: for all 20 buildings, a
  description with "shoots" / "fires" needs `BuildingDef.Attack`, one that "spots" / "reveals" / "detects" hidden
  enemies or units needs `Detector` above 0, and a building with either field says so. The shipped tower sentences
  ("... spots hidden enemies and shoots at intruders; needs Age II.") already matched: no text edit, `data-hash` unchanged.
- `K2` (data mutation in a scratch `TestDataDir`: Watchtower `detector` removed, Lookout Tower `attack` removed, Tent given
  both) and `K3` (text mutation: each tower loses one phrase, Billet says it shoots, Tent says it spots hidden enemies)
  each fail naming building and field, e.g. `whirlwind_lookout_tower attack: description says it shoots, data has no attack`.
