# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-07-0236** (session 1 / 8 on 2026-11-07; base `052c39e`). All three tracks
accepted: sim **M5-4d slice 2** (rebuilding + the high-ground look; D25's map text landed; BUG-1111 fixed; **M5 criterion 3 ticked, M5
5 / 9**), view **M6-VH6** (BUG-1114 fixed, BUG-1115 items 1-3), data **D-R33** (both `KnownBeatenAlive` allowances out on the base,
BUG-1104 fixed). Two test-only merge-time rounds, both the Producer's patches rehearsed on a merged clone: the sim's 4-seat Pale Hills
seed-2 row a skipped known stall (BUG-1116 / BUG-1068) and the data's seed-19 allowance re-listed (the sim's rebuild stands that side
up again at 20:00). **Open S1 / S2: none.** Counters after: **sim 3 / 4 (feature), view 0 / 4, data 1 / 4.** Bug ids next: **sim from
1120, view from 1127, data from 1137.** Save formats **6 / 7** (unchanged), replay format unchanged, golden unchanged, the data hash
unmoved (map descriptions are outside it). Inbox **New: empty** at this ACCEPT.

## Standing rules (1-41)

Unchanged 1-40 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content and
QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit `docs/factions/**`
for those cells only). **New 41:** when a sim task can move a measurement a data test allowance rests on (the `KnownBeatenAlive` lists,
the pairing tables' "defeated" cells), the sim brief names the allowance and the data task that touches it re-measures on the sim's
branch, not on the base; otherwise the two are not planned in the same session. (D-R33 emptied the seed-19 entry on `af7047b` while
M5-4d slice 2 put that side back on its feet: green on each branch, red on the merged tree.)

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only
  when it fails alone.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- A developer must never kill another track's Godot or `testhost` process. The scene loop's own timeout handles a hung scene.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase. A sim fix round costs ~45 minutes of suite;
  plan the sim's slice small enough for one.
- **Pinned matches that move with any AI change:** `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks` (Pale Hills
  seed 1 at **34,219**, Vathar at 22,794), `MapSlice2StressTests.KnownStalls` (Pale Hills 3 / 4 / 6 at 29,562, 5 at **55,631** of
  72,000, generated 6 at 26,034), `StandardPresetPinQaTests` (7 digests), the Malazan / Whirlwind page tables (rule 40), and now the
  **`KnownBeatenAlive` lists** (rule 41). Any sim task touching the AI says which moved and why.
- **Known stalls on Pale Hills seed 2** (both skipped with ids): `MapSlice2StressTests.NormalVersusNormal_PaleHillsSeed2_Stalemate_...`
  (2 seats, BUG-1068) and `HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall` (4 seats, BUG-1116 / 1068 / 1119). The sim's
  hardening re-runs both after BUG-1119 and the lone-idle-worker hunt gap.

---

## Sim track

### Next: M5-4c slice 2, the Hard knobs · feature (counter 3 / 4 → 4 / 4) · QA full

**Goal.** M5 criterion 4 (Easy / Normal / Hard per docs/02 "AI opponent"): the three Hard knobs the data already carries
(`ai.json` difficulty blocks: `gatherBonus`, `focusFire`, `pullBackWounded`; D16 pinned them to docs/02 and the faction pages).
Design clear from docs/02's difficulty table; ~800-1,000 lines; one slice.

**Scope (the dev reads docs/02 "AI opponent" and docs/03 "Implementation (M5-2)" / "(M5-3)" / "(M5-4f)" first).**
- `gatherBonus` (Hard gathers faster): an exact, hashed carry on the AI seat's gathering (no float drift between runs: an integer
  accumulator per worker or per seat, saved in `AIST` if it must persist → **save-format bump 8 / 9** with `SaveHeader.VersionFor`,
  the refusal rows and the fixtures, said in the report; prefer a rule that needs no new saved state).
- `focusFire` (Hard concentrates fire): the wave's members `Attack` the one enemy unit the wave picks (lowest hp in reach, ties by slot)
  instead of each its nearest; Normal / Easy unchanged.
- `pullBackWounded` (Hard pulls wounded back): a member below a fraction of hp (a `rules.json` `ai.*` knob, hashed, validated,
  `DataValidationTests` rows) walks to the rally and returns when healed or after a time.
- **Rider, BUG-1119 (S3) only if a few lines:** at the census, drop every remembered enemy building whose owner is defeated (a read-only
  `PlayerView.IsDefeated(player)` if none exists; the defeat is public); then re-run `HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall`
  once (un-skip it only if it ends by 144,000; else leave the skip and say the numbers).
