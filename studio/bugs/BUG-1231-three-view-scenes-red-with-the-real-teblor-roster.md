# BUG-1231: Three view-owned scenes are red once the real Teblor roster is in the shipped data

| Field | Value |
| --- | --- |
| Severity | S3 (blocks D28's landing with BUG-1222: `main`'s scene loop would go red; nothing plays wrong) |
| Status | open (the view's next task, M7-V3b; D28 is held on its branch for it) |
| Found | 2026-11-11-1317 ACCEPT (Producer, the scene loop on the three-way merged scratch clone) |
| System | view tests: `game/tests/QaM7V3aTest`, `game/tests/ThreeFactionTest` (+ `ThirdFactionForge`), `game/tests/ReplayPlaybackTest` |
| Fixed by | |

## Repro
1. A scratch clone of `studio/2026-11-11-1317-sim` (de0a2a6) with `studio/2026-11-11-1317-view` (ceda412) and
   `studio/2026-11-11-1317-data` (5aa5e1c) merged in that order (copy the LFS assets in, or the import hangs: BUG-0860).
2. `powershell -File tools/qa/smoke.ps1` (PASS), then `powershell -File tools/qa/scene-loop.ps1`.

## Expected
The scene loop is green with a third faction in the shipped data (96 / 96 on sim + view without the data branch).

## Actual
`SCENE LOOP FAIL: 3 of 96: QaM7V3aTest, ReplayPlaybackTest, ThreeFactionTest`:
- `QaM7V3aTest`: "forged data failed to load: common/factions.json: order: 9 factions is above the maximum 8" (the scene
  forges six more factions on top of the shipped ones to reach `DataLimits.MaxFactions`; with three shipped it makes nine).
- `ThreeFactionTest`: "forged factions: malazan, whirlwind, teblor, third" and "the added third slot: count 3, faction 2
  (want 3, s mod factions)" (the forge appends a faction to whatever ships and the scene expects exactly three).
- `ReplayPlaybackTest`: "the fixture holds not three openings per faction" (`ThreeOpenings()` asserts every faction's
  `ai.json` has three openings; the Teblor ship one, `standard`).

## Notes
- The M7-V3a brief staged everything on "two shipped + one forged"; the real roster makes that three + one. Same family as
  BUG-1222 on the sim side.
- Means that fit: the forge cuts its copy to the first two factions of the order before adding its own (as the sim's
  `TestDataDir.CopyOfShippedTwoFactions` does), or counts from what ships (`MaxFactions - shipped`); `ReplayPlaybackTest`'s
  fixture reads the two factions it was written for (or "at least one opening, three for the factions that ship three").
- Proof: the scene loop green on a scratch clone with the data branch merged, and green on the branch itself.
