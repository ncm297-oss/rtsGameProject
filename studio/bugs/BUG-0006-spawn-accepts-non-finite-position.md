# BUG-0006: SpawnUnit accepts NaN/Infinity positions into sim state

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-03-0907, task M1-1 |
| System | command queue / commands |
| Fixed by | |

## Repro
Un-skip `Rts.Sim.Tests.QA.SimCoreQaTests.SpawnUnit_NonFinitePosition_NeverEntersState` (fails with
`slot 0 position <NaN, ∞>`). By hand:
1. `sim.Enqueue(Command.SpawnUnit(0, 0, new Vector2(float.NaN, float.PositiveInfinity)));`
2. `sim.Tick(); sim.Tick();`
3. `sim.World.Units.Position[0]` is `<NaN, Infinity>`.

## Expected
QA invariant: no NaN/infinite positions in the sim. Commands are the boundary where outside input
(mouse ray casts, AI) enters, so they should be validated there: reject or drop a command with a
non-finite position, as the store-full case already drops the spawn.

## Actual
The position is copied verbatim into `Position` and `PrevPosition`.

## Notes
SpawnUnit is a test/dev-console command today, so S4. Worth a shared "validate command" step
before Move/Attack/Build commands arrive in M1/M2, where a bad screen-to-ground ray is a real
source of NaN.
