# BUG-1119: A defeated seat's remembered buildings still count as known enemy buildings, so a survivor waits for a wave it can never afford instead of hunting

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-07-0236, task M5-4d-2 (the dev's fix-round-1 probe of BUG-1116; filed by the Producer at the ACCEPT) |
| System | AI (`AiPlayer` enemy-building memory / `MilitaryManager.TryHunt`), match end |
| Fixed by | |

## Repro
1. Pale Hills, 4 Normal AI seats, free-for-all, seed 2 (`HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt(2, false)`,
   the known-stall row since the 2026-11-07-0236 ACCEPT; the dev's probe output is quoted in docs/03 "Implementation (M5-4d slice 2)").
2. By 96,000 p2 (`cadre_rush`) is defeated (0 workers, 0 army, no building). At 144,000 p3 (`standard`, 7 army units, 14 gold)
   still lists 18 of p2's buildings as known enemy buildings ("ghosts known by owner: p2 18").

## Expected
Once a seat is defeated (public: the result banner, `World.IsDefeated`), no enemy's memory should hold its buildings: they
are gone with the collapse rule (M5-H2). With no enemy building known and a wave sent before, p3's army would hunt (M5-4d),
whatever its size, and find p1's three workers or p0's base.

## Actual
The ghosts are fog-correct memory of buildings p3 never re-saw after they collapsed, so `EnemyBuildingKnown` stays true,
the hunt never starts, and p3 waits for a 28 half-pop wave it cannot afford (gold 14). Together with BUG-1068's gold
starvation this is why the seed-2 free-for-all has no result.

## Notes
- Fix sketch (the sim's hardening, a few dozen lines): at the census, drop every remembered enemy building whose owner
  `PlayerView` reports defeated (add a read-only `PlayerView.IsDefeated(player)` if there is none; the defeat is public
  under the owner's win rule). Not a fog leak: the banner tells every player.
- Also covers a wave in flight on a ghost of a defeated seat (it turns into a hunt as a razed target does).
