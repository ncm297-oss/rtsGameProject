# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-11-0314** (first full session of 2026-10-11, 1 / 8; base `main`
8f68b1a). All three tracks GO: sim **M5-1** (feature, QA full), view **M5-V1** (feature, QA standard), data **D13 = the D12
landing + the Line v Shock balance pass** (feature, QA light for the landing, standard for the pass). Bug ids: sim from
**BUG-0540**, view from **BUG-0550**, data from **BUG-0560**. Integration order sim → view → data.

## Where we are (checked at this PLAN)

- M0-M4 Done; **M5 (AI opponent) Next, 0 / 6.** `main` builds 0 warnings; the non-Perf suite result is in the session log.
- `main` does **not** carry D12 yet: golden `cross_map_seed1.replay` header `data-hash` **889D6BEA3A49F08E**, no `detector` in
  `units.json`; `origin/studio/2026-10-10-2334-data` @ c0dd350 exists and still conflicts with `main` in exactly the three
  recorded files (`git merge-tree` at this PLAN: `StealthLoaderTests.cs`, `StealthContentHashTests.cs`, `studio/bugs/README.md`;
  `coverage.md` and `docs/03` auto-merge now). The data track lands it first (below). **The sim's and view's branches touch no
  file under `game/data/**`, no `Replays/*.replay`, neither stealth test file and not `studio/bugs/README.md` rows other than
  appending their own.**
