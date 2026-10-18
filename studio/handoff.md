# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-17-1624** (the third full session of 2026-10-17, 3 / 8). All three tracks
**ACCEPT**: sim **M5-H3** (hardening), view **M6-VH3** (hardening), data **D-R31** (the rule-31 re-print of the cells M5-H3 moved,
dispatched at the integration). Merge order sim → view → data (the data branch already carries the sim branch). Golden `data-hash`
**C059AD620DE581F1** (unmoved; `game/data` untouched by all three). The next PLAN starts from the integration commit on `main`.
Counters after: **sim 0 / 4, view 0 / 4, data 1 / 4** (a re-print doesn't advance the data counter, as at 0554). Bug ids next: **sim from
BUG-0940, view from BUG-0950, data from BUG-0960** (0910-0915 sim, 0920-0922 view used this session). Open S1 / S2: **none**; S3 16, S4 15
(by the index; recount at PLAN). Producer's full non-Perf run on the merged tree: **6,714 / 1 / 13 of 6,728 in 32 m 29 s**; the 1 =
`BuildingSightLoaderTests.ABadBuildingSightRule_IsAnErrorAtTheField("0")`, green 19 / 19 alone (**BUG-0915**, S4 flake).

## Where we are

- **M5 3 / 9** (criteria 1, 2, 7 ticked; 3, 4, 5 advanced). **M6 2 / 10** (criteria 2 and 4 ticked; 1 waits on team (Requests 38), 3's save
  half on Requests 42, 5's art pass on the owner's look call).
- **Waiting on the owner:** the art call (docs/04 "Look test (M6-V10)"); the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780, "densest
  cluster"; the fonts / music / export templates; (new, not blocking) a look at the PC's sustained clock (BUG-0764 was re-baselined
  around it; BUG-0912 says the 4 ms design budget is unguarded until then).
