# BUG-0047: M1-4d-3 test-guard gaps (perf row unguarded, "first Idle" endings, loose chain check, 24 h replays)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-05-1609, task M1-4d-3 |
| System | tests (movement, replay) |
| Fixed by | |

## Repro / Actual (four small items)
1. **The 2,500 tight blob target is not guarded.** `Stress/LocalMovementStressTests+Serial.Perf_TightBlob_AvgAndWorstTick(2500, enforce: false)`
   only prints; the brief's "<= 4.5 ms" lives nowhere in the suite (see BUG-0044 for the measured miss).
2. **Corridor tests stop at the walker's first Idle tick.** `CrowdRoutingTests.ParkedPairInACorridor_YieldsAsAChain_ToABlockedWalker`
   and `QA/ShoveQaTests.WalkerInOneCellCorridor_PastAParkedFriendlyPair_Arrives` end their loop when
   the walker goes Idle. Since M1-4d-3 Idle is not final (pending walk-backs move units 20+ ticks later),
   so both pass while the final state is a failure (BUG-0042). Tests that judge an outcome should tick
   until nothing moves and no `WalkBack` is pending. (QA's own repro is left as is; the new QA test
   `WalkerPastAParkedPair_StillAtItsGoalOnceTheWalkBacksSettle` covers the settled state.)
3. **The shove legality checker was widened loosely.** The developer extended `QA/ShoveQaTests.CheckShoves`
   with `ChainReach` for chain shoves (an assertion change in a QA file, needed for the new rule). It
   accepts any Idle unit linked to a friendly walker by up to two touching friendly Idle units, in any
   direction; the rule only moves units *ahead along the push*, and never a member touching an enemy.
   A shove sideways or backwards through a chain would pass the checker.
4. **Replays longer than 24 h are written but refused on read.** `ReplayRecorder` caps the checkpoint
   interval at `Replay.MaxTickCount` but not the recorded tick count, so a match recorded past 1,728,000
   ticks produces a file `Replay.Validate` rejects (`InvalidHeader`). Outside any documented match
   length; noted so the CLI (M1-8) refuses or splits instead of silently writing an unplayable file.

## Expected
Each guard checks what its name and the brief say.

## Notes
None of these is a wrong game rule today; all four make a future regression easier to miss.
