# BUG-1790: A side that falls after the draw reads Draw live but Defeat after loading a save taken after the fall

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-26-0207, task M6-V29 (QA) |
| System | HUD: match result (`Match.ResultFor`) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V29Test.tscn` (section "fall after draw"): a two-seat dev match on a
   data copy with `draw.afterSeconds` 20 and no starting gold draws at tick 401 (banner "Draw"); Continue watching; player 1
   razes player 0's hall, player 0 is out at about tick 555.
2. Live: `match.Outcome` stays Draw, one banner (correct). `Match.ResultFor(w, 0)` now returns Defeat.
3. Pause, Save, Quit to menu, Load that save: the banner reads **Defeat**. The scene prints
   `KNOWN BUG-1790: a save taken after the local player fell after the draw loads to Defeat ('Defeat'); live read Draw`.
4. The recording beside the same save, played back from the start, reads **Draw** (the latch fires on the draw tick, before
   the fall), so live and playback agree and only the load differs.

## Expected
The brief's ruling (M6-V29, docs/01 2026-11-26 (1)): a side standing at the draw gets Draw, "nobody is defeated by a draw",
"the same answer whether the result is latched live, read after loading a save, or reached in replay playback"; the QA focus
adds "the sim says a draw is final: the view must too".

## Actual
`ResultFor` computes `sideOut` from the current `IsDefeated` and returns Defeat for any drawn match where the side is out
now, without knowing whether it fell before or after the draw. A loaded match latches on its first tick after the load, so
a save taken after the fall reads Defeat.

## Notes
- Reachability: low. After the draw the AI seats stand down (`View.MatchOver`), so the local side has to lose its last unit
  and last production building afterwards (walking into a tower, a summon expiring, a human seat in a dev match), then save
  and load. The developer flagged it (docs/03 "Implementation (M6-V29)" Question 1).
- docs/03 Question 1 says "a recording watched past it reads Defeat"; measured, playback reads Draw (point 4). The doc
  sentence should be corrected whatever the Producer decides.
- A view-only fix covers the common case: with exactly two sides, a drawn match can only be drawn while both stand
  (`TwoSidesStand`), so a side that is out in a drawn two-sided match fell after the draw and should read Draw. Three or
  more sides need the sim's per-seat defeat tick (the developer's suggested request).
- The stats screen shows the fallen seat's "Defeated at" time (0:27 after a 0:20 draw); that is the sim's fact and names no
  winner, so it is not part of this bug.
