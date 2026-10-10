# BUG-0371: M4-V6b nits: Slowed marker hard to see at RTS zoom, a fogged resolve can flash in mid-fade, late first draw jumps

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-10-0215, task M4-V6b |
| System | view: ability views (status markers, resolve flashes) |
| Fixed by | 956eab3 (M4-VH2): `SlowColor` (0.08, 0.3, 0.95); `ResolveFlashes.MarkDrawn(slot, tick, shown)` decides once; regression `ViewApi/ResolveFlashesTests.ALateFirstDraw_*` and `TheFirstFramesFogAnswer_*` |

## Repro
1. `dotnet build RtsGame.sln`
2. Windowed: `& $env:GODOT --path game res://tests/AbilityViewTest.tscn -- --shots <dir>`; open
   `ability-seed1-statuses.png` and `ability-seed1-burning.png`.

## Expected / Actual
a. **Slowed marker contrast.** At the default zoom (30) the markers are about 4-5 px cubes. The flame-orange Burning
   marker reads; the blue-grey Slowed marker (`SlowColor` 0.55, 0.66, 0.82) almost vanishes against the sand and the
   pale unit bodies in `ability-seed1-statuses.png`. A darker or more saturated blue, an outline, or a slightly larger
   marker would help. Taste call for the owner or the M6 art pass.
b. **A fog-hidden resolve can appear mid-fade.** The brief says "none for a cast the fog hides". `AbilityViews.SyncFlashes`
   tests `FogView.ShowsPoint` every frame: a flash whose point was hidden at the resolve is marked drawn and skipped, but
   if the next fog update (up to 4 ticks later) shows that cell, the same flash is drawn for the rest of its 0.5 s.
   Deciding visibility once, on the first frame, would match the criterion exactly. (By reading the code; the
   `FogFlashRow` cast is 60 m from every own unit, so it never crosses the edge.)
c. **Late first draw jumps.** `ResolveFlashes.Age` ages a flash no frame has drawn from `tick - 1` at most, then from its
   real start once drawn. When a frame covers several ticks (OnTicked collects at tick N, the frame draws at N + 2),
   the disc is drawn at age ~0.1 and then jumps to ~0.3 on the next frame. Invisible at 20 Hz and 60 fps; noted for
   low frame rates.

## Notes
None affects the sim or determinism (`StatusFlashQaTests.NewViewApiHelpers_DoNotChangeTheSim_*` proves the helpers are
read-only). The headless renderer keeps no MultiMesh instance data (`GetInstanceColor` reads black), so marker colours
can only be checked windowed; `QaV6bTest` skips the colour readback when headless.

## Verification (QA 2026-10-10-0624, M4-VH2)
- a: QA `game/tests/QaVH2MarkerShot.tscn` windowed at zoom 20 (the camera's minimum; 15 clamps to 20), 30 and 60 on
  sand with both factions' bodies, Slowed alone and Slowed + Burning: the deep-blue marker reads at all three. Taste
  default; the owner may revisit.
- b, c: the two new `ResolveFlashesTests` rows pass; `AbilityViews.SyncFlashes` calls `MarkDrawn` once on the first
  frame and skips a flash whose `Shown` is false (code read). Expiry runs from the same origin.
