# BUG-1068: Normal v Normal on Pale Hills reaches no result in 60 minutes on seeds 2-6 (seed 1, the proof row, is the only one inside 36,000)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-06-0326, task M6-S8 (QA) |
| System | Map data (`game/data/maps/pale_hills.json`) x AI waves (`MilitaryManager`: waves target buildings only) |
| Fixed by | |

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
