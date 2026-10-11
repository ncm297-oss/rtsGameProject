# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-10-1954** (fifth full session of 2026-10-10, cap 8). All three tracks
accepted; **integration order sim → data → view** (the conductor merges; whichever of sim / data merges first lands D11). After it,
`main` carries M4-5 (stealth / detection), M4-V7 slice 2, D11 and D-H3. **M4: 10 / 10 criteria met.** What is left before the
sign-off is each track's end-of-M4 work: **sim M4-H3** (hardening), **view M4-VH3** (hardening), **data D12** (the casters'
`detector 10`). The next session runs those three; the sign-off follows when all three are on `main` (the Producer, at that
session's ACCEPT or the one after: every required criterion verified, coverage ✅ for the M4 systems, no open S1 / S2, the retro
in docs/05, the next milestone M5 set to Next).

## Where we are

- M0-M3 Done. **M4 10 / 10** (criterion 7 ticked this session). No open S1 / S2. Counters after this session: sim 3 / 4, view 3 / 4,
  data 0 / 4; the sim and view take their end-of-M4 hardenings next regardless of the counter.
- **Golden** `cross_map_seed1.replay` `data-hash` is **889D6BEA3A49F08E** after the integration (M4-5 moved it from 4383EBB9345FABF7;
  every `k` line equal; both hashes in `GatherWedgeQaTests.SameGameDataHashes`). **D12 moves it once more** (the casters' `detector`
  is hashed); rule 3 (append to `SameGameDataHashes`), rule 8 (the shipped hash is pinned by the golden only; the data branch's
  `QA/Content/DescriptionWordingQaTests.TheShippedContentHash_IsTheGoldenReplaysHeader` reads the replay header and follows).
- **Memory bound** 231,920,000: 231,890,440 used (**29,560 B margin**). BUG-0480's fix (a detection scratch, `int[UnitCapacity]`
  ≈ 16,408 B) fits once; any further per-slot array re-baselines, itemised in the brief and the report (rule 7).
- **`EmptyTick(2500)`** sits on its 500 µs gate on base and head alike (BUG-0451); the counting rule stands (a Perf failure counts only
  alone and above base). M4-H3 decides: buy back idle-path cost, else re-baseline to 550 µs with the trend itemised.
- **Bug ids next session:** sim from **BUG-0510**, view from **BUG-0520**, data from **BUG-0530**.
- Standing rules unchanged (1-8: full suite incl. Perf alone + the merged scene loop for vision / combat / loader changes; a new
  `UnitState` ships its `ui.json` line; whoever moves `data-hash` adds it to `SameGameDataHashes`; `ui.json` keys named in the brief;
  a sim behaviour change a view scene asserts on gets a named item in the view brief; a new `Command` kind goes into
  `CommandDoorFuzzStressTests`; per-slot memory itemised; the shipped `data-hash` pinned in the golden only, never a literal).
- Producer decision this session: **the view's stealth visuals ("M4-V8", docs/03 "For the view (M4-V8)") are an M8 task** (the
  roadmap lists them there; no unit can be stealthed before Shadow; `Fog.CanSeeUnit` already hides an undetected stealthed enemy).
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).

## Sim track

### Next session plan: M4-H3, the end-of-M4 sim hardening · hardening · QA full for the BUG-0461 rule, standard for the rest · up to ~1,500 lines incl. QA

Work the debt backlog most valuable first, as many as fit; every fix gets a regression test that failed first; golden untouched unless a
re-record is already needed (then BUG-0275 item 1 rides along).

