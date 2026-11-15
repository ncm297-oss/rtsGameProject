# BUG-1390: The Teblor `standard` opening's tooltip says the first raid has Javelin Throwers; it is four Warriors

| Field | Value |
| --- | --- |
| Severity | S3 (player-facing text that misdescribes the AI; nothing crashes; the brief asked for the fixture's text as it was) |
| Status | fixed (D32, QA verified 2026-11-14-1246) |
| Found | 2026-11-14-0616, task D31 (QA) |
| System | data: `game/data/factions/teblor/ai.json` `openings[0].description` (and `Content/Fixtures/teblor_standard_d29.json`, `docs/factions/teblor.md` openings row and Owner review row, which quote it) |
| Fixed by | D32 (25aa174): description "War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then Javelin Throwers and Shamans join them and a second Longhouse goes up." in `ai.json`, `Content/Fixtures/teblor_standard_d32.json` and both teblor.md rows; pinned by `TeblorOpeningFixtureTests.TheShippedOpening_IsTheD32Fixture`, `TeblorRosterPinTests.TheOwnerReviewTable_IsTheFilesWords_CharacterForCharacter_BothWays`, `AiContentTests.A_EachPage_IsItsAiFile_BothWays` and QA's `QA/Content/D32DescriptionQaTests` (each claim backed by a step) |

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

## Verification (D32 QA, 2026-11-14-1246)
- The first raid is still four Warriors from the Warrior-only Age I mix (wave 16 half-pop, Warrior 4 half-pop:
  `D32DescriptionQaTests.FourWarriorsRaidAt8ArmyPop_IsTheWaveOverAWarriorOnlyAgeIMix`); the text no longer puts Javelin
  Throwers beside it. The developer narrowed the brief's sentence (no Breakers, Destrier Riders or War-dogs) because none
  is fielded by 15:00 on the Normal row; QA's own harness agrees (0 / 0 / 0 of 64).
- One-sided edits: `ai.json` description only (3 classes red), fixture only (2), the page's opening row only (2), the
  Owner review row only (3). How often the remaining claims hold in play: BUG-1421 item 1.
