# BUG-1390: The Teblor `standard` opening's tooltip says the first raid has Javelin Throwers; it is four Warriors

| Field | Value |
| --- | --- |
| Severity | S3 (player-facing text that misdescribes the AI; nothing crashes; the brief asked for the fixture's text as it was) |
| Status | open |
| Found | 2026-11-14-0616, task D31 (QA) |
| System | data: `game/data/factions/teblor/ai.json` `openings[0].description` (and `Content/Fixtures/teblor_standard_d29.json`, `docs/factions/teblor.md` openings row and Owner review row, which quote it) |
| Fixed by | |

## Repro
1. Read `game/data/factions/teblor/ai.json` `standard`: description "War Lodge and Javelin Ground first, then Age II;
   four Warriors raid at 8 army pop, with Javelin Throwers beside them."
2. Same file: `compositions` Age 1 is `{ "teblor_warrior": 45 }` only; Javelin Throwers are in the Age 2 composition.
3. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TeblorOpeningQaTests"`: on the generated map, seed 1,
   Normal, the first wave starts at 340-362 s and Age II lands at 464-489 s, so the first raid is sent in Age I, from
   the Warrior-only composition.

## Expected
docs/02 / CLAUDE.md rule 8: player-facing text says what the AI does. The description should not promise Javelin
Throwers beside the raiding Warriors (they arrive only after Age II, with Riders, Shamans, Breakers and War-dogs).

## Actual
The tooltip reads as if the 8-pop raid is Warriors plus Javelin Throwers. The old description ("...then Javelin
Throwers, Destrier Riders, Shamans, Breakers and War-dogs join in Age II") was accurate; V24's dropped the Age II mix.
The developer named this as a deviation; the brief required V24's text "description included", so the fix is a
Producer call (a wording change moves the data hash again, so it waits for the next hash move).

## Notes
- Suggested wording (the owner's taste): "War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8
  army pop, then Javelin Throwers, Destrier Riders, Shamans, Breakers and War-dogs join in Age II."
- Changing it touches `TeblorOpeningFixtureTests.TheShippedOpening_IsTheHandOver_PlusItsOwnShare025` (it requires the
  description to equal the fixture's) and the two teblor.md rows; both pins catch a one-sided edit.
