# BUG-1068: Normal v Normal on Pale Hills reaches no result in 60 minutes on seeds 2-6 (seed 1, the proof row, is the only one inside 36,000)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open, narrowed to seed 2 (M5-4d 2026-11-06-0926: Pale Hills seeds 3 / 4 / 5 / 6 and generated seed 6 end (`MapSlice2StressTests.NormalVersusNormal_KnownStalls_EndWithin72000`, un-skipped: 29,421 / 29,421 / 52,557 / 29,421 / 26,875 ticks); seed 2 is a stalemate, both sides holding two halls at 60:00: `NormalVersusNormal_PaleHillsSeed2_Stalemate_EndsWithin72000`, skipped under this id) |
| Found | 2026-11-06-0326, task M6-S8 (QA) |
| System | Map data (`game/data/maps/pale_hills.json`) x AI waves (`MilitaryManager`: waves target buildings only) |
| Fixed by | seeds 3-6 and generated 6: M5-4d (b7a3f83 + 6a3c83c + 1eccd06, the hunt); seed 2: not a hunt case, the sim's next hardening or M5-4d slice 2 |

## Repro
1. Remove the `Skip` from `Stress/MapSlice2StressTests.NormalVersusNormal_KnownStalls_EndWithin72000`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MapSlice2StressTests.NormalVersusNormal_" --logger "console;verbosity=detailed"`
   (the same `MapFileTests.MapMatch` the developer's proof row uses: two Normal AI seats, Malazan v Whirlwind, starts 0 and
   1, run to 72,000 ticks).

## Expected
The brief's goal: each new map is "proven playable by a Normal v Normal match to a result". Criterion 3 only names seed 1,
and seed 1 passes (Whirlwind won at 34,175 ticks, 1,825 under the limit). But the map should give a result about as often as
the other maps do.

## Actual
At e3fd8fb, seeds 2-6 to 72,000 ticks (60:00):

| Map | Results by 60:00 | Notes |
| --- | --- | --- |
| **Pale Hills** | **0 / 5** | Seeds 3, 4, 6 (one match: openings 0 / 2): Malazan has 0 halls and **1 unit** left, Whirlwind 87 units. Seed 5: the mirror (Whirlwind 0 halls, 6 units; Malazan 36). **Seed 2: both still hold 2 halls**, 37 v 27 units, 7 / 9 waves, 20,450 / 20,670 wood: a true stalemate |
| Vathar Crossing | 5 / 5 | 15,722-31,550 ticks, all inside 36,000 |
| Raraku | 5 / 5 | 23,045-35,706 ticks |
| generated (128) | 4 / 5 | seed 6: Malazan razed, 1 unit left (the same last-unit stall) |

So on Pale Hills the AI's only result in six seeds is seed 1, and that one is near the limit. Four of the five misses are the
stall the developer reported (a wave targets buildings only, so the loser's last units, out in the woods, are never
hunted). It also happens on the generated map, but much less often. Seed 2 is specific to this map: neither side breaks
the other in an hour.

## Notes
- A human opponent hunts the last unit, so a human match is less affected. A 3-4 AI free-for-all on Pale Hills (the map's
  4-player use) will hit the stall more often.
- Fix options (the Producer's call): AI waves that hunt the last known units once no enemy building is known (a generic
  AI change, also fixes generated seed 6); and/or retune Pale Hills (shorter routes between camps, or fewer tree rows
  splitting the waves) and check seed 2's stalemate.
- Seeds 3, 4 and 6 are one match on every map file: a map file draws nothing from the map stream, so the seed only
  changes the AI's opening picks (the same is true on Raraku). Not a bug, but "seeds 2-6" gives 3 distinct matches, not 5.

## M5-H5 (2026-11-08): seed 2's gold measured, not fixed (more than a few lines)

- **The map's gold runs out by design of the AI's reach, not of the map.** Pale Hills holds 36,000 gold: per start two
  2,500 mines beside the hall and two 2,000 mines about 50 m off (cells (83, 49) / (87, 49) for the north-west start), all
  inside `ai.gatherReach` (64 m) of the hall, so each seat mines its own 9,000 and nothing else. In a 2-seat match (starts
  0 and 1) the two empty starts' 18,000 (cells (192, 63) and (63, 192), about 260 m from either hall) are never touched:
  the one `expand` step (at 480 s) looks for a mine within `ai.expandHallReach` (40 m) of the hall, and nothing later
  reaches farther.
- Probe, items 1 and 3 in (BUG-1119, BUG-1120), Pale Hills seed 2 to the limit:
  - 2 seats: at 36,000 p0 gold 18 / wood 3,255 (7 army, 20 half-pop, threshold 32), p1 gold 2 / wood 4,154 (1 army); at
    48,000 the same gold, wood 6,965 / 8,494; both keep their halls and production; no result by 72,000.
  - 4 seats: every seat under 25 gold from about 48,000; at 144,000 p0 gold 6 / wood 31,230 with 13 army under its 32
    threshold, p1 gold 4 with 3 workers and 3 production buildings (so p0 and p3 know an enemy building and never hunt),
    p3 gold 22 / wood 30,250 with no army.
- So the stall is: no gold, no army growth, a wave threshold never reached, and an enemy building still known, so no
  hunt either. Fix candidates, each a rule change past this hardening's "a few lines": (a) a seat whose known gold is
  spent reopens `expand` toward the nearest known unclaimed mine at any distance (needs the scout to have seen it: on
  seed 2 the far starts lie outside the scout's head ring); (b) a seat that can no longer add to its army (gold under its
  cheapest unit's, no gold node left in reach) waves with what it has; (c) map data (more or nearer neutral gold; the data
  track's). Both seed-2 rows stay skipped under this id.

