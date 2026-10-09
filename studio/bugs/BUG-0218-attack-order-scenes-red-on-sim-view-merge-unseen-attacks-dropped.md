# BUG-0218: AttackOrderViewTest and QaV6Test are red on the sim + view merge: explicit Attacks on unseen enemies are dropped

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-08-1814 (resumed 1435 track), task M4-3a |
| System | view test scenes (`game/tests/AttackOrderViewTest.cs`, `game/tests/QaV6Test.cs`, view-owned, from the M4-V2 held branch) vs the M4-3a vision gate on explicit Attack (`CombatSystem.MayAttack`) |
| Fixed by | M4-V3 fix round 2 (view track): the scenes stage every clicked target in the player's sight (`game/tests/AttackStage.cs`); every `Check` kept |

## Repro
1. Scratch clone of the repo; `git checkout -b integ studio/2026-10-08-1435-sim` (66e8cb5), then
   `git merge studio/2026-10-08-1435-view` (e1f3333). Only `studio/bugs/README.md` and `studio/qa/coverage.md`
   conflict (records; resolved with `--ours`); every code file merges cleanly.
2. `dotnet build RtsGame.sln` (0 warnings), `powershell -File tools/qa/smoke.ps1` (PASS), then
   `powershell -File tools/qa/scene-loop.ps1`: **SCENE LOOP FAIL: 2 of 33: AttackOrderViewTest, QaV6Test**. Every other
   scene passes, including SfxTest, QaM24Test and MinimapTest. Each scene alone, twice more: the same failures every
   time (deterministic).
   - AttackOrderViewTest, seed 1: `A + click enemy: 8 of 10 selected units don't hold the target` (they keep enemy 10,
     want 11), `shift: 2 of 10 queues hold both Attacks` (the second queued Attack becomes a Noop), and
     `right-click Tent: unit 0 target {0,0} building False mode None, want the Tent`. Seed 6 passes.
   - QaV6Test: `seed 6 queue: no unit queued all three Attacks behind its Move`.
3. Control: the view branch alone (e1f3333, sim at dd5b5b9's code), scratch clone: both scenes PASS.
4. Attribution: on the merge, removing only the two `&& VisionSystem.UnitSees...` terms that M4-3a added to
   `CombatSystem.MayAttack` (scratch, not committed): QaV6Test PASS, and every AttackOrderViewTest order row passes.
   That run's only remaining failure was one seed-6 steady-frame allocation check (168 B); this is likely a side effect
   of the mutant, since the unmutated merge measures 0 B there.

## Expected
Brief criterion 8: "Scene loop (the script fetched from the held view branch) 30 / 31 with only `MinimapTest` red;
any other red scene is this task's." That count includes the view branch's scenes. The handoff's integration gate:
"the integration scene loop (33 scenes) must be fully green after sim + view merge."

## Actual
The scenes order Attacks on enemies the issuing player can't see: the Tent "behind the lines", and the second and
third enemies picked for the A-click and Shift rows sit outside every selected unit's sight. Per the brief and
docs/03 "Vision, detection, fog", an explicit Attack, queued or not, on a target its owner doesn't see is dropped
like a forbidden one. So the sim behaves as specified. The scenes assume any enemy pixel can be attacked, which has
been false since M4-3a.

## Notes
- Owner: the view (the sim can't edit `game/tests/`). `--no-combat` is not a fix here, because these scenes test
  Attack orders. The scenes must order Attacks only on enemies the player sees, for example by staging the lines
  within sight, adding a spotter unit next to the Tent and the far enemies, or picking only seen enemies (read
  through the fog). Every `Check` stays.
- The rows were never run against the fog sim. The 1435 QA ran the scene loop on the sim branch's own `game/tests`
  (29 scenes), and the view branch's QA runs on dd5b5b9's sim. So the conflict appears only when the branches merge.
- Alongside BUG-0212 and BUG-0213, this gates the integration.

## Fix (M4-V3 fix round 2, view track)
- New `game/tests/AttackStage.cs`, the staging both scenes now share: Heavy Infantry and Raiders in two-column lines 2-3
  cells either side of the centre (filled from the centre row out, skipping blocked cells; staging throws if any Raider is
  farther than the Heavy Infantry's sight less 1.5 m from every own unit), the Tent 10-16 cells behind the Raiders as
  before, and player 0's Billet moved from behind player 0 to beside the Tent (centre cell within 4 cells, 8 m, of a Tent
  cell; 12 m building sight) as the spotter that sees it. The own-Billet right-click row still runs on seed 1 (on seed 6
  the Billet has no clean pixel; the row was optional before too).
- `AttackOrderViewTest.PanelRows`: the chase target is the farthest enemy within the unit's sight less 2 m (was: the
  farthest on the map, which under fog may be unseen, and without fog, on seed 6 with the new lines, was 14.4 m off and
  given up by the BUG-0137 rule before the unit swung).
- No `--no-combat`, no sim change, every `Check` unchanged.
- Proof: scratch merge of `studio/2026-10-08-1435-sim` (a27757c) into the view branch (8b7d654 + this fix): before the
  fix `AttackOrderViewTest` FAILs with the repro's rows; after it, `scene-loop.ps1 -Filter AttackOrderViewTest` and
  `-Filter QaV6Test` PASS on every run. View branch alone: both PASS on every run, and the full 33-scene loop is green.
