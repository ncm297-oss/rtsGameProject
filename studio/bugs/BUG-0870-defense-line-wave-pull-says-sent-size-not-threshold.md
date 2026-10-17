# BUG-0870: Faction pages' Defense lines say a wave gives defenders only while it keeps "the size it was sent at"; docs/03 says its start threshold

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-17-0056, task D20 |
| System | docs (faction pages, AI defense) |
| Fixed by | d7201bf (D20 fix round 1), verified by QA 2026-10-17-0056 |

## Repro
1. Read `docs/factions/malazan.md` and `docs/factions/whirlwind.md`, section "AI build order", the **Defense** bullet
   (re-worded by D20, 5485af3).
2. Or un-skip `QA/Content/D20DefenseWordingQaTests.DefenseLine_WavePull_IsBoundByTheStartThreshold` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D20DefenseWordingQaTests"`.

## Expected
docs/03 "Implementation (M5-4f)", "Who answers" (b): members of the wave are candidates "each only if the members left keep
the wave's **start threshold** (`attack.minArmyPop` half-pop and `attack.minArmyCost`, each times `waveSizeMultiplier`,
kept at the wave's start)". A wave sent above its threshold gives its surplus (`Ai/ProportionalDefenseTests`: a 12-pop wave
with a 6-pop threshold gives six members to eight Raiders). The brief asked for the Defense lines to be re-worded against
docs/03 case by case.

## Actual
Both pages: "taken from the army at home first, then from a wave out, nearest home first, only while the wave keeps the
size it was sent at." Read literally no member can ever be pulled (any pull shrinks the wave below the size it was sent at),
which contradicts docs/03 and the same line's next sentence ("the wave's spare units"). None of D20's pin rows covers the
clause (they pin the numbers only).

## Notes
A one-phrase fix, e.g. "only while the members left keep the wave's start threshold (`attack.minArmyPop` /
`minArmyCost`)". Minor, same lines: "nearest home first" covers both orders; docs/03 orders the home army by distance
to the defense point and wave members by distance to the hall.

## Verification (QA, fix round 1)
Both Defense bullets now read "taken from the army at home first (nearest the defense point first), then from the members
of a wave out (nearest the Garrison Keep / Holy Camp first), each only while the members left keep the wave's start
threshold (`attack.minArmyPop` and `minArmyCost` times the difficulty's wave size x, so a wave sent above it gives its
surplus)", matching docs/03 M5-4f (a) and (b) clause by clause (the hall is each faction's Town Hall per its building
table). `DefenseLine_WavePull_IsBoundByTheStartThreshold` is un-skipped and green (2 / 2), with the dev's
`DefenseLine_CandidateOrders_MatchDocs03` (2 / 2). The same old wording in docs/02 is BUG-0871.
