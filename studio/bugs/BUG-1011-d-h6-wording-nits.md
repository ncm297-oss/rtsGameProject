# BUG-1011: D-H6 wording nits (page cross-references, the "allowed" line, the 27-seeds claim)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-18-1236, task D-H6 (QA) |
| System | docs/factions/malazan.md, QA/Content/D21OpeningsQaTests.cs output and comments |
| Fixed by | |

## Repro
Read the lines below at 52e82c9.

## Expected
Cross-references point at text that exists; printed reasons name the right bug.

## Actual
1. `docs/factions/malazan.md` line 120: "BUG-0981's pattern (the missing reserve above)". The reserve paragraph is
   below it (line 131, "a reserve for a due step: the AI saves nothing for ...").
2. `D21OpeningsQaTests.cs` line 213 cites the malazan page's "Fast Age II can fall without attacking". No such text;
   the paragraph is "This opening can fall to a rush without ever attacking" and the section is "Fast Age II against
   the raider rush (BUG-0982, D-H6, 2026-10-18)".
3. `BothSidesWave` prints `allowed: player p (<opening>) was defeated without a wave (BUG-0982, kept by measurement)` for
   any side and opening. BUG-0982 is about `cadre_rush`; a `crossbow_push` or Whirlwind side defeated without a wave
   would be credited to a decision that never covered it (seed 25 of crossbow_push v raider_rush is one such case,
   BUG-1010).
4. The page's last line: "On the 27 measured pairing seeds, `standard` sent a wave every time." `standard` plays in 9
   of the 27 runs (3 Whirlwind openings x 3 seeds) per faction; say "on its 9 measured seeds" (true: no `standard`
   side sent zero waves in the report, second- or third-seed rows).
5. `KnownBeatenAlive`'s comment says "Reported to the Producer (a new bug)" without an id; BUG-1010 is it.

## Notes
All text-only; no behaviour changes.
