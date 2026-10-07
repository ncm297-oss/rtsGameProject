# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session 2026-10-06-2326 (7th full session today, cap 8; base 439227a =
`origin/main`). **Hardening session on the sim track (M3-H1, QA full) and the view track (M2-H2, the M2
end-of-milestone hardening, QA standard); the data track STOPs cheaply** (no inbox tweaks, no schema to fill).
**Bug numbers: sim from BUG-0094 (0094-0100 free, then 0112+), view from BUG-0104 (0104-0110 free, then 0122+).**

## Where we are

- `main` = 439227a: M3 3 / 8 (resources, gather loop, placement + construction + repair); M2 10 / 10 criteria
  ticked, sign-off after this session's hardening. 32 open bugs (S3 18, S4 14), no S1 / S2. Producer checks at this
  PLAN: build 0 errors / 1 warning (CS8602, BUG-0084); non-Perf suite (see the session log); smoke PASS
  (`Rts.Sim 0.0.1`, 287 trees, 8 mines, stopped at tick 85, no ERROR).
- Shared-file rule: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md (append),
  `studio/bugs/README.md` (append rows). Expect append-only conflicts there; keep every side, sim then view.
- Golden: only the sim track may regenerate it (BUG-0093 / 0080 must not move the movement checkpoints unless the
  session log says why; `data-hash` is untouched this session).

## Sim track

### Current session plan: M3-H1 — sim hardening (QA full)

**Goal.** Close the M3-3 / M3-2b residuals before M3-4 adds production: a Cancel or destruction must never leave
ground nobody can reach (BUG-0093), refused Build orders must be cheap (BUG-0091), back-to-back closings must not
freeze every army (BUG-0080), plus the S4 nits and the M6 save/load note. No new features.

**Scope (in).** Items 1-7 below, most valuable first; stop adding items at ~1,500 changed lines (code + dev tests).
**Out:** M3-4 production, anything in `game/` or `game/data/factions/`, re-flagging load-time pockets (they stay plain
`Blocked`), the crowd-cost work (BUG-0028 / 0032 / 0046 / 0050), `FlowField.Build` readability (leave).

1. **BUG-0093, pocket rule (S3).** When a building is freed (`Cancel`, or `BuildingStore.Damage` to 0: same `Free`
   path) its footprint cells reopen **only if** at least one footprint cell is adjacent (the adjacency `SealCheck`
   uses) to a passable cell outside the footprint. Otherwise the cells stay `Blocked` and gain a new
   `NavFlags.Pocket` bit; no opening change is published (`Version` / `BlockVersion` unchanged). When a later opening
   change opens cells next to `Pocket` cells, the pocket cells reachable from the newly opened cells (through other
   pocket cells) reopen with them if the union touches a passable non-pocket cell; otherwise the newly opened cells
   become `Pocket` too. Invariant: after any sequence of Build / Cancel / Damage / fell, every passable cell reaches
   every other passable cell. `Pocket` cells are `Blocked` to the flow-field builder (the QA
   `FlowFieldBuildEquivalenceQaTests` pin stays green), to `CanPlace` and to the exposure rule.
2. **BUG-0091, cheap checks first (S3).** The `Build` apply path (`ConstructionSystem` `StartBuild`) runs
   UnknownType / WrongFaction / OffMap / Blocked / UnitInTheWay / CannotAfford / StoreFull **before** the seal flood;
   `World.CanPlace` keeps the documented reason order. Both answer pass / fail identically for every anchor.
3. **BUG-0080, closings don't freeze armies (S3).** Preferred: a closing change leaves cached fields **usable-stale**
   like an opening change does (walkers keep following them; rebuilds go missing first, then oldest stale), relying
   on the per-step clip against `Blocked` cells so no walker ever enters a closed cell; the M3-2b progress-mark reset
   on closings stays. Do this only if a test proves the guarantee (500 walkers on fields that all predate a building
   dropped across their paths: no unit ever occupies a `Blocked` cell, every group arrives or detours). If the clip
   can't be proven in this session, do **not** change the cache: rewrite the docs/03 "Known limits" sentence (~line
   987) to the measured bound and say so in the report.
4. **BUG-0081 (S3, docs only).** docs/03 "Save/load and replays": Producer decision: a save file stores the cached
   fields' contents (direction bytes + stamps), so save → load reproduces the unsaved run exactly; replays replay from
   tick 0 and need nothing. Owner may revisit at M6.
