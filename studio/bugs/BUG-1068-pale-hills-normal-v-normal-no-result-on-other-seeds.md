# BUG-1068: Normal v Normal on Pale Hills reaches no result in 60 minutes on seeds 2-6 (seed 1, the proof row, is the only one inside 36,000)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open, narrowed to seed 2 (M5-4d 2026-11-06-0926: Pale Hills seeds 3 / 4 / 5 / 6 and generated seed 6 end (`MapSlice2StressTests.NormalVersusNormal_KnownStalls_EndWithin72000`, un-skipped: 29,421 / 29,421 / 52,557 / 29,421 / 26,875 ticks); seed 2 is a stalemate, both sides holding two halls at 60:00: `NormalVersusNormal_PaleHillsSeed2_Stalemate_EndsWithin72000`, skipped under this id) |
| Found | 2026-11-06-0326, task M6-S8 (QA) |
| System | Map data (`game/data/maps/pale_hills.json`) x AI waves (`MilitaryManager`: waves target buildings only) |
| Fixed by | seeds 3-6 and generated 6: M5-4d (b7a3f83 + 6a3c83c + 1eccd06, the hunt); seed 2: not a hunt case, the sim's next hardening or M5-4d slice 2. **Candidate (b) is built on the M5-9a branch (2026-11-08-1556, `ai.starvedWaveAfter`): seed 2 ends there at 53,797 and its FFA at 80,278, both rows un-skipped; not on `main` yet. Candidate (a), the expansion reach, is the next sim task (M5-9b, same branch): see the Producer triage below** |

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


## Developer notes, M5-9a round 2 (2026-11-08-1556; applied by the Producer at ACCEPT)

## M5-9a round 2 (2026-11-08-1556): candidate (b) built (`ai.starvedWaveAfter`), seed 2 still no result

- Candidate (b) is built behind `rules.json` `ai.starvedWaveAfter` (60 s shipped, 0 = off) and `ai.starvedMinArmy` (1)
  (docs/03 "Implementation (M5-9a)", round 2; BUG-1153's fix attempt). Rule (i) is the brief's: the seat can pay for **no**
  unit of its composition with what it holds.
- **Pale Hills seed 2, Normal v Normal, to 72,000: still no result** (both rows stay skipped under this id). Probed every
  6,000 ticks: from 36,000 seat 1 (Whirlwind) has **no army** (gold 14, 19-24 workers, 17-18 buildings), so it is never
  starved ((iii) needs a free army unit); seat 0 (Malazan) keeps **7 army** under its threshold with **gold 48** and wood
  piling to 14,535, 30 workers, 20 buildings, and is **never stamped**: 48 gold pays for a Crossbowman (36 gold, 45 wood),
  so (i) fails, yet the production pick waits for the mix's most under-weighted unit it cannot pay (M5-2's "it waits, it
  doesn't spend on a cheaper unit") and the gold never moves. Seat 0 knows 12 enemy buildings and never waves or hunts.
- So on seed 2 the lever's rule (i) is too narrow: the seat is starved in effect (it trains nothing for 36,000 ticks) but
  can still pay for one cheap entry. A reading that would catch it (not built, out of round 2's brief): (i) judged on the
  unit the production pick would train (or "no Train sent and none affordable above the reserve for N thinks"). With it,
  seat 0's 7 army against a seat with none would likely end the match. The Producer's call.
- Seed 1 (BUG-1153's row): the lever ends the stalemate at 71,660, outside 36,000 (see BUG-1153).
- **M5-9a round 2, fix round 1 (2026-11-08-1556, the developer): seed 2 ends.** With BUG-1154's fix (the starved check
  reads the unit production would train), Pale Hills seed 2's Malazan seat (48 gold, its pick a Heavy Infantry at 54 gold
  with no gold left) is stamped at 26,780 and waves; the match ends at **53,797 (44:49), seat 0 the winner**.
  `MapSlice2StressTests.NormalVersusNormal_PaleHillsSeed2_Stalemate_EndsWithin72000` and
  `HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall` (the FFA, ends at 80,278, seat 3) are **un-skipped and green**;
  QA's sweep row Pale Hills 2 green. Candidate (a) (the expansion reach) is not built; seed 1 (BUG-1153) still ends late.
  The Producer may close or narrow this bug.

## Producer triage, 2026-11-08-1556 ACCEPT: candidate (b) built on the branch; candidate (a) is the next sim task (M5-9b)

- On the M5-9a branch (`studio/2026-11-08-1556-sim`, held: BUG-1153) candidate (b) ends seed 2 (53,797) and its FFA (80,278):
  the two skipped rows are un-skipped there. Nothing of it is on `main`, where both rows stay skipped under this id.
- **Candidate (a) is planned as M5-9b on the same branch** (the default in BUG-1153's triage, unless the owner picks another
  route in the inbox): a seat whose known gold nodes in `ai.gatherReach` of its drop-offs are spent (or all taken) re-opens
  `expand` toward the nearest known unclaimed mine at any distance, and the scout's sweep must give it the sighting (seed 2's
  far starts lie outside the head ring: the scout learns the unused start positions from the map, which every seat knows,
  not from the fog). Behind a data knob with 0 = off so the no-change twins still hold. Measured on every pinned row.
- This bug closes when seed 2 and the Pale Hills seed 1 FFA end on `main` without a skip; it narrows to "the map's gold
  runs out by 24:00 for a seat that never expands" if (a) lands and some row still runs past the hour.

## Developer notes, M5-9b (2026-11-09-0426): candidate (a) built on the branch

- `FarExpansion` (docs/03 "Implementation (M5-9b)"; `rules.json` `ai.farExpandReach` 384 m, 0 = off): a seat short of
  gold for its pick, with no gold worker and no gold node it knows, walks a worker to the nearest free start location (no
  player starts there, or its player is defeated) and round it, and builds its cheapest gold-free drop-off (depot / supply
  cache, 0 gold / 75 wood) by a free mine it has seen within the reach (no enemy drop-off by it); the existing rebalance moves
  gatherers there. The `expand` step and its Town Hall are unchanged. The start list is map data every seat is given.
- Measured on the branch (Normal v Normal, to 72,000 unless said): seed 1 **48,858** (seat 1; BUG-1153 outcome (b)); seeds
  3 / 4 / 6 **29,514** (was 31,891); seed 2 **79,504** (seat 1, decided by 40:00; was 53,797); seed 5 **96,321** (seat 0;
  was 45,358). Seeds 2 and 5 now end past 72,000 (BUG-1157: the decided match's finish; their `MapSlice2` rows skipped with
  the measurement, seed 2 under this id). FFA seed 2 **97,140** (seat 3; was 80,278), green inside 144,000; FFA seed 1 no
  result by 144,000 (no free gold left with four seats: every start's mines are in someone's reach and spent by ~26,000),
  skipped under this id and BUG-1116.
- So (a) does what the Producer asked of it on the 1v1 maps (the unused starts' 18,000 gold is taken: gathered gold
  14,000-17,800 a seat on seeds 2 / 5 against 9,000 before), and the stalls left are the end game's finish (BUG-1116 /
  BUG-1157), not gold starvation. The four-seat FFA has no gold left to find.
