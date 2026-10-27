# BUG-1042: With allies at mirror start locations, an AI seat that only knows its ally's base never starts a wave

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-27-0158, task M6-S4 |
| System | AI (ScoutManager, MilitaryManager) with teams |
| Fixed by | |

## Repro
1. Four AI seats, teams **1, 1, 2, 2** (player p faction p mod 2, difficulty (p + seed) mod 3), on a 256 map with 4 start
   locations, 24 forests, 16 gold mines, one raider of each seat in the next seat's base
   (`AiScenes.Generated(seats, seed, ..., raiders: 1, configure: 256 map)`). On this map the seats start west (0), east
   (1), north (2) and south (3), so each seat's ally is at its mirror through the map centre.
2. Run 12,000 ticks (10 minutes). `Stress/TeamAiSoakStressTests` rows with `adjacent: true` (seeds 1-3) run it; the wave
   check is skipped there with a pointer to this bug.

## Expected
QA focus for M6-S4: "both AIs still wave at the enemy". Teams change who is an enemy, not whether an AI attacks.

## Actual
`WavesStarted` per seat after 12,000 ticks:

| Seed | teams 1, 1, 2, 2 | teams 1, 2, 1, 2 (same map, same seats) |
| --- | --- | --- |
| 1 | 0, 1, 0, 1 | 1, 1, 1, 2 |
| 2 | 0, 1, 0, 1 | 1, 2, 2, 1 |
| 3 | 1, 1, 0, 0 | 2, 2, 1, 0 (seat 3 is Easy and starved; the same with every team 0) |

Seed 1, teams 1, 1, 2, 2, seat 2 (traced every 500 ticks): `KnownEnemyBuildings` stays **0** for all 12,000 ticks while its
fog holds 7-8 ghosts (its ally's buildings). Its army grows to **17 units by tick 9,000**, but no wave can start
(`MilitaryManager.TryStart` returns when `NearestEnemyBuilding` finds nothing). Its scout is still sweeping (`ScoutPhase`
1, 49 of 109 waypoints visited at tick 12,000): the head's ninth waypoint, the hall's mirror, is the ally's base, and the
nearest-first grid sweep then works through the ally's half of the map. Seat 0 traces the same way (kEB 0, scout phase
1 to the end) and its economy also collapses: its own raider idles in the ally's base, so the one enemy raider in its base
kills its workers for 3,000 ticks while the raider walks 400 m home to defend.

The safety half holds on every row: no Attack, AttackMove or cast at an ally, no wave at an ally's building, no Defending
without an enemy army near the hall or a damaged building.

## Notes
- The cause predates teams: a wave needs a known enemy building, and the scout's head assumes "the other base is at the
  mirror" (docs/03 M5-3, BUG-0593). In a free-for-all the mirror is always an enemy. With teams it can be the ally, and the
  seat then has no target for minutes. The teams 1, 2, 1, 2 layout used by `AiTeamTests` and the dev's soak rows puts
  allies side by side, so it never hits this.
- Possible directions (Producer's call; AI cooperation is out of M6-S4's scope): skip a head waypoint whose base turns out
  to be an ally's, sweep the grid farthest-from-the-ally first, or let a seat with an army and no known enemy building wave
  toward the map centre or an unexplored quadrant.
- Not reachable in the shipped game yet: the setup screen has no team column (view follow-up).

## Round 1 (QA, 2026-10-27-0158)
Not changed by 0a2b9e2 (left for the Producer as an AI / scout design call). The mirror-layout soak rows still skip the wave
check. Still open.

## Producer triage (2026-10-27-0158 ACCEPT)
S3, **planned as the first criterion of the next sim task (M6-S5, teams slice 2)**, not a hardening item: the view's team column
(M6-V16) lands next session, and a human picking "slot 1 and 2 on team 1" on the 4-start map puts its AI ally at the mirror start,
which is exactly this case. Direction for the brief: (a) a head waypoint whose base turns out to be an ally's is dropped and the
sweep continues from the unexplored grid points farthest from the ally's known buildings; (b) with shared vision (slice 2's rule)
an enemy building the ally sees counts as known, so a wave can start on it; (c) only if cheap: a seat with a wave-sized army and no
known enemy building attack-moves toward the nearest unexplored start-location candidate. The soak rows' skipped wave check
becomes strict with the fix.
