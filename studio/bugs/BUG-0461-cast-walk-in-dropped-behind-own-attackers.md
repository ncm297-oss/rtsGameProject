# BUG-0461: A cast's walk-in hemmed in by own attacking units gives up after 1 s and drops the cast; on flat ground the caster vibrates in place until then

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-1314, task M4-V6d (QA, confirming the developer's "seen for the sim track" notes in docs/03 "Implementation (M4-V7)") |
| System | abilities walk-in (`AbilitySystem.Run`) + local movement give-up (`MovementSystem`, `GiveUpTicks` = 20) |
| Fixed by | |

## Repro
Replays saved by the M4 sandbox while it was being written, in this machine's Godot user data folder
(`%APPDATA%\Godot\app_userdata\RtsGame\`), played into a bare sim with the shipped data at commit 3f86bd7 (they refuse
once the data hash changes):
1. `m4playable-seed6-fail-tick1000.replay`: the own Priest of the Whirlwind (slot 24) gets `UseAbility` Sandstorm at
   (203, 123) on tick 969, from (186.1, 111.9) on level 1. Its walk to the point (level 0, passable, field cost 12.8)
   leads east along a strip to the only ramp down, three cells away (cells x 95-97 between rows 56 and 57); own
   Heavy Infantry and Crossbowmen in `Attacking` stand at 0.8-2.5 m round it on that ramp top. It walks 1.1 m in
   9 ticks, then counts `StuckTicks` 1 → 19 standing still (tick 979-998), goes Idle on tick 999 and the cast is
   dropped on tick 1000, 19.2 m from the point (range 18).
2. `m4playable-seed6-fail-tick2185.replay`: the Sapper (slot 26) at (189.2, 112.0) on the same strip, 7 own units in
   `Attacking` / Idle within 2.5 m; a Cusser at (208, 120) on tick 2164: it never moves (velocity 0), stuck 1 → 19,
   Idle on tick 2184, cast dropped on tick 2185. This is also the developer's "Sapper on a rise at about (187, 111)
   took no step on eight Move orders".
3. `m4playable-seed1-fail-tick7001.replay`: the Sapper (slot 26) on flat level-0 ground at (194.5, 122.4), three own
   units `Attacking` at 1.5-2.0 m; Cusser at (226, 122) on tick 1643. From tick 1644 to 1662 its position flips every
   tick between (194.557, 122.360) and (194.514, 122.524), velocity (0.043, -0.164) then (-0.043, +0.164): a 2-tick
   limit cycle of 0.17 m. The same cycle ran before the cast (an earlier Retaliate chase, ticks 1640-1643).
   `StuckTicks` reaches 19, it goes Idle on tick 1663, and the cast is dropped on tick 1664, 31.5 m short (range 6).

## Expected
docs/02 "Ability system": "the player selects the ability (hotkey or button), then clicks a target"; docs/03 M4-4a: a
caster out of range walks into range, and the cast is dropped "if it stops walking first (arrived as near as the ground
allows, stuck)". A caster held up by its own army in a fight at a ramp is not "as near as the ground allows". A player
would expect it to wait, path round, or squeeze through, and at least not to vibrate in place.

## Actual
The walk-in uses the plain Move give-up (20 stuck ticks = 1 s). Own units in `Attacking` are never shoved (docs/03 "Who
is shoved: an Idle unit"), so a caster behind them gives up after 1 s and the click is silently lost: no cast, no
cooldown, no feedback. On flat ground in case 3 the walker also jitters 0.17 m each tick for the whole second, which a
player would see.

## Notes
- The M4 sandbox works round it: it walks the Sapper with plain Moves, re-clicks dropped casts (at most twice) and
  pulls the army back first (docs/03 "Implementation (M4-V7)", "What a player would also do").
- Producer's call whether this is the movement rule (shove or swap with own `Attacking` units, BUG-0035 / 0037 family)
  or the cast rule (a longer patience for a cast's walk-in, or feedback when a cast is dropped). Case 3's limit cycle
  looks like a movement bug in its own right (the oscillation also showed during a chase, before any cast).
