# BUG-0047: M1-4d-3 test-guard gaps (perf row unguarded, "first Idle" endings, loose chain check, 24 h replays)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-05-1609, task M1-4d-3 |
| System | tests (movement, replay) |
| Fixed by | 6d1cbfd (M1-9); see the M1-9 re-check below |

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

## Re-check round 1 (2026-10-05-1609, fix commit 6abd200): 3 of 4 done
- Item 2 done: the dev corridor tests tick until nothing moves and no walk-back is pending.
- Item 3 done: `ShoveQaTests.ChainReach` now requires each link ahead of the previous along the walker's
  push, a walker in reach of the first member, at most MaxChainShove - 1 links, and no member after the
  first touching an Idle enemy. Checked: a strict tightening of the round-0 checker (everything it accepts
  the old one accepted). It treats only Idle enemies as stopping a chain where the sim also stops at a
  standing Moving enemy, so it is still a little looser than the rule there.
- Item 4 done: `ReplayRecorder.ToReplay` throws past `Replay.MaxTickCount` (new `ReplayFormatTests` case).
- Item 1 open: the 2,500 tight blob row still has `enforce: false`.

## Re-check M1-9 (2026-10-06-0905, commit 6d1cbfd): fixed (item 1 was the last open item)
`CrowdPerfTests.TightBlob2500_OnePlayer_AverageTickAtMost4_5Ms` enforces the 4.5 ms target (Perf,
run alone): 4.52 ms on base 1f533aa (fails), 4.33 ms on 6d1cbfd.
