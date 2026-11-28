# BUG-1931: A seat that knows only a far mine hauls from it for good and never builds a depot by it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (triaged at the 2026-11-27-1857 ACCEPT: with BUG-1930, not blocking) |
| Found | 2026-11-27-1857, task M6-S15 (B, BUG-1891's trace; filed from the developer's patch, confirmed by QA) |
| System | AI far expansion (`AiPlayer.UpdateFarArm`) |
| Fixed by | |

## Repro
`Stress/GoldLeftTraceTests` (d26338d3), seat 3: its only gold is the mine 351 m from its only drop-off.

## Expected
A depot by a known far mine (the far expansion's own end state).

## Actual
The far expansion arms only with no gold node known; seat 3 knows this one, so its workers walk about 700 m a load
(bank 30 -> 0 -> 70 -> 30 -> 40 gold from 48,000 to 72,000).

## Notes
- docs/03 "Implementation (M6-S15)" B.
