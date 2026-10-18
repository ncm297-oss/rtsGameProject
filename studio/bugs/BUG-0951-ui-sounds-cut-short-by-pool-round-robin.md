# BUG-0951: In a brawl on screen, UI sounds (Alert, Ready, Command, BuildingDone) are cut off after ~0.2-0.7 s: the 8-player pool's round robin hands their player to later world plays

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-2344, task M6-V12 |
| System | view: `game/scripts/Sfx.cs` (`Play`, the player pool) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V12Test.tscn` (QA, 200 v 200 at 8x, camera on the fight; a UI
   event every 400 ms in turn: Alert, Ready, Command, BuildingDone).
2. Two runs:
```
brawl 200 v 200 at 8x: 300 deaths by tick 1685 in 10.5 s; world plays 316 (30.2 / s), capped 157; ... 5 plays inside 100 ms: 0
  (tightest 5-span 100.1 ms); UI tried 26, started 26, cut short 25 of 26 [[Alert, 7], [Ready, 7], [Command, 5], [BuildingDone, 6]]
brawl 200 v 200 at 8x: 289 deaths by tick 4810 in 30.0 s; world plays 341 (11.4 / s), ... UI tried 74, started 74, cut short 27 of 72
```
"Cut short" = `Sfx.PoolSize` (8) later plays started before the UI clip's own length had passed, so the round robin
(`_pool[_next]`, `_next = (_next + 1) % PoolSize`) re-used its player and `player.Stream = ...; Play()` stopped it.

## Expected
Brief / docs/02: "your own cues (select, order, refused, building placed or finished, unit or tech ready, alert) always
play". The world cap allows 4 world starts per 100 ms (up to 40 / s), so a pool of 8 cycles in as little as 0.2 s.

## Actual
Every UI play starts (criterion 3's "never dropped by the cap" holds), but the `Alert` clip (1.2 s), `BuildingDone`
(0.98 s), `Ready` (0.42 / 0.76 s) and `Command` (0.32 s) are cut part-way whenever the fight on screen is busy, which
is exactly when the alert matters. The long world clips (`Cast` 1.4 s, `BuildingRazed` 1.12 s) are cut the same way.

## Notes
Fix ideas (the developer's call): reserve players for UI events (e.g. 2 of the pool), or pick a player that is not
playing (or the one with the least time left) instead of the strict round robin, and never steal a UI event's player
for a world play. `SfxTest`'s brawl row checks only `Play`'s return value, which is why it passes. The QA row
(`QaM6V12Test`, "BUG-0951: ...") stays red until fixed.
