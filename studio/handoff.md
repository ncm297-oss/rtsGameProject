# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-05-0805** (session 1 / 8 on 2026-11-05; the third resume of
2026-10-27-0658, whose branches carried the work). **Sim M6-S5 ACCEPT (on `main` as 1a90a08); view M6-V16 ACCEPT on its branch, then
ESCALATE at integration: BUG-1064 (S2, test-only), held on `origin/studio/2026-10-27-0658-view` (2d4dbe4, `main` already merged in);
data STOP (cheap).** Counters after: **sim 2 / 4, view 0 / 4 (1 / 4 once M6-V16 lands), data 1 / 4.** **Open S2: BUG-1064 only.**
`main` = 1a90a08: the sim branch's full non-Perf suite 7,674 / 0 / 14 (Producer); the merged view branch: build 0 warnings, non-Perf
7,692 / 0 / 14, loop 75 / 76 (conductor). Golden `data-hash` **4F8CCF2688F9F22F** (moved once this session: `ai.gatherReach`; `k`
lines identical). **M5 4 / 9, M6 3 / 10** (criterion 1 ticks when M6-V16 lands). Bug ids next: **sim from 1050, view from 1065, data
from 1081**.

## Where we are

- **A 2v2 will be playable from the menu once the view branch lands (next session):** Skirmish → four slots → Team 1 / 1 / 2 / 2 →
  Start. On `main` today: allies share vision, never attack each other, the AI ally scouts away from its partner's base and gathers
  near home (`ai.gatherReach` 64 m); on the held branch: the Team column, the banner per side, the stats by team, Load Game's "(T1)".
- **BUG-1064 (S2, test-only):** `FogViewTest.AllyGhost` (view, BUG-1062 item 3's row) asserts the ally's hall is a known ghost before
  its shared-vision skip; an ally's building is never a ghost since M6-S5, so the row fails on the merged tree. One-line fix (move the
  skip above the check) or drop the row.
- **Save format 3, replay format 7** (a free-for-all still writes 6). Rule 36 (frozen save / replay API) held on both tracks.
- **Open S3 (new):** BUG-1047 / 1048 (sim: the gather reach's two blind spots, one decided rule: the reach lifts when the head ring
  is seen, a kind with no node in reach takes the nearest known one; the fix goes before the map-file format), BUG-1063 (view branch:
  four label rows push the stats screen's Back half off 1152 × 648). **S4:** BUG-1049 (sim nits), BUG-1062 item 2 (view branch: a
  one-team setup never ends; **decided: refuse it at Start** with a `ui.json` reason). BUG-1061 / 1062 / 1063's files and index rows
  are on the view branch, not on `main`, until it lands.
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Standing rules (1-37)

Unchanged. Rule 31 was satisfied this session without a re-print: the 27 pairing runs print the same on 424a5e8 and on head.

---

## Sim track

### Current session plan (next session)

**Feature (counter 2 / 4 → 3 / 4): M6-S6, Request 43: starting resources.** QA full (the save / replay format moves; AI openings at
"high"). Bug ids from 1050. Base `origin/main` after both merges.

**Goal.** docs/02 "Game flow" 2's "starting resources (standard / high)": a preset per match, chosen in the setup (the view's half comes
next view session), carried by the save and the replay, and the AI's openings behaving at "high" (no idle bank).

**Scope:**
1. `rules.json` `startingResources` presets (ids, `displayName`, gold / wood each; validation; in `ContentHash`); `SimConfig.StartingResources`
   (an id or index, validated); `World` seeds each seat's gold / wood from it at tick 0 (today's numbers become the `standard` preset,
   bit for bit: the FFA pins and the golden `k` lines must not move for `standard`).
2. **Save format 4 / replay format 8**: the preset after the team bytes (save) / a header line (replay); format 3 / 7 read as `standard`;
   `Validate` refuses an unknown id. Rule 36: no new signature; `SaveSummary` (view-owned, `ViewApi/`) reads bytes **after** the team
   bytes, so put the new field after them and name its offset in docs/03 for the view.
3. The AI at `high`: a pairs-report row (`AiOpeningPairsReportTests` or a new `Stress/` row) showing each opening still spends its
   start (no bank over the `standard` run's 10-minute bank × 2 at 10:00); if an opening idles, the executor's reserve rule is the lever,
   not the data (rule 31: the data track re-prints if cells move at `standard`; they must not).
4. Golden regen once (the hash moves: the new `rules.json` field), reason in the commit; move the `4F8CCF2688F9F22F` pins.
5. Docs: docs/02 "Game flow" 2 (what the presets are), docs/03 a "Implementation (M6-S6)" section + the save / replay format notes,
   `studio/qa/coverage.md` row, the bug index.
6. **OUT:** the setup's list (view), per-slot presets, a "low" preset (owner's taste), BUG-1047 / 1048 (M6-SH1 or the map format), any
   `ViewApi/**` or `game/**` change outside `rules.json`.

