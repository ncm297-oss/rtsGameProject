# BUG-0952: The alert's 10 s suppression runs on frame time, so how often a raid re-alerts depends on game speed, and a replay watched at another speed shows different alerts (docs/03 says the same)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-17-2344, task M6-V12 |
| System | view: `game/scripts/AlertHud.cs` (`_Process` -> `Step(w, (float)delta)`), `sim/Rts.Sim/ViewApi/AttackAlerts.cs` (`Now` = sum of `dt`) |
| Fixed by | M6-V12 fix round 1 (9555c93): `AlertHud` steps the alerts from `SimRunner.Ticked` with `AttackAlerts.SetNow(tick x 0.05 s)`; `AlertTest` now runs the HUD's own path, live at 4x / 2x and the replay at 8x; `AttackAlertsTests` 1 / 3 / 8 ticks per frame |

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

## Re-verify (2026-10-17-2344, fix round 1, QA)
- `AlertTest` (full loop): live alerts at ticks 244 / 454 / 664 (live run at 4x then 2x), the replay at 8x the same
  three at the same ticks, kinds and positions. The scene no longer disables `_Process` or feeds its own dt.
- QA's `AttackAlertsQaTests.TickClock_HitEveryTick_ReAlertsEvery200Ticks_Or201FromFloatRounding` (start ticks 0,
  72,000, 288,000): a unit hit every tick, driven as the HUD does (SetNow then a dt-0 step), re-alerts every 200 ticks;
  the gap is 201 on some ticks (the float clock: `(t + 200) x 0.05f - t x 0.05f` can round just under 10 s; 2 of 19 gaps
  from tick 0, 0 of 19 from 72,000 and from 288,000), never 199 or 202+. Deterministic (a pure function of the tick), so replays agree; docs/03
  says "200 ticks". Noted in BUG-0954, not a defect.
