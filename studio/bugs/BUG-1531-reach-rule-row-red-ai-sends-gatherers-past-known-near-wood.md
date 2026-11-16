# BUG-1531: ReachRuleQaTests (seed 2, 80 m) red on the default loop: the AI sends gatherers 174-197 m out while live wood 90 m from its hall is known

| Field | Value |
| --- | --- |
| Severity | S2 (the default loop is red outside the rows M6-SH2's criterion 11 allows: only the criterion-10 content rows may be red; the developer listed it "for ruling", untraced) |
| Status | open |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | AI economy (gather targeting under the lifted reach); exposed by the drop-off rule (BUG-1520) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReachRuleQaTests.NearestForestFarPastTheReach"`
2. Row `(seed: 2, cleared: 80)` fails: `farthest wood target 201.1 m vs control 116.6 m` (bound: control + 30 m).

## Expected
docs/03 "Implementation (M6-S7)": once the reach lifts the seat takes the nearest known node, as the 512-reach control
does, so it sends no gatherer more than 30 m past the control's farthest.

## Actual
Measured by QA on three trees (same row, same machine):

| Tree | farthest target (reach 64) | control (reach 512) | wood by 8,000 |
| --- | --- | --- | --- |
| base `fdc8710` | 116.6 m | 116.6 m | 150 |
| base + item 0 only (the drop-off rule, no retrain) | **201.1 m** | 116.6 m | 170 |
| base + retrain only (base economy) | 116.6 m | 116.6 m | 150 |

So item 0 alone moves the row; the retrain plays no part. A trace of seat 0 on the item-0 tree (QA scratch probe):
seat 0 never has a drop-off site (its only Builds are a Billet and a Barracks), so the drop-off rule does not touch its own
workers; the match drifts through player 1. At tick 4,781 the AI sends Laborer 28, standing at the 117 m forest, a Gather
to a node 174.2 m from the hall while it knows 60 live exposed wood nodes, the nearest **90.0 m** from the hall (16
gatherers). Later sends go to 174 m (three more Laborers) and 197 m. Wood banked is not lower (170 against 150).

## Notes
- Not a defect of the drop-off rule itself (seat 0 has no site); a pre-existing gather-targeting shape (the node chosen
  from the worker's spot, or a node mark such as BUG-1151's re-send) that the new timing exposes. Seeds 1 and 3 are
  unchanged in kind (seed 1 was already 210.6 m against a 211.1 m control on the base).
- For the Producer: either trace the 4,781 choice and fix the targeting, or re-stage the row with the reason (it then
  stops guarding "nearest known node once lifted" on this seed).