- **OUT:** the house rule (BUG-1116's hardening item), expansion rebuild, re-scouting, any view change, `game/data/factions/**`, the
  data's test folders.

**Acceptance criteria.** 1. Build 0 warnings, non-Perf green, Perf alone green, smoke PASS. 2. Built-scene tests by the command log for
each knob (Hard does it, Normal doesn't). 3. Determinism: two processes on a Hard v Hard match; a save mid-wave hash-equal 1,000 ticks.
4. Fog: the oracle on a Hard seat's every think. 5. `AiPerfTests` 1 ms / 0 B hold with 8 Hard seats. 6. Every pinned match that moves
regenerated in the same commit and listed (the watch-out above). 7. docs/02's difficulty table true; docs/03 "Implementation (M5-4c
slice 2)". **Criterion 4 ticks at the ACCEPT.**

### Then (sim)
- **The sim's hardening at 4 / 4** (the session after): BUG-1119 (if not the rider), the lone-idle-worker hunt gap (`KnownStalls` seed 5's
  worker 3 m past a sweep point's sight), BUG-1116's house rule re-measured with both in, BUG-1118 item 1, BUG-1058, BUG-1059, BUG-1044's
  rest, BUG-1067, BUG-1068's gold, the movement S3s BUG-0911 / 0889 / 0144, the `HuntRing` load range, `HasMapId`'s special case; then both
  seed-2 known stalls re-run. Then BUG-1010's lever, D14 c's schema (M5 criterion 9; the data track waits on it), M5-5's 20-minute test.

---

## View track

### Next: STOP (cheap) unless an owner item is in the inbox

Every M6 view item left needs the owner: the art pass (the look call), music / fonts / export templates (downloads), the .NET 10 SDK
(an install). The view's debt is one S4 (BUG-1126, a blank middle tooltip line in shapes no shipped text has) and BUG-1115 item 4 (the
art pass's theme): not a session's worth. **If the inbox has an owner item** ("look: go" / "look: outfit first" / "look: stylized",
".NET 10 installed", templates dropped, sound notes): that is the task, a feature session, QA standard (the .NET move: one commit, sim
+ game projects, M6 criterion; the export script `tools/export.ps1` once the templates are in; the art pass per docs/04).

---

## Data track

### Next: D-R34, the prose round the re-printed cells + the seed-19 paragraph + BUG-1136 · feature (counter 1 / 4 → 2 / 4) · QA light

**Goal.** The sim's M5-4d slice 2 re-printed 12 cells of `docs/factions/malazan.md` and one of `whirlwind.md` (rule 40) and the prose
round them is stale (BUG-1118 item 3); the seed-19 paragraph got one Producer sentence at the merge and wants a proper re-write; D-R33's
nits (BUG-1136). ~100 lines, docs and the data's test folders only.
1. `docs/factions/malazan.md`: the prose near the "Age II against the Whirlwind" table and BUG-0982's table quotes 13:25 / 12:33 /
   "defeated 10:24" where the cells now read 13:50 / 15:03 / "never"; re-word to the cells (the cells themselves are the sim's, keep
   them). The seed 19 / 25 paragraph (lines ~544-556): say plainly that since M5-4d slice 2 seed 19 stands at 20:00 again (its last
   worker rebuilds; 1 pop, no building, 522 gold; BUG-1116 / BUG-1119) and is a named allowance again, seed 25 defeated at 12:34.
2. `docs/factions/whirlwind.md`: the prose round the `raider_rush` v `crossbow_push` seed-19 cell (Age II 10:21) if it quotes 10:34.
3. **BUG-1136:** `MapTextContentTests.ThePin_NamesEveryShippedMap_RarakuFirst` renamed to what it does (e.g. `ThePin_HasRarakuFirst_AndPrintsUnpinnedMaps`;
   update every reference, incl. QA's `DH7MapTextQaTests` comments); the `_out` field and constructor moved to the class's top; the
   re-wrapped short line on the Malazan page.
- **Files:** the two faction pages, `sim/Rts.Sim.Tests/Content/MapTextContentTests.cs`, `QA/Content/DH7MapTextQaTests.cs` (comments).
  **OUT:** `game/data/**`, the page tables' cells, `KnownBeatenAlive`'s entries (rule 41: the Producer's merge-time listing stands until
  the sim's hardening re-measures), any sim code.

**Acceptance criteria.** 1. Build 0 warnings; the data filter (`Content|DataValidation|DataContentHash|MapFile`) green; smoke PASS.
2. No stale time in either page's prose (the dev greps the old numbers: 13:25, 12:33, 10:24, 10:34, 17:18, 15:39). 3. BUG-1136 and
BUG-1118 item 3 `fixed` in the report with the proof. 4. `git diff -- game/data` empty; the data hash unmoved.

### Then (data)
- The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780); D14 c's content
  when the sim ships its schema; M7's rosters when M7's schemas exist. With none of those, the data track STOPs (cheaply).
