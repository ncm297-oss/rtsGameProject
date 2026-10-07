# Handoff: brief for the current / next session

Written by the Producer at the ACCEPT of session 2026-10-06-2114 (6 / 8 today; all three tracks ACCEPT, no fix
rounds). **The next session is a hardening session on the sim and view tracks (both 4 / 4) and a cheap STOP on the
data track** unless the inbox holds D1 / D2 review tweaks. **Bug numbers: sim from BUG-0094, view from BUG-0104,
data from BUG-0112.**

**Process fix for the next PLAN:** write the full numbered acceptance criteria of every track into this file's
"Current session plan" (this session only wrote the scope summary, and both QA inspectors had to reconstruct the
criteria from it).

## Where we are

- `main` (once the conductor merges sim, then view, then data) = 8a93b44 + M3-3 + M2-7 + D2. **M3 3 / 8** (resources,
  gather loop, placement + construction + repair); **M2 10 / 10 criteria ticked**, not signed off: the M2
  end-of-milestone hardening session is next, then sign-off. Data: units + buildings pinned to the faction pages;
  techs wait for the M3-5 schema. 32 open bugs after the merge (S3 18, S4 14), none S1 / S2.
- Producer checks at this ACCEPT: see the session log's Tests rows (non-Perf suites rerun per track; golden diff on
  the sim branch verified `data-hash` + `checksum` only; view smoke + headless bench run).
- Expected merge conflicts (append-only hunks all three tracks touched): `docs/01-vision.md` change log (sim and view
  each added a row after the M3-2b row), `studio/bugs/README.md` (sim 0091-0093, view 0101-0103, data 0111 all
  appended after the 0090 row), `studio/qa/coverage.md` (sim and view each added a table row; all three appended a
  note at the end). Keep every side, in sim, view, data order. No golden conflict this time: only the sim branch
  regenerated it.
- **Golden rule when sim and data both regenerate it:** merge sim, then view, then data; on the conflict take the
  sim side, then on the merged tree run
  `$env:RTS_REGEN_GOLDEN=1; dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~ReplayGoldenTests`
  (that run fails by design), commit the file, run it once more without the variable.
- Shared-file rule: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md (append).
  `game/data/**`: name the files per track in each brief; the data track never touches `game/data/common/**`,
  the sim track never touches `game/data/factions/**`.

## Sim track

### Next task candidate (sim): sim hardening session (QA standard; sim debt backlog in STATE)

Batch, most valuable first, inside ~1,500 lines:

1. **BUG-0093 (S3)**: a Cancel (or `Damage` to 0) of a building enclosed by other buildings reopens its cells as a
   pocket nobody can reach, and a Gather on a tree exposed only to that pocket loops for ever (BUG-0078's symptom with
   player commands). Options, Producer's preference first: (a) when a building is freed, flood from its reopened
   cells; if they reach no other passable region, keep them blocked with a new `NavFlags.Pocket` that an opening
   change next to them clears (re-flood then); (b) exposure / stand-cell choice means "reachable from the worker"
   (a flood per Gather resolve, bounded by the pocket); (c) document and leave. Un-skip the QA row.
