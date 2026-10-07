# Handoff: brief for the current / next session

Written by the Producer at the ACCEPT of session 2026-10-07-0800 (the resumed 2026-10-06-2326; first full session of
2026-10-07, cap 8). Both tracks ACCEPT: **sim M3-H1** (sim hardening) and **view M2-H2** (M2 end-of-milestone hardening,
**M2 signed off**). Data STOPped cheaply. The next session is a **feature session on both tracks** (counters reset:
sim 0 / 4, view 0 / 4; data stays 2 / 4). **Bug numbers for the next session: sim from BUG-0097 (0097-0100 free, then
0112-0121), view from BUG-0106 (0106-0110 free, then 0122-0131), data from BUG-0132.**

## Where we are

- `main` after the merge of 363c499 (sim) and 51fa9e1 (view): M2 **Done** (retro in docs/05); M3 3 / 8 (resources,
  gather loop, placement + construction + repair sim half; data: units + buildings pinned to the faction pages). 20 open
  bugs (S3 12, S4 8), no S1 / S2. Producer checks at this ACCEPT: sim branch build 0 errors / 1 warning (the CS8602 the
  view branch removes), view branch 0 / 0; non-Perf suites green on both (numbers in the session log); view smoke PASS.
- Shared-file rule: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md (append),
  `studio/bugs/README.md` (append rows). Append-only conflicts are expected there; keep every side, sim then view.
- Golden: only the sim track may regenerate it. M3-4 touches the hash (production queues, pop) so a golden regen is
  likely; the log must say which checkpoints moved and why (the march scenario has no production, so ideally none).

## Sim track

### Next task candidate: M3-4 — production queues, rally points, population and cap, refunds (feature, QA full)

**Goal.** The next unmet M3 criterion: "Production queues (5 slots), rally points, population and cap, refunds on
cancel." Buildings with `trains` (the data already lists what each hall trains) take a `Train` command, queue up to 5
items, pay at queue time, spawn the unit at the building's edge after `trainTime` (data, converted to ticks), send it to
the rally point if one is set, and refuse or wait when population is at the cap (`popProvided` of finished own
buildings, starting cap from `rules.json`, the design doc's max). Cancelling a queued item refunds its cost in full
(docs/02: refunds on cancel). Read docs/02 "Economy" / "Production" and "Population" before scoping.

**Scope notes.** Public read access for the view (`Buildings.Queue(slot)` / progress, `World.Pop` / `PopCap` per
player) ships with it (the view's M3 selection panel needs them). The AI build-order hook is M5 (don't design it now).
`SetRally` is a building command (not a unit order). Spawn placement: the nearest free cell outside the footprint, the
same rule as push-out (`ConstructionSystem.PushOut`'s ring search; consider sharing it). Hash every new field; replay
format stays 3 if the new kinds ride in `c` lines. Keep the public setup API additive. **Out:** techs / Age II (M3-5),
`trainedAt` / `requires` validation (M3-6), rally-point visuals, HUD.

**Watch-outs.** `trainTime` is seconds in data → ticks in the loader (existing convention). Population half-steps
(docs/02: 2-3 pop units, half-step support for M7) are already in the loader as half-pop ints: use them, don't add a
float. A unit spawning into a full footprint ring with no free cell should wait a tick, not stack (the BUG-0095 lesson).
The 500-unit perf criterion must hold with 20 halls producing.

**Debt to fold in only if a few lines:** none. BUG-0095 / 0096 (S3) wait for the next sim hardening session.

### After M3-4 (sim)
M3-5 Age II research + Forge upgrades (ships `techs.json` and the building `requires` field the data track asked for,
Requests 8); M3-6 `trainedAt` / `requires` resolution and validation (+ loader nits BUG-0008 / 0010).

## View track

### Next task candidate: M3-V1 — HUD resource bar, worker orders in the window (feature, QA standard)

**Goal.** The first view half of M3: the owner can see the economy run and drive it. (1) A **resource bar** (top of the
`Hud` layer): gold, wood, and population once the sim ships it (`World.Gold` / `Wood` spans exist; show pop only if
`World.Pop` is on `main`, else leave the slot). (2) **Right-click on a tree or mine** with workers selected sends
`Command.Gather` (the view resolves the node under the cursor from `World.Resources` cells; non-workers in the selection
get a plain Move to the click, as RTSs do). (3) **Worker feedback**: a small cargo marker or label state for
`Gathering` / `Returning` / `Building`, and a hit-point / progress bar over construction sites (`Buildings.Hp`,
`Work` / `WorkNeeded`). (4) The default match spawns a Town Hall per player and a few workers (the CLI's `--workers`
rule exists in `tools/Rts.Cli`; the view needs its own, in `Match.Start`: an open 4 x 4 spot nearest the start block,
`SpawnBuilding` dev command until M6 start locations) so there is something to gather for.

**Scope notes.** Player-facing text (resource names, state labels) comes from data where it exists (`displayName`);
numbers only otherwise. `ViewApi` additions stay read-only (a `ResourcePicker` that maps a ground point to a node slot
is fine). The build ghost, command card and build menu are M3-V2. **Out:** production UI (needs M3-4 on `main`), fog.

**Watch-outs.** `World.CanPlace` (M3-V2) writes flow-field scratch: call it on the sim thread between ticks. Hash-twin
proof for every new read (the standing QA rule). The M3-H1 push-out sets `PrevPosition` with `Position`, so the view
never draws a pushed unit sliding through a building. BUG-0104 (S3, bench march bound) and BUG-0105 (S4) wait for the
next view hardening session, unless the bench test is touched anyway.

### After M3-V1 (view)
M3-V2 command card (Tab subgroups, grid hotkeys per docs/02 "Controls"), worker build menu, build ghost + placement
(`World.CanPlace` once per frame, one `Command.Build` per selected worker), Cancel / Repair buttons; M3-V3 production
UI (queue, rally point marker) once M3-4 is on `main`; then the M3 "Playable: build a full Malazan base and reach Age II".

## Data track

### Next session plan: STOP (cheap) unless the inbox has D1 / D2 tweaks
No schema the track needs is on `main` yet (`techs.json` + building `requires` come with M3-5 / M3-6; `abilities.json` /
`statuses.json` and tower fields with M4; `ai.json` with M5). BUG-0111 (S4, test-only) rides with the next data task
that touches `Content/*ContentTests`. Counter stays 2 / 4. If the owner answers the D1 / D2 review in the inbox, that
tweak is the data task (QA light; golden `data-hash` regen).

## Watch-outs (all tracks)

- Two feature tasks build and test at once: Perf rows can fail from CPU contention; a failure counts only alone.
  `TightBlob2500` has 0.02 ms of headroom alone (4.48 vs 4.5 ms): if it fails alone, that is a real regression.
- The view's M3-V1 reads `World.Pop` only if the sim's M3-4 has merged first; plan the view task so it doesn't block on
  it (show the slot empty or hide it).
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" (suggested text under For your review).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths.
