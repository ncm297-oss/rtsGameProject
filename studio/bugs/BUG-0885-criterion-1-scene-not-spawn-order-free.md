# BUG-0885: Criterion 1's ten-unit flank scene is not spawn-order free (rule off too): criterion 6 as written can't be met

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | closed (known limit, M5-H3 decision: docs/03 "Determinism", the slot-order class) |
| Found | 2026-10-17-0554, task M5-P2 |
| System | movement / surround rule (slot-order dependence) |
| Fixed by | d048a21 (M5-H3): documented. `QA/FlankQaTests.Criterion1Scene_BothSpawnOrders_FirstParting` gains a same-order row (never parts, rule off or on); the reversed rows keep "no earlier than the rule off". Green at QA 2026-10-17-1624. |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FlankQaTests.Criterion1Scene_BothSpawnOrders_FirstParting"`
2. Criterion 1's scene (10 Crossbowmen on a Town Hall, 10 Heavy Infantry behind, Attack), the groups spawned in order and
   reversed, every melee position compared per tick for 200 ticks.

## Expected
M5-P2 criterion 6: "a hash twin of criterion 1's scene in both spawn orders (bit-equal positions, as the detour test)".

## Actual
| Reversed | First tick the positions part, rule off | rule on |
| --- | --- | --- |
| melee only | 1 | 1 |
| row only | 24 | 28 |
| both | 1 | 1 |

The flank rule makes it no worse (the row asserts that), and one flanker is bit-equal in either row order (the dev's
`OneFlanker_*` rows and QA's `OneWalker_RowSpawnedReversed_BitEqualPath`, 6 rows). The parting is pre-existing: with the melee
reversed at tick 1 (the dev: slice 1's surround claims are taken in slot order), with the row reversed later (BUG-0046's
slot-order wall clips).

## Notes
- Criterion 6 as written is NOT MET; the dev substituted the one-flanker rows. The Producer decides whether that substitute
  meets the intent. The tick-1 parting from surround claims in slot order may deserve its own fix (spawn order is not part
  of the game's input contract, so this is not a determinism break: same seed + same commands still hash equal).