2. **BUG-0091 (S3)**: `ConstructionSystem.Check` runs the seal flood before the cheap rules; `StartBuild` only
   needs pass / fail, so run UnitInTheWay / CannotAfford / StoreFull first there (CanPlace keeps the documented
   reason order; the view's ghost asks it once per frame, not per worker). Re-measure the 100-refused-Builds row.
3. **BUG-0080 (S3) design**: a closing change every tick starves all but 2 goal groups. Decide: keep a closed field
   usable when none of its directed cells was blocked (per-change bounding box test), or document the bound as
   "placements per second x groups / 2". Measured at a realistic rate (a House every 2 s): 0.80 s longest wait.
4. **BUG-0081 (S3, M6 design note)**: write the save/load decision for stale-but-usable fields into docs/03.
5. **BUG-0092 (S4)**: loader floor for the repair factors (reject below 2^-16), a Build clears the issuing worker's
   own Hold before the UnitInTheWay rule, push-out past 8 rings must not stack (search outward on rings until a free
   cell), push-out `Occupied` through the spatial hash, set `PrevPosition` on a pushed unit; docs/03 notes
   (CanPlace writes scratch: call between ticks on the sim thread).
6. **BUG-0079 item 3** (docs sentence on `--workers 0`), **BUG-0076**, **BUG-0071** (widen `SearchPlug` by
   `MaxUnitSpeed`, un-skip the QA row), **BUG-0072** (CLI `--record` pre-check).
7. Notes: the felling Perf row's 20 % headroom; `FlowField.Build` readability note (leave).

Then M3-4 production queues (5 slots), rally points, population and cap (`popProvided` applies), refunds; M3-5
Age II research + Forge upgrades (ships the `techs.json` schema and the building `requires` field the data track
asked for); M3-6 `trainedAt` / `requires` resolution and validation (QA D2 note: `trainedAt` is still an unchecked
string).

## View track

### Next task candidate (view): M2 end-of-milestone hardening (QA standard)

Batch, most valuable first:

1. **BUG-0101 (S3)**: the bench's `OrderAcross` must march across the map: target the passable cell nearest
   (0.85 W, 0.5 H) when the army is west of centre (and the mirror), or the opposite map corner; `QaM27Test`'s
   order-reach line then asserts >= ~100 m. Update docs/03 (M2-7) and the figures table if the numbers move.
2. **BUG-0102 (S3)**: `fps` = `Stats.Count / Script.Elapsed`; drop the TimeFps mean and its docs sentence.
3. **BUG-0087 (S3)**: stop the 8 `Sfx` players in `_ExitTree` / on the close request; drop the 0.3 s wait in
   `SfxTest`; fix the docs/03 sentence calling the leak a test-only artefact.
4. **BUG-0085 (S3)**: `ViewApi.StartLayout.Block` skips cells 8-adjacent to a `NavFlags.Resource` cell and (new, seen
   in M2-7) cells on a cliff edge / another level, so the seed-1 west army isn't inside a forest or straddling a
   cliff; tighten QA's `StartBlocks_OnTheMatchMap_*`.
5. **BUG-0083 (S3)**: cache the overlay label string or correct the docs/03 "0 bytes" claim.
6. **BUG-0069 (S3, owner may pick)**: default 2 x 2-cell owner centre inside the one-cell rim.
7. **BUG-0103, BUG-0084, BUG-0086, BUG-0088, BUG-0070 (S4)**: delta-smoothing sentence in docs/03, BenchScript
   remark, invariant culture on the two info lines, `--bench` upper bound (3,600 s), cliff tint, arrow dip, CS8602,
   per-type prop upload, `OrdersTest` double-tap row on `Time.GetTicksMsec()`, doc nits.
8. Export hygiene notes (M6): exclude `game/tests/` from the release build; load `game/data/` from a `.pck`.

**M2 sign-off check after it** (Producer, with `stop_at_milestone_end: no`): every M2 row in `studio/qa/coverage.md`
✅ for Unit, Invariant fuzz and Determinism. Today: "Camera, selection, orders" ✅ ✅ ✅; "Presentation timing & terrain
mesh" ✅ ✅ ✅; "HUD: minimap" ✅ ✅ ✅; "Debug overlay" ✅ ✅ ✅; "Resource props" ✅ ✅ ✅; "Playable check" ✅ ✅ ✅;
"Placeholder audio" ✅ ✅ — (Determinism "—": `Sfx` has no sim reference; the Producer records that as not
applicable). So the sign-off needs only the hardening session done and no open S1 / S2. Write the M2 retro in
docs/05, set M3's view side as the view track's milestone, add the "how to try it" For your review entry.

Then M3 view side: HUD resource bar (`World.Gold` / `Wood`), selection panel, command card (Tab subgroups), build
ghost + placement (calls `World.CanPlace` once per frame on the main thread between ticks; sends one
`Command.Build` per selected worker: the first places, the rest join), worker / gather / build feedback
(`Cargo`, `Gathering` / `Returning` / `Building`, site hp bar from `Buildings.Hp` / `Work`).

## Data track

### Next task candidate (data): owner review tweaks, else STOP

1. If the inbox has answers to the D1 / D2 review (names, numbers, descriptions, page tables): plan those edits first
   (QA light; golden `data-hash` regen if `units.json` / `buildings.json` change).
2. Otherwise **STOP the data track cheaply** (no schema it needs is on `main`): `techs.json` + building `requires`
   land with M3-5 / M3-6; `abilities.json` / `statuses.json` with M4; `ai.json` with M5; tower attack / sight /
   detector fields with M4. BUG-0111 (S4, test-only) goes into the next data task that touches
   `Content/UnitContentTests` / `BuildingContentTests`, or a data hardening session once the counter reaches 4.

## Watch-outs

- Two hardening sessions at once still build and test together: Perf rows fail from CPU contention (six did once this
  session under QA's windowed benches); a failure counts only when it fails alone. `OrdersTest.tscn` flakes under load
  (BUG-0088, fixed in this hardening batch).
- The M2 hardening session changes `BenchRunner` / `StartLayout`: rerun the pinned `BenchTest` row windowed and
  refresh the docs/03 figures table if the "across" march moves the numbers (it shouldn't by much).
- Sim hardening touches `NavGrid` flags if option (a) for BUG-0093 is taken: that changes `ComputeSteps` inputs; the
  QA `FlowFieldBuildEquivalenceQaTests` pin must stay green (a pocket flag is a Blocked flag to the builder).
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" and has no CLI line (STATE's For your review
  carries the suggested text).
- Builders: absolute paths only; never touch the owner's main checkout. Keep worktrees on short paths.
