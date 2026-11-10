# BUG-1177: M7-V1 scale nits: shots leave from a fixed 1.2 m, a scaled unit's drawn head is not clickable

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-10-0146, task M7-V1 |
| System | unit views / projectile views / selection (view) |
| Fixed by | |

## Repro
1. Forged data copy (as `game/tests/QaM7V1Test.cs` `Forge`): Heavy Infantry `scale` 1.5, Crossbowman 0.5, Sapper 3.0.
2. `& $env:GODOT --headless --path game res://tests/QaM7V1Test.tscn`, read the `zoom 20` / `zoom 40` lines.
3. Read `game/scripts/ProjectileViews.cs` (`LaunchHeight = 1.2f`, used for every aimed shot and the hit flash).

## Expected
Items for the Producer's / owner's call, not acceptance failures:
- Shots and hit flashes start / land at a height that follows the drawn body once units have a scale (the brief's
  list of "what follows the body" doesn't name projectiles, so this is outside M7-V1's criteria).
- The hit-test rule (sim radius, scale-1 body) is the brief's Producer default and docs/03 says so; it is listed here
  because at real zooms it makes most of a big unit's body unclickable, which a playtest is likely to report.

## Actual
- `ProjectileViews.LaunchHeight` is a fixed 1.2 m: a 0.5 Crossbowman is 0.9 m tall, so its bolt leaves 0.3 m above
  its head; a 3.0 Sapper is 5.4 m tall, so a shot at it flashes at its knees.
- Measured (QaM7V1Test): at zoom 20 a 1.5 Heavy Infantry's drawn head is 32 px above the sim centre (pick floor
  12 px), a 3.0 Sapper's 81 px; at zoom 40, 14 px and 36 px. Clicking the head selects nothing, a box over only
  the head selects nothing, and `SelectionController.EnemyAt` on an enemy 3.0 Sapper's head returns false, so a
  right-click there is a Move to the ground behind it, not an Attack. A Teblor at 1.5 (the shipped case to come)
  has its upper third outside the click circle at the closest zoom.

## Notes
- The scaled body clipping into a building beside it (a 3.0 Sapper against the Garrison Keep reaches 0.70 m into the
  footprint, sim body 0.50 m from the nearest blocked cell) is the documented "known look" in docs/03
  "Implementation (M7-V1)" and is not part of this bug.
- Fix options if wanted: `LaunchHeight` as a share of `UnitViews.DrawnHeight` of the shooter / target; for the hit
  test, pick by the drawn capsule's vertical extent while keeping the sim radius sideways.
