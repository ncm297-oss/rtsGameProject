# BUG-0941: BUG-0910's fix adds about 0.03 ms (+18 %) to the full-building-store think: blocked steps run `AnyBuilder` every think

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-17-2344, task M5-8a (QA, full tier) |
| System | AI: `AiPlayer.TryBuild` (BUG-0910 fix), `BuildOrderExecutor` |
| Fixed by | |

## Repro
1. Paired runs, same PC, quiet machine (no other track's test host), alternating base b30462a and head 13d9ad6:
   `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~AiThinkCostQaTests" --logger "console;verbosity=detailed"`.
2. Read the `forests 12` / `forests 6` avg think lines.

## Expected
Brief M5-8a criterion 7: the think-cost gates green alone (they are), and the change costs nothing per think beyond noise.

## Actual
Avg ms a think, three alternating pairs:

| Row | base | head |
| --- | --- | --- |
| forests 12 | 0.166 / 0.165 / 0.164 | 0.202 / 0.199 / 0.198 |
| forests 6 | 0.177 / 0.178 / 0.177 | 0.209 / 0.209 / 0.203 |

The commands sent are identical (3,723 / 4,768 on both sides), so behaviour didn't change; only cost did. A mutation in a
scratch clone (head with the BUG-0910 line put back to `NoSiteFound = true`) measures 0.168 / 0.167 and 0.172: the whole
difference is the fix. In this scene the building store is full, so `FindSite` stops at its cell-free test. Before, that
set the 5-think wait (BUG-0791); now each blocked build step goes through `TryBuild` at every think and runs `AnyBuilder()`
(a loop over every worker's `BuilderRank`) before `FindSite`'s cheap `CanPlaceAnywhere` test.

The storms-fuzz row (`AiPerfTests...EightShippedStormsAlive`) is within noise (0.353 / 0.344 head vs 0.342 / 0.344 base),
and every gate stays far under its 1 ms budget, so this is polish, not a budget problem.

## Notes
- A probe in the scratch clone: `if (!View.CanPlaceAnywhere(type, out _)) return false;` placed before the
  `_queueFull || !AnyBuilder()` line of `TryBuild` gets back about half (0.179 / 0.178 and 0.198 / 0.187). Behaviour is the
  same, because `FindSite` would return -1 at the same test, and it sets no wait either way.
- The rest is the executor walking the blocked steps at every think, which is what BUG-0910 asks for.
