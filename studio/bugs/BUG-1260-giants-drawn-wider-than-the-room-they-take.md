# BUG-1260: A Teblor giant is drawn 1.125 m wide but takes 0.75 m of room, so giants standing together are drawn inside each other

| Field | Value |
| --- | --- |
| Severity | S3 (visible in every Teblor match since the roster landed; nothing plays wrong) |
| Status | open (the view's next task, M7-V3c; Producer default below, owner may revisit) |
| Found | 2026-11-11-2256, task M7-V3b (the developer's and QA's first screenshots of the real Teblor; filed by the Producer at ACCEPT) |
| System | view: `UnitViews.DrawnRadius` (`radius x scale`), the placeholder capsule, rings, corpse disc |
| Fixed by | |

## Repro
1. `main` with the Teblor roster (after session 2026-11-11-2256). Skirmish, faction Teblor, start.
2. Look at the five starting Gatherers beside the Longhouse, or train two Warriors and let them stand at the rally point.
3. Pictures: `studio/review/2026-11-11-2256/teblor-2-base.png` (five Gatherers as one lumpy wall),
   `teblor-3-warrior-beside-soldier.png` (a Warrior about three times a Heavy Infantry's width),
   `teblor-4-crowding.png` (a forged pack of three Warriors and five Gatherers, rings and bodies overlapping).

## Expected
Units that the rules keep apart are drawn apart: a body is no wider than the room the unit takes.

## Actual
- The six giants have `radius` 0.75 (the page's collision radius, already a giant's) and `scale` 1.5. The view draws the
  capsule, rings and corpse disc at `radius x scale` = 1.125 m, so two giants standing shoulder to shoulder (centres 1.5 m
  apart) overlap by 0.75 m. A giant reads about 2.8 times a soldier's width (0.4 m) and only 1.5 times its height: wider
  rather than taller from the game camera.
- The scale is counted twice in the width: once by the data's wider radius, once by the view's multiplication. With a
  scale-1.5 unit whose radius stayed 0.4 (M7-V1's test copy) the same code looked right.
- Item 2 (S4, nit): in QA's 1 fps shot scene the Warriors were drawn white for the first frame (team colour one frame
  late). Seen once, in a scratch scene; check whether a normal match shows it.

## Notes
- Not caused by M7-V3b (test files only) or by D28 (the page's numbers); it is M7-V1's drawing rule meeting the roster.
- **Producer default for M7-V3c (owner may revisit):** the body's width, the rings and the corpse disc follow the
  collision radius alone; `scale` sets the height (and the bar and shot heights, as today). A giant is then 0.75 m wide
  (1.9 times a soldier) and 1.5 times as tall, and nothing overlaps. Alternative: keep `radius x scale` and lower the
  giants' radius in `units.json` to 0.5 (data + page; moves the golden header and every Teblor measurement).
- When real models arrive the model's own width replaces the capsule; the rings should still follow the collision radius.
