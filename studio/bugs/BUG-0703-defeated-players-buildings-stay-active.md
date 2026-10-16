# BUG-0703: A defeated player's standing buildings stay active (its towers keep shooting and scoring kills, its research runs on)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M5-H2, verified by QA 2026-10-16-0847) |
| Found | 2026-10-14-0946, task M5-4e |
| System | defeat rule (World.Defeat.cs), towers, research |
| Fixed by | 47ca4a2 (M5-H2), `MatchOverTests.TheHouseGoneOnTheDefeatTick_*` / `ACollapse_HashTwinsEqual` / `FourAiSeats_APlayerCollapsesMidMatch_*`; QA `QA/CollapseRuleQaTests` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~WinConditionQaTests.ADefeatedPlayersTower_KeepsShooting_Measured"`
   (player 0 holds a finished Watchtower, its last unit is freed, it is defeated; a soldier of player 1 is then parked in
   the tower's range).
2. `...WinConditionQaTests.ADefeatedPlayersCommandsOnItsStandingBuildings_AreDropped(defeated: True)`: a Forge whose
   research was queued before the defeat keeps it queued and running after it.

## Expected
Undefined: docs/02 "Game flow" 4 and docs/03 "Implementation (M5-4e)" say a defeated player's Houses, camps, Forge,
towers and sites "stay standing after the defeat" but say nothing about whether they still act. Before M5-4e a
defeated player had no building left, so the question never came up.

## Actual
```
a defeated player's tower vs a soldier parked in range for 60 s: soldier dead True (hp 108 -> 0), player 0's kills 0 -> 1
after the defeat: the Forge's queue 1, tech A researched False   (still queued and progressing)
```
Nothing in the sim reads `IsDefeated` apart from the command door, `CanPlace` and the AI think (`Simulation.cs:123`,
`:450`, `ConstructionSystem.cs:75`), so a defeated side's towers fire, take kills and give sight, and its Forge finishes
research.

## Notes
- Why it matters: in a 3-4 seat match a player that is out can still kill another player's last unit with a tower and
  so knock that player out too; the post-match stats (kills) of a player that is out keep moving. In a 2-seat match it
  only matters after `MatchOver`.
- Options for the Producer / owner: (a) keep it, and say so in docs/02 / docs/03 (the ruins fight on until razed);
  (b) a defeated player's buildings go neutral / inert (towers stop, queues refunded or frozen); (c) they are destroyed
  on the defeat tick (AoE-style). (b) and (c) touch the tower system and the hash; (a) is a doc line.
- The `..._Measured` row pins today's behaviour so a change is seen; flip it when the decision lands.

## Producer decision (2026-10-14-0946 ACCEPT)
Option (c): **a defeated player's remaining buildings collapse on its defeat tick** (freed like razed ones, death events for the
view's rubble, no kill credited, queues gone). Recorded in docs/01 (Producer decision, owner may revisit) and docs/02 "Game flow" 4.
Built at the sim's M5-H2 hardening (first item): flip `WinConditionQaTests.ADefeatedPlayersTower_KeepsShooting_Measured` and
`...CommandsOnItsStandingBuildings_AreDropped(defeated)` to the new rule; `MatchOverTests` gains "the House still standing" → "the
House gone on the defeat tick"; the hash twin; the view's `MatchOverViewTest` losers own one hall only, so they are unaffected.

## QA verification (2026-10-16-0847, M5-H2)
Verified: `QA/CollapseRuleQaTests` (a 4-seat combat fuzz x 4 seeds x 2,000 ticks and a 4-seat match with three AI seats whose weak seats fall in combat, twins equal and a per-tick checker: no building, unit, resource or tech change of an out player after its tick; Collapsed events only on the defeat tick; kills and losses = counted deaths; cells open; no dangling target or builder; no projectile from an out player; two players out on one tick; the last builder killed on its site; a tower mid-wind-up never fires; a collapsed slot reused by a rival). Follow-ups: two tests that stage a lone non-producing building with no unit now measure the collapse instead of what they measured (BUG-0761, BUG-0762).
