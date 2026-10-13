# BUG-0594: M5-3 small findings: the rebalance drops a partial load, an untested scout skip, `waveCooldown` 0 unbounds the loop

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-12-1930, task M5-3 |
| System | AI `EconomyManager` (rebalance, scout skip), `rules.json` `ai.waveCooldown` |
| Fixed by | |

## Repro / Actual
1. **The rebalance throws away cargo.** `EconomyManager.Rebalance` (unbriefed, added by the developer) moves the first
   `Gathering` worker in slot order on the long kind to the other kind. `EconomySystem.StartGather` discards a load of the
   other resource (the AoE rule, docs/03 "Implementation (M3-2)"), so each switch can lose up to `WorkerCarry` gold. Picking
   a worker with an empty (or the smallest) load would avoid it. Not measured in play; the loss is at most one load per think.
2. **The scout skip in the idle-worker pass has no test.** Mutation in a scratch clone: removing `|| ai.IsScout(...)` from
   `EconomyManager.Gatherers`' idle loop leaves every test green (`ScoutManagerTests`, `AiPlayerTests`, `AiMilitaryQaTests`).
   It is rarely reachable (the scout manager runs first and leaves a just-ordered scout pending), but nothing pins it.
3. **`waveCooldown` 0 is valid data and unbounds the attack-retreat loop.** The range is 0-3,600 s (brief item 6). At 0, a
   retreat's survivors can be sent straight back at the next think (`MilitaryManagerTests.AttackRetreatLoop_...` shows the
   unbounded count). docs/03 says the cooldown bounds the loop; a line saying that 0 turns the bound off (or a minimum above 0)
   would close it.

## Expected
1. A rebalance that doesn't destroy gathered cargo. 2. A test that fails when the scout is ordered by the economy mid-sweep.
3. The docs say what `waveCooldown` 0 means, or the loader refuses it.

## Notes
None blocks M5-3.
