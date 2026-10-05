# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

## Current session plan (2026-10-05-1234, PLAN written 12:34)

TASK_ID M1-5 · feature · QA full · base 7805e62 (main green: build 0/0, 947 passed / 8 skipped
non-Perf, smoke PASS). Inbox note "Downloads stay with the owner" processed. Producer re-order:
M1-4d-3 crowd routing moved to the debt backlog (S3, hardening session); M1-5 does not depend on it.

**Goal:** prove the M1 headline with a real test: one player's army of 200 mixed units, spawned in a
compact area near one edge of the default generated 128x128 map, ordered to a point on a plateau far
away so the route crosses cliffs and at least one ramp; all arrive within a derived time limit, none
gives up, none ever stands in a blocked cell, deterministic. Today's
`MovementSystemTests.TwoHundredUnits_OneMove_...` starts within 40 cells of the center and proves
nothing about chokepoints; `Fuzz_GroupsFunneledThroughGaps` only reports give-ups. If the
measurement shows give-ups in ramp queues (likely: the give-up rule needs 25% of speed of progress
*every* tick, and `BestRemaining` ratchets down on stuck ticks, so a steady crawl gives up after 1 s),
fix the give-up rule for queued walkers in this task with the smallest rule change.

**Scope:** scenario helper(s) in `MoveScenario` (or a new `CrossMapScenario`): spawn N units of one
owner in the passable cells within path cost <= R of a start cell near a map edge; goal = center of
the passable cell of level >= 1 with the greatest field cost from the start region (so a ramp is
unavoidable), ties to the lowest index; print path length (m), levels crossed, ticks taken.
`ScenarioTests` (new file): the 200-unit cross-map theory over >= 5 seeds, per-tick blocked-ground
check, two-sim hash twin, a 2-owner row as a report. Time-limit rule in docs/03. Give-up fix only if
needed, pinned by a unit test. docs/03 "Measured (M1-5)". OUT: crowd routing (M1-4d-3,
BUG-0028/0032/0033), replay (M1-6), perf targets (M1-7), CLI (M1-8), formations, multi-group
targets beyond the report row, time-sliced builds, BUG-0030.

**Acceptance criteria:**
1. A test spawns 200 units (every shipped type, one owner) in a compact start region near a map
   edge on the default 128x128 generated map and orders them to a goal on a plateau (level >= 1)
   whose shortest path crosses >= 1 ramp; the test asserts the level change and prints path meters.
2. For >= 5 seeds: all 200 `MoveScenario.Arrived` within the limit; 0 units with `GoalCell == -1`;
   every unit Idle with zero velocity; `FirstPackViolation` null.
3. Every tick, `FirstUnitOnBlockedGround` is -1 (none in blocked cells, none off the map).
4. Determinism: two sims, same seed and commands, equal `StateHash` at every 100-tick checkpoint
   and at the end, for >= 2 seeds.
5. Time limit = 2 x the undisturbed walking time of the slowest shipped unit over the longest
   start-to-goal path (field cost x CellSize / slowest data speed, in ticks), computed in the test
   from the field and the data (no magic number), documented in docs/03; actual ticks printed.
6. If a give-up rule change was needed: a focused unit test that fails on the old rule (single-file
   queue or a crowd at a 3-cell gap, none give up); every existing give-up and crowd test green with
   no bound loosened silently (re-measured numbers reported); `MovementConstants` + docs/03
   updated; any new per-unit state hashed and covered by `StateHashTests`.
7. Full suite green in one run; the scenario ticks allocate 0 bytes in steady state
   (`AllocationProbe.AssertZero`, `SerialCollection`); perf rows unchanged (tight blob 2,500 <= 4.5 ms
   avg; 500 moving < 4 ms).
8. docs/03 "Local movement" gains a "Measured (M1-5)" paragraph: ticks taken vs limit, give-ups
   before/after any fix, path length. QA updates the coverage map's Scenario column.

**Design references:** docs/05 M1 criterion 5; docs/03 "Navigation grid" (ramps 3 cells wide, cliff
cells, sealed pockets), "Flow fields", "Local movement" (give-up: `GiveUpTicks` 20, `StuckFraction`
0.25, `BestRemaining`; BUG-0027 back-off semantics); docs/02 ramps as chokepoints (6 m wide).
CLAUDE.md rules 2, 4, 5, 6.

