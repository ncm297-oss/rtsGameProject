# QA coverage map

Maintained by the QA inspector. One row per game system, one column per kind of test. Every
system should reach at least ✅ in Unit, Invariant fuzz, and Determinism before its milestone is
signed off; Scale and Soak apply from M1 and M5 respectively.

Legend: ✅ covered · 🟡 partial · ❌ missing · — not applicable yet

| System | Milestone | Unit | Scenario | Invariant fuzz | Determinism | Scale/perf | Soak | Visual | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Solution skeleton & build | M0 | ✅ | — | — | — | — | — | 🟡 | `SimInfoTests`; `QA/ArchitectureTests` (no Godot refs in Rts.Sim.dll, strict csproj settings, forbidden-API source scan); `tools/qa/smoke.ps1` gate (build + import + boot + log check). Fresh clone verified 2026-10-03. Visual: windowed boot only, empty scene. |
| Tick loop & command queue | M1 | ✅ | — | 🟡 | ✅ | 🟡 | — | — | Dev: `SimulationTests`, `UnitStoreTests`, `SimRngTests`, `SimMathTests`, `StateHashTests`, `AllocationTests`. QA (M1-1): `QA/SimCoreQaTests` (RNG extreme seeds/interleave/uniformity, SimMath huge/NaN/signed-zero/dense-grid, forged handles, enqueue failure atomicity, 10k-command ordering, hash bit-flip/swap sensitivity); `Stress/SimCoreStressTests` (1M handle churn, 20x fuzz determinism with 100-tick checkpoints, 40 seeds distinct, 0-byte 10k flood, perf: flood sort and empty tick at 500/1k/2.5k units). Fuzz is partial: only Noop/SpawnUnit exist; extend as Move/Attack land. Skipped known-bug tests: BUG-0005 (BUG-0003/0004/0006 fixed in M1-2, tests un-skipped and mutation-checked). |
| Data loader & validation | M1 | ✅ | — | ✅ | ✅ | ✅ | — | — | Dev: `DataValidationTests` (shipped data loads, per-field faults, duplicates, malformed/missing files, all-errors-at-once), `DataConversionTests` (ticks, half-pop, id order, two loads equal). QA (M1-2): `QA/DataLoaderQaTests` (truncation at 400+ cut points, 35 malformed-content cases incl. BOM, lone surrogate, invalid UTF-8, deep nesting, wrong types, NaN/Infinity literals, int overflow; locked file, file-is-directory, stray/missing faction folders; seeded 400-iteration byte-mutation fuzz with GameData sanity invariants; 10k-unit load time/alloc (Perf); 2k duplicate ids; renamed/unicode/trailing-slash copy gives identical GameData; error list stable across loads; Find* round-trip; reflection check that GameData types expose only init-only primitives/strings/ImmutableArray). Skipped known-bug tests: BUG-0007/0008/0009/0010. Design conformance: unit stats checked against both faction pages by hand (all 14 match). |
| Terrain levels & nav grid | M1 | ✅ | — | ✅ | ✅ | ✅ | — | — | Dev: `HeightmapTests`, `NavGridTests`, `MapGeneratorTests` (+ `MapAssert`). QA (M1-3): `QA/MapQaChecker` (independent invariant checker: levels, ramp heights, flags/cost, border, 4-neighbour step rule, diagonal-leak check for no-corner-cutting, own flood fill); `QA/MapQaTests` (named seeds 0/1/42/2^63/max-1/max, 39 degenerate param sets with a 20 s hang guard, MapGen draw-count stability, Combat/AI streams untouched across 4 map setups x 3 seeds, World terrain == direct generation, WorldToCell boundaries/subnormals/non-square, CellCenter round trip for all 262k cells of a 512 map, 200k fuzzed queries, 1M-query 0-byte allocation); `Stress/MapStressTests` (sweeps: 2x1,000 default seeds Perf-tagged, 100+100 default, 500 tiny 32x32, 200 128x64; 512x512 time/memory; bounded worst-case params). Mutation-checked: removing pocket sealing, the ramp-mouth exception, the level-gap check, the border ring, or WorldToCell's floor each fails tests. Skipped known-bug tests: BUG-0011, BUG-0012 (x2), BUG-0014. No scenario/visual yet (no movement or rendering). |
| Flow fields & steering | M1 | — | — | — | — | — | — | — | |
| Replays | M1 | — | — | — | — | — | — | — | |
| Camera, selection, orders (view) | M2 | — | — | — | — | — | — | — | |
| Economy & buildings | M3 | — | — | — | — | — | — | — | |
| Combat & projectiles | M4 | — | — | — | — | — | — | — | |
| Fog, high ground, stealth | M4 | — | — | — | — | — | — | — | |
| Abilities, statuses, zones | M4 | — | — | — | — | — | — | — | |
| AI opponent | M5 | — | — | — | — | — | — | — | |
| Menus, save/load, export | M6 | — | — | — | — | — | — | — | |

M0-1 (2026-10-03-0826): skeleton row filled. The architecture scan in `QA/ArchitectureTests` covers every future sim file automatically.

M1-1 (2026-10-03-0907): tick loop / command queue / handles / RNG / SimMath / state hash row filled. `QA/ArchitectureTests` now also forbids the rest of the Math trig family and `using static System.Math[F]`.

M1-2 (2026-10-03-1151): data loader row filled. Bug-fix regression tests for BUG-0003/0004/0006 verified by mutation (reverting each fix makes its test fail).

M1-3 (2026-10-03-1235): terrain row filled. Data loader: `QA/DataLoaderBoundsQaTests` adds exact-limit tests (1e6 / 3600 s, at and just above), popCap message checks, indexed `requires[j]`/`tags[j]` paths; BUG-0007/0009 fixes mutation-checked (each guard removal is caught; the MaxSeconds guard is caught only by the QA test). BUG-0007/0009 tests un-skipped.