5. **BUG-0092 (S4).** (a) loader rejects `repair.rateFactor` / `costFactor` below 2^-16 with a `DataError`; (b) a
   Build clears the issuing worker's own Hold before UnitInTheWay (its own Hold never blocks its Build); (c) push-out
   past ring 8 keeps searching outward ring by ring until a free cell (never stacks two pushed units); (d) push-out
   finds occupants through the spatial hash (400 units round a Keep: push-out under 1 ms Debug); (e) a pushed unit's
   `PrevPosition` = its new position; (f) docs/03 note: `CanPlace` writes flow-field scratch, call it on the sim thread
   between ticks.
6. **Small ones.** BUG-0079 item 3 (docs/03 `--workers` sentence: "N above 0"); BUG-0076 (empty resources list is a
   `DataError`: one type per kind required; the redundant full flood after the ring test becomes a debug assertion or
   goes; `-0` mine spacing rejected); BUG-0071 (shove pass widens `SearchPlug`'s query by `MaxUnitSpeed` as
   `SettleBackedOff` does; un-skip the QA row); BUG-0072 (CLI opens the `--record` file before the run; invalid names /
   denied folders fail before tick 1).
7. **Notes, no code:** felling Perf row headroom (rerun alone before filing); `FlowField.Build` eight-way loop stays.

**Acceptance criteria (sim).**
1. The QA row `ConstructionQaTests.ACancelledSiteBesideATree_LeavesThePocketBug0078Described_Report` is un-skipped
   and asserts: after the Cancel the four cells are `Blocked | Pocket`, `Version` and `BlockVersion` unchanged by the
   Cancel, the tree has no exposed side, and the worker is not still `Gathering` on it after 60 s (Idle or on another
   node; wood unchanged).
2. A dev test builds a pocket, then opens a neighbouring cell (fell a tree / cancel the enclosing House): the pocket
   cells reopen in the same tick as one opening change; a chain of two pockets reopens transitively; a cell opened
   into a pocket alone becomes `Pocket`.
3. Invariant fuzz (dev or QA, 4 seeds x 300 mixed Build / Cancel / Damage / fell actions): every passable cell
   reachable from every other after every action; twin runs hash-identical every tick.
4. `PlacementTests`' 8 x 250 Build-vs-CanPlace pass / fail equivalence stays green; a dev row shows 100 refused Builds
   (CannotAfford, far anchor round a wall) in one tick cost under 2 ms Debug (22 ms before); `CanPlace` reason order
   unchanged (existing test).
5. BUG-0080: either (preferred) the QA `GridChangeQaTests` "closing every tick" row (adapted to real closings: a
   House placed each tick off every path, QA reviews) shows every group walking (longest field *wait* 0 ticks, all
   arrive or detour) and a new test proves no walker occupies a `Blocked` cell while following a usable-stale field
   after a closing; or the docs/03 sentence states the measured bound and the report says why the change was not made.