**Tests required:** `ScenarioTests` theory (>= 5 seeds) + two-sim determinism + 2-owner report row;
the give-up queue unit test if the rule changes; Serial allocation row; existing suites untouched.

**Constraints:** no allocation in per-tick code (new per-unit arrays preallocated and hashed if
state); start-of-tick reads only (two-pass model); the test's limit reads speeds from data; sim
production change small (target < 150 lines); test code ~400-600 lines; one implement commit;
`FieldBuildOrderTests` / `FieldBuildFairnessQaTests` must stay untouched and green.

**QA focus:** (a) if the give-up rule changed: a truly wedged unit must still give up (wall-hugging
unit overlapped by a stranger, unit boxed in by another group's blob, the `LocalMovementQaTests`
rows) and the Move-spam churn must not return; mutate the rule back and confirm the new test fails.
(b) more seeds (20-50) of the cross-map scenario, one owner and two; 500 units through one ramp;
goals in the farthest corner of a level-2 plateau (two ramps in series); a start region that
includes a ramp; the time-limit formula vs actual (report the margin per seed). (c) per-tick
blocked-ground check on every seed; two-sim interleaved hash equality; 0 bytes; perf at 500 / 1,000
across the map (report). Scale numbers: 200 / 500 / 1,000 units; seeds 1-50.

## Where we are

M0 Done. M1 is 4/8: sim core (M1-1), data loader (M1-2), terrain + nav grid + spatial hash
(M1-3/4a), flow fields + cache + build cap + local movement incl. shoving (M1-4b/c/d-1/d-2).
Branch `studio/2026-10-05-1013` ACCEPTED: build 0/0; 1014 passed / 11 skipped / 1025 in one run
(1 m 53 s); smoke PASS. The conductor merges it to main. Open bugs: 9 S3, 4 S4, none block.
Sessions today: 2/10. Feature sessions since last hardening: 2/4 (next hardening after 2 more, or
at M1 end).

**First things at PLAN:**
1. Main moved during the session (owner commits 724234a, 432fafe, c089405): `studio/inbox.md`
   has a New note "Downloads stay with the owner" (process it: it's already reflected in STATE's
   M6 queue line; move it to Processed), `studio/autopilot.md` gained `usage_stop_percent` 90,
   docs/07 and `.claude/` changed. Make sure the merged `studio/STATE.md` and `handoff.md` are the
   ones from this session, not main's older copies.
2. Verify a claim: e.g. `LocalMovementTests.UnitOverlappingAnEnemyStandingUnit_WithACliffBehind_WalksAway`
   exists and fails if `limit = MathF.Max(gap, 0f)` in `Constrain` is reverted to `gap`.

What the sim does now (`MovementSystem`, docs/03 "Local movement"): Moving units sorted by (goal
cell, slot); build pass (oldest order first, cap 2); **Plan** per unit from start-of-tick state:
aim, push, sidestep, `Constrain` against standing non-groupmates (a shovable friendly Idle unit
yields up to its speed), refused steps slide; arrival within 1 m or touching an arrived groupmate;
stuck 20 ticks -> give up. A walker stuck 10 ticks (`PushAfterStuckTicks`) may also push a *lone*
parked unit (`ActPush`, stuck count holds). `AddShoves` sums each walker's overlap into
`World.ShoveStep`. **Apply** moves walkers; **ApplyShoves** then moves shoved Idle units (clamped to
speed, `SqueezeLimit` 0.5 x radii, `KeepLinks` so blobs bend, `KeepOffWalls`, refused into blocked
cells) and `RecheckAnchors` drops `GoalCell` for units no longer linked to their point. Never shoved:
enemies, Moving units (incl. waiting for a field), units on their point (unless lone + walker
stuck). Scratch on `World`: `ShoveStep/ShoveNeighbors/ShovedGoals/AnchorQueue/AnchorLinked`,
`MaxUnitSpeed`. No new hashed state.

## Next task candidates

