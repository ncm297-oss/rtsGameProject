# BUG-0067: M2-3 input nits: 300 ms double-tap edge depends on the clock value; A-targeting outlives an emptied selection

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-06-0655, task M2-3 |
| System | view input (ControlGroups, SelectionController) |
| Fixed by | M2-H1 (3dc0568): `ControlGroups.Tap` compares whole milliseconds; `SelectionController` ends targeting when the pruned selection is empty (each frame and on the next left press); `OrdersControllerQaTests.DoubleTap_Exactly300ms_*` un-skipped, passes |

## Repro
1. **Double-tap edge.** `OrdersControllerQaTests.DoubleTap_Exactly300ms_GivesTheSameAnswerAtAnyClockValue`
   (`sim/Rts.Sim.Tests/QA/ViewApi/OrdersControllerQaTests.cs`, skipped with this bug id). Remove
   the `Skip` and run `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~DoubleTap_Exactly300ms`.
   `SelectionController` passes `Time.GetTicksMsec() / 1000.0`, so the gaps are whole
   milliseconds. A gap of exactly 300 ms is a double-tap at 1,303 of 2,858 start times and a single
   tap at the other 1,555. For example, `Tap(0, 0.8)` then `Tap(0, 1.1)` is not a double-tap
   (1.1 - 0.8 = 0.30000000000000004 > 0.3), but `Tap(0, 0.0)` then `Tap(0, 0.3)` is.
2. **Stale targeting.** Select units, press A, then the selected units all die (or are freed)
   before the next click. The debug label shows `sel 0 ... A`. The next left click on the map is
   swallowed: `IssueAt` hits the map, `Order` returns on the empty selection, targeting ends, and
   the click selects nothing. So the player's first click on a unit after their army died does
   nothing. Found by reading `SelectionController._UnhandledInput` and `Order`; not scripted.

## Expected
1. The 0.3 s rule gives the same answer for the same gap at any time. Compare whole milliseconds
   (`ulong` ms and `<= 300`), or add a small epsilon.
2. Targeting ends when the selection becomes empty (for example in `_Process` after the prune),
   so the next click is a normal selection click.

## Actual
See the repro. Neither nit can corrupt state. A human can't reliably hit exactly 300 ms, and the
stale-A case needs the whole selection to die (no combat before M4).

## Notes
For the view hardening session. The regression test for item 1 already exists (skipped).

## Re-check (2026-10-06-0905, M2-H1 commit 3dc0568): fixed
Verified: the un-skipped row fails on base code and passes on 3dc0568. QA `ViewHardeningQaTests.Tap_AgreesWithAnIntegerMillisecondModel_OverRandomClocks`: 20,000 random tap sequences with clocks up to ~1e11 ms and gaps of 300 / 301 / random ms, 0 disagreements with an integer-ms model. QA `game/tests/QaH1Test.tscn` (3,000 random steps through the viewport, including deaths of the whole selection with and without a frame before the next input) passes. The same scene on base scripts reports "targeting survived a frame after the whole selection died" 31 times.
