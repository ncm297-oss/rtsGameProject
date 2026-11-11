# BUG-1187: D-R36 nits: an unindented doc-comment line, a 170-character Malazan line, the War band description's unit list

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D-R37, verified by QA 2026-11-10-2116) |
| Found | 2026-11-10-1236, task D-R36 |
| System | Tests (comment), docs: `docs/factions/malazan.md`, `docs/factions/teblor.md` |
| Fixed by | c49e971 (D-R37): `D21OpeningsQaTests` line 310 indented; malazan.md line 417 split (108 characters); the War band description names "Javelin Throwers" and "War-dogs" (158 characters, the same in both tables) |

## Repro
1. `sim/Rts.Sim.Tests/QA/Content/D21OpeningsQaTests.cs` line 306 (the BUG-1183 edit): `/// 270 wood and the Whirlwind's
   Age II 10:34, ...` starts at column 0; its neighbours are indented four spaces inside the class.
2. `docs/factions/malazan.md` line 417 (the BUG-1182 re-wrap) is 170 characters ("help: at 16 or 14 army pop ... So the
   wave stays at 18"); the lines around it are wrapped at 120, and BUG-1183 was about the same kind of overlong line.
3. `docs/factions/teblor.md`, the `standard` opening's player-facing description (openings table and Owner review):
   "... then Shamans, Destrier Riders, war-dogs and Breakers join in Age II." It writes the unit lower-case ("war-dogs"; the
   `displayName` is "War-dog") and leaves out the Javelin Thrower, which is 15 of the Age II mix (more than the Shaman's or
   Breaker's 10) and has its own step (23).

## Expected
Comment indented like its block; the docs line wrapped at 120; the description naming the units the way their
`displayName` does and not skipping one of the larger shares (or saying "the rest of the clan").

## Actual
As above. Nothing fails; the description is still a proposal for the owner's review.

## Notes
Fold into D28 (the description is copied into `ai.json` there, so fix it before it ships).
