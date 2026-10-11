# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-11-0314** (first full session of 2026-10-11, 1 / 8). All three tracks
accepted and merged (sim 0b463ac → view 4d27b70 → data f5a6c6d). **M5 (the AI opponent) is in progress: 0 / 6, criterion 1
advanced.** Counters after this session: sim 1 / 4, view 1 / 4, data 2 / 4. Bug ids next: sim from **BUG-0570**, view from
**BUG-0580**, data from **BUG-0590**. Integration order sim → view → data.

## Where we are (checked at this ACCEPT)

- `main` after this integration: build 0 warnings; the full non-Perf suite on the Producer's scratch merge of all three branches
  is in the session log's Tests rows. Golden `cross_map_seed1.replay` header **`data-hash 5760E83CB301B62E`**, `checksum`
  48AD14AAE5093A7C (D13; D12's C4C16BD39408471A and D13's both in `GatherWedgeQaTests.SameGameDataHashes`). `ai.json` is **not** in
  `ContentHash` (M5-2 adds it with one golden regen).
- `Rts.Sim.Ai.PlayerView` is on `main` (docs/03 "Implementation (M5-1)"); no AI acts yet. The `ai.json` schema is on `main` with the
  minimal files (docs/03 "For the data track (M5-1)").
- **Memory bound** `QA/FieldBuildFairnessQaTests.cs:473` 231,920,000: 231,900,840-231,908,984 measured (margin ~11 KB). **M5-2's
  per-slot or per-player AI arrays re-baseline it** (itemised: bytes per slot x capacity x players, in the test's comment and docs/03).
- Perf: `EmptyTick(2500)` gate 550 µs; `TightBlob2500` 4.5 ms with ~0.1 ms headroom alone; `PlayerViewPerfTests` banded 0.23 ms
  (gate 0.3), interleaved 0.57 ms reported (BUG-0542 S4: a gated row for it in the next sim hardening). A Perf failure counts only
  alone and above base. Three suites run at once; the non-Perf suite is ~15-19 min under load (M5-1's fuzz added ~17 s a seed).
- No open S1 / S2. S3 15, S4 15. **BUG-0540 (S3) is M5-2's first item; BUG-0560 (S3) is the view's first item.**
- Standing rules 1-9 unchanged (full suite incl. Perf alone + the merged scene loop for vision / combat / loader changes; a new
  `UnitState` ships its `ui.json` line; whoever moves `data-hash` appends to `SameGameDataHashes`; `ui.json` keys named in the brief;
  a sim or data change a view scene asserts on gets a named item in the view brief; a new `Command` kind goes into
  `CommandDoorFuzzStressTests`; per-slot memory itemised; the shipped `data-hash` pinned in the golden only; a brief names every test
  file another track's change will break, the final assertion, and which branch ships the edit). **New rule 10:** every agent's
  scratch folder under the scratchpad is named `<track>-<session>-<purpose>` (two QAs shared `scratchpad/clone` this session and one
  overwrote the other mid-run), and **every script uses absolute paths** (`[IO.File]` with a relative path writes into the process's
  working directory, which may be the owner's checkout). **Rule 11:** the dev sets the statuses of the bugs its task fixes (QA checks).
- `CLAUDE.md` still says "Current milestone: M1" (owner's file; drift noted, not changed). The owner's checkout `C:\Dev\rtsGameProject`
  is ~165 commits behind `origin/main` (not the studio's concern).
- Inbox: nothing new at this session's PLAN. Wanted from the owner (non-blocking): the D13 choice (×1.1 / ×1.5 / an hp-armor-cost pass).

## Sim track

### Next task candidate: M5-2, the AI's first decisions · feature · QA full · ~1,200-1,500 lines incl. QA (slice if the managers run long)

**Goal.** Criterion 2's first half: an `AiPlayer` that reads only its `PlayerView` and acts only through `Command`s, running the
`ai.json` build order and a basic economy and production; and the BUG-0540 leak closed first so criterion 1 can be ticked.

**Scope (in order).**
1. **BUG-0540.** Per-player node memory in the fog (one last-known entry per resource slot, the `Fog.Ghosts` pattern, hashed or
   derived: say which and why; the view's `ViewApi.SeenResources` is the rule to match), `PlayerView.TryGetResource` / `NextResource`
   answering the remembered node until a footprint cell is visible; un-skip QA's `ANodeDepletedOutOfSight_IsStillAnswered_UntilItsGroundIsSeen`,
   delete the "today" row; the oracle (`Ai/PlayerViewOracle`) learns the rule; docs/03 "Lifetime" reworded. Memory itemised.
2. `sim/Rts.Sim/Ai/AiPlayer.cs` (+ `Blackboard.cs`, `BuildOrderExecutor.cs`, `EconomyManager.cs`, `ProductionManager.cs`): one per AI
   seat (`SimConfig` gains the AI seats and difficulty per player, or a `Simulation.AddAi(player, difficulty)`; say which), ticked by
   the sim **before** phase 1 of the tick it thinks on (every `ThinkTicks`), enqueuing commands that apply the normal way (so a replay
   that records commands reproduces the AI's actions: state whether AI commands are recorded or re-derived; the docs/03 "AI
   architecture" section decides). The source-scan guard must stay green: nothing but `PlayerView.cs` names `World`.
3. `BuildOrderExecutor`: steps by `time` / `workers` / `pop` with `buildOrderSlack`; `build` places through a **fog-safe placement
   check** (a new `PlayerView.CanPlace` that counts only buildings the player sees or remembers; `World.CanPlace` counts hidden enemy
   buildings, docs/01 2026-10-09), `train` through `Command.Train`, `research` through `Command.Research`, `expand` as a Town Hall
   placement near the nearest known unclaimed gold mine (or M5-3 if it needs scouting: say so). Economy: idle workers to gold / wood by a
   target ratio, a house when within 2 pop of the cap. Production: fill the composition by weight once the build order has no due step.
4. `ai.json` into `ContentHash` with **one golden regen** (`k` lines equal: no AI seat in the golden match); `DataContentHashTests` row.
5. Tests: `Ai/AiPlayerTests` (the executor runs the shipped steps in order; slack; a refused Build is retried next think, not spammed),
   a command-door fuzz (an AI seat under random human commands for 3 seeds x 2,000 ticks: every AI command is a legal `Command`, the
   hash twin against a recorded-commands replay equal), determinism (two runs with an AI seat, same hash), the architecture scan, perf
   (8 AI seats thinking at 2,500 units: the think under 1 ms Debug amortised, 0 B steady state), memory itemised.
6. Out of scope: the military / attack / scout managers (M5-3), difficulty tuning (M5-4), the view's AI seat (M5-V2), the data's real
   build orders (D14; M5-2 keeps the two minimal `ai.json` as they are unless a field is needed: then the schema change + minimum entries
   only). BUG-0511 / 0512 only if touched.

**QA focus.** The AI never reads hidden state (the scan + the oracle under AI seats); AI commands apply through the same door and
drop the same way as a human's; replay reproduction with an AI seat; a build order with impossible steps (no gold, blocked ground)
never stalls the tick or spams; memory itemisation.

## View track

### Next task candidate: BUG-0560 first, then M6-V1, the shell's first slice · feature · QA standard · ~1,000 lines incl. QA

1. **BUG-0560** (`game/tests/M4PlayableTest.cs` step 5): accept the step on the cast's resolve event and check the Burning marker on a
   staged target as step 8 does, or fail fast with "the fight ended before Telas Fire burned anyone"; 6+ seed-runs under load as the
   proof. A few dozen lines; the loop is every track's gate.
2. **M6-V1:** a main menu scene (`res://scenes/MainMenu.tscn`: Skirmish, Quit) → skirmish setup (map seed + size, 2 slots: faction per
   slot, the human seat; AI difficulty greyed with a `ui.json` text until M5-2) → the match; Esc pause menu (resume, quit to menu).
   Every text from `ui.json` (keys named in the brief). The test scenes and `--scene` flags keep working headless. The remembered
   site drawn as a foundation (`Fog.Ghosts` `Site`) if budget remains.
3. **M5-V2 outranks the shell the moment M5-2's `AiPlayer` is on `main`:** `--ai <difficulty>` in the sandbox, seat 1, the match end
   printed; `M4PlayableTest` unaffected.

## Data track

### Next task candidate: inbox answers first, else D14, `ai.json` build orders for both factions · feature · QA light · ~500 lines incl. QA

Files: `game/data/factions/malazan/ai.json`, `game/data/factions/whirlwind/ai.json` only (the sim's M5-2 does not edit them).
Content: each page's "AI build order" section + docs/02 "AI opponent" → real `buildOrder` steps (the opening, the halls by worker
count, Age II by pop, the expand step), compositions per age, attack / retreat thresholds, per-difficulty numbers (Easy 2 s / Hard
0.5 s think, Hard gather 0.25). Every id must exist and belong to the faction (docs/03 "For the data track (M5-1)" lists every
refusal). `Content/AiContentTests` pins the pages' build-order lines to the files both ways. **No golden change** (`ai.json` is
outside `ContentHash` until M5-2; if M5-2 lands first in the same session, the data track re-pins the header: name it in both briefs).
BUG-0561 folded in. If the owner answers D13 with "×1.5" that comes first: two values, `DamageCalcTests` 11 → 15, `UnitRosterQaTests`,
the parity test, both pages, docs/02 line 51, one golden regen.

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows may fail under load; rerun alone before filing.
- `studio/bugs/README.md`: each track appends only its own rows; the sim's and data's copies must not touch the view's 0520 / 0521 rows.
- Scratch worktrees: 15+ owner-housekeeping entries remain in `git worktree list` (`git worktree prune` after deleting the folders).
- The Producer's scratch worktree from this session (`scratchpad/producer-merge-0314`) is deleted at the end of the ACCEPT; if it
  remains, `git worktree prune` clears it.
