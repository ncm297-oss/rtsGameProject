# BUG-1891: Pale Hills four-seat seed 4: 726 gold left on the map is never mined and a seat's carried gold is never delivered

| Field | Value |
| --- | --- |
| Severity | S4 (a measure-and-report row; the match never ends with BUG-1890) |
| Status | open (not traced further) |
| Found | 2026-11-27-0817, task M6-SH3 (B's pre-trace) |
| System | AI economy (gather reach / delivery) |
| Fixed by | |

## Repro
`Stress/DrawReachPreTraceTests` (seed 4) at 72,000, base 0159c425 and after M6-SH3 B alike: 726 gold left on the map;
p0 (26 workers, four finished drop-offs) mines none of it; p3 (six workers, two finished drop-offs) carries 30 gold that is
never delivered (QA's probe in BUG-1860 saw the same from 60,000 to 144,000).

## Expected
A seat with workers and a drop-off gathers the gold it can reach and delivers what it carries.

## Actual
Nothing moves from about 60,000 to 144,000; the draw rule counts the 726 gold for p0 and p3 (they have workers and drop-offs),
so the match is never spent (with BUG-1890: the seats also never spend their banks).

## Notes
- Not traced in M6-SH3 (out of scope: an economy question). First suspects: the mine is out of every seat's known nodes or
  beyond `ai.gatherReach`, or marked dropped; p3's loaded workers may be stuck or Idle with full loads.
