# BUG-0541: M5-1 small findings (tiny composition weights stored as 0, a source-scan hole, Easy "rarely uses abilities")

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-11-0314, task M5-1 |
| System | ai.json loader, AI architecture guard, AI data |
| Fixed by | |

## Repro
1. **Tiny weight loads as 0.** `QA/AiLoaderQaTests.ATinyCompositionWeight_IsRefused_OrStoredAboveZero` (skipped with this
   id; un-skip to see it fail): `"malazan_crossbowman": 1e-300` (or `1e-46`) in `compositions[0].units` passes `Checker.Pos`
   (a double above 0) and is stored as `(float)w` = `0f`. `AiComposition.Weights` and docs/03 promise "above 0". A
   weight-proportional picker would then never pick that unit, or divide by a zero total if every weight is tiny.
2. **The Ai/ source scan skips code lines that start with `*`.** `PlayerViewArchitectureTests.Scan` drops every line whose
   trimmed text starts with `*` (meant for block-comment bodies), so an expression continued on a new line slips through. A
   file `sim/Rts.Sim/Ai/Planted.cs` containing
   ```csharp
   internal static int Peek(object o) => 1
       * ((Rts.Sim.World)o).Units.Count;
   ```
   builds and `NoAiFileButPlayerView_NamesTheWorld_TheSimulation_AStoreOrTheFog` passes (shown in a scratch clone; the plain
   `private readonly World? _world` plant does fail it). Contrived, but the guard is the M5 criterion's enforcement.
3. **Design wording.** docs/02: Easy "rarely uses abilities"; shipped Easy `useAbilities: false` (a flag can't say
   "rarely"). Either docs/02 says "doesn't use abilities" or the schema needs a rate (M5-2's ability manager).
4. **Note (no fix asked):** handles carry slot indices, so an AI that watches the slot its new unit lands in learns how full
   the unit store is (a weak side channel on hidden enemy counts). The brief allows handles; recorded for the M5-2 design.

## Expected
1. A weight that becomes 0 (or below `float.Epsilon`) as a float is an error (or the floor is a minimum like 1e-6).
2. The scan only skips comment lines (a `*` line inside a `/* */` block), or strips comments properly.

## Actual
As above.

## Notes
None of these blocks M5-1.

## Update (2026-10-12-1930, QA)
Item 1 **fixed** in M5-3 `238f261`: a weight above 0 that rounds to 0f is a load error at `compositions[k].units.<id>`
(`Data/AiLoaderTests.ACompositionWeightRoundingToZero_IsALoadError`; QA's `AiLoaderQaTests.ATinyCompositionWeight_IsRefused_OrStoredAboveZero`
un-skipped, with a 1e-40 subnormal row that loads above 0; a mutation that drops the check fails the dev's row). Items 2-3 are
still open, so the bug stays open.
