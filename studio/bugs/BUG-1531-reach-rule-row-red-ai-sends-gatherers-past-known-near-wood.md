# BUG-1531: ReachRuleQaTests (seed 2, 80 m) red on the default loop: the AI sends gatherers 174-197 m out while live wood 90 m from its hall is known

| Field | Value |
| --- | --- |
| Severity | S2 (the default loop is red outside the rows M6-SH2's criterion 11 allows: only the criterion-10 content rows may be red; the developer listed it "for ruling", untraced) |
| Status | open (for ruling; trace confirmed by QA re-check 1) |
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

## M6-SH2 fix round 1 (the developer): traced, for ruling
- The 174 m target at 4,781 is a gold mine. The rebalance (no worker idle) moves wood gatherer Laborer 28, standing at the
  117 m forest, to gold; `EconomyManager.NearestNode` takes the mine nearest the worker (61 m from it, 174 m from the hall)
  over the home mines (about 100 m from it). At 6,361 that mine's gatherer is rebalanced back to wood, and the wood nearest
  it lies 197 m from the hall (the row's 201 m). The documented rule ("nearest the worker") applied to a worker far from
  home; the control met no such rebalance. A cure (a rebalance's pick measured from the worker's drop-off, or by the haul)
  changes a documented rule and every pin: the Producer's call. The row stays red, named.

## QA re-check 1
- The developer's trace holds (QA probe, every change of player 0's gather node past 140 m from the hall): at 4,782 unit 28
  goes to **gold** node 3, 174.2 m from the hall and 60.8 m from the worker, while the home mine is 18.4 m from the hall
  and 97.0 m from the worker; units 34, 1, 0 and 10 are sent to the same far mine at 5,442 / 5,942 / 7,482 / 7,622, each
  from the 115 m forest. At 6,362 unit 1 goes to wood node 32, 201.1 m from the hall.
- The 6,362 Gather was sent to (233, 119), a remembered tree that is gone; the sim resolved the position to live node 32,
  4 m away, which the seat has not seen. That is the command's position resolution, not a fog read by the AI.
- Judgement: a ruling is genuinely needed. The code follows the documented rule ("the known node of that kind nearest the
  worker"); QA's row assumes picks are about as near home as the 512-reach control's. Either the rule changes (measure from
  the worker's drop-off: an AI change that moves every pin) or the row is re-stated. Neither is the developer's call.
  It still leaves the default loop red outside criterion 10's rows, so criterion 11 stays unmet until the ruling.