- **Inbox at this ACCEPT:** empty. Check again at the next PLAN: an owner answer outranks the plans below.
- **Red on `main` after this integration: one scene-loop row, `CombatViewTest` (BUG-0920, S3, view; red since the M5-P2 merge, not
  M6-VH3's doing).** Build, smoke and the full non-Perf suite are green. The view's first item next session fixes it (below).

## Standing rules (1-33)

Unchanged 1-31 (the 2036 handoff in git history has the long form; 31 = the rule-31 re-print, applied this session: BUG-0790 moved the
spending cells and one counter-triangle cell; the data track re-printed them on the merged tree). New this session:

- **32 (ui.json, from QA's M6-VH3 note).** `game/data/common/ui.json` is sim-owned in the ownership table, but its text keys are the
  view's: a view brief may add or reword keys there **when it names them in the brief**, the sim track doesn't touch `ui.json` that
  session, and `ShellTest`'s key test covers every new key. Nothing else under `game/data/common/` is the view's.
- **33 (QA tooling).** `tools/qa/scene-loop.ps1` and `tools/qa/smoke.ps1` are view-side QA tooling: the view track may change them
  (named in the brief); the rest of `tools/**` stays the sim's.

---

## Sim track

### Next session: feature (counter 0 / 4). Candidate order, with the reasons

1. **D14 b, the schema half (M5 criterion 8's sim side) + BUG-0910 folded in.** Why first: no S1 / S2, no inbox, no red sim test; among
   the open requests and criteria this is the one that **unblocks the idle data track** (its openings content the session after), and
   BUG-0910 (S3, a few lines: `TryBuild` sets `NoSiteFound` only when `FindSite` ran its ring search past `CanPlaceAnywhere`; QA's skipped
   `SimHardeningM5H3QaTests.ABuildStepWaitingOnARequirement_*` is the regression row) lives in the same executor code. The schema per the
   roadmap: `ai.json` holds several **named openings** per faction (build order, army mix, attack thresholds), the AI picks one with its
   seeded RNG at match start (**Easy always the first**), age-agnostic; validation + `DataValidationTests` + the minimum entries the sim's
   own tests need (one opening = today's order, so `data-hash` moves once at most, with the reason); `PlayerView` unchanged; the pick is
   AI state (hashed? decide and document: it decides every later think, so hash it or derive it from the seed). Rule 31 doesn't apply
   (no movement / chasing change) unless the executor's pacing moves the spending cells: name `Content/AiSpendingReportTests` and the
   D20 / D-R31 pins (`D20PinQaTests`) as cells the fix of BUG-0910 may move (it changes when a requirement-blocked Build goes out).
2. **Requests 42, the save format** (M6 criterion 3's save half), the session after: the whole sim state + the outside-hash state that
   decides the next tick: `World.SurroundClaim` / `SurroundHolder`, the AI `Blackboard` whole (**now incl. `StepNoSiteUntil`**, M5-H3),
   `BuildingGhost.Site`, `MatchStats`, `UnitStore.Flank`, the D14 b opening pick. Uncertain work: slice it (~800 lines: write / read /
   round-trip with every array compared; the view's screen later).
3. Then Requests 38 (teams), M5-4c slice 2 (criterion 4: `gatherBonus`, `focusFire`, `pullBackWounded`), M5-4d (expansion / rebuilding /
   high-ground scouting / hunting the last units), D14 c, M5-5 (the 20-minute AI-v-AI test), M5-6. Pathing slice 3 when the owner asks.

### Debt left by M5-H3 (next sim hardening, counter 4 / 4)
BUG-0912 (S3: the far-corner gate 0.64 → more pairs and ~0.35 ms; a fresh-process gate row for the 4 ms 500-unit budget), BUG-0911 (S3,
pre-existing: spam Moves behind a 10-line at 30-45° never arrive; under `FlankKeepMinMembers`, no kept side), BUG-0889 (S3: the
never-strike keeps on halls), BUG-0144 (S3), BUG-0914 + BUG-0915 (S4 flakes: the H3 0-byte probe and the `buildingSight` loader row each
failed once in a full run, green alone; make both failure messages say what happened; S3 and first item if either fails again), BUG-0913 (S4 nits, 7 items; item 7 = `NoSiteRetryThinks` in C#, the owner may want it in `rules.json`), BUG-0113 item 2,
BUG-0704 item 3, BUG-0850's leftovers if any.

### Watch-outs (sim)
- **BUG-0790's stand-point change moves every diagonal footprint walk** (BUG-0913 item 4), not only crowded faces: the seed-21 match
  gathers far more wood (235 → 895 at its end) and the menu-map spending cells moved; any gather / build / repair change re-runs
  `GatherWedgeQaTests` (the seed-21 wedge is at 478 of its 600 bound) and the rule-31 cells.
- `Blackboard.StepNoSiteUntil` is AI state outside the hash: **Requests 42 must save it** (listed there now).
- Criterion 1's HI row still sits on its 8 / 10 bound; re-run `FlankTests` after any movement change. `UnitStore.Flank` is hashed once
  non-zero (older owner recordings expire where the rule fires).
- The Perf gates now measure this PC's **sustained** clock (docs/03 "Re-baselined for this PC"): a feature task's cost is shown by
  paired A/B, never by a gate alone; a Perf row red on the head only is a regression.
- The view's QA noted (BUG-0921): with the flank rule, seed 11's four-seat match reaches Age II at 17:30 instead of 14:11 (fights
  decide faster, economies differ). Not a bug; M5-5's 20-minute test should expect this spread.

---

## View track

### Next session: feature (counter 0 / 4). Candidate order

1. **BUG-0920 first (S3, the one red loop row on `main`):** `CombatViewTest`'s "hit while lit" row relied on seed 1's brawl cadence, which
   the flank rule changed; stage the row on its own (two attackers on one target inside the flash window) or count it over both seeds.
   A few lines; the loop back to all-green is the gate.
2. **Then M6-V12: real SFX + alerts with sound** (M6 criterion "Real SFX and placeholder music; alerts with sound"). The SFX packs are
   downloaded (docs/04 "UI, fonts, audio": Kenney RPG Audio / Impact / Interface, Sonniss; folder layout `game/assets/audio/sfx/<category>/
   <event>_<variant>.wav`, `.wav` for SFX): import through the pipeline, **every file in docs/04's licensing log in the same commit**, no
   raw pack committed, `.gitattributes` covers `.wav` before the first one lands (check). Alerts per docs/02 "HUD" ("Your base is under
   attack": text, a minimap ping, a sound; Space jumps the camera to the last alert, docs/02 Controls l. 349). Music stays the owner's
   (placeholder music = the hook and the bus, no track). The view's existing `SfxEvent` plumbing (M2 tones) is the seam. Text from
   `ui.json` (rule 32). QA standard; the smoke must stay silent of `ERROR` on import.
3. Then save / load when Requests 42 lands, teams when 38 lands, `tools/export.ps1` when the owner's templates arrive, criterion 7's
   hand-made maps when the sim ships a map file format (ask: Requests), the art pass when the owner answers the look call, criterion 6.

### Debt left by M6-VH3 (next view hardening)
BUG-0922 (S4: the four-seat zero band covers the x axis; the contrast row can't fail; 13 labels past 16 h), BUG-0550 item 2 (the art
pass), BUG-0521 items 2-3 (unreachable), BUG-0743 item 3 (wontfix candidate).

### Watch-outs (view)
- `scene-loop.ps1` now has `-TimeoutSec` (default 600): a hung scene is FAIL "timeout", no Godot process left. Use `-Filter` for one scene.
- `QaM6V11Test` runs 21,600 ticks (BUG-0921): ~93 s; the loop's slowest rows are `M3PlayableTest` (~2 min) and this.
- A clone without `git lfs pull` fails `LookTest` cleanly now (exit 1, "could not load ... LFS pointer").

---

## Data track

### Next session: STOP (cheap) unless the inbox has an answer

- Nothing unblocked: the D14 b openings need the strategies schema (planned as the sim's next task above: **the session after next, the
  data track writes the openings** for both factions against docs/factions sketches and docs/02 "AI opponent"); the 12th-house `ai.json`
  step is parked (moves the golden: batch with the next hash-moving data change, which D14 b's schema may be); the M7-M9 rosters need
  schemas the roadmap hasn't reached.
- **An inbox answer first** if present: D19's table, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780, "densest cluster".
- **Rule 31 at the next integration:** the sim's BUG-0910 fix can move the spending cells (a requirement-blocked Build goes out sooner);
  if the full suite shows `AiSpendingReportTests` / `D20PinQaTests` red on the merged tree, the conductor dispatches the re-print as
  this session did (D-R31: `docs/factions/*.md` only, no `game/data` change, push when the full non-Perf suite is green).

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/03: each track appends its own rows; the conductor unions them.
- Builds share the machine: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only when
  it fails again alone.
- BUG-0914 / BUG-0915: if the H3 0-byte probe or the `buildingSight` loader row fails in another full run, it becomes S3 and the sim's
  first item. Avoid running the view's scene loop against the sim's full suite on the same disk when a clean number matters.
