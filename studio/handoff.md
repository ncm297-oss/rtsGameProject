# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-12-1930** (first full session of 2026-10-12, 1 / 8; all three tracks
ACCEPT: sim M5-3, view M6-V2, data D15). Counters after it: **sim 3 / 4, view 3 / 4, data 4 / 4** (the data track's next session is
**D-H4**, a hardening; sim and view have one feature session left before their hardenings). Bug ids next session: **sim from
BUG-0620, view from BUG-0630, data from BUG-0640.** Inbox: empty at this ACCEPT (no owner answers on D13 / D14 / D11 / D12 / D15 yet).

## Where we are

- `main` after this integration (sim 68f04d6 → view 39ceb79 → data 9f70371): build 0 warnings; the Producer's scratch merge of all
  three: build 0 warnings, the AI / content / ViewApi / replay slices and smoke (the session log's "Merged tree"). Golden
  `cross_map_seed1.replay` format 5, **`data-hash` 8409DB7517579FEF** (`ai.json` and `rules.json` `ai` are in `ContentHash` now: a
  data-track `ai.json` edit regenerates the golden header), 1 player, no AI seat.
- **M5 1 / 6, criteria 2 and 3 advanced** (scout + military managers in; ability manager, defense, expansion, rebuilding,
  high-ground scouting, difficulty knobs left). **M6 criterion 1 advanced** (faction + difficulty per slot in; 2-4 slots, colour,
  team left). No open S1 / S2. Open S3: sim BUG-0592 (re-triaged), BUG-0598; view BUG-0600 / 0601 / 0603 / 0604; others S4.
- **The AI on `main` now:** scouts at 15 s, gathers both resources, rallies 12 m in front of its hall, waves at 20 pop (Malazan) /
  14 (Whirlwind) × the difficulty's multiplier, retreats at half losses, one wave a minute at most. On the generated test map it
  still finds wood late on seeds 1-2 (3-3.7 min) because the map has no start locations; from the menu the match is still the dev
  sandbox (100 units a side in contact). Both are next session's tasks.

## Standing rules (1-15)

1-14 as in the 1930 PLAN handoff (full suite incl. Perf alone for sim rule changes; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a
data brief names every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the
Producer the rest; content-independent sim tests; `git checkout HEAD -- path`; the budget is a line).
15. **One system per brief.** Three sessions running every sim / view task shipped 2-2.5× its budget. A brief names one system as
    required and lists anything else as "only if the dev's own count is under N lines at the report"; **the dev's count excludes QA's
    files**; the dev reports its count and the Producer checks it at ACCEPT.

---

## Sim track

### Current session plan: M5-4a, start locations in the generator + Requests 37 + BUG-0598 · feature · QA full · dev ≤ 1,000 lines (QA outside)

**Goal.** Give generated maps real start locations so every base starts with gold and wood in reach: today the generator places
forests and mines at random, the view's `StartBase.Plan` improvises a hall site, and on some seeds a base sees no node (BUG-0604) and
the AI's first wood comes in after 3 minutes (BUG-0592). This is M6 criterion 7's generator half, pulled forward because M5-5's
20-minute winner test and M5-6's playable run need it. Plus the sim's match-over rule the view asked for (Requests 37) and the
frozen-wave guard (BUG-0598).

**Scope.**
1. **Start locations** (`Map/MapGenParams` + the generator, `Map/MapGen*`): `StartLocations` (default 2; 2-4 accepted, docs/02
   "Map": symmetric, mirror or rotational) chosen before forests and mines: each a flat open disc of radius `StartClearRadius` cells
   (default 8) on level 0 or one plateau, at least `StartEdgeMargin` (default 10 cells) inside the map, player 0's in the west half,
   player 1's its point-mirror through the map centre (3-4 players: rotational, M6-V5's job to use); the generator retries the
   terrain seed if no pair fits (the existing `MaxAttempts` path). Per start location: **2 gold mines** of `startMines.gold` (docs/02
   "Economy": 2,500) at `StartMineDistance` m (default 14-18, outside the clear disc, on the same level), **one forest** of
   `ForestMinTrees`+ trees at `StartForestDistance` m (default 18-24, same level, not blocking the ramp side), then the remaining
   forests and mines as today (`Forests` / `GoldMines` counts include the start ones). `World.StartLocations` (read-only span of
   `StartLocation { Cell, Level }`), in the state hash? **No**: it's generator output like the heightmap (document the reason as the
   heightmap's: the seed makes it).
