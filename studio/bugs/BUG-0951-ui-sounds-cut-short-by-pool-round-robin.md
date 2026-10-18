# BUG-0951: In a brawl on screen, UI sounds (Alert, Ready, Command, BuildingDone) are cut off after ~0.2-0.7 s: the 8-player pool's round robin hands their player to later world plays

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-17-2344, task M6-V12 |
| System | view: `game/scripts/Sfx.cs` (`Play`, the player pool) |
| Fixed by | M6-V12 fix round 1 (9555c93): 8 world players + 6 UI players (`Sfx.WorldPlayers` / `UiPlayers`), a play takes an idle player of its range or the one ending first; regression rows `QaM6V12Test` brawl (`LastPlayer` / `PlayerStarts`) and QA's `PoolSplit` (test clock) |

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

## Re-verify (2026-10-17-2344, fix round 1, QA)
- `QaM6V12Test` brawl, 200 v 200 at 8x, four runs (one in the full loop): UI cut short **0 of 72, 0 of 23, 0 of 23,
  0 of 24** (was 25 / 26, 27 / 72, 23 / 71). UI started 74 / 74, 25 / 26 (+1 merged into the HUD's own Alert play in
  the same 50 ms), 25 / 26 (+1), 26 / 26. The world cap still holds: 0 windows of 5 plays in 100 ms.
- The developer changed this QA row's metric from "PoolSize later plays of any kind" (the old round robin's reach, no
  longer meaningful once the UI has its own players) to "a later start on the same player before the clip ends"
  (`LastPlayer`, `PlayerStarts`), and allowed a UI try to merge into the HUD's own play of the same event (at most a
  quarter). QA reviewed both: the new metric is the right one for the new pool, and a merge is the documented rate limit
  ("a burst is one sound"), not a drop. Not a loosening.
- QA's own row, independent of the brawl's timing (`QaM6V12Test.PoolSplit`, a test clock): an Alert then 2 s of world
  plays every 10 ms: 80 world plays, 0 on a UI player, the Alert's player reused 0 times while it sounded. Seven UI plays
  in 70 ms on 6 UI players: the seventh (BuildingPlaced) took player 8, whose Select (70 ms) ended first; the 1.2 s Alert
  on player 9 was not cut.
- By design a UI play can still cut another UI play when more than 6 UI clips sound at once (the one nearest its end
  goes); no realistic input reaches that.