1. **M1-4d-3: crowd routing (feature, QA full, uncertain: plan ~800 lines, first slice).** Goal:
   groups sent to nearby points mostly arrive; parked groups don't wall their own army in chokes.
   Closes BUG-0028, BUG-0032, BUG-0033. Candidate pieces, pick the slice with the best expected
   gain per line: (a) **unit-aware routing**: a per-cell crowd cost (standing units of any owner
   counted into the field's cost when built/refreshed, or a cheap local detour when the next cell's
   center is covered) so walkers go round other groups' blobs instead of through them; (b) units
   that lost their anchor (`GoalCell` -1 after a shove) walk back to their goal (re-issue the stored
   `Goal` internally, keep `OrderTick`); (c) BUG-0028 (b): no stuck tick against a walker blocked
   only by units waiting for a field; (d) a stuck walker makes a parked *group* yield sideways out
   of a 1-cell choke and drop its goal (un-skip `ShoveQaTests..._PastAParkedFriendlyPair_Arrives`).
   Re-measure the M1-4d-2 criterion-6 rows (500x500 ≤3%, 128x64 ≤5%, 500→4 ≥80%, 2,500→4 ≥60%)
   and the mixed-owner crossing (seed 73, 2/200 now, 28 before shoving); if a target is missed
   again, re-set it with the reason in docs/03 rather than carrying it a third time. Watch: field
   cost changes touch `FlowField` hashes/cache keys (BUG-0021 hashing) and `FieldBuildOrderTests`;
   determinism tests must stay green; perf tight blob 2,500 ≤ 4.5 ms avg.
2. M1-5 scenario test (200 units across 128x128 with obstacles). Design is clear; could go first if
   the next PLAN judges crowd routing too risky for today, but M1-5's "none stuck" will be weaker
   without it.
3. M1-6 replay + determinism golden (first commit fixes BUG-0014 seed mixing).
4. M1-7 perf test (document maps > 256 unsupported, BUG-0023); M1-8 CLI.
5. Hardening (after 2 more feature sessions or at M1 end): BUG-0034 flaky perf row, BUG-0030,
   BUG-0025/0026, BUG-0005.

## Watch out for

- Budget: M1-4d-2 ran ~2,170 changed lines (~380 production, ~1,400 QA tests) against an 800
  brief; accepted because the production part was small and QA-owned tests don't count against
  the dev. Keep the next movement task's production slice small and measured.
- QA will re-run the crowd rows and compare against the brief's targets: write targets you have
  evidence for, or write them as "report and re-set" explicitly.
- Determinism: all unit-unit interaction reads start-of-tick state (Plan/Apply/ApplyShoves);
  neighbor order ascending slot from `QueryRadius`; a walker's own sums are in slot order (documented
  limit: reversed spawn order can differ at the last bit when 3+ units touch). Shoves and anchor
  re-checks are order-independent by construction (whole-group set property).
- `MovementConstants`: `GiveUpTicks` 20, `PushAfterStuckTicks` 10, `StuckFraction` 0.25,
  `ArrivalSpacing` 0.6, `ShoveSpacing` 0.5. New tuning numbers go there and into docs/03.
- Every Perf or allocation test in `SerialCollection`; allocation asserts via `AllocationProbe.AssertZero`.
  BUG-0034: the 10-tick distinct-targets perf row can flake under full-suite load.
- `StateHash` covers `State/Goal/GoalCell/OrderTick/StuckTicks/BestRemaining`, cache metadata and
  `Move`'s handle; `Speed/Radius`, spatial hash, scratch arrays and field contents are derived.
  `NavGrid.Version` is not hashed (fine until M3). `FlowFieldCache.Get`/`TryGetCached` move hashed
  LRU state: sim-only.
- Generator pins: 15 hashes in `MapGeneratorTests` and QA `PreBug0015*` oracles. `NavGrid.Flood`
  has no bounds checks (ring must stay blocked). `CellQueue` is a 4-bucket Dial queue.
- `TestSim.Config(...)` is the one way tests build a `SimConfig`. `ArchitectureTests` and
  `SpatialHashTests.Source_UsesNoHashCollectionsOrLinq` grep sim source. `SimRng` is a mutable
  struct: always `ref world.Rng(stream)`. Soak rows (~100 s) are in the suite.
- One implement commit per session; ask for a report line after the first part, not a second commit.
