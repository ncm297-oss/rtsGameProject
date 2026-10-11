# BUG-0560: M4PlayableTest step 5 can wait out the tick budget: a Telas Fire that burns no one is recast after its cooldown, and if the fight has ended by then `CastOnBlob` waits for a Whirlwind unit that no longer exists

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (verified by QA 2026-10-11-0634) |
| Found | 2026-10-11-0314, task D13 (QA, the data branch's M4PlayableTest gate) |
| System | test scene `game/tests/M4PlayableTest.cs` (view track), step 5 / `CastOnBlob` |
| Fixed by | 0d58553 (M6-V1): step 5 done on the cast's resolve, `CastOnBlob` fails fast when `_enemyArmy` is all dead, step 8 asserts the Burning marker on staged Zealot A (none on B) |

## Repro
1. `dotnet build RtsGame.sln` on the D13 data branch (`f740736`).
2. Run `powershell -File tools/qa/scene-loop.ps1 -Filter M4PlayableTest` while the machine is loaded (QA had its own
   non-Perf `dotnet test` and the other tracks' suites running). Under load the clicks land on whichever tick the frame clock
   reaches (as in BUG-0460 / BUG-0463).
3. 1 FAIL in 12 QA seed-runs (8 seed 1, 4 seed 6), on seed 1:
   `M4 PLAYABLE TEST FAIL 5. Telas Fire on a Whirlwind blob (Tab, Q, click) (seed 1, tick 5502): a visible Whirlwind unit within 40 m of a Cadre Mage for Telas Fire: not by tick 5500`
4. The run's replay is checked in next to this file: `BUG-0560-m4-playable-seed1-telas-fire-no-enemy-left.replay`
   (data-hash 5760E83CB301B62E, the D13 data). QA re-simulated it in a bare sim with a throwaway xUnit probe (deleted after use).

## Expected
Acceptance (D13 criterion 5, and every track's loop gate): M4PlayableTest passes on seeds 1 and 6 whenever the product is
right. A step that can no longer find what it needs should fail fast with a message naming that, not run to the budget.

## Actual
The probe's trace (player 0's Cadre Mages, nearest Whirlwind unit player 0 can see, Whirlwind units alive):

| Tick | What |
| --- | --- |
| 672 | UseAbility: the Priest's Sandstorm (step 4) |
| 700 | one Cadre Mage dead (losses 1); mage 23 alone, 17 Whirlwind alive |
| 825 | UseAbility: Telas Fire, cast 0 of step 5, mage 23 Attacking, nearest seen enemy 12.7 m |
| 850 | mage 23 Idle, kills 2 -> 4: the cast burned no seen unit (its blob died first); the script waits for the 25 s cooldown |
| 1358 | UseAbility: Telas Fire, cast 1 (after a long walk in: mage 23 Moving 1375-1475, Casting at 1500) |
| 1500 | 2 Whirlwind alive |
| 1600 | **0 Whirlwind alive** (kills 18); cast 1 burned no one either |
| 1600-5502 | cast 2's `CastOnBlob` waits in `Until(Blob(...) >= 0 || casters.All(dead))` for a visible Whirlwind unit within 40 m: none exists, the mage lives, so it runs to the 5,500-tick budget |

The script's retry loop (BUG-0463's fix: "at most twice more") assumes the enemy army outlives two 25 s cooldowns. When
the line wins fast (here the fight was over by tick 1600), it doesn't. The wait has no exit for "no enemy unit left alive",
and the failure message points at visibility instead.

The same seed passed 7 times out of 8 (step 5 done in 82-88 ticks, the first cast burning) and seed 6 4 times out of 4, so
this is a timing fault in the test script, not a product rule. D13 shortens no fight (Line kills Shock more slowly at ×1.1),
and a pre-D13 replay with the same "tick 5501" budget symptom (`%APPDATA%\Godot\app_userdata\RtsGame\m4playable-seed1-fail-tick5501.replay`,
2026-10-10 16:59, data-hash D22C849A175F9ED5, only two UseAbility commands) shows a budget-length stall on seed 1 (step not recorded) predates
D13, though its mechanism was not traced.

## Notes
Suggested fix (view track, test code only): in step 5, if no Whirlwind unit of `_enemyArmy` is alive (or none has been seen
for N ticks) when a recast is due, either fail at once with "the fight ended before Telas Fire burned anyone" or, better,
accept the step on the first cast's resolve event (the cast resolved; the Burning marker is what the view shows) and check
the marker on a staged target like the step-8 duel does. Related: BUG-0460, BUG-0463 (same scene, same frame-clock timing class).

## Verification (QA, 2026-10-11-0634)
`M4PlayableTest` (seeds 1 and 6 per run) 8 runs in a row with the full `dotnet test` suite running beside it: 8 / 8 PASS,
16 seed-runs. Seed 1 step 5 done in 82-86 ticks (one cast, a Burning marker seen each time), whole run 2,900-3,083 ticks;
seed 6 step 5 in 23-36 ticks, whole run 3,429-4,284 ticks (budget 5,500). Step 8 printed "the Burning marker over Zealot A"
2-3 ticks after the duel's cast resolved in every run. The fail-fast path itself was not reproduced by QA (the dev's
negative check killed the enemy before step 5 and saw the message at tick 686).
