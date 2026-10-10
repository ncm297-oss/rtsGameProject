# BUG-0462: M4-V6d / M4-V7 nits: the selfAura hover ring floats across a cliff, the seed-6 sandbox stills are missing, marker rows merge at zoom 60, and shot frames skip the fog check

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-1314, task M4-V6d (QA) |
| System | ability views (`AbilityViews.SyncSelfRing`), `game/tests/M4PlayableTest.cs` |
| Fixed by | |

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