**Acceptance criteria:** (1) build 0 warnings, the full non-Perf suite green, Perf green alone; (2) presets loaded, validated, hashed;
`standard` equals today's start bit for bit (the five FFA pins and the golden `k` lines unmoved); (3) `high` gives every seat the preset's
gold / wood at tick 0 and a human-only `high` scene hashes differently from `standard` only by the ledger; (4) save format 4 and replay
format 8 round-trip the preset; a format-3 save and a format-7 replay load / play as `standard`; the forged-id refusals; (5) the AI's
openings at `high`: the pairs row, no opening banks idle; (6) docs + the index; (7) no Godot / wall clock / unseeded RNG; nothing in
`ViewApi/**` or `game/**` but `rules.json`.

**Design references:** docs/02 "Game flow" 2, docs/03 "Save format (M6-S2)" + "Implementation (M6-S4)" (the header's tail), docs/03's
`rules.json` knob table, docs/03 "Testing strategy" (golden). **Tests required:** `DataValidationTests` / `DataContentHashTests` rows,
`SaveFormatTests` / `SaveRefusalTests` / `ReplayFormatTests` rows, `SharedVisionFfaPinTests` unmoved, a `high` determinism twin, the
pairs row.

**Constraints most at risk:** rule 36 (frozen save / replay signatures); `SaveSummary`'s offsets (the view reads the header by shape);
no per-tick allocation; the golden regen in the same commit as the field.

### Next task candidates (in order)

1. **A map file format** for M6 criterion 7's three hand-made maps (`game/data/maps/`, hashed in the header) — **BUG-1047 / 1048's rule
   goes in first** (a hand-made map can put the nearest forest past 64 m).
2. **M6-SH1 (sim hardening at 4 / 4):** BUG-1047 / 1048 (if not done), BUG-1049, BUG-1044's rest, BUG-0960's throw, the movement S3s
   (BUG-0911 / 0889 / 0144), BUG-1022 / 1026 notes.
3. M5-4c slice 2; M5-4d (expansion, rebuilding, high-ground scouting, hunting the last units); BUG-1010's lever; D14 c; M5-5.

### Watch-outs (sim)

- `ViewApi/SaveSummary.TryRead` reads the team bytes right after the tick and refuses a team byte over 8: a new header field must come
  **after** them, or the view's list breaks.
- `GatherWedgeQaTests.SameGameDataHashes` is the pin that moves with every `rules.json` field; `ReplayGoldenTests` regen with
  `RTS_REGEN_GOLDEN=1`.
- `AiGatherMarkQaTests`' gone-node scene runs at `gatherReach` 512 by design (its subject is the mark).
- The soak's `RaidedTicksIfStarved` 2,000 sits between 1,957 (reach-off failures) and 2,570 (lowest exempt seat): a raid-behaviour
  change moves a row across it (BUG-1049 item 3).
- `VisionGatePerfTests` fresh-process row 3.66-3.69 ms against 4 ms; two tracks = two test hosts: rerun a Perf flake alone.

---

## View track

### Current session plan (next session)

**M6-V16 (resumed, third time): fix BUG-1064, rerun the loop, land · feature (counter 0 / 4 → 1 / 4 when it lands) · QA standard
(re-check) · bug ids from 1065.** Work on `origin/studio/2026-10-27-0658-view` at 2d4dbe4 (it already contains `main` 1a90a08; do not
rebase). The task is accepted on its merits (session log); only the merged tree's scene loop is red.

**Scope:**
1. **BUG-1064:** `game/tests/FogViewTest.cs` `AllyGhost`: the check `CanSeeBuilding(0, hall) && Ghosts(0)[hall].Known` (~line 736)
   runs before the shared-vision skip (~line 744). Either move the skip above it (`CanSeeBuilding` true and `Known` false = an ally's
   building, allied-as-own since M6-S5: print the skip line, end the match) or drop the row and record in docs/03 "Implementation
   (M6-V16)" + BUG-1062 item 3 that the ally-ghost branch of `SelectionController.EnemyAt` is unreachable (an ally's building is never
   a ghost while it stands and collapses when the ally is out). Prefer dropping it: a row that always skips proves nothing.