2. **`StartBase.Plan` on them** (`ViewApi/StartBase.cs`, read-only, view-owned file but the sim track takes it this session by
   Producer decision since the plan changes with the sim's generator; tell the view in the report): the hall spot is the start
   location's centre (the footprint fits by construction); workers face the nearest mine as today; `StartLayout.Block` for the dev
   armies unchanged (it stays a dev feature). A map without start locations (hand-built test maps, `LocalMovementTests.Flat`) keeps
   today's search.
3. **The AI's generated test scene on them** (`AiScenes.Generated`): bases at the start locations; the mirrored enemy base makes the
   scout's mirror point hit. Re-measure `ScoutManagerTests.GeneratedMap_TheAiFindsWood_AndGathersIt` and `AiVersusAiTests`: the
   asserted target is **first wood in by tick 2,000 on seeds 1-3** and known wood by 1,200 (BUG-0592's ruling); if a seed misses,
   report the numbers and keep the measured assertion (don't tune the generator to the test).
4. **Requests 37** (docs/02 "Game flow" 4: a player is defeated when they have no buildings left): `World.IsDefeated(p)` (derived:
   no live building incl. sites **and** the player had one at some tick; a player that never had a building is never defeated, so
   `--no-bases` dev runs are unaffected), set in phase order once per tick after construction / combat, hashed (one bit per player);
   `World.DefeatEvents` (player, tick) read by the view like `AbilityEvents`; `Simulation.MatchOver` (true when at most one
   undefeated player remains among players that ever had a building; the sim keeps ticking: the view decides what to show). The AI
   seat of a defeated player stops thinking (no commands after defeat). docs/02 wording kept; docs/03 "Implementation (M5-4a)".
5. **BUG-0598:** a wave where no member has been ordered and none made progress for `ai.waveCooldown` ticks ends with the cooldown
   (members home); or the "crowded" branch stays only while some member is Attacking the target (the view shows `UnitState`). Un-skip
   QA's `AiMilitaryQaTests.AWaveWhoseMembersAllStandByATargetNoneCanStrike_DoesNotStayForever`. **BUG-0594 items 1-2** only if under
   budget.
6. docs/03: "Implementation (M5-4a)" (the generator's start rule, the hash reason, `IsDefeated`, the wave guard); docs/02 "Map"
   table's Gold row unchanged (already 2 per start); docs/01 row for the start-rule defaults; `MapGenParams` docs row.

**Out:** defense, the ability manager, difficulty knobs (`gatherBonus`, `focusFire`, `pullBackWounded`), expansion, rebuilding,
periodic re-scouting, ramps / high ground (M5-4b, after the sim's hardening); 3-4 player start-base plans in the view (M6-V5); hand-made
maps (M6-7); the menu's match composition (view M6-V3); any `game/data/factions/**` edit; a new `Command` kind (none needed).

**Acceptance criteria.**
1. Generated 128 / 192 / 256 maps, seeds 1-12: two start locations, point-mirrored (|p1 - (size - p0)| ≤ 1 cell), each with 2 mines
   and a forest within the documented distances on the same level, the clear disc passable and flat; `MapGenTests` rows.
2. `StartBase.Plan` puts each hall at its start location on every seed 1-12; a hand-built map without start locations plans as before
   (`StartBaseTests` unchanged rows green).
3. The AI on the generated scene: known wood by tick 1,200 and first wood in by 2,000 on seeds 1-3 (or the measured numbers reported
   and asserted); `AiVersusAiTests` wood by 3,000 both seats.
4. `IsDefeated` / `DefeatEvents` / `MatchOver`: a hand-built world whose only building is razed → defeated that tick, the event once;
   never-built player never defeated; rebuilt-then-razed defeated again (one event per drop); the AI of a defeated seat sends nothing
   after; hashed (`StateHashTests` row); recorded replays unchanged in format.
5. BUG-0598's QA row un-skipped and green; the loop-bound rows still green; `AiWaveStressTests` green.
6. Determinism + replay: golden regen **only if** the generator's output for seed 1 changes (it will: the cross-map golden runs on a
   generated map) → one regen with every `k` line **expected to change**; the reason in the commit; `SameGameDataHashes` unchanged
   (`data-hash` moves only for data). `AiDeterminismTests`, `CommandDoorFuzzStressTests` green.
7. Cost: generation time on the 256 map under the existing gate; no per-tick cost (`IsDefeated` is O(players) from counters).
8. Build 0 warnings; non-Perf green; Perf alone green; docs; the dev's line count ≤ 1,000 reported (QA outside).

**Design references.** docs/02 "Map and terrain" (2 mines per start, symmetric layouts), "Economy" (start mines 2,500), "Game flow" 4
(defeat), "AI opponent"; docs/03 "Start armies (debug until real start locations)", "Implementation (M3-V1)" (`StartBase`), "Map
generation" (`MapGenParams`, `MaxAttempts`), "Implementation (M5-3)" (the scout's mirror assumption); BUG-0592's ruling, BUG-0604,
BUG-0598.

**Tests required.** `Map/MapGenTests` start-location rows (seeds 1-12 × 3 sizes), `StartBaseTests` rows, `ScoutManagerTests` /
`AiVersusAiTests` re-measured, `World/DefeatTests` (new), `StateHashTests` row, QA's BUG-0598 row un-skipped, the golden regen.

**Constraints.** Seeded RNG only (`SimRng` streams in the generator); no allocation per tick for `IsDefeated`; `StartBase.cs` is the
only view-owned file the sim touches (named here); no Godot; the dev's count ≤ 1,000 lines excluding QA files.

**QA focus (full).** Start locations on 36 seed × size pairs: never off-map, never on a cliff edge, mirror exact, mines / forest
reachable on foot from the hall (`ReachableOnFoot`), the clear disc holds the hall + 5 workers; a map where the pair can't fit
(tiny `Width` 32) fails loudly or falls back as documented; `IsDefeated` with sites only, with a site cancelled, with two players
razed the same tick, with 4 players; the AI of a defeated seat silent; the golden's new `k` lines reproduced twice; the scout's
numbers on the new scene; BUG-0598's scene and a 2-tower variant.

### Watch-outs (sim)
- The cross-map golden is generated-map seed 1: its `k` lines **will** move with the generator; say so in the commit, regenerate once.
- `AiScenes.Generated` places bases itself today (not mirrored: BUG-0593's cause); moving it to the start locations is item 3.
- Keep `MaxTries` as is; BUG-0598's guard is separate.

---

## View track

### Current session plan: M6-V3, the menu's match as the design says + per-player colours · feature · QA standard · dev ≤ 800 lines (QA outside)

**Goal.** From the menu, Skirmish → Start still drops 100 soldiers a side in contact over the pop cap (BUG-0604): the owner's first
game against the AI is a brawl, not a build-up. Make the menu's match the docs/02 opening (a Town Hall and the starting workers per
player, nothing else; the dev flags keep the sandbox), give each player its own colour (BUG-0601: a mirror match is two clicks away),
and close the third pause-state holder (BUG-0600).

**Scope.**
1. **Menu match = hall + workers.** `SkirmishSetup.MatchArgs` passes `--units 0`; `Match.Start` with 0 units skips `SpawnArmies`
   and plans the bases from the start locations when the sim provides them (`World.StartLocations`, the sim's M5-4a this session:
   **merge order sim → view**, so build against `main` after the sim lands; if the sim's branch isn't merged when you start, code
   against today's `StartBase.Plan` and say so), else today's plan; the camera opens on the local player's hall. `--skirmish` (the
   smoke's second pass) does the same as the menu's Start (BUG-0602 item 2). The sandbox scenes keep `--units 100` explicitly where
   they need armies (`M4PlayableTest`, `QaShellTest` leak rows: check each `*Test.tscn`'s args).
2. **Per-player colours.** A colour per slot in the setup (`OptionButton` of 8 named colours from `ui.json` `setup.colour_*`, defaults
   faction colour 0 / 1 → distinct: slot 1 the first colour, slot 2 the second), `--colour <slot> <index>` flag; `Match` builds
   `playerRgb` from the picks (`UnitViews`, `BuildingViews`, minimap dots, hp bars, zone rims, ghosts: everything keyed by faction
   colour today reads the player's). A mirror match shows two colours (QA's `QaM6V2Test` swapped-HUD row flipped to a `Check`).
3. **BUG-0600:** `Minimap._jumping` dropped on `NotificationPaused` / `Unpaused`; a sweep of `game/scripts` for any other
   press-to-release flag (grep `IsActionPressed` / `IsActionReleased`); `QaM6V2Test.MinimapHeldIntoPause` probe → `Check`.
4. **BUG-0602 item 1:** `{{0}}` refused by `UiText.IsSizeFormat` (format with 128 must differ from the literal).
5. **Match over on screen, dev-level only:** when the sim's `Simulation.MatchOver` exists on `main`, the HUD shows a one-line
   `hud.match_over_win` / `hud.match_over_loss` banner (text from `ui.json`), the match keeps running; the stats screen is M6-V4.
   If the sim's part isn't on `main` yet, skip this item and say so.
6. docs/03 "Implementation (M6-V3)"; `ui.json` keys listed; `ShellTest` rows.

**Out:** 2-4 slots and teams (M6-V5: needs the sim's 3-4 player start locations and a team rule, Requests 38 to file), the stats screen,
save / load, settings, the look test, export.

**Acceptance criteria.**
1. Menu → Start (defaults): `Pop 5 / 10`-class opening (5 workers, a hall each, no soldiers); `Match started:` says `0 units per player`;
   the camera on the local hall; `AiSeatTest` sees the AI gather by tick 400 (workers Gathering / Returning, not just queue items).
2. `--skirmish` = the menu's Start; smoke PASS both passes; every sandbox scene that needs armies still gets them (loop green).
3. Colours: a mirror match draws two colours in units, buildings, minimap, bars (QA's row a `Check`); the picks survive Quit / Back;
   `--colour` junk refused like `--seed junk`; no C# literal (names from `ui.json`).
4. BUG-0600's probe a `Check` and green; no other press flag survives a pause (the sweep's result in the report).
5. BUG-0602 item 1 refused as one load error naming the key.
6. If built: the match-over banner once per match end, from `ui.json`, hash-neutral (a twin row).
7. Build 0 warnings; loop green; docs/03; the dev's count ≤ 800 reported (QA outside).

**Design references.** docs/02 "Game flow" 2-4 (slots: colour; the match; defeat), "Economy" (starting workers); docs/03
"Implementation (M6-V1 / V2)", "Start armies (debug ...)"; BUG-0600 / 0601 / 0602 / 0604; the sim's M5-4a report (start locations,
`MatchOver`).

**Tests required.** `ShellTest` rows (units 0, colours, flags), `AiSeatTest` gather check tightened, `QaM6V2Test` rows flipped,
`MinimapTest` pause row, xUnit `ViewApi/` rows only if a ViewApi helper is added.

**Constraints.** Dumb views; player-facing text from `ui.json`; no sim change outside read-only `ViewApi/` (`StartBase.cs` is the
sim's this session: don't touch it; a colour accessor the view needs that the sim lacks → Requests); dev ≤ 800 lines.

**QA focus (standard).** The menu opening on 6 seeds × 3 sizes (no soldiers, hall + 5 workers each, both halls see a mine); 10 quit
cycles from the lean match; the colour picks under junk flags and repeated flags; a mirror match's minimap dots distinct; pause with
the minimap pressed, Esc twice, resume: camera 0 m; the match-over banner with `--no-bases` (never).

---

## Data track

### Current session plan: D-H4, the data track's fourth clean-up · hardening · QA light · ~300 lines incl. QA

**Goal.** The counter is 4 / 4. Work the data debt: BUG-0610, the backlog's small items, and anything the owner answers in the inbox
(inbox tweaks first; each data change regenerates the golden header now that `ai.json` is in the hash: say so in the commit).

**Scope.**
1. **Inbox answers first** if any arrived (D13's ×1.1 / ×1.5, D14's build-order timings, the D11 / D12 wordings, D15's house-rule
   choice): each as a data edit + page pin + one golden regen (`data-hash` only; every `k` line equal, asserted).
2. **BUG-0610:** drop the faction assert in `AiHouseRuleReportTests` (or keep it as a precondition comment); read the report's
   inputs through `AiPlayer`'s public-to-tests surface only where one exists, else leave and add a one-line comment naming the
   fields read (the sim track changes `Blackboard` again in M5-4a: the conductor rebuilds after the merge); date the class comment's
   numbers "measured 2026-10-12 on M5-2's AI" and move them to the session report.
3. **Backlog sweep:** the data debt list in STATE (D-H2's known gaps are documented, not work); `docs/factions/**` for any cell the
   sim's M5-3 re-printed (the four Raider v Wickan Lancer cells: confirm the pins agree with the pages after the merge, nothing else to
   do if green).
4. **Re-run the house-rule report on M5-3's AI** (the scout now takes a worker at 15 s and the rebalance moves gatherers): one
   paragraph in the report with the new counts; no file change.

**Out:** any balance pass without an inbox answer; M7-M9 data; `ai.json` edits (none asked).

**Acceptance criteria.**
1. BUG-0610 fixed (statuses set); the three report rows green and printing on the merged tree.
2. Any inbox tweak landed with its pin and the golden header regen, `k` lines equal.
3. No `sim/Rts.Sim/**` change; build 0 warnings; the full non-Perf suite green with the sim's and the view's branches merged first.

**Design references.** docs/03 "For the data track (M5-1)"; docs/factions/*.md "AI build order (Normal)"; `studio/bugs/BUG-0610`.

**Tests required.** The report rows; `Content/**` and `QA/Content/**` green.

**Constraints.** Tests + `game/data/factions/**` + `docs/factions/**` only; the golden only for a data change.

**QA focus (light).** The report rows on the merged tree; any pin an inbox tweak moves fails by name before the page is updated.

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing.
- `studio/bugs/README.md` and `studio/qa/coverage.md`: each track appends only its own rows (unions at the merge).
- **Merge order sim → view → data.** The view's item 1 reads the sim's `World.StartLocations` if it is on `main`; the view's dev says
  which it built against.
- The sim touches `ViewApi/StartBase.cs` this session (named above); the view doesn't.
- The golden's `k` lines move this session (the generator): only the sim regenerates it.
