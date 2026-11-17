# BUG-1441: M7-V5 Teblor figure look nits: a foot off the ramp, overlays a metre over the head, the javelin into the ground, the Shaman's cast plays idle

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 2 fixed by M6-VH8, session 2026-11-15-1536, verified by QA: `OverlayLineTest`; item 4 fixed by M6-V23 (verified by QA: `CastClipTest`; BUG-1530 is its S4 follow-up); item 3 waits for an art task with a re-import; item 1 is the owner's taste) |
| Found | 2026-11-14-2156, task M7-V5 (QA) |
| System | view: unit models (Teblor figures), overlays |
| Fixed by | M6-VH8 (item 2), M6-V23 (item 4) |

## Repro
1. `& $env:GODOT --path game --resolution 1600x900 res://tests/QaM7V5Test.tscn -- --shots <dir>` (headless without
   `--shots`; the numbers below are its log lines (A) and (D)).
2. Look at `ramp-20m.png`, `hurt-warrior-bar-14m.png`, and the developer's `closeup-teblor_javelin_thrower-attack.png`.

## Expected
Giants stand on the ground and read cleanly at 1.5x (QA focus: "feet on the ground at 1.5x", "overlays above a 2.8 m
head, not inside it").

## Actual
Nothing here breaks a criterion; each is a look item for the Producer / owner:
1. **A foot off a ramp.** The figure's origin is on the terrain at the unit's point (0.0000 m off in 15,750 checks and on
   ramps / at a cliff lip), but there is no foot placement: within a 1.5x foot's reach (0.45 m) the ramp falls up to
   **0.27 m** under the origin (the downhill foot hangs, the uphill one sinks). At 1x the same ramp gives about 0.18 m.
   At the foot of a cliff wall the ground 0.45 m away is 4 m higher (a foot inside the wall; the 0.75 m capsule did the
   same). No figure hangs over a cliff's lip (the units stop before it).
2. **Overlays a metre over the head.** The hp bar, cast bar, status marks, cue words and cargo marker sit over
   `UnitViews.DrawnHeight` (3.75 m for a giant: the capsule's), while the animated figure's head bone tops out at 2.30-2.34 m
   and its raised hand at 2.67 m (row A). Above the head as required, but about 1-1.4 m clear of it; a Heavy Infantry's
   bar sits about 0.1 m over its head. Documented by the developer in docs/03 "Implementation (M7-V5)" as an owner finding.
3. **The javelin into the ground.** The Javelin Thrower's `attack_throw` follow-through drives the in-hand javelin box
   well into the ground (close-up picture). Props through hands are fine per the brief; through the ground reads odd up
   close.
4. **The Shaman's cast plays idle.** `Casting` maps to `idle` for every model type (M6-V20's `ClipFor`), so the Shaman's
   Frenzy cast shows no gesture while its `attack_cast` exists. Disclosed by the developer.

## Notes
- None of these is new mechanics; 1 would need foot IK or a per-type sink, 2 a per-model overlay height (the figure's
  drawn height instead of the capsule's), 3 a prop offset for the throw pose, 4 a `cast` clip slot.
- QA's `game/tests/QaM7V5Test` row A pins the overlays' line above every Teblor clip's highest bone, so a fix to 2 that
  drops the line into a figure turns it red.

## M6-VH8
- Item 2 fixed: the overlays sit over the drawn figure for a model type (`UnitViews.OverlayLines`: the highest bone of
  every clip the type holds, plus 0.18 m, times the data scale; a capsule type keeps its capsule's top). Giants: line
  2.94 m (was 3.75), 0.27 m over the highest bone (a finger in `gather_wood`, 2.67 m); men 1.96 m, 0.18 m over 1.78 m.
  New scene `OverlayLineTest`; QA's `QaM7V5Test` row A is unedited and green.
- Item 4 not done: no re-import is needed, but a Casting clip needs a seventh `UnitModels.Clip` (breaks QA's
  `QaM6VH7Test` row 1) or a type-aware `ClipFor` (moves the contract `QaM6V20Test` row 9 / `TeblorModelTest` read).
- Item 3 not done: a prop offset needs a Blender re-import (left for an art task). Item 1 is the owner's taste.

## M6-V23
- Item 4 fixed: `UnitModels.Clip.Cast`, a seventh slot named `attack_cast` (the library clip), and `ClipFor` maps
  `Casting` to it; by the state-name rule every model type holds it with no re-import, so the casting Shaman plays
  `attack_cast`; a library without it plays `idle` with no warning. New scene `CastClipTest`; picture
  `shaman-casting-zoom20.png` (`CastClipTest` windowed `-- --shots`). `QaM6VH7Test` row 1, `QaM6V20Test` row 9 and
  `TeblorModelTest` needed no edit (the slot's name is a library clip: 0.5 s, longer than the 0.3 s stagger bound).
