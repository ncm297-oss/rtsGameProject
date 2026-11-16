# BUG-1510: D-R39 nits: two overlong page lines; "beaten: hall lost" excuses every step of a hall-less seat

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-15-1536, task D-R39 |
| System | faction pages, `Content/AiOpeningPairsReportTests` |
| Fixed by | |

## Repro
1. Layout: `docs/factions/malazan.md` line 627 ("seeds 14, 18 and 23, with seed 8 waving; ... `standard` sent a wave on
   each of its 9") is 139 characters; `docs/factions/whirlwind.md` line 507 ("M6-S10 gather change. On the first seed of
   `holy_war` ...") is 130. The pages wrap at about 120 (BUG-1421 item 4 asked for the same wrap on teblor.md).
2. Reason scope: run `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~NormalAiVersusAi_EveryOpeningPair"`
   and read the (2, 0) row (seed 13). At 20:00 the Whirlwind seat has 4 workers, cap 8 and 1,072 gold / 257 wood, and
   every undone build step (Raider Camp, Archer Camp, Smithy ...) reads "beaten: hall lost".

## Expected
1. Prose lines wrapped near 120 like the rest of the page.
2. The brief (D-R39 Scope 2) asks for "beaten: hall lost" on a seat whose hall is razed, and the developer built exactly
   that, so this is not a criterion miss. Noted for the Producer: the reason is checked last and excuses *any* step once
   no finished town hall stands, including a step whose real blocker is not the hall (a seat with workers and gold that
   never rebuilds its hall or a Raider Camp is the "AI stops rebuilding" pattern of BUG-1465 / BUG-1116, and the row no
   longer flags it).

## Actual
As in Repro. QA's `QA/Content/DR39ReStatementQaTests.RazingOneHall_WhileASecondFinishedHallStands_IsStillADeadEnd`
proves the reason does not fire while any finished hall stands; it does not (and per the brief should not) tell a
hall-less seat that could still act from one that cannot.

## Notes
- `AiOpeningPairsReportTests.WaveDue` counts every live non-worker unit and ignores the military's "no wave while
  defended" gate and the wave cooldown, so its "due" tick is the earliest a wave could start. That is the conservative
  side for the `DefeatedBeforeDue` excuse (an earlier "due" makes the excuse harder to claim); whirlwind.md's "wave due"
  column should be read that way.
- docs/03 "Implementation (M6-S10)" still describes the (0, 2) / (2, 0) rows as red (the developer listed it; outside the
  data track's files).