1. **BUG-0461 (S3):** a cast's walk-in hemmed in by own `Attacking` units counts the plain Move give-up (`GiveUpTicks` 20) and drops the
   cast silently (three replays in the bug file). Direction (Producer default, owner may revisit): a walk-in whose caster is blocked by
   an own unit within 1.5 m in its direction of travel does not count stuck ticks (or a cast's own patience, 3 s), and a dropped cast
   raises an `AbilityEvent` with a `Dropped` flag the view can show; the 0.17 m limit cycle on flat ground is a movement bug of the
   BUG-0035 / 0037 family: fix if a few lines, else document in docs/03 "Known limits". QA full on this one (the sandbox replays are
   the regression scenes; `M4PlayableTest` must still pass on seeds 1 and 6 in the merged loop).
2. **BUG-0480 (S3):** `StealthSystem.OnStealthStarted` writes into `World.ShoveNeighbors` while `PassiveSystem.OnDeath` iterates it:
   give detection its own scratch (`World.DetectNeighbors`, itemised; or the one-shot `Neighbors` when free), un-skip QA's
   `AnOnDeathPassiveApplyingStealthed_ReachesEveryListedFriend(true)`; docs/03 "Implementation (M4-5)" known limit (2) updated.
3. **BUG-0481 (S3):** the loader requires exactly one `revealed` kind when any `stealthed` kind is present (error at `statuses`); a
   `Data/StealthLoaderTests` row; QA's `StealthedWithoutARevealedStatus_IsRefusedAtLoad` un-skipped; docs/03 data bullet.
4. **BUG-0482 (S4)** if a few lines: a reveal replaces the oldest non-stealth entry when the store is full, else document the limit.
5. **BUG-0451 decision** (above). **BUG-0401 (S3):** an ordered chase whose target is reachable (finite flow-field cost at the unit's
   cell) never counts stalled scans; the seed-6 `QaGhostViewTest` layout as an xUnit row.
6. Then: BUG-0302 b / c (the memory itemisation note; `AbilityPerfTests` phases run twice), BUG-0275 items 1-2 (decide), BUG-0271 /
   0272 (the dev recommends `wontfix` as documented limits: decide and record), BUG-0144, BUG-0142 items 1-2, BUG-0113 item 2,
   BUG-0094, BUG-0391, BUG-0422 items 2-3; bug files set `fixed` / `wontfix` with the proving test named; the README index rows.
7. docs/03 updated with every behaviour change; docs/01 row for any rule decided (BUG-0461's direction, BUG-0401's).

**Out of scope:** any feature, `game/data/factions/**` (D12 writes `units.json` this session), `ui.json`, `DataContentHashTests.cs`.

**Acceptance (shape):** each worked bug's regression test failed first and passes; full suite incl. Perf alone green (BUG-0451 rule);
smoke PASS; the merged scene loop 41 / 41 (the sandbox on both seeds); 0 warnings; memory delta itemised; docs/03 + docs/01;
bug files + README rows. Then the M4 sign-off is the Producer's at ACCEPT once view and data are also in.

## View track

### Next session plan: M4-VH3, the end-of-M4 view hardening · hardening · QA standard · up to ~1,500 lines incl. QA

1. **BUG-0430 (S3) + BUG-0462 item 1:** a zone disc / the hover and targeting rings are flat planes at the centre's height, so across a
   cliff they sink under the higher ground or float 4 m: project them onto the terrain (a decal, a heightmap-sampling shader, or a
   terrain-hugging mesh with the depth test off); windowed stills at the cliff (`qa-v6c-cliff-zoom30/50` as the before) looked at.
2. **BUG-0490 (S4):** `TickBudget`'s comment and docs/03 state "about 3,100 / 4,500 worst seen" (or the measured range with QA's 3,081 /
   4,488); one `FillMaxHp` a frame shared by `UnitViews` and `CombatViews`.
