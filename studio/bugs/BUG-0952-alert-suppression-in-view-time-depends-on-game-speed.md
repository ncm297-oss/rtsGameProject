# BUG-0952: The alert's 10 s suppression runs on frame time, so how often a raid re-alerts depends on game speed, and a replay watched at another speed shows different alerts (docs/03 says the same)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-2344, task M6-V12 |
| System | view: `game/scripts/AlertHud.cs` (`_Process` -> `Step(w, (float)delta)`), `sim/Rts.Sim/ViewApi/AttackAlerts.cs` (`Now` = sum of `dt`) |
| Fixed by | |

## Repro
`AttackAlerts` with one own unit hit on every tick for 600 ticks (30 s of game time), stepped once per tick:
- 1x (`dt` = 0.05 s per tick): `Now` reaches 30 s -> 3 alerts (0, 10, 20 s).
- 8x (`dt` = 0.00625 s per tick of frame time): `Now` reaches 3.75 s -> 1 alert.
- 0.25x: 12 alerts.
In the game `AlertHud._Process` passes the frame's real `delta`, so this is what the player sees: at 8x a base under
steady attack re-alerts once per 80 s of game time; at 0.25x every 2.5 s. A replay (Load Game -> Watch) at 8x of a
match played at 1x raises fewer alerts, at other ticks.

## Expected
Brief criterion 4: "a replay of the same match shows the same alerts"; docs/03 "Implementation (M6-V12)": "Replays:
the local player is seat 0, the same code runs, so playback shows the same alerts at the same ticks." docs/02 HUD:
"at most one per 30 m per 10 s" (a player reads this as game seconds).

## Actual
Only true when frames line up with ticks the same way in both runs at the same speed. `AlertTest` proves the replay row by
turning `AlertHud`'s own `_Process` off and calling `Step(w, 0.1f)` from `SimRunner.Ticked` (a fixed 0.1 s per tick),
which is not the path the game runs, so the scene can't see the difference.

## Notes
The brief did say "10 s of view time", so this may be an intended reading; the docs' replay claim is wrong either way.
Simplest fix: step the alerts from `SimRunner.Ticked` with the tick length (`1 / 20` s of game time), i.e. suppression in
game time (and the frame-time hit detection becomes per tick, which also makes live and replay identical). The text's 4 s
and the ping's 3 s can stay in frame time. Producer / owner call on which time the 10 s is.
