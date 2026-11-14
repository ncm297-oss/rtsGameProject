# BUG-1290: A wide unit at a small scale is drawn narrower than its collision radius (Godot clamps the capsule)

| Field | Value |
| --- | --- |
| Severity | S3 (no shipped unit hits it; valid data inside the loader's limits does) |
| Status | fixed |
| Found | 2026-11-12-0546, task M7-V3c (QA) |
| System | view: `UnitViews.Bind` capsule mesh, `UnitViews.DrawnRadius` / `DrawnHeight` |
| Fixed by | M6-V20 (view, 2026-11-13-1118, fbe19b5): `UnitViews.DrawnHeight` = max(`(2 radius + 1) x scale`, `2 radius`); `QaM7V3cTest` green |

## Repro
1. `dotnet build RtsGame.sln`, then `& $env:GODOT --headless --path game res://tests/QaM7V3cTest.tscn`.
2. The scene forges a data copy with Heavy Infantry `radius` 0.9 `scale` 0.5 and Crossbowman `radius` 1.0 `scale` 0.5
   (both inside `DataLimits`: radius 0.4-1.0, scale 0.5-3.0; the copy loads with no error).

## Expected
docs/03 "Implementation (M7-V3c)": every unit's capsule is `radius` wide (radius `def.Radius`) and
`DrawnHeight` = `(2 radius + 1) x scale` tall; the rings and the corpse disc are multiples of that width.

## Actual
```
  malazan_heavy_infantry radius 0.9 scale 0.5: capsule 0.7 x 1.4 (DrawnRadius 0.9, DrawnHeight 1.4)
  malazan_crossbowman radius 1 scale 0.5: capsule 0.75 x 1.5 (DrawnRadius 1, DrawnHeight 1.5)
QA M7-V3C TEST FAIL: malazan_heavy_infantry (radius 0.9, scale 0.5): capsule radius 0.7, want the collision radius 0.9 (BUG-1290)
QA M7-V3C TEST FAIL: malazan_crossbowman (radius 1, scale 0.5): capsule radius 0.75, want the collision radius 1 (BUG-1290)
```
The body is drawn as a 1.4 m ball on 1.8 m of room, while its selection ring (1.35 x 0.9), target ring and corpse disc
(0.9) still follow `DrawnRadius` = 0.9: the ring sits well outside the body and the corpse is wider than the unit was.

## Notes
- Cause: `new CapsuleMesh { Radius = DrawnRadius(def), Height = height }` sets `Radius`, then `Height`; Godot's
  `CapsuleMesh.set_height` clamps the radius to half the height. With the width no longer scaled, any type with
  `scale < 2 radius / (2 radius + 1)` (radius 0.6 below 0.545, 0.75 below 0.6, 1.0 below 0.667) gets a height under its
  width. Under M7-V1's `radius x scale` it could not happen (the height was always over twice the width).
- New in M7-V3c: the old formula never produced it (the M7-V3c mutant that restores `x scale` in `DrawnRadius(UnitDef)`
  draws these at 0.45 / 0.5, unclamped).
- Fix is the view's choice (owner may weigh in): e.g. keep the height at least `2 x radius` for such types (a ball, then
  the rings match), or let the loader / docs bound `scale` against `radius`. `DrawnHeight` feeds the bar and markers, so
  whatever the capsule does, they should follow the same number.
- `TeblorViewTest`, `ScaleViewTest` and `UnitViewsTest` don't see it: shipped units are at scale 1 or radius 0.75 / scale
  1.5, and the forged scale-0.5 Crossbowman has radius 0.4 (height 0.9 > 0.8).
- Regression test: `game/tests/QaM7V3cTest.tscn` (red now; green once fixed).

## Fix (M6-V20)
The Producer default: a type whose `(2 radius + 1) x scale` is under `2 radius` is drawn as a ball `2 radius` tall, so
Godot never clamps the capsule's radius; `DrawnHeight` returns that same number, so the hp bar and the markers follow.
No shipped type is affected. Proof: `QaM7V3cTest` PASS (it runs with `UnitViews.UseModels = false`: it pins capsules);
QA's fixed-frame `QaM6V20Shots` pictures with `--placeholder-art` are byte-identical to the base's at 40 m.
