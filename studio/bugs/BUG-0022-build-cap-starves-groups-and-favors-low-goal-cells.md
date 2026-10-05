# BUG-0022: With the one-build-per-tick cap, re-ordered groups can freeze for good, and goals with a low cell index always go first

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-04-0120, task M1-4b (fix round 1 re-check) |
| System | movement / pathfinding (MovementSystem build cap) |
| Fixed by | 8c96b53 (M1-4c); QA/FieldBuildFairnessQaTests; over-capacity remainder in BUG-0025 |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.FieldBuildCapQaTests.BuildCap_64GroupsRetargetedEvery20Ticks_EveryGroupMovesInEveryWindow`.
2. Run it. The setup is 512 units in 64 groups of 8. Every 20 ticks (1 s) each group gets a new
   random goal. The run lasts 15 windows.

Also see `BuildCap_500UnitsWith500DistinctGoals_EveryUnitArrives_NoDeadlock`, which passes and
prints the numbers below.

## Expected
A group with an order starts moving within a short, bounded delay. Which group waits should not
depend on where on the map its goal lies, because that is a fairness issue between players once
the AI and chase orders exist.

## Actual
- **Starvation.** 483 of about 960 group-windows never moved at all. The worst window had 45 of
  64 groups frozen. A window allows 19 builds, so any group past the 19th lowest new goal cell
  never gets a field before its order is replaced.
- **Positional bias.** Groups are served in goal-cell index order (`GoalCell << 32 | slot`, sorted
  ascending). The mean goal row of frozen groups was 78.5, against 42.5 for groups that moved. Goals
  near row 0 always win. With bases at opposite ends of the map, one player's orders get priority
  under load.
- **Throughput with more live goals than cache slots.** 500 units with 500 distinct goals all
  arrive, so there is no deadlock. But it takes 14,587 ticks (12 min) against about 2,677 ticks
  for the slowest unit if every field were ready, which is 5.4x slower. It also costs 12,646 field
  builds for 500 goals: each build evicts a field another active group still needs, so the cap is
  mostly spent rebuilding evicted fields.

## Notes
- docs/03 lists two costs: staggered starts, and stuck units holding a slot. It does not cover:
  - re-ordered groups never starting;
  - the bias toward low goal-cell indexes;
  - the rebuild churn when more goals are live than the cache has slots.
- No M1-4b acceptance criterion covers this. It doesn't matter yet (no AI, no chase orders), so it
  is S3. It will matter at M3 (AI) and for attack-move or chase re-targeting.
- Ideas, not prescriptions:
  - Serve misses oldest order first (FIFO by order tick), not by cell index.
  - Use a time budget instead of a count. A 0.7 ms build against the 4 ms average budget leaves
    room for several builds per tick.
  - Have units with no field yet step straight toward the goal or hold, rather than freeze.
  - Scale cache capacity with live goals.
- Related: BUG-0021 (cache contents now affect results).

**Producer triage (2026-10-04-0120):** S3, fix with BUG-0021 in the next movement task. Direction:
serve misses oldest order first (a hashed per-unit `OrderTick`, sort key (OrderTick, GoalCell,
slot) for the build pass), keep a small count cap (2 is fine against the 4 ms average budget: one
build is ~0.7 ms Debug on the default map), and let cache capacity scale with `UnitCapacity`
(e.g. max(32, UnitCapacity / 8); 80 KB per field on the default map) to cut rebuild churn. No
wall-clock budget in the sim. Positions of waiting units stay put (never step without a field).

**QA verification (2026-10-04-2056, M1-4c):** fixed while live goals fit the cache. Misses are
served oldest order first, two per tick, and the cache is sized by `CapacityFor` (64 fields at
512 unit slots).
- The QA starvation test, retuned by the dev to the decided cap (34-tick windows = 1 command tick +
  ceil(64 / 2) build ticks + 1 spare), passes with 0 of 960 group-windows frozen (was 483). QA
  accepts that window.
- `QA/FieldBuildFairnessQaTests`: two bases (rows 10/118), 64 groups, 2,000 ticks, batches of
  1/2/4/8 on alternating ticks. Every order is served within ceil(B/2) - 1 ticks, nothing
  overtakes an older order, and per-player mean waits match within 0.4 ticks (7.17 vs 7.24 at
  batch 4).
- Positional bias is left only in same-tick ties, as specified. It is measurable: up to 3.7 ticks
  mean, filed as BUG-0026 (S4).
- With more live goals than slots, eviction still picks by goal cell and churns. Filed separately
  as BUG-0025 (S3).
