# BUG-0871: docs/02 AI opponent says a wave gives defenders "as long as the wave keeps the size it was sent at"; docs/03 M5-4f says its start threshold

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-17-0056, task D20 (fix round 1; the dev's note, confirmed by QA) |
| System | docs (02 game design, AI opponent) |
| Fixed by | The Producer at the 2026-10-17-0056 ACCEPT (commit 547ac44): docs/02 "AI opponent" line 398 now reads "as long as the members left keep the wave's start threshold (`attack.minArmyPop` / `minArmyCost` ...)"; verified on `main` 4d5af86 at the 2026-10-17-0554 PLAN |

## Repro
Read `docs/02-game-design.md`, "AI opponent", the bullet starting "The defense is sized by the threat" (lines 396-399):
"then from its attack wave's units nearest home as long as the wave keeps the size it was sent at".

## Expected
docs/03 "Implementation (M5-4f)", "Who answers" (b): wave members, nearest the hall first, "each only if the members left
keep the wave's **start threshold** (`attack.minArmyPop` half-pop and `attack.minArmyCost`, each times
`waveSizeMultiplier`)". A wave sent above its threshold gives its surplus. The faction pages now say this (BUG-0870,
fixed in d7201bf), so docs/02 is the one page left with the old wording.

## Actual
Read literally no member can ever be pulled (any pull shrinks the wave below the size it was sent at). "Nearest home"
also blurs the two candidate orders (home army nearest the defense point, wave members nearest the hall).

## Notes
- Outside the data track's files (docs/02 is the Producer's / sim track's), so not fixed in D20.
- The dev also named `docs/01-vision.md` line ~191. QA checked it: that row already says "as long as the wave keeps its
  start threshold", which is right. Only its "nearest home" is loose, and as a decision-log summary that is acceptable.
  Not part of this bug.
- Suggested fix, one phrase: "as long as the members left keep the wave's start threshold (`attack.minArmyPop` /
  `minArmyCost`)".
