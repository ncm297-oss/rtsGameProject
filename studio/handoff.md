# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-06-0326** (session 1 / 8 on 2026-11-06). **All three tracks ACCEPTED, 0 fix
rounds:** sim **M6-S8** (Pale Hills + Vathar Crossing + `mirror-map` + BUG-1057), view **M6-V19** (BUG-1092 + the map's own counts +
`--map-size` warned), data **D24** (D22 + D23 landed on `main` at last, BUG-1102 items 1-2, the id-based pin; **conditional: BUG-1103**, one QA row on the data
branch pins the shipped map count at 1 and reads 5 on the merged tree: a one-line fix before the data merge). Merge order sim → view → data;
the Producer rehearsed it on a scratch clone (conflicts only in `studio/qa/coverage.md` and `studio/bugs/README.md`, both sides kept):
build 0 warnings, see the session log for the suite numbers. **Open S1 / S2: none.** Golden header **30C51BB619A7E650** (informational;
byte-identical). The shipped data hash moved twice (two map files, three descriptions): **every recording / save from before refuses.**
Bug ids next: **sim from 1070, view from 1094, data from 1103.** Counters after this session: **sim 1 / 4, view 4 / 4, data 4 / 4**
(view and data hardenings next, M6-VH5 and D-H7).

## Where we are

- Playable from the menu: 1v1 to 2v2 (4-player free-for-all too) against AIs, Standard / High, teams, speed, the Map list now **Generated /
  Pale Hills / Raraku / Vathar Crossing** with their final (Raraku, presets) or provisional (the two new maps) sentences as tooltips.
- **M6 5 / 10: criterion 7 ticked this session** (three maps + the generator in the setup + the mirror tooling). Left for M6: the art pass
  (the owner's call), music / fonts / export templates (owner downloads), `tools/export.ps1`, .NET revisit, a friend's playtest. M5 4 / 9.
- **Known rough edges on the new maps (S3, not blocking):** BUG-1068 Pale Hills Normal v Normal reaches a result on seed 1 only (seeds 2-6
  stall: the loser is razed to its last unit out in the woods and AI waves target buildings only; seed 2 a true stalemate); BUG-1067 a
  seat whose three scouts all die at the door can go 13+ min without wood (pre-existing; the hall's 12 m sight is short of the 22 m
  woodlot). Both point at M5-4d (hunt the last units, expansion) as the sim's next feature.
- **PLAN-time chores for the Producer (do at the next PLAN, nothing for a builder):** (0) **BUG-1103:** check the data branch landed on `main`
  with the one-line fix (close the bug file + index row); if the conductor held it, the data track's step 0 is the fix + the merge. (1) `studio/bugs/BUG-1102-*.md` + its index row →
  `fixed` in full (items 1-2 D24, item 3 the sim's M6-S8 docs wording: docs/02 ~l.343 "Raraku's layout is a placeholder", docs/03's M6-S7
  paragraph "its layout a placeholder"); the file is on the data branch so it was left alone at this ACCEPT. (2) Verify the merged
  `main` carries both sides of `coverage.md` / `README.md` (the sim's BUG-1067 / 1068 / 1069 rows, the data's BUG-1081 / 1102 rows).
  (3) Trust-but-verify candidate: `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks` carries `[Trait("Category","Stress")]`
  (docs/03 corrected by the Producer, BUG-1069).

## Standing rules (1-38)

Unchanged (38: the Producer runs every suite on a scratch `git archive` / clone export, never in a worktree). **New 39:** a size budget
"excluding generated map content" means terrain rows, forest rows and the tool's source quarters do not count, nor do QA's suites;
the developer's hand-written lines do (BUG-1069 item 3's ruling).

---

## Sim track

### Next task candidates (counter 1 / 4; feature session)