6. docs/03 save/load paragraph carries the BUG-0081 decision; docs/01 change log gains one row ("Producer decision,
   owner may revisit").
7. BUG-0092 (a)-(f): one regression test each for (a)-(e) (a factor of 2^-17 is a `DataError`; a holding worker's
   Build on its own cell is accepted; 90 units round a 2 x 2 site all land on distinct free cells; 400-unit push-out
   under 1 ms; `PrevPosition` equals `Position` after a push), (f) in docs/03.
8. BUG-0076 / 0071 / 0072 / 0079-3: regression tests (empty resources list, `-0` spacing, the un-skipped BUG-0071 QA
   row, CLI bad `--record` name fails before the run) and the docs sentence.
9. `ReplayGoldenTests` green with the checked-in golden **unchanged** (no placement / fell in the golden scenario); if
   it must change, the log says exactly why and which checkpoints moved.
10. `AllocationTests`: the pocket decision, the cheap-first Build path and push-out allocate 0 bytes per tick; the
    felling + builders perf row stays within budget (avg under 1.3 ms Debug).
11. docs/03 "Implementation (M3-3)" (the pocket paragraph ~line 1251) and "Known limits" updated; bug files get
    `Fixed by` lines; `studio/qa/coverage.md` row appended.

**Design references.** docs/02 "Buildings" (no walls; placement never cuts ground), docs/03 "Pathfinding" (sealed
pockets at load; grid changes M3-2b; "Known limits"), "Economy implementation" (M3-2 exposure rule, M3-3 never-seal),
"Save/load and replays". Deterministic: no `Dictionary` iteration order, no wall clock.

**Tests required.** Listed per criterion above; all in `sim/Rts.Sim.Tests` (dev) and `QA/` (QA).

**Constraints.** No allocation in per-tick code (the pocket flood borrows `FlowFieldCache.BuildScratch` like
`SealCheck`); nothing in `game/` or `game/data/factions/`; `Rts.Sim` warnings as errors; golden unchanged.

**QA focus (sim).** Attack the pocket rule: pockets inside pockets, a pocket touching the map border, a pocket on a
different level than its neighbours, a building freed while a worker is being pushed out, Cancel and fell in the same
tick, 1,000 random Build / Cancel / Damage / fell on 6 maps against an independent reachability oracle every tick.
If item 3 is implemented: walkers on usable-stale fields pressed against a fresh building (any unit inside a `Blocked`
cell is S1), give-ups while waiting for the refresh, 32 groups with a House dropped every tick for 300 ticks (longest
wait, arrivals), determinism twins. Perf: 100 refused Builds a tick, 400-unit push-out, closing-change handling
allocation-free. Rerun a failed Perf row alone before filing.

### After this session (sim)
M3-4 production queues (5 slots), rally points, population and cap (`popProvided`), refunds on cancel; M3-5 Age II
research + Forge upgrades (ships `techs.json` and the building `requires` field the data track asked for, Requests 8);
M3-6 `trainedAt` / `requires` resolution and validation.

## View track

### Current session plan: M2-H2 — M2 end-of-milestone hardening (QA standard)

**Goal.** Clear M2's debt before the Producer signs M2 off: the benchmark must measure a real cross-map march, the
default match must start in a clearing, sounds must not leak at quit, and the minimap dot must show its player colour.

**Scope (in).** Items 1-8, most valuable first; ~1,500 changed lines at most. **Out:** any M3 HUD work (resource bar,
build ghost), sim files outside `sim/Rts.Sim/ViewApi/`, real art, BUG-0025 (sim).

1. **BUG-0101 (S3).** `BenchStep.OrderAcross` targets the passable cell nearest (0.85 W, 0.5 H) for an army west of
   centre (mirror for east), falling back to the opposite map corner; `QaM27Test` asserts the target is at least 100 m
   from the army centre and the centre moves at least 25 m in the 10 s run. Rerun the 60 s windowed bench (vsync
   off) and refresh the docs/03 M2-7 figures table if the numbers move.
2. **BUG-0102 (S3).** `fps` on the `bench:` line = timed frames / elapsed seconds; drop the `TimeFps` mean and its
   docs/03 sentence.
3. **BUG-0087 (S3).** `Sfx` stops its 8 players in `_ExitTree` and on the close request; `SfxTest` drops its 0.3 s
   wait; the docs/03 sentence calling the leak a test-only artefact is corrected.
4. **BUG-0085 (S3).** `ViewApi.StartLayout.Block`: every cell of a block is passable, on the same level as the block's
   first cell, and has no `Blocked` or `Resource` cell among its 8 neighbours (so no start block touches a forest,
   a mine, a cliff edge or the border); deterministic; `--units 1000` still fills both blocks. Tighten QA's
   `StartBlocks_OnTheMatchMap_*` to "no spot touches a node or a cliff".
5. **BUG-0083 (S3).** Rebuild the overlay label string only when a shown number changes; docs/03 "Debug tooling"
   says exactly what allocates (layers 0 B; label only on change).
6. **BUG-0069 (S3, Producer default, owner may pick).** Minimap dot = 2 x 2 owner-coloured cells inside a one-cell
   rim (4 x 4 cells); `MinimapRaster` perf row stays under 0.3 ms at 2,000 units; dot tests updated.
7. **S4 batch.** BUG-0103 (docs/03 delta-smoothing sentence for vsync-on rows, drop the "no bunching" remark,
   invariant culture on the two info lines, `--bench` capped at 3,600 s), BUG-0084 (bluer / stronger cliff tint,
   arrow tips lifted by the cell slope, CS8602 in `DebugOverlayTest.cs:173`, allocation probe relists with a live
   field), BUG-0086 (upload only the changed type's buffer), BUG-0088 (`OrdersTest` double-tap row waits on
   `Time.GetTicksMsec()`), BUG-0070 (docs/01 minimap row, docs/03 "over 220", one double-tap constant, wall test with
   a step on the far edges).
8. **Export hygiene notes (docs/03 "Build and export", M6):** exclude `game/tests/` from the release build; load
   `game/data/` in a `.pck`-safe way.

**Acceptance criteria (view).**
1. `QaM27Test` asserts target distance >= 100 m and centre displacement >= 25 m in 10 s (was 17.4 m); headless
   `--bench 2 --mute` exits 0 with one `bench:` line; windowed `--bench 60 --vsync off` numbers recorded in docs/03.
2. `bench:` `fps` within 2 % of frames / elapsed on a 10 s run (QA measured 111 vs 119.9 before); docs/03 matches.
3. 10 headless runs of the existing scenes show no `ObjectDB` leak warning; `SfxTest` has no fixed wait; docs/03 fixed.
4. Dev test: seeds 1-20, 100 per player: zero spots with a `Blocked` or `Resource` cell among their 8 neighbours and
   all cells of a block on one level; `--units 1000` fills 1,000 per side (the dots shot test); QA row tightened.
5. Allocation probe: overlay on, 300 frames with unchanged counts: 0 bytes from the label path; docs/03 claim true.
6. `MinimapRasterTests` dot shape = 2 x 2 centre in a one-cell rim; a lone dot's centre pixels at 220 px / 128 cells
   are the owner colour (QA screenshot crop); perf row under 0.3 ms at 2,000 units.
7. One regression test or doc fix per S4 item in 7; `dotnet build` has 0 warnings in `game/`.
8. docs/03 "Build and export" carries the two M6 notes.
9. All 17 game test scenes PASS headless; `tools/qa/smoke.ps1` PASS; non-Perf suite green; bug files get `Fixed by`.
10. No sim state change: the view track's `ViewApi` edits are read-only (`StartLayout`, `BenchScript`,
    `MinimapRaster`); `ReplayGoldenTests` untouched.

