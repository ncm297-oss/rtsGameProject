# BUG-0530: D12 leaves the non-Perf suite red: two sim-track stealth tests assert the pre-D12 "no shipped detector" state

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-10-2334, task D12 |
| System | sim tests (`Data/StealthLoaderTests`, `Vision/StealthContentHashTests`), owned by the sim track |
| Fixed by | |

The data change itself is correct (docs/02 line 33 Caster row and line 248 "all casters (10 m)"). The two tests were written
in M4-5 to hold "no shipped unit detects until D12"; D12 made that false on purpose. The D12 brief forbids C# outside the data
test folders, so the developer could not update them, and the brief did not name them as an exception (it did for the golden).

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StealthLoaderTests|FullyQualifiedName~StealthContentHashTests"`
   at D12 HEAD 8fbede1:
   - `StealthLoaderTests.Shipped_TheKindsAndRulesLoad_AndNoShippedUnitDetectsYet` (line 58,
     `Assert.All(d.Units, u => Assert.Equal(0f, u.Detector))`): "2 out of 14 items in the collection did not pass".
   - `StealthContentHashTests.TheStealthKinds_TheRulesFields_AndADetector_EachChangeTheHash` (line 41,
     `SetUnitField(..."malazan_cadre_mage", "detector", "10")`): "Assert.Equal() Failure: Values differ" (the mutant now equals
     the shipped value, so its hash equals `shipped` and the distinct count drops by one).
2. Both pass at the diff base 3584a3a.

## Expected
D12 acceptance 5: non-Perf suite green.

## Actual
Non-Perf suite: 2 failures. Both are genuine stale assertions, not product bugs.

## Notes
Fix (sim-track files, 2 small edits):
- `StealthLoaderTests` line 57-58: assert every `UnitSlot.Caster` unit has `Detector == 10f` and every other unit `0f`; rename
  the test (e.g. `Shipped_TheKindsAndRulesLoad_AndOnlyTheCastersDetect`); fix the comment.
- `StealthContentHashTests` line 41: mutate to `"12"` (line 42's `"11"` stays; both differ from the shipped 10).
Routing: the integration order is data -> sim -> view and the sim track's M4-H3 also edits `Data/StealthLoaderTests` (the
BUG-0481 row), so `main` is red between the data merge and the sim merge unless the two edits land with D12. Recommended:
the Producer names the two edits as a conductor fixup in the D12 merge commit (a named exception like the golden), and the
M4-H3 merge keeps both sides of `StealthLoaderTests`. Alternative: land them in M4-H3 and merge sim before data, or merge both
before running the gate. Brief-planning gap: a data task that flips a "not yet" assertion in a sim-track test should list
that test as an exception.
