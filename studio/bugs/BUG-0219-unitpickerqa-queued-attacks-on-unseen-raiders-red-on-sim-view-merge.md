# BUG-0219: `UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything` is red on the sim + view merge: its queued Attacks target Raiders 20 m off, which the fog drops

| Field | Value |
| --- | --- |
| Severity | S2 (a red xUnit row on the merged result; gates the M4-3a sim merge) |
| Status | fixed |
| Found | 2026-10-08-1814, integration (the conductor's full non-Perf run on `origin/main` 853a60c merged into `studio/2026-10-08-1435-sim`, a84ff0d; confirmed by the Producer alone) |
| System | view-owned QA row `sim/Rts.Sim.Tests/QA/ViewApi/UnitPickerQaTests.cs` (M4-V2, line ~280) vs the M4-3a vision gate on explicit `Attack` (`CombatSystem.MayAttack`) |
| Fixed by | 2cddf26 (M4-VH1, first commit, this test only: `CombatScenes.Spot` on the three Raiders, every assertion kept) |

## Repro
1. In the sim worktree at a84ff0d (M4-3a + the view's M4-V3 from `main`): `dotnet build RtsGame.sln`, then
   `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~ThreeQueuedAttacks_ThenStop"`.
2. Fails every run at `UnitPickerQaTests.cs:292`: `Assert.Equal(h[1], u.Target[me.Index])` expects `EntityHandle {1,1}`, gets
   `{0,0}` (no target). Full non-Perf on the merge: 4,003 passed / 15 skipped / **1 failed** (this row).
3. Control: green on the view branch alone (dd5b5b9's sim, no fog) and on `main` at 853a60c.

## Expected
The row's own comment: "The Raiders are 20 m off (beyond the infantry's 14 m sight): nothing should pull it back to any of
them" after a Stop. The three queued Attacks must first be accepted so the Stop has something to clear.

## Actual
Since M4-3a (docs/03 "Vision, detection, fog"), an explicit `Attack` on a target its owner cannot see is dropped like a
forbidden one. The lone Heavy Infantry at (20, 40) is the only thing player 0 has; the three Raiders at x = 40 are 20 m away,
outside its 14 m sight, so all three orders are dropped: no target, no queue, and the Stop clears nothing. The sim behaves
as specified; the row was written before fog existed (the same class as BUG-0218, whose scene-side fix staged targets in
sight). Neither track's QA ran the full xUnit suite on a merged tree: the sim's confirm pass and the view's round 2 ran the
scene loop on their scratch merges, and the row is an xUnit QA row, not a scene.

## Notes
- Owner: the view (the file is under `QA/ViewApi/`, view-owned). Fix: stage the Raiders in player 0's sight so the Attacks
  are accepted, while keeping them outside the infantry's own 14 m scan radius so the row's point (nothing pulls it back
  after Stop) still holds. The sim's test harness already has it: `CombatScenes.Spot(sim, 0, h[k])` for the three Raiders
  (it reveals the unit to the player, `Fog.Reveal(unit, player, int.MaxValue)`), or `CombatScenes.Spotter(sim, 0, at)` for
  a held own unit beside them. Three lines; every assertion stays. The Producer reran the row alone on a84ff0d: fails,
  "Expected EntityHandle { Index = 1, Generation = 1 }, Actual { Index = 0, Generation = 0 }".
- Gate for the sim merge: with the fix on the view branch, the full non-Perf suite must be green on the view + sim merge
  (not only the scene loop). New integration rule (Producer, 2026-10-08-1814): **the integration gate runs the full non-Perf
  xUnit suite on the merged result, in addition to the scene loop and smoke.**
- Planned: first item of the view track's next session (a few lines, QA light), then the sim branch merges.

## Verification (QA 2026-10-08-2144, M4-VH1)
- 2cddf26 sits directly on the diff base 4c1f168 and touches only `UnitPickerQaTests.cs` (+5 lines: three `Spot` calls
  and a comment). In a scratch clone the row fails at 4c1f168 (`Assert.Equal() Failure`) and passes at 2cddf26.
- Full non-Perf at 08dc8e3: 4,022 / 13 skipped / 1 failed. The one failure is `Content/CounterTriangleMarginsTests`
  (data track, fixed on the data branch), not this row.
