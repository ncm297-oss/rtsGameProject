# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session 2026-10-06-0905 (2 / 8 today). Both tracks ACCEPT,
0 fix rounds. **M1 is signed off** (Producer, autopilot); the sim track starts M3. The view track's
hardening is done; next is M2-5. Both feature counters are at 0 / 4.

## Where we are

- `main` after the two merges: build 0 warnings; sim branch non-Perf 1841 / 16 skipped / 0 failed,
  Perf alone green; view branch 1890 / 20 / 0; smoke PASS; all 9 Godot scenes PASS. The conductor
  re-checks `main` after merging (expected overlap: `studio/bugs/README.md`, `studio/qa/coverage.md`,
  docs/03; keep both sides).
- Open bugs 16 (S3 10, S4 6), none S1/S2. New this session: sim BUG-0058 (fixed in-session),
  BUG-0071 (S3), BUG-0072 (S4); view BUG-0069 (S3), BUG-0070 (S4).
- Roadmap: M0 Done, **M1 Done (2026-10-06)**, M2 6 / 10 (view, Next), **M3 Next (sim)**.
- Bug numbers: sim from BUG-0073, view from BUG-0083.

## Sim track: next task candidates (feature session, QA full for sim rules)

1. **M3-1, first slice of the economy:** resource entities (gold mines and trees) as a data-driven
   store with generational handles, `game/data/` resource definitions, and tree depletion that
   updates the nav grid (`NavGrid.Version` must join the state hash now: docs/03 known limit). Read
   docs/02 "Economy" and docs/03 "Entities" first; keep it to one system (~800 lines) because the
   resource/nav interaction is new. Hash twins, replay golden (regen only if a map change is on
   purpose), 0-byte ticks.
2. Then: worker gather / return loop with drop-off choice; building placement + construction;
   production queues; Age II; full Malazan / Whirlwind data.
3. Requests for the sim track still open: 4 (`PrevFacing`, low priority; fold into any M3 slice
   that touches `UnitStore`). Request 3 (`DataError.ToString`) is done.
4. Debt for the next sim hardening (4 feature sessions away): BUG-0071 (S3, shove-pass plug cache
   vs stale hash; widen the query by `MaxUnitSpeed`), BUG-0072 (S4, open the `--record` file before
   ticking), BUG-0008 / 0010 (loader, or fold into the M3 data task), BUG-0005 before M5.

## View track: next task candidates (feature session, QA standard)

1. **M2-5 debug overlay:** nav grid (passable / blocked / ramp cells), flow arrows for the selected
   units' goal via `FlowFieldCache.PeekCached` (read-only; never keep a field across ticks), a
   tick-time graph; one toggle key (rebindable action), off by default; `--screenshot` of it on.
   Stays in `game/` + `sim/Rts.Sim/ViewApi/` (read-only helpers).
2. Then M2-6 placeholder audio (generated tones or CC0 already in the repo; no downloads), M2-7
   playable check at 60 FPS with 100 units, then the M2 end-of-milestone hardening and sign-off.
3. Debt for the next view hardening: BUG-0069 (S3, lone minimap dots read as the rim colour; the
   owner may pick a style, default = 2 x 2 owner centre in a one-cell rim), BUG-0070 (S4 nits:
   docs/01 minimap row, docs/03 range, twin double-tap constants, edge wall test), export hygiene
   (M6), edge-pan blocker note.

## Watch-outs

- Perf rows fail from CPU contention while the other track's QA runs: a failure counts only when it
  fails again alone (the tight-blob row has ~3% headroom at 4.33-4.35 ms vs 4.5).
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" and has no CLI line; the
  For-your-review entries ask the owner to update it (now: sim M3, view M2).
- The view dev's relative-path file I/O briefly rewrote two files in the owner's main checkout this
  session (restored byte-identical). Builders: absolute paths only, and never touch the main checkout.
- Remote Control is unavailable in unattended sessions; don't retry it.
- Keep the public setup API additive for the view (`SimConfig`, `Simulation.Enqueue`, `Command.*`).
  M1-9 renamed one public const (`MaxPlugSpan` -> `MaxPlugCluster`), unused by `game/`.
