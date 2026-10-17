# BUG-0888: A walker behind the cliff end of an own 24-line goes round the open end and gives its Move up on the walk back

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-0554, task M5-P2 (QA re-check after fix round 1) |
| System | movement, flank rule budget (`FlankMembersPerStride`, `FlankMemoryTicksPerMember`) |
| Fixed by | |

## Repro
1. `QA/FlankFixRound1QaTests.LongLineAgainstACliff_WalkerTakesTheOpenEnd` (both rows skipped with this id). The map is
   64 x 64 with cells y <= 15 at level 1, so the cliff edge row at y 15 is blocked. 24 own Crossbowmen stand 1 m apart
   along y from the cliff foot (first centre 32.5 m) at x 60, planted on holding Raiders 14.5 m in front. One Heavy
   Infantry 3 m behind member 1 (or 4), with a Move to 5 m in front of that member.
2. The trace (cbeb944, member 1): it flanks south along the whole line (ticks 10-160, stuck count 2 -> 14), rounds the
   open end at (60, 56.3) at tick 165, and walks back north along the line's front. At tick 229, at (61.5, 47.3), 14 m
   short of the goal, the stuck count reaches the limit and the Move is given up. Member 4: the same, given up at 229 at
   (61.5, 44.7).

## Expected
The walker arrives by the open end, as fix round 1's long-row rows do with the goal beyond the middle. Docs/03: "The
other side is taken only where the kept one has no open ground (a cliff at that end)". The budget is "about as long as
the walk round".

## Actual
With the near end sealed, the walk is the full line twice (about 46 m, about 300 ticks at 3 m/s). The budget is about
20 counts x a stride of 12 (24 / `FlankMembersPerStride`), about 240 ticks. The walk back past the end earns no progress
on the field's estimate: its best was set 8 m from the goal, behind the line. So it runs out about 5 s short.

## Notes
- Better than before the round: 3ec22ee gives up at tick 56 / 64, behind the line. The rule off gives up at tick 31.
- A 10-row against the same cliff (QA's `FlankQaTests.RowAgainstACliff_*`) arrives. The gap grows with the line's length
  when the nearer end is the sealed one.
- Fix direction (developer's call): size the budget by the walk round the end actually taken, not half the line.