**First choice: M5-4d slice 1, "finish the match": the AI hunts an enemy's last units once it knows no enemy building, and expands to a
known mine when its start mines run low** · feature · QA full · ~1,200 lines. Why now: BUG-1068 (Pale Hills: 4 of 5 seeds end with the
loser razed and 1 unit alive, the generated map's seed 6 the same) and docs/05 M5 criterion 3 (expansion / rebuilding / hunting). Scope
sketch: `MilitaryManager` wave target = the last-known enemy unit position (the AI's own fog-filtered last-known list, nearest to the wave)
when the enemy has no known building and the match isn't over; a sweep of the enemy's start quarter when nothing is known; expansion:
when the seat's start mines hold under `ai.expandBelow` gold (new knob, data), place a drop-off (the faction's) by the nearest known mine
not within `ai.workerFleeRadius` of an enemy building; the 20 + 5 pins and 27 pairings may move (say which, the golden `k` lines must not
unless a pinned match changes: then regenerate in the same commit and say why). Acceptance: `MapSlice2StressTests.NormalVersusNormal_KnownStalls_EndWithin72000`
un-skipped with Pale Hills seeds 3 / 4 / 6 ending (seed 2's stalemate may stay listed), the generated seed 6 ending; BUG-1068 `fixed` or
narrowed to seed 2. **Rider if a few lines:** BUG-1067 (a seat capped with no known forest: let the hall's own sight reach its woodlot,
or gatherers search `ai.gatherReach` round the hall once the reach lifts; measurement row `EveryScoutKilledAtTheDoor_FirstWood_ByMap`).
- Else: M5-4c slice 2 (the three Hard knobs), BUG-1010's lever, D14 c's schema, M5-5's 20-minute test.
- At the next sim hardening (counter 4 / 4): BUG-1058 (the released scout's node), BUG-1059 (the header literal), BUG-1044's rest, the
  movement S3s (BUG-0911 / 0889 / 0144), BUG-1067 if not taken as a rider.

### Watch-outs (sim)
- Three maps now: `GameData.Maps` order is `pale_hills`, `raraku`, `vathar_crossing`; look maps up by id (`FindMap`), never `Maps[0]`.
- A map file draws nothing from the map stream, so seeds that pick the same openings play the identical match (QA note): "five seeds"
  on a map file is three distinct matches at most. Pick seeds by opening pair when a brief wants distinct matches.
- `ResourceStore.DefaultCapacity` 4,096 caps a map's nodes (Pale Hills 3,812): a map with more trees needs a sim change (not planned).
- Two 256-map matches to the end are ~20 s each under `Stress`; `MapSlice2StressTests` adds ~10 min; the non-Perf suite is ~30 min alone.
- `VisionGatePerfTests` 3.66-3.69 ms against 4 ms flakes beside another host: rerun alone.

---

## View track

### Next task: M6-VH5, the view's hardening (counter 4 / 4 → 0 / 4) · QA standard

Debt, most valuable first:
1. **The 82-scene loop on the merged `main` with three maps** (the first look at Pale Hills / Vathar Crossing in the list: "Pale Hills
   has room for 4 players" fits the 300 px holder; four slots on Pale Hills from the menu; a 256 map's match from the menu with ~3,800
   tree views: the view's frame cost at match start and the minimap at 256; QA's screenshots).
2. **BUG-1093** (S3): `TeamsTest`'s one-off GodotSharp "Handle is not initialized" in the full loop; find the cause or make the row robust.
3. **BUG-1081 item 2** (S4): the 150-180-character tooltips; the dev judged an `OptionButton` popup tooltip can't wrap: pre-wrap the data
   text at ~60 characters in the view (a helper, tests compare through it), or show the sentence in a label under the list if the layout
   has room; say which.
4. The `FreeForAllTest` startup crash watch (0xC06D007F once beside three hosts): file if it repeats.
5. Docs nits: docs/02 ~l.488 and docs/03 ~l.6825 past the ~120-column wrap (M6-V19 QA).
- OUT: the export script (owner templates), music, the art pass, any sim change.

### Watch-outs (view)
- `MapListTest` / `QaM6V18Test` / `QaM6V19Test` look maps up by id (QA verified on the branch with one map); the merged tree is the first run
  with three. The setup's greyed counts widen the SpinBox range for a map past 64 (Pale Hills 60 forests: inside the cap).

---

## Data track

### Next task: D-H7, the data's hardening (counter 4 / 4 → 0 / 4) · QA light — or D25 first if the Producer prefers the text to land

**Step 0 if the integration did not do it: BUG-1103** (S2, test-only): `sim/Rts.Sim.Tests/QA/Content/D24MapPinQaTests.cs` line 49
`Assert.Equal(3, d.Maps.Length)` → `Assert.Equal(TestSim.Data.Maps.Length + 2, d.Maps.Length)`; if the data branch was held, the next data
session cuts from `main`, merges `origin/studio/2026-11-06-0326-data`, applies the line, runs the data filter, and commits "D24: land + BUG-1103".

The data's debt is small; the Producer may fold D25 into the same session as the hardening's "feature-sized" item, or run D-H7 cheaply:
1. `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25}: re-measure on today's `main` (the
   AI knobs moved since) and take out any case that passes.
2. The pages' claims that reference the generated map's numbers: a sweep for lines the map files make stale (none expected; say so).
3. BUG-1102 closed in full (the Producer does the file at PLAN; the data track adds nothing).
**D25 (feature, after or with D-H7):** Pale Hills' and Vathar Crossing's final `description` (≤ 200 characters, no digit, no ratio word, no
faction word, the checker), pinned in `MapTextContentTests.Pinned`; a review table for the owner; the provisional sentences are the sim's
(studio/handoff.md of 0326, docs/03 "Implementation (M6-S8)"). The data hash moves once; no golden regen (decoupled).

### Watch-outs (data)
- The pin is id-based now: pin only ids you list; the checker runs over every shipped map.
- `rules.json` and `maps/*.json` are the sim's files: touch only the `description` keys (the named exception).

---

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rerun alone before filing.
- Merge order sim → view → data; studio lists (`coverage.md`, `bugs/README.md`) are the usual conflicts, both sides kept.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase.
