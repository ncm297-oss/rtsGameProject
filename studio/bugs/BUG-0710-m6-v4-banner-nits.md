# BUG-0710: M6-V4 nits: the HUD's K / L under the banner lag the stats by the killing tick; `ViewApi/MatchOver` is now dead code

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-1616, task M6-V4 (QA) |
| System | view: match-over banner (`game/scripts/Main.cs`, `SimRunner.cs`), HUD `ResourceBar` K / L line; `sim/Rts.Sim/ViewApi/MatchOver.cs` |
| Fixed by | |

## Repro
1. `& $env:GODOT --path game res://tests/MatchOverViewTest.tscn -- --shots <dir>` (windowed).
2. Look at `matchover-banner.png`, then `matchover-stats.png`.

## Expected
The numbers on screen agree while the banner is up: the HUD's kills / losses line under the semi-transparent banner and the
stats screen read the same sim state (the tick that ended the match).

## Actual
1. **HUD K / L one tick behind.** The banner shot shows `K 0 / L 0` in the HUD (top right, visible through the 45 % backdrop)
   under "Victory", while the stats screen opened from that banner says Kills 1 for player 0 and Losses 1 for player 1. The
   banner pauses the tree from inside the killing tick's `Ticked` handler (and `SimRunner` breaks its loop), so the
   `Pausable` HUD never runs its `_Process` for that tick; it shows the frame before the kill until Continue. The developer
   listed this as known ("the HUD K / L counters are a frame stale while paused"). It is cosmetic, but it is exactly the
   number the banner is about.
2. **`ViewApi/MatchOver` is unused.** Since M6-V4, `Match` reads `World.DefeatEvents` and nothing in `game/` or `Rts.Sim`
   calls `MatchOver`; it is kept only because the sim's `ViewApi/MatchOverTests` and QA's `QA/ViewApi/MatchOverQaTests` test
   it (docs/03 "Implementation (M6-V4)": "Delete it with those tests when QA agrees"). **QA agrees**: delete
   `sim/Rts.Sim/ViewApi/MatchOver.cs` with both test files in a later view item (QA's rows go with it; the behaviour they
   pinned is the sim's `DefeatEvents` rule now, covered by the M5-4b defeat tests and `MatchOverViewTest` / `QaM6V4Test`).

## Notes
Fix idea for 1: have the HUD read the sim when it is shown under the banner (a `Sync()` call from `Main.OnMatchEnded`, as
the pause path could do for any paused frame), or let `ResourceBar` run `Always` for its text only. Not a criterion of M6-V4.
