# BUG-1119: A defeated seat's remembered buildings still count as known enemy buildings, so a survivor waits for a wave it can never afford instead of hunting

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M5-H5, 2026-11-08) |
| Found | 2026-11-07-0236, task M5-4d-2 (the dev's fix-round-1 probe of BUG-1116; filed by the Producer at the ACCEPT) |
| System | AI (`AiPlayer` enemy-building memory / `MilitaryManager.TryHunt`), match end |
| Fixed by | M5-H5 cd211f2 (fix round 1: 6c3639a); `Ai/EndGameHardeningTests.ADefeatedSeatsGhost_IsNoKnownEnemyBuilding_SoTheSurvivorHunts`, `AWaveOnADefeatedSeatsGhost_TurnsIntoAHunt` (both red before the fix) |

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

## Fix (M5-H5, 2026-11-08)
- `PlayerView.IsDefeated(player)` (read-only, `World.IsDefeated`; public under the win rule) and `AiPlayer.IsLiveEnemy(owner)`
  (an enemy not defeated). Every reader of remembered enemy buildings uses it for ghosts: the census's
  `KnownEnemyBuildings`, `NearestEnemyBuilding` (wave targets, the rally direction), `EnemyBuildingKnown` (a wave's target
  still known) and `ScoutManager`'s door rule. The fog's ghost list is untouched (the player's screen still shows them);
  only the AI forgets them. A wave on such a ghost turns into a hunt at the next think, as on a razed target.
- Not applied: `BuildOrderExecutor.HallNear` (the expansion spacing rule, any owner's hall ghost) still counts a defeated
  seat's hall ghost; changing it moves expansion sites in 3+ seat matches for no stall measured.
- Re-measured (Pale Hills seed 2): the 4-seat free-for-all still has no result by 144,000 (p0 41 units, 13 army under its
  32 half-pop threshold, gold 6 / wood 31,230; p1 3 workers and 3 production buildings, which p0 and p3 know, so nobody
  hunts; p2 defeated; p3 23 workers, no army, gold 22). The ghosts of the defeated p2 are gone from p3's count (`ghostsDef`
  0 at every 12,000-tick probe), so what is left is BUG-1068's gold. The 2-seat row is unchanged (no seat is defeated
  before the end in a 2-seat match). Both rows stay skipped under BUG-1068 / BUG-1116.
- Pins: none moved (a 2-seat match ends on the first defeat; the 4-seat rows `HuntStressTests.FourAiSeats_*` seeds 1 / 3
  and the teams row still end).
