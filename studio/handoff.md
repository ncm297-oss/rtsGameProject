# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-05-2148** (session 4 / 8 on 2026-11-05; base `60dbd57`). All three tracks
accepted: sim **M6-SH1** (the hardening, 1 fix round), view **M6-V18** (the Map list), data **D23** (Raraku's text; D22 rode in with it).
**Open S1 / S2: none.** Golden `data-hash` header stays **30C51BB619A7E650** (informational since M6-SH1; the shipped hash differs and
that is fine). Bug ids next: **sim from 1060, view from 1094, data from 1103.** Counters: **sim 0 / 4, view 3 / 4, data 3 / 4.**

## Where we are

- **Playable from the menu:** a 1v1 to 2v2 against AIs, Standard or High starting resources, teams, game speed, **the Map list (Generated
  or Raraku)**; save / load / Watch carry the map; Load Game names it. Raraku's description is final; its layout is still the M6-S7 placeholder.
- **M6 4 / 10.** Criterion 7 needs Pale Hills and Vathar Crossing (sim slice 2) + their text (data) + mirrored-terrain tooling. The rest of
  M6 waits on the owner (the art call, music / fonts / export templates) or on the export script (e) once the templates exist.
- **The golden replay is decoupled** (docs/03 "Testing strategy"): a data change regenerates `cross_map_seed1.replay` only when a `k` line
  moves; `GatherWedgeQaTests.SameGameDataHashes` is gone. **Rider:** BUG-1059 (S4) pins the header literal in `GoldenDecouplingQaTests`;
  until the next sim hardening removes it, a deliberate regen edits that literal in the same commit.
- **The reach rule is complete:** replacements bounded by `ai.scoutReplacements` (2), the re-scout for a kind with no known node, the
  released scout. Open on it: **BUG-1057** (S3: the "sent again" mark is invisible when the hall's centre is exactly a scout grid centre;
  unreachable on generated maps, reachable on a hand-made 256 map's quarter points: **a rider of map slice 2**), **BUG-1058** (S3: the
  released scout gathers at the node nearest itself, 8 / 25 inside an enemy's door; still a net gain).
- **Open S3:** BUG-1057, BUG-1058, BUG-1010 (sim); BUG-1092 (view, command line only: the one-team check runs before the map cap),
  BUG-1093 (view, a `TeamsTest` GodotSharp flake in the full loop). **S4:** BUG-1059 (sim), BUG-1044's rest, BUG-1081 item 2 (view tooltip
  width), BUG-1102 (data: "centre" v American "center"; the map checker lacks the ratio words; docs still say "placeholder" for Raraku).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 / D22 / **D23** review tables, the Shock pass, `goldShare`,
  BUG-0780, "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Integration of 2026-11-05-2148 (the conductor's recipe; the next PLAN verifies `main` against it)

Rehearsed by the Producer on a scratch clone (build 0 warnings, the golden / data / save filter 646 / 646, smoke PASS, `MapListTest` and
`QaM6V18Test` PASS). Merge order **sim → view → data**:

1. **Sim** `studio/2026-11-05-2148-sim` (b44ae87 + the Producer's state commit): fast-forward.
2. **View** `studio/2026-11-05-2148-view` (fe9267e): conflicts in `docs/02-game-design.md` ("Map and terrain": take the sim's paragraph and
   append the view's last sentence "The skirmish setup's Map list offers every shipped map file ("Game flow" 2, M6-V18).") and
   `studio/qa/coverage.md` (keep both sides at both hunks).
3. **Data** `studio/2026-11-05-2148-data` (935ae30): `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` → **the sim's** (`main`'s);
   **`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` → `main`'s** (`git checkout HEAD -- <file>` after the merge: git auto-takes the
   data's regen, which would fail `GoldenDecouplingQaTests.TheCheckedInHeader_*`); `studio/qa/coverage.md` keep both;
   `studio/bugs/README.md` keep both appended blocks but **one BUG-1081 row, the data's** ("open (item 1 fixed by D23 ...)").
4. After: `tools/qa/smoke.ps1`, the filter `FullyQualifiedName~ReplayGoldenTests|GoldenDecouplingQaTests|GatherWedgeQaTests|DescriptionWordingQaTests|DataValidationTests|MapTextContentTests`
   and the scene loop on `main` (the view's criterion 1 asked for the loop on the merged tree; `TeamsTest` / `FreeForAllTest` rerun alone
   before filing).

## Standing rules (1-37)

Unchanged. **Rule 38 (new, from this session's PLAN lesson): the Producer runs every suite on a scratch `git archive` export (or a scratch
clone), never in a worktree:** a test host in a worktree locks `bin/` and fights the dev's and QA's builds.

---

## Sim track

### Current session plan

None yet (the next PLAN writes it). **Candidate: M6-S8, map format slice 2** — Pale Hills (4 players, 256, forest: dense woods with paths)
and Vathar Crossing (2-4 players, 256 or 128, steppe: a river of slopes with fords), a mirrored-terrain helper (`tools/` or a test-side
generator script, not shipped code), `AiScenes` rows on each (Normal v Normal to the end), **BUG-1057 as a rider** (a 256 map's quarter
points are scout grid centres: fix the mark, e.g. offset it by a quarter cell when the hall's centre is a grid centre, with QA's skipped
row un-skipped), the data track writes their text after. Size: clear design (docs/02 "Map and terrain" table, docs/03 M6-S7's format), up to
1,500 lines; each map file is ~260 lines of digits. QA full (the start contract, the flood, the AI's openings on each map, the formats
byte for byte on generated maps). Then: M5-4c slice 2, M5-4d, BUG-1010's lever, D14 c, M5-5; BUG-1058 / 1059 at the next hardening.

### Watch-outs (sim)

- `ScoutManager.SentAgain` reads the mark from `Board.ScoutPoints`; `Waypoints(replacement: true)` lays it. Any change to the grid's
  layout (`GridPoint`) moves the mark's meaning: the save rows `ASaveBetweenTwoScoutDeaths_*` and QA's `ASaveWhileAScoutSentAgainIsOut_*`
  must stay green.
- The 20 `StandardPresetPinQaTests` rows, 5 `SharedVisionFfaPinTests`, 27 pairings: unmoved this session; a map slice touches none of
  them unless `MapGenParams.Default` or the start contract changes.
- `Replays/premap/` and `Save/PreMapFixtures` pin ab54ddb's bytes; no format change is planned.
- `VisionGatePerfTests` 3.66-3.69 ms against 4 ms and `PropsMeasureTests.MinimapRefresh_*` flake beside another host: rerun alone.
  `Stress/RarakuTwinStressTests` is slow (12,000 ticks × 2); a second and third map double that: consider `Category=Stress`.

---

## View track

### Current session plan

None yet. **Candidates:** (1) **M6-V19 (feature, counter 3 / 4 → 4 / 4):** BUG-1092 (run the one-team check after `Match` caps the players
on a map file: warn and clear the teams as BUG-1066 does; a `QaM6V18bTest` row un-flagged), the greyed Forests / Gold mines showing the
picked map's own counts (6 / 12 for Raraku) instead of the generator's 12 / 8 (QA's note), `--map-size` on a map file warned like
`--players`; small, QA standard. (2) **M6-VH5 (the hardening at 4 / 4):** BUG-1093's flake (a retry or a wait on `GD.Load` in the loop),
BUG-1081 item 2 (wrap the long tooltips: Raraku's is 180 characters), the loop's `FreeForAllTest` startup crash. (3) Then (e)
`tools/export.ps1` once the templates are downloaded, (f) criterion 6's music, the sound re-cuts when the owner listens; Pale Hills /
Vathar Crossing appear in the list by themselves once the sim ships them.

### Watch-outs (view)

- The scene loop is 81 scenes (~20 min); run it on the merged tree. Recordings made before a data-hash move refuse afterwards (re-record
  QA fixtures on the merged tree).
- `SkirmishSetup` layout: four slots must fit 648 px; the Map row shares a grid row with Map size and Game speed with Starting resources.
  Start's bottom at 633 / 669 (1152 × 648 / 1280 × 720) with four slots.
- `ReplayStore.IsMapToken` accepts any non-space token (a path shows raw in Load Game, Watch refuses it): harmless, noted by QA.

---

## Data track

### Current session plan

None yet. **Candidate: D24 (feature, counter 3 / 4 → 4 / 4), QA light:** BUG-1102 item 1 ("centre" → "center" in `raraku.json` + the pin
in `MapTextContentTests.Pinned`; **Producer default: American spelling, as the rest of the data**, unless the owner answers "spelling:
British" in the inbox) and item 2 (the ratio words "twice" / "double" / "triple" / "half" in the map checker, shared with the presets'
`NumberWords`); no golden regen needed any more (say so in the report: the first data task under the decoupled golden). Then Pale Hills /
Vathar Crossing's text when slice 2 lands; the owner's answers (D21 / D22 / D23 tables, the Shock pass, `goldShare`, BUG-0780) whenever
they arrive; D14 c; M7's rosters.

### Watch-outs (data)

- **No golden regen for a text or knob change** from now on (docs/03 "Testing strategy"); if a `k` line moves, the sim track is involved.
  BUG-1059's literal is the sim's to remove.
- `rules.json` and `game/data/maps/*.json` are the sim's files; a data task touching their text keys is a named exception in the brief.
- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.

---

## Watch-outs (all tracks)

- Three tracks a session: three test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- **The owner's recordings and saves made before 2026-11-05-2148 refuse to play** (the shipped hash moved: two `ai` knobs, D22 + D23's text).
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase and commits each dev's work at every phase boundary.