**Design references.** docs/02 "Map" (start locations are open bases), "Minimap" (dots in player colours); docs/03
"Rendering and presentation" M2-4 / M2-5 / M2-7 sections, "Debug tooling", "Build and export"; docs/01 minimap row.

**Constraints.** ViewApi additions read-only; C# only in `game/`; smoke must PASS; screenshots looked at for the
minimap dot and the start block (dev and QA).

**QA focus (view).** The bench's new target on seeds where (0.85 W, 0.5 H) is a cliff or a forest (fallback path);
start blocks on seeds 1-40 and `--units 1000` (fit, level, no node / cliff neighbour); the 2 x 2 dot at 100 and 990
units (lone dots at the border, a ramp, a cliff lip: crop and read pixels); `Sfx` quit path 10 / 10 clean; the
`OrdersTest` row under load (run the suite twice while the sim QA runs). Perf rows that fail under load count only
when they fail alone.

### M2 sign-off check (Producer, at ACCEPT, `stop_at_milestone_end: no`)
Every M2 coverage row ✅ for Unit / Invariant fuzz / Determinism (today all are, audio's Determinism "—" recorded as
not applicable: `Sfx` has no sim reference); no open S1 / S2; this hardening session done. Then: M2 Done in docs/05
with its retro, the view track's milestone becomes M3 (view side), For your review entry "how to try M2".

### After this session (view)
M3 view side: HUD resource bar (`World.Gold` / `Wood`), selection panel, command card (Tab subgroups), build ghost +
placement (`World.CanPlace` once per frame on the sim thread between ticks; one `Command.Build` per selected worker),
worker / gather / build feedback (`Cargo`, `Gathering` / `Returning` / `Building`, site hp bar).

## Data track

### Current session plan: STOP (cheap)
No inbox answers to the D1 / D2 review and no schema the track needs is on `main` (`techs.json` + building `requires`
come with M3-5 / M3-6; `abilities.json` / `statuses.json` and tower fields with M4; `ai.json` with M5). BUG-0111 (S4,
test-only) rides with the next data task that touches `Content/*ContentTests`. The track's counter stays 2 / 4.

## Watch-outs

- Two hardening sessions build and test at once: Perf rows fail from CPU contention; a failure counts only alone.
- Sim item 1 adds a `NavFlags` bit: the view's overlay reads flags but needs no change (`Pocket` cells are `Blocked`,
  drawn red). Sim item 3 changes cache semantics: keep `FlowFieldCacheTests` and the M3-2b QA rows green, and prove
  the clip guarantee before merging it.
- View item 4 changes where the default armies stand: the M2-7 screenshot set and the bench numbers may move a little;
  record them.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" (suggested text under For your review).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths.