2. `powershell -File tools/qa/scene-loop.ps1` whole (76 scenes) and `tools/qa/smoke.ps1` on the merged branch: both green.
3. **Riders only if a few lines each:** BUG-1063 (S3: shrink the graph's minimum height when the stats table has four label rows, or
   put the label in the group's first row's player column; flip `QaM6V16Test.StatsLayouts`' `Known("BUG-1063", ...)` row to a check);
   BUG-1062 item 2 (decided: Start disabled while every shown slot is on one team, a `ui.json` `setup.*` reason line, a `TeamsTest`
   row; restage `TeamsTest.TeamFlagStart` on a two-team match). Else leave them for M6-VH5.
4. **OUT:** anything in `sim/**` (the ViewApi files are done), team colours, the starting-resources list (M6-S6 first).

**Acceptance criteria:** (1) build 0 warnings; the whole scene loop green on the merged branch (a wall-clock flake counts only if it
fails alone); smoke PASS; (2) `FogViewTest` PASS with the ally-ghost row skipping with a printed line or removed, and docs/03 + BUG-1062
item 3 say which; (3) the M6-V16 criteria of this session's brief still hold (`TeamsTest`, `TeamFilesTest`, `QaM6V16Test` PASS; the
ViewApi suites green); (4) riders, if taken, each with a scene row; (5) nothing in `sim/**`.

**Design references:** docs/02 "Vision and fog of war" (an ally's building is always seen), docs/03 "Implementation (M6-V16)" tests
paragraph. **Tests required:** `FogViewTest` green; the loop. **Constraints:** none new; the merged branch must not be rebased.

### Next task candidates

1. The starting-resources list in the setup (after M6-S6): `--resources <id>`, the list from the sim's presets (`displayName`), the
   save / replay rows in Load Game.
2. (e) `tools/export.ps1` once the export templates are downloaded; (f) criterion 6's music once a track is dropped in.
3. The three maps' setup list once a map format exists.

### Watch-outs (view)

- **Run the scene loop on the merged tree before claiming green:** this session's QA and Producer both ran the scenes on the view
  branch alone (no shared vision there) and `AllyGhost` passed; it fails on the merge (BUG-1064). When a view task's scenes depend on sim
  behaviour landing in the same session, loop a scratch merge with the sim branch (the 0158 / 1236 sessions did).
- `FogViewTest.AllyGhost`'s premise is gone: an ally's hall is never a ghost since M6-S5 (`UpdateGhosts` skips allies); the ally-ghost
  branch of `EnemyAt` is unreachable.
- `ViewApi/SaveSummary` hard-codes the header's shape: the team bytes are after the tick (format 3); M6-S6 adds a field after them.
- `Match.ResultFor` in a 2v2: no banner while the local player is out and an ally fights on (by design, docs/02 "Game flow" 4).
- `scene-loop.ps1 -Filter` is a file glob; the loop is 76 scenes now, ~23 min.

---

## Data track

### Current session plan (next session)

**STOP (cheap, counter stays 1 / 4)** unless the inbox's **New** carries an answer (D21 names / descriptions / the Malazan standard wave,
D19's table, the Shock pass, `goldShare`, BUG-0780) — each a small page + `ai.json` / data task with a review table (golden regen once)
— or M6-S6's `startingResources` presets land on `main` (then: the `displayName` / `description` text of the presets, a review table,
and the pages' one line on what the AI does at "high"). BUG-1080's two wordings fold into the next page-touching task. **No rule-31
re-print is owed:** M6-S5 moved no pairing cell (27 runs identical on 424a5e8 and head).

### Next task candidates

1. The owner's answers above.
2. The `startingResources` text + the pages' "high" line after M6-S6; BUG-1080 folded in.

### Watch-outs (data)

- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells`.

---

## Watch-outs (all tracks)

- The view branch is already merged with `main` 1a90a08 (2d4dbe4; the docs/02 / docs/03 / docs/01 / index / coverage conflicts resolved
  with both sides kept). When it lands, `studio/bugs/README.md` needs BUG-1064's row (added on `main` at this integration) kept beside
  the view's 1061-1063 rows; the conductor merges `main` into the branch first if `main` moved.
- The next view session's ACCEPT re-reads nothing of M6-V16's merits (accepted here); it checks the loop on the merged tree, BUG-1064's
  row, the riders if taken, and then ticks M6 criterion 1 and sets the view counter to 1 / 4.
- Two tracks a session: two test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- **The owner's recordings made since 2026-10-19 refuse to play** (the data hash moved with `ai.gatherReach`); saves from before M6-S4
  refuse too (format 3).
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase and commits each dev's work at every phase
  boundary (the 0658 / 2050 / 0749 deaths: `sessions/2026-10-27-0658-incident.md`).