3. **BUG-0431 (S4):** a distinct own-storm rim colour (not within 0.06 of Whirlwind's `#C8892E`), checked in a windowed still.
4. **BUG-0462 item 3** (marker rows merge at zoom 60: fix or an M6 note), **BUG-0400**, **BUG-0250 item 2**, **BUG-0148 item 1**,
   **BUG-0126 items 5 / 6**, **export hygiene** (exclude `game/tests/` from the release build; load `game/data/` and `ui.json` in a
   `.pck`-safe way), docs/03 "Build and export".
5. Bug files + README rows; docs/03 per change.

**Out of scope:** stealth visuals (M8), any `game/data/**`, replay or `DataContentHashTests.cs` edit; no reference to the stealth
kinds needed. The sim's M4-H3 may change the cast walk-in rule (BUG-0461): `M4PlayableTest` works round it with plain Moves, so no
scene change is expected; if the sim adds an `AbilityEvent` `Dropped` flag, the view ignores it this session (an M5 / M6 HUD item).

**Acceptance (shape):** every worked bug's regression (xUnit or a scene row) failed first; scene loop 41 / 41; smoke PASS; 0 warnings;
no new player-facing literal; windowed stills for the visual fixes looked at; docs/03; bug files.

## Data track

### Next session plan: inbox tweaks first, else D12 · feature · QA light · ~500 lines incl. QA

**Base:** `main` after this integration (the `detector` schema and the `stealthed` / `revealed` statuses are on it). Files this task may
write under `game/data/`: `game/data/factions/malazan/units.json`, `game/data/factions/whirlwind/units.json` (no other track writes
`game/data/**` that session); `docs/factions/malazan.md`, `docs/factions/whirlwind.md`; `sim/Rts.Sim.Tests/Content/**`,
`sim/Rts.Sim.Tests/QA/Content/**`; `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` + the `SameGameDataHashes` line in
`QA/GatherWedgeQaTests.cs` (the one golden regen; named exception).

1. `"detector": 10` on **`malazan_cadre_mage`** and **`whirlwind_priest`** (the two shipped casters; docs/02 "Caster" class row and
   "Stealth and detection": "all casters (10 m)").
2. A Detector cell on both faction pages (the unit tables or the Unit notes, in the pages' existing style) pinned both ways by
   `Content/UnitContentTests` (or the page compare the pages already use), with mutants that fail by name.
3. The `stealthed` / `revealed` texts pinned against docs/02 "Status effects" rows 224-225 (`Stealthed` "Invisible and untargetable to
   enemies without detection", `Revealed` "Cannot be stealthed (overrides Stealthed)"): out of `AbilityContentTests.PendingStatuses`;
   the descriptions in `common/statuses.json` are the sim's (numberless; the data track may propose rewording in the report, not edit
   `common/**`).
4. One golden regen (`data-hash` 889D6BEA3A49F08E → new; every `k` line equal; appended to `SameGameDataHashes` with the reason).
5. BUG-0500 (the two linger patterns, QA's row un-skipped) and the BUG-0470 status line (item 3 was fixed at the 1314 ACCEPT) if a few lines.
6. The review table in the report (unit / field / old → new / why; the two page cells quoted).

**Out of scope:** the balance pass (after the M4 sign-off, on the owner's D6 answer or the Producer default 40-65 %), any
`common/**` edit, any C# outside the data test folders.

**Acceptance (shape):** `DataValidationTests` green; both detectors load as 10 m (`UnitDef.Detector`); the page cells pinned both ways
(mutants fail by name); `PendingStatuses` without Stealthed / Revealed and `DescriptionProblems` 0 on shipped text; the golden diff is
`data-hash` + `checksum` only; `ReplayGoldenTests` + `GatherWedgeQaTests` green; non-Perf suite green, 0 warnings; the review table.

## Watch-outs (all tracks)

- **Integration of this session:** sim → data → view. Conflicts foreseen by `git merge-tree`: `studio/bugs/README.md` (sim + data and
  sim + view: union by id), `studio/qa/coverage.md` (sim + view: keep both rows' additions). docs/03 auto-merges;
  `DataContentHashTests.cs` is the identical hunk. The merged scene loop (41, both playables last) is the gate.
- **Next session's integration:** sim and view (hardenings) touch no `game/data/**`; data (D12) alone moves the golden. If M4-H3
  changes any hashed state or re-records the seed-21 replay, it says so and the data brief's regen comes after the sim merge (same
  session, conductor order sim → view → data, or data → sim if the sim's golden is untouched: the Producer states the order at PLAN).
- **The M4 sign-off** needs `studio/qa/coverage.md` ✅ for Unit / Invariant fuzz / Determinism on every M4 system (the Fog / stealth row
  is ✅ ✅ ✅ now; check the Abilities row's Scenario 🟡 is not one of the three required columns) and the retro in docs/05.
- Perf under three suites fails on base and head alike (`TightBlob2500`, `EmptyTick(2500)`); a failure counts only alone and above base.
- Scratch directories in the shared scratchpad carry the agents' names; never `rm -rf` a shared name. The data QA's `base` / `base2`
  worktrees of this session are owner / conductor housekeeping (`git worktree prune`).
