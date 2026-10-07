# Handoff: brief for the current / next session

Written by the Producer at the ACCEPT of session 2026-10-06-1744 (5 / 8 today; all three tracks ACCEPT).
The next session is a **feature** session on every track (counters after this one: sim 3 / 4, view 3 / 4,
data 1 / 4). The session after it is the sim hardening session and the view's M2 end-of-milestone
hardening (both reach 4 / 4 then).

## Where we are

- `main` (once the conductor merges sim, then view, then data) = a80c143 + M3-2b + M2-6 + D1. M3 2 / 8
  (resources; gather loop; BUG-0073 / 0074 / 0077 closed by M3-2b), M2 9 / 10 (left: M2-7 playable check),
  the data half of "factions fully defined in data" is units + buildings (techs wait for the M3-5 schema).
  26 open bugs (S3 15, S4 11), none S1/S2.
- Producer checks at this ACCEPT: build 0 errors, 1 warning (CS8602 `game/tests/DebugOverlayTest.cs:173`,
  BUG-0084); sim branch 2383 / 12 skipped / 0 failed non-Perf, data branch 2345 / 15 / 0; felling Perf row
  alone 0.402 / 0.406 ms; view smoke PASS, `SfxTest` / `QaM26Test` PASS; CLI `--workers 10` twins identical.
  A scratch merge of all three branches built, regenerated the golden (`data-hash` + checksum vs the sim
  branch's copy) and passed `ReplayGoldenTests`; its full non-Perf suite: 2416 / 12 skipped / 0 failed,
  smoke PASS, felling Perf row 1 / 1 alone.
- **Bug numbers: sim from BUG-0091, view from BUG-0101, data from BUG-0111** (0088 was the Producer's).
- **Golden rule when sim and data both regenerate it:** merge sim, then view, then data; on the conflict
  take the sim side, then on the merged tree run
  `$env:RTS_REGEN_GOLDEN=1; dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~ReplayGoldenTests`
  (that run fails by design), commit the file, run it once more without the variable. Verified this
  session: the result differs from the sim copy by `data-hash` + `checksum` only.
- Shared-file rule: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md
  (append). `game/data/**`: name the files per track in each brief; the data track never touches
  `game/data/common/**` (sim owns it), the sim track never touches `game/data/factions/**`.

## Sim track

### Next task candidate (sim): M3-3 — building placement and construction (QA full)

The roadmap criterion "Building placement (ghost preview, validity), construction with multiple builders,
repair"; sim side only (the ghost preview itself is view work after this). D1 gives every slot to place.
Points for the brief:

- Validity in the sim (`BuildingStore.Fits` today checks footprint cells only): open ground (no cliff /
  ramp / node / building), no unit inside (or push units out, the RTS norm; today's dev command refuses),
  and **no sealing of passable ground** (a flood check after the footprint is taken), which closes BUG-0078
  and makes "exposure" mean "reachable". The docs/03 sentence "Unreachable targets can't happen yet: the
  nav grid seals every pocket" needs the building case.
- `Command.Build` (worker, building id, cell): cost deducted at placement, a construction site with
  progress; several builders: `t x 3 / (n + 2)` (docs/02 "Buildings"); cancel refunds; destruction
  (`Free` + grid clear = an opening change); repair. A placed site is a closing change (`BlockVersion`),
  which M3-2b already handles; BUG-0080 (back-to-back closings starve groups) becomes reachable once
  several placers exist: keep it S3 unless M3-3's tests show stalls at a realistic placement rate.
- Fold in while in `Rts.Sim/Data`: the stale "M3-2 ships the Town Hall only" comment in
  `BuildingSlot.cs` (BUG-0090 part); BUG-0079's "missing `cost` is three errors" if it is a few lines.
- Minimum data entries only (`rules.json` numbers for construction if new), the data track fills nothing
  for this task. Keep `Command` layout additive; keep `SimConfig` additive.
- Size: a whole criterion, so up to `max_task_lines` (1,500) if docs/02 is specific; otherwise slice
  (placement + validity first, construction second).

Then: M3-4 production queues (5 slots), rally points, population and cap, refunds; M3-5 Age II research
+ Forge upgrades (ships the `techs.json` schema and a building `requires` field: both requested by the
data track); M3-6 `trainedAt` / `requires` resolution and validation.

### Sim hardening (the session after next; sim debt backlog in STATE)

BUG-0080 (design: keep a closed field usable when none of its directed cells was blocked, or accept and
document), BUG-0081 (M6 save/load design note: decide and write it in docs/03), BUG-0079, BUG-0076,
BUG-0071, BUG-0072, BUG-0008 / 0010 (loader), BUG-0005 before M5, BUG-0025 / 0026, the felling Perf
row's 20 % headroom (widen the scene or pin p50 if it ever flakes alone), `FlowField.Build` readability
note (8 written-out blocks; fold back only if tests move to Release).

## View track

### Next task candidate (view): M2-7 — playable check (QA standard)

The last M2 criterion: "the owner moves an army of 100 placeholder units around a generated map at
60 FPS". The owner's playtest is the real check; the studio's part:

- Measured: windowed, default map (12 / 8), HUD and sounds on, 100 units per player: frame time and FPS
  from the label over a scripted minute (select, order across the map, minimap jumps, zoom extremes),
  plus `--units 1000 --zoom 60` as the stress figure; vsync on and off numbers.
- `PrevFacing` blending (sim request 4, landed in M3-2): unit views turn smoothly between ticks.
- A stable screenshot set under `--screenshot` for the record (overview, a ramp crossing, the minimap
  corner, overlay on); keep the files out of the repo unless small.
- Anything cheap from docs/02 "Presentation" that the criterion needs (a selection count is already in
  the label); nothing new in scope beyond polish.
- Then the Producer ticks the criterion on the numbers and asks the owner to play (For your review).

### View hardening (the session after next): M2 end-of-milestone

BUG-0069 (lone minimap dots; owner may pick the variant), BUG-0070, BUG-0083, BUG-0084, BUG-0085 (start
block clear of trees), BUG-0086, BUG-0087 (stop the Sfx players at quit), BUG-0088 (OrdersTest double-tap
row on the wall clock), export hygiene notes. Sign-off check: every M2 row in `studio/qa/coverage.md` ✅
for Unit, Invariant fuzz and Determinism; where a view row's fuzz column is 🟡 by nature, QA must either
add the fuzz or the Producer records why it doesn't apply.

Then M3 view side: HUD resource bar (`World.Gold` / `Wood`), selection panel, command card (Tab
subgroups), build ghosts (after M3-3), worker / gather feedback (`Cargo`, `Gathering` / `Returning`).

## Data track

### Next task candidate (data): owner review tweaks, else D2

1. If the inbox has answers to the D1 review (names, numbers, descriptions): plan those edits first
   (QA light; golden `data-hash` regen).
2. **D2: faction pages carry a stats table per building** (`docs/factions/malazan.md`, `whirlwind.md`
   "Buildings": today only names and ids; add hp / armor / cost / time / footprint / provides from
   `buildings.json`, so the pages stay the design source) and **unit description polish**
   (`game/data/factions/*/units.json` `description`: each a player-facing line ≤ 160 chars ending with
   a period; names match `displayName`s). A units.json text change moves the `data-hash`: regen with
   checkpoints byte-identical. Files: the two faction pages, the two `units.json`. Nobody else touches
   them. QA light.
3. Waiting on schemas: `techs.json` + building `requires` (M3-5 / M3-6), `abilities.json` /
   `statuses.json` (M4), `ai.json` build orders (M5), tower attack / sight / detector fields (M4
   combat / fog); M7-M9 faction data when those milestones open; balance passes (QA standard) after the
   M4 sandbox.

## Watch-outs

- Three tracks build and test at once: Perf rows fail from CPU contention (the felling row first: ~20 %
  headroom); a failure counts only when it fails alone. `OrdersTest.tscn` can also flake under load
  (BUG-0088): rerun alone before filing.
- Golden conflicts between sim and data: the rule in "Where we are".
- Keep worktrees on short paths: the suite run from a scratch worktree under a ~130-character temp path
  sat nearly idle for half an hour before passing (xUnit duration 14 min, wall 45); the same tree at
  `.claude/worktrees/merge-check` ran normally.
- If M3-3 adds `rules.json` fields, the sim task ships the minimum entries; the data track stays out of
  `common/`.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" and has no CLI line (STATE's For
  your review carries the suggested text).
- Builders: absolute paths only; never touch the owner's main checkout. Remote Control unavailable in
  unattended sessions. The routine's hourly schedule may be gone (see Waiting on you).
