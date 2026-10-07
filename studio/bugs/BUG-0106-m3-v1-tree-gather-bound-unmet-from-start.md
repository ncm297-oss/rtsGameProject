# BUG-0106: Right-click on the nearest tree from the start: workers reach Gathering at tick 123, not within 60 (criterion 3 wording vs walk time)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-07-0925, task M3-V1 |
| System | HUD / right-click Gather (view), start bases, criterion wording |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaV1Test.tscn` (row `FreshTreeAndMine`).
2. Default match, seed 1: select the five base workers at tick 2, right-click the tree nearest the Town Hall.

## Expected
M3-V1 criterion 3: "Right-click on a mine / tree with 5 workers selected: all 5 reach `Gathering` within 60 ticks
(test scene), gold / wood rises within 1,200 ticks."

## Actual
```
fresh seed 1 Wood: node 12.7 m from the hall; all Gathering at tick 123, total rose at tick 436
fresh seed 1 Gold: node 10.0 m from the hall; all Gathering at tick 23, total rose at tick 310
fresh seed 31 Wood: node 52.2 m from the hall; all Gathering at tick 301, total rose at tick 852
fresh seed 31 Gold: node 26.1 m from the hall; all Gathering at tick 115, total rose at tick 566
```
Only seed 1's mine meets the 60-tick bound. The developer's `EconomyViewTest` checks the tree with a 200-tick bound,
after sending the workers to the mine first, so the "tree within 60 ticks" half of the criterion is not tested as
written.

## Notes
The view part is correct: every worker gets its Gather on the click, and the sim resolves the node. The time is walking
(60 ticks = 3 s = 12 m at 4 m/s) plus 5 workers sharing one tree's free sides. The workers stand on the hall's
mine-facing side, and nothing places the hall near wood. The 60-tick bound only holds for a node about 12 m from the
workers, so the criterion is really a distance rule. Producer's call: reword it as "walk time + N ticks" or "within 60
ticks of arriving", or accept the dev test's wood row as the reading. Not a product defect, so S3, not S2. Seeds 6 and
31 already miss the bound for the mine too, which the developer reported.
