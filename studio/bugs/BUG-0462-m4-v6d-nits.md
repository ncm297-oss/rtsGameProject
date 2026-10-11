# BUG-0462: M4-V6d / M4-V7 nits: the selfAura hover ring floats across a cliff, the seed-6 sandbox stills are missing, marker rows merge at zoom 60, and shot frames skip the fog check

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (items 1, 2, 4, 5); item 3 wontfix (M6 art pass) |
| Found | 2026-10-10-1314, task M4-V6d (QA) |
| System | ability views (`AbilityViews.SyncSelfRing`), `game/tests/M4PlayableTest.cs` |
| Fixed by | item 2, 4: bf3c75e; item 5: M4-V7b (residual BUG-0490 item 1); item 1: afa2661 (M4-VH3, the hover and targeting rings are `GroundDisc`s on the terrain; `AbilityViewTest` hover and targeting rows check every ring point on the ground, failed first at 27 / 72 and 59 / 72 points off); item 3: wontfix, an M6 art-pass note in docs/03 "Implementation (M4-VH3)" |

## Repro / Actual
1. **Hover ring across a cliff.** `build/shots/m4-v6d/ability-seed6-aura-hover.png` (the developer's windowed still):
   the lead Heavy stands at a plateau's edge, and the 4 m radius ring is one flat torus at the caster's ground height, so
   its far half hangs over the cliff and the low ground. The same effect as BUG-0430 (zone discs), now on the hover ring
   (and the existing targeting radius ring). Fold into BUG-0430's fix.
2. **Stills listed for the owner.** docs/03 "Implementation (M4-V7)" says the stills were "looked at, seeds 1 and 6",
   but `build/shots/m4-v7/` holds only the five `m4-seed1-*.png`; there are no seed-6 sandbox stills on disk to list.
3. **Marker rows at zoom 60.** QA's `game/tests/QaFrenzyZoomShot.tscn` (windowed): a Heavy and a Raider 2.5 m apart
   with all four statuses. At zoom 60 each marker is about 5 x 7 px and the two units' rows touch, so they read as one
   row of eight (orange, blue, violet, magenta, orange, ...). The colours themselves stay apart at zoom 20, 30 and 60,
   and magenta is never confused with violet. Polish for the M6 art pass.
4. **Fog check gap in windowed runs.** `M4PlayableTest.Shot` awaits three `FramePostDraw` signals directly rather than
   through `Frame()`, so those frames skip `CheckFog`. This applies only with `--shots`; headless counts are unaffected.

## Expected
1. The ring follows the ground (or is clipped sensibly) like the disc fix BUG-0430 asks for. 2. The stills the docs
promise exist (or the docs say seed 1 only). 3. Some gap between neighbouring units' marker rows. 4. Every frame of
the run checked, as docs/03 states ("on each frame of the whole run").

## Re-check (QA, 2026-10-10-1314 round 1)
- Item 2 fixed by bf3c75e: `build/shots/m4-v7/` now holds the five seed-6 stills (first-contact, storm, telas-fire,
  cusser-rubble, frenzied-zealot). QA looked at cusser-rubble (grey rubble box drawn on the footprint, in sight) and
  frenzied-zealot (magenta marker over the hurt Zealot, none over B).
- Item 4 fixed by bf3c75e: `Shot()` calls `CheckFog()` after each `FramePostDraw` wait.
- Items 1 and 3 are still open, as the developer said.

## Re-check (QA, 2026-10-10-1314 round 2)
- Items 1 and 3 are still open.
- New item 5. **The tick-budget margin is stated too high.** `M4PlayableTest.TickBudget`'s doc comment and docs/03
  give seed 6 as 3,424-3,878 ticks "so at least 1,600 ticks (40 %) of margin". In one of QA's 12 round-2 runs (loop 1,
  run 7, under suite load), seed 6 took 4,212 ticks: step 6 ("kills and losses move") ran 1,554 ticks against the usual
  650-760, with kills 19 against 18. The margin was still 1,288 ticks (23 %). A worst-case Telas recast that waits for
  the 25 s cooldown adds about 500 ticks, so the stated margin isn't a guarantee. Update the measured range or the
  budget note.

## Re-check (QA, 2026-10-10-1954, M4-V7b)
- Item 5 fixed: `TickBudget`'s comment and docs/03 now give a measured range with step 9 (seed 1 2,869-3,028, seed 6
  3,710-4,372), name the 4,212 run, and state the margin as measured, not guaranteed. QA's 14 seed-runs went slightly past
  that range (seed 1 up to 3,081, seed 6 up to 4,488, still 1,012 ticks / 18 % under 5,500). Filed as BUG-0490 item 1
  (S4), not reopened here.
- Items 1 and 3 not in this task's scope (end-of-M4 view hardening).


## Verification (QA 2026-10-10-2334, M4-VH3)
- Item 1 fixed: the selfAura hover ring and the targeting rings are `GroundDisc` bands on the terrain cells (BUG-0430's
  fix). `AbilityViewTest`'s hover row checks the ring on the ground every run; QA's `QaVh3Test.tscn` puts the cursor on a
  cliff lip, one cell either side and a ramp (seeds 1, 6): every point of the radius ring's band on the terrain + 0.12 m,
  every mesh triangle inside one cell. Looked at `qa-vh3-seed6-cliff-rings-zoom30.png`: the orange radius ring and the
  pale-blue range ring lie on both levels.
- Item 3 closed wontfix: two units' marker rows touching at zoom 60 is left to the M6 art pass (the brief allowed "fix or
  an M6 note"; docs/03 "Implementation (M4-VH3)" records it). Not re-measured.
