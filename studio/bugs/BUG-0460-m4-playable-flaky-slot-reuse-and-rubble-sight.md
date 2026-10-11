# BUG-0460: M4PlayableTest fails now and then: a recycled slot hides the duel's mage, and the rubble check reads only the footprint's centre cell

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-10-1314, task M4-V6d (QA) |
| System | test scene `game/tests/M4PlayableTest.cs` (M4-V7 slice 1) |
| Fixed by | bf3c75e (`M4PlayableTest`: fresh units by `EntityHandle` via `LiveHandles`/`Fresh`; rubble checked with `SeesFootprint`, any footprint cell). Verified by QA 2026-10-10-1314 round 1 |

## Repro
1. `dotnet build RtsGame.sln`
2. Run the scene headless several times, e.g. `powershell -File tools/qa/scene-loop.ps1` and
   `& $env:GODOT --headless --path game res://tests/M4PlayableTest.tscn` (with and without `-- --seed 6`). The commands
   land on whichever tick the frame clock reaches, so CPU load changes which tick everything happens on.
3. QA saw 2 failures in 10 seed-runs (6 scene runs): 4 plain runs PASS, the scene loop FAIL on seed 1, and
   `-- --seed 6 --break 9` failing at step 7 before it ever reached step 9.

## Expected
Acceptance criterion 3: "`M4PlayableTest.tscn` PASS on both seeds headless"; criterion 5: "Scene loop green (the new
scene added to it)". The scene is a gate in the loop, so it has to pass every time the product is right.

## Actual
Two separate script faults, both timing-dependent:

1. **Step 8, seed 1 (scene loop run):**
   `M4 PLAYABLE TEST FAIL 8. a Frenzied Zealot swings faster (seed 1, tick 2353): duel spawns: zealots 5 28, mage -1`.
   `ZealotDuel` snapshots the live slot *indices* (`before = new HashSet<int>(... U.Alive[i])`), enqueues the three
   spawns, waits 2 ticks and takes `fresh = alive && !before.Contains(i)`. Replaying the saved
   `m4playable-seed1-fail-tick2353.replay` shows a unit in slot 6 dying on tick 2351, and the Cadre Mage spawned on
   tick 2352 reusing slot 6 (alive 22 → 21 → 24, mage `[6 o0 <67, 125> hp 60]`). Slot 6 was in `before`, so the mage
   is filtered out. `Stage()` and `AbilityViewTest.FourMarkersRow` use the same index pattern (safe there today only
   because no unit dies in that window).
2. **Step 7, seed 6 (`--seed 6 --break 9`):**
   `M4 PLAYABLE TEST FAIL 7. Cusser on a building, rubble shown (seed 6, tick 3462): the rubble is not in sight`.
   Replaying `m4playable-seed6-fail-tick3462.replay`: the Raider Camp (slot 6, centre (197, 139)) died on tick 3462
   with the Sapper already dead and the nearest own unit 15.8 m from the centre (a Cadre Mage; the rest 17-19 m,
   attacking from the footprint's edge). `Fog.IsVisible(0, centre cell)` was false, while the building (any footprint
   cell) was still seen, so the rubble is drawn correctly but the script's centre-cell check fails.

## Notes
- Fix ideas (test code only): compare handles (index and generation), not indices, or take the spawn's handle from
  the units whose generation changed; check the rubble with `CanSeeBuilding`'s rule (any footprint cell) or wait for
  an own unit in sight of the centre, as a player would.
- The four green runs: seed 1 2,594-2,610 ticks, seed 6 3,232-3,874 of the 5,500 budget; fog 0 violations in all.

## Verification (QA, 2026-10-10-1314 round 1)
- Diff read: `Stage()` and `ZealotDuel()` now diff live `EntityHandle`s (`EntityHandle` is a `record struct`, so set
  membership compares index and generation); `SeesFootprint` loops the same `FootprintWidth x FootprintHeight` cells
  from `B.Cell` (the anchor, saved before the building dies) as `FogStore.SeesFootprint`.
- 23 seed-runs headless: 10 scene runs (18 seed-runs, all while the non-Perf suite ran), 3 `--seed 6`, and the scene loop (2): no step-7 or
  step-8 slot/rubble failure. The rubble was in sight every time (centre cell visible in all of them). Step 8 found its
  three spawns every time. `--seed 6 --break 7`, `--seed 6 --break 8`, `--seed 1 --break 7` each failed at the named
  step and exited 1. The full scene loop ran 41/41 PASS.
- The scene is still flaky for two other reasons (step 5 caster death, step 8 window length): BUG-0463.