- **Memory bound** `QA/FieldBuildFairnessQaTests.cs:473` 231,920,000: 231,908,912 used (11,088 B margin). **M5-1's per-player
  buffers re-baseline it** (itemised: bytes per slot x capacity x players, in the test's comment and docs/03).
- Perf: `EmptyTick(2500)` gate 550 µs; `TightBlob2500` 4.5 ms with ~0.1 ms headroom alone. A Perf failure counts only alone and
  above base. Three suites run at once this session.
- No open S1 / S2. S3 13, S4 12 (BUG-0511 / 0512 first in the next sim hardening). Counters: sim 0 / 4, view 0 / 4, data 1 / 4.
- Standing rules 1-9 unchanged (full suite incl. Perf alone + the merged scene loop for vision / combat / loader changes; a new
  `UnitState` ships its `ui.json` line; whoever moves `data-hash` appends to `GatherWedgeQaTests.SameGameDataHashes`; `ui.json`
  keys named in the brief; a sim or data change a view scene asserts on gets a named item in the view brief; a new `Command` kind
  goes into `CommandDoorFuzzStressTests`; per-slot memory itemised; the shipped `data-hash` pinned in the golden only; a brief names
  every test file another track's change will break, the final assertion, and which branch ships the edit).
- `CLAUDE.md` still says "Current milestone: M1" (owner's file; drift noted, not changed).
- The owner's own checkout `C:\Dev\rtsGameProject` is 164 commits behind `origin/main` (not the studio's concern; noted in STATE).

## Sim track

### Current session plan: M5-1, `PlayerView` + the `ai.json` schema · feature · QA full · ~1,200 lines incl. QA

**Goal.** Criterion 1 of M5: the AI's only window on the world, fog- and stealth-filtered, with a test that proves it leaks
nothing hidden; and the `ai.json` loader so the data track can write build orders next session while the sim writes the
managers. No AI decisions, no commands sent by the AI yet.

**Scope.**
1. `sim/Rts.Sim/Ai/PlayerView.cs` (namespace `Rts.Sim.Ai`), constructed from `(World, int player)`; read-only. Answers: own
   units / buildings (always), enemy units only where `Fog.CanSeeUnit(player, slot)` (stealth applied through `World.HiddenFrom`),
   enemy buildings only where `Fog.CanSeeBuilding` or in `Fog.Ghosts(player)` (with `Known` / `Site`), resource nodes only on
   explored cells, `IsExplored` / `IsVisible` per cell, own gold / wood / pop / pop cap / age / techs, own buildings' queues. Enumerations
   are index-ordered (slot order), span- or callback-based; no `World`, `UnitStore`, `BuildingStore` or `FogStore` reference escapes
   (return values are value types, ids, handles, positions, `ReadOnlySpan`s of value types the view already exposes nothing hidden
   through). docs/02 "AI opponent" (the AI sees only its own fog view), docs/03 "AI architecture".
2. Tests for criterion 1 (`sim/Rts.Sim.Tests/Ai/`): (a) a fuzz over 3 seeds x 2,000 ticks with 3+ players, fog on, stealthed units
   (test-only stealth sources as `StealthTests` do), towers and ghosts: every `PlayerView` answer every tick equals a brute-force
   oracle that walks `World` and applies `Fog.CanSeeUnit` / `CanSeeBuilding` / the explored grid (0 leaks, 0 misses); (b) a
   source-scan architecture test: no file under `sim/Rts.Sim/Ai/` other than `PlayerView.cs` contains the token `World` (the
   same pattern the ViewApi guard uses); (c) the hash twin: a run that constructs and fully enumerates a `PlayerView` for every
   player every tick has the same state hash as a run that never does.
3. `game/data/factions/<id>/ai.json` schema (`sim/Rts.Sim/Data/AiDef.cs` + `DataLoader.Ai.cs`), required for every shipped
   faction, per docs/03 "Data format" + "AI architecture": per difficulty (`easy` / `normal` / `hard`, all three required)
   `thinkInterval` (s, converted to ticks, >= 1 tick), `buildOrderSlack` (s), `waveSizeMultiplier`, `gatherBonus` (fraction; Hard
   0.25 per docs/02), micro flags (`focusFire`, `pullBackWounded`, `useAbilities`), `expandAfter` (s); shared: `buildOrder` steps
   (`at` = a time in s or a worker / pop count; `do` = `build` / `train` / `research` / `expand` with a unit / building / tech id and a
   count), `compositions` (unit id → weight, Age I and Age II), `attack` (min army pop and min army cost before a wave),
   `retreat` (hp fraction and loss fraction). Validation: ids exist and belong to the faction, numbers finite and in `DataLimits`
   ranges, no duplicate step, every error at once (`DataValidationTests` rows per fault). Minimum shipped entries: one real
   step and one composition per faction, defaults per difficulty (docs/02 "AI opponent" numbers: Easy thinks every 2 s, Hard
   every 0.5 s). The exact JSON shape goes into docs/03 as "For the data track (M5-1)" with what is refused.
4. **`ai.json` does not enter `ContentHash` in M5-1** (Producer decision: nothing the AI reads changes sim state yet; M5-2 adds
   it with one golden regen when the AI first sends commands). Golden untouched; no `game/data/**` file other than the two new
   `ai.json` files; no `Replays/` change.
5. Perf: enumerating a `PlayerView` for 8 players at 2,500 units under 0.3 ms a tick (a `[Trait("Category","Perf")]` row), 0 B
   steady state; any per-player buffer pre-sized at construction and **itemised in the memory re-baseline**
   (`FieldBuildFairnessQaTests.cs:473` comment + docs/03 row).
6. docs/03: a new "Implementation (M5-1)" under "AI architecture" (what `PlayerView` answers, what it never answers, the
   `ai.json` shape, the ContentHash deferral under "Not yet"); docs/01 change-log row.
7. Out of scope: any manager, any `Command` from the AI, `AiPlayer`, difficulty display names (M5-4 / M6 lobby), BUG-0511 / 0512
   unless the task touches their code, the stealth test files, `ui.json`.

**Acceptance criteria.** (1) `PlayerView` exists and exposes nothing `Fog` hides: the 3-seed x 2,000-tick oracle fuzz passes with
0 leaks and 0 misses, including units stealthed mid-run, a storm's blocker, ghosts of buildings that are gone, players 2+. (2) The
source-scan test fails when a `World` reference is planted in a second `Ai/` file (mutation shown in the report). (3) Hash twin
equal across 2 seeds x 1,000 ticks. (4) Both factions' `ai.json` load; `DataValidationTests` rows for a bad id, a foreign-faction
id, a missing difficulty, a negative interval, a duplicate step each fail by name; `ai.json` missing for a faction is an error.
(5) Perf row green alone; 0 B; the memory bound re-baselined and itemised. (6) Golden byte-identical to `main`; `dotnet build` 0
warnings; the full non-Perf suite green; Perf alone green; docs/03 + docs/01 updated.

**QA focus.** Leaks: a unit stealthed in phase 5 of the same tick the view is read; a unit inside an enemy Sandstorm; a tower
revealed by its own high-ground hit (counts as seen); a ghost whose building died unseen; player indices past 2; a `PlayerView`
kept across ticks (document whether it is valid for one tick only). The brute-force oracle every tick. Determinism: reads leave
the hash unchanged. Loader: hostile `ai.json` (NaN, huge counts, 10k steps, unknown `do`, empty compositions). Memory: the
re-baseline numbers add up.

## View track

### Current session plan: M5-V1, the lost-cast cue + the flat rings (+ export hygiene if budget remains) · feature · QA standard · ~1,000 lines incl. QA

**Scope.** (1) The HUD reacts to `AbilityEvent.Dropped` (Requests 34; `Resolved` false, `Dropped` true): the caster's cast bar /
cast-point ring ends with a short red flash on the caster (0.5 s) and the existing order-refused sound; if any text is shown it
comes from `ui.json` key **`hud.cast_lost`** ("Cast lost"), added by the view as a named exception (the sim's M5-1 does not touch
`ui.json`). No cue for a cast the player cancelled with a new order. `AbilityViewTest` rows: a walk-in blocked by enemies drops
after 3 s and shows the cue once; a cast cancelled by a new order shows nothing; the hash twin unchanged. (2) BUG-0520: cast-point
rings and resolve flashes as `GroundDisc`s from a small pool (rebuilt only when a cast point changes); the cliff row from
`QaVh3Test`'s layout. (3) BUG-0521 item 1 (compare the clamped hole). (4) If budget remains: an export preset excluding
`game/tests/` from the release build (docs/03 "Build and export"); the `.pck`-safe data load waits on Requests 33. Out of scope:
`game/data/**` except the one `ui.json` key, replays, `DataContentHashTests.cs`, any `sim/**` beyond read-only `ViewApi` additions.

**Named item (rule 8/9):** the data track may change the line units' `attack.bonusVs.mounted` (1.5 → lower) this session. The
view changes nothing for it; the data QA runs `M4PlayableTest` seeds 1 / 6 with the new numbers as its own gate. If a step's
timing breaks there, it is reported as a view-track item for next session, not relaxed this session.

**Acceptance criteria.** (1) The cue fires exactly once per `Dropped` event, never on the player's own cancel (the two new
`AbilityViewTest` rows pass; the hash twin equal). (2) BUG-0520's cliff row: ring and flash points on `TerrainHeight.At` + lift
with 0 misses on both levels; the pool allocates nothing after warm-up. (3) BUG-0521 item 1 closed with a test. (4) Smoke PASS;
the merged scene loop 43 / 43 (or 42 / 42 + the new rows); `game/tests/` scenes still run; no new literal reaches the screen;
docs/03 "Implementation (M5-V1)"; bug statuses set.

**QA focus.** The cue once per drop (QA's `ADroppedCast_FiresOneDroppedEvent_*` is the sim side); no cue on cancel or on a
resolve; a drop while the HUD is on another selection; the pool at 12 selected mages; `M4PlayableTest` seeds 1 / 6 still pass
(it works round BUG-0461 with plain Moves); the merged loop.

### Later
M5-V2 an AI seat in the sandbox (`--ai <difficulty>`, seat 1, the match end printed) once M5-1 and the first managers are on
`main`; a remembered site drawn as a foundation (`Fog.Ghosts` `Site`); then the M6 shell.

## Data track

### Current session plan: D13 = (a) the D12 landing, then (b) the Line v Shock balance pass · feature · QA light (a) / standard (b) · ~700 lines incl. QA

**(a) Land D12 first (~50 lines, the session's first commit).** On the session's data branch (from `main`): `git merge
origin/studio/2026-10-10-2334-data` and resolve exactly as recorded (session log 2026-10-10-2334, the table): 
`sim/Rts.Sim.Tests/Data/StealthLoaderTests.cs` → **D12's side of both hunks** (test name
`Shipped_TheKindsAndRulesLoad_AndOnlyTheCastersDetect`, assertion `u.Slot == UnitSlot.Caster ? 10f : 0f`; `main`'s BUG-0481 row
and `Remove` helper stay); `sim/Rts.Sim.Tests/Vision/StealthContentHashTests.cs` → **`main`'s file as is**; `studio/bugs/README.md` →
union by id. D12's golden (`data-hash` C4C16BD39408471A, `checksum` D3392E846CAC0118, every `k` line equal) and its
`SameGameDataHashes` line stand. Named exception to "no C# outside the data test folders" for those two files only. Gate:
`dotnet build` 0 warnings, the full non-Perf suite green (the Producer's scratch merge last session: 980 / 3 / 0 on the affected
filters). Commit: `D12: land the casters' detector (merge main into the held branch)`.

**(b) The Line v Shock balance pass (Producer default from D6, owner may revisit).** The target band: *the winner keeps 40-65 %
of its cost in both seats, seats within 15 points.* Today Line beats Shock at 88-100 % (both pages, "Balance baseline"). **The
one lever:** the line units' `attack.bonusVs.mounted` (Heavy Infantry and Raider, today 1.5; docs/02 "Template bonuses: Line deals
×1.5 vs Mounted"), the same value for both factions, tried in {1.4, 1.3, 1.2, 1.1, 1.0}: pick the value whose four Line v Shock
cells (both pairs, both seats) land in the band; if none does, the value nearest the band that keeps Line the winner in all four
cells, and say so. **Not touched:** hp, armor, attack, cost, speed, radius, sight, detector, text, the shock units, the ranged /
caster / siege rows (Shock v Ranged 69-80 % stays for a later pass; casters are measured without abilities), `damage_table.json`
(sim-owned). Re-pin every page cell from the harness (`Content/CounterTriangleMarginsTests`, both pages' "Balance baseline"
tables); update docs/02 line 51 ("Line deals ×1.5 vs Mounted") and the faction pages' unit rows to the new value; docs/01 row
"Producer decision, owner may revisit"; the review table (unit / field / old → new / why) in the report.

**Named sim-track pins (rule 9; the data branch edits them, listed in the report):** `sim/Rts.Sim.Tests/DamageCalcTests.cs`
lines ~74-79 (Heavy Infantry `bonusVs { mounted: 1.5 }` → `Compute(...) == 15`: the literal becomes `10 * newValue` rounded as
the formula does); `QA/Content/UnitRosterQaTests.cs` (the data's own folder: the `"mounted=1.5"` cells). Any other sim-track test
that fails must fail only on a pinned shipped literal; list each; a behaviour failure (a winner flips, a scenario stalls) means
the lever is backed off one step. **The golden** `cross_map_seed1.replay`: `bonusVs` is in `ContentHash` → the header moves once
more (append to `SameGameDataHashes`); the `k` lines move only if a line unit hits a mounted unit in that march (state which).

**Acceptance criteria.** (1) `main`'s golden header reads C4C16BD39408471A after (a) and the two stealth test files equal the
recorded resolution; the full non-Perf suite green after (a). (2) After (b): the four Line v Shock cells in the band (or the
nearest value with the reason), Line the winner in all four, every other page cell unchanged except the re-printed Line v Shock
rows and "Time to last death"; `CounterTriangleMarginsTests` and `Scenario/CounterTriangleTests` green. (3) The data diff is
exactly two `bonusVs.mounted` values; docs/02 line 51, both pages' unit rows and `UnitRosterQaTests` agree with the data;
`DataValidationTests` green; `DescriptionProblems` 0. (4) Golden re-recorded once for (b) (header + any `k` lines, stated), in
`SameGameDataHashes`; `TheShippedContentHash_IsTheGoldenReplaysHeader` green. (5) **`M4PlayableTest` seeds 1 / 6 PASS headless on
the data branch** (QA runs them; a failure stops (b), ships (a) alone and reports). (6) The review table; build 0 warnings; the
full non-Perf suite green.

**Design references.** docs/02 "Template baseline stats" (line 51), "Faction template" (the counter rules), docs/factions/
malazan.md + whirlwind.md "Balance baseline" and unit tables; the D6 entry under For your review (the band).

**QA focus (standard).** The harness numbers at the chosen value and one step either side (the report shows the sweep); a flip of
any winner anywhere; `M4PlayableTest` seeds 1 / 6; the golden `k` lines; both factions' pages re-printed from the harness, not by
hand; the D12 landing's two files byte-equal to the recorded blobs (`92e6a333` loader test, `2139d2dc` hash test).

### After that
Inbox tweaks first (the owner's answers to D11 / D12 / this table); `ai.json` build orders for both factions once M5-1's schema is
on `main` (QA light; docs/02 "AI opponent" per difficulty); the Shock v Ranged pass; M7-M9 faction data.

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows may fail under load; rerun alone before filing.
- The data branch moves the golden header twice this session (D12's C4C16BD39408471A, then the bonusVs value); the sim's and
  view's branches must not touch `Replays/`.
- `studio/bugs/README.md`: each track appends only its own rows (the D12 landing's union touches existing rows; merge order
  sim → view → data keeps it mechanical).
- Scratch worktrees: 15+ owner-housekeeping entries remain in `git worktree list` (`git worktree prune` after deleting the
  scratchpad folders).
