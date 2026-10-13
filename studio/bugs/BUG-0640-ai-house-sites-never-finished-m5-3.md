# BUG-0640: On M5-3's AI two of Malazan's Billet sites are placed and never finished (cap 82 at 30 min, was 98 on M5-2)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-13-0146, task D-H4 |
| System | AI (build order / construction), sim track |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiHouseRuleReportTests" --logger "console;verbosity=detailed"`
2. Read the `malazan, 36000 ticks` row.

## Expected
Every house the order places gets built: on M5-2's AI the same row had 11 Billets placed, all finished, cap 98 at pop 82
(D15 QA report, `studio/qa/2026-10-12-1930-D15.md` criterion 5). A Billet's 50 wood is paid at placement, so the end of wood
on this map should not stop a placed site from finishing.

## Actual
```
houses standing: 11 (placed by order steps 11, by the rule 0, unknown 0); finished 9
cap reached 100 at tick -1 (never); pop provided at the end 82 (cap 82), pop 82
time | pop | cap | houses finished | house sites | ...| workers
14:00 | 67 | 74 | 8 | 2 | ... | 30
17:00 | 76 | 82 | 9 | 2 | ... | 30
30:00 | 82 | 82 | 9 | 2 | ... | 30
```
Two sites stay unfinished from 17:00 to 30:00 with 30 workers alive and gold piling up (2,234 -> 4,524). The 16 lost pop of cap
(82 + 2 x 8 = 98) is exactly the M5-2 cap, so the stall is the unfinished sites, not the wood. Pop is capped at 82 for the
last 11 minutes, which stops the AI's army growth.

## Notes
- The AI has no "resume an unfinished own site" path: `OwnBuildingSites` is only used to skip sites (`AiPlayer.cs` 411 / 478,
  `ProductionManager.cs` 44), and `EconomyManager.cs` 60 returns early while any house site exists, so a stuck site also
  switches the economy house rule off for the rest of the game.
- Suspected cause: M5-3's scout / gatherer rebalance retasks the builder before the site is done (the developer flagged the same
  two sites in the D-H4 report). Not root-caused by QA (light tier).
- Same scene, Whirlwind 10 min: 7 placed, 7 finished; Malazan 10 min: 8 placed, 7 finished, 1 site still open at 10:00.
