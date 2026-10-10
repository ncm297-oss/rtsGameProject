# BUG-0463: M4PlayableTest still fails now and then: the Telas caster dies on its cast tick (step 5), and a 61-tick swing window lets unhurt Zealot B tie Frenzied A (step 8)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-10-1314, task M4-V6d (QA re-check after fix round 1) |
| System | test scene `game/tests/M4PlayableTest.cs` (M4-V7 slice 1) |
| Fixed by | |

## Repro
1. `dotnet build RtsGame.sln`
2. Run `& $env:GODOT --headless --path game res://tests/M4PlayableTest.tscn` ten times in a row while
   `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` runs (QA used a loop script in its scratchpad). The commands land
   on whichever tick the frame clock reaches, so load changes the tick of every click.
3. QA saw 2 FAILs in 10 scene runs, both on seed 1, both in places the BUG-0460 fix did not touch:
   - run a-2: `M4 PLAYABLE TEST FAIL 5. Telas Fire on a Whirlwind blob (Tab, Q, click) (seed 1, tick 724): the caster took no cast (cast ability -1)`
     (replay `%APPDATA%\Godot\app_userdata\RtsGame\m4playable-seed1-fail-tick724.replay`)
   - run a-5: `M4 PLAYABLE TEST FAIL 8. a Frenzied Zealot swings faster (seed 1, tick 2620): over 61 ticks Zealot A (hp 23, Frenzied) swung 4 times, unhurt B 4`
     (replay `...\m4playable-seed1-fail-tick2620.replay`)

## Expected
Acceptance criteria 3 and 5: M4PlayableTest PASS on both seeds headless, and the scene loop green. The scene is a loop
gate, so it must pass every time the product is right.

## Actual
Two more timing-dependent script faults. QA replayed both saved replays in a bare sim with a throwaway xUnit probe
(deleted after use):

1. **Step 5: the caster dies on the cast's own tick.** The Telas Fire `UseAbility` for the Cadre Mage (unit 23, gen 1)
   is stamped tick 722. The mage is `Attacking` at (167.6, 128.7) from tick 717, and it is **dead at the end of tick 722**:
   the cast began and the slot was freed in the same tick. Two ticks later `CastOnBlob` checks
   `U.CastAbility[caster] == abilityIndex || Resolved()` (line 321) before it checks that the caster is alive, so the
   run fails with a misleading message ("took no cast") about a caster killed in the fight. Under load the click lands a
   few ticks later than usual, deeper into the fight. The walk-in/drop retry loop around it never runs, and there is
   no second caster to fall back on.
2. **Step 8: the 60-tick window is sometimes 61 ticks.** Probe swing ticks (a swing = `CooldownTicks` rising):
   A (Frenzied from 2559, 13-tick period) 2559, 2572, 2585, 2598, 2611; B (20-tick period) 2560, 2580, 2600, 2620.
   The script resets the counters on a frame with `Tick == 2559` and stops at the first frame with `Tick >= start + 60`.
   At 8x speed a frame advances several ticks, so it stopped at 2620 (61 ticks). Counted ticks 2560-2620 give A 4 and
   B 4, so `a > b` fails. For exactly 60 ticks (2560-2619) the count is A 4, B 3, a pass. In 60 ticks a 13-tick period
   gives at least 4 swings and a 20-tick period at most 3, so the check is sound only when the window is exactly 60
   ticks. In a 61-tick window both can be 4. Of the 16 duels that passed in QA's loop runs, 4 ran 61 ticks and passed
   only because of the phase.

## Notes
- Fix ideas (test code only): (1) check `U.IsAlive(caster)` first. On a caster death mid-step, either fail with a
  message that says so, or let the scene pick a fresh caster or blob the way a player would. Keeping the mage out of
  melee (cast from the edge) would also lower the chance. (2) Count swings in `OnTicked` only for ticks
  `start+1 .. start+60` (or the ticks `[start, start+60)`), however far the frame clock runs past the end. Or compare
  rates with a margin that holds for any window of length L (A >= floor(L/13), B <= ceil(L/20)).
- The BUG-0460 faults are fixed: no slot-reuse or rubble-sight failure in 18 seed-runs.
- The same "count over a frame-measured window" pattern may exist in other scenes. Worth a grep when fixing.
