# BUG-1026: The reserve gate counts the army that is away on a wave, so 77 % of reserve thinks hold production while the wave is out, not after the army is rebuilt as the docs say

| Field | Value |
| --- | --- |
| Severity | S4 (was S3: the docs half is fixed; what is left is a tuning question) |
| Status | open (docs half fixed by the Producer at the 2026-10-19 ACCEPT: docs/02 "AI opponent" and docs/03 "Implementation (M5-4g)" now say the gate counts the whole army, the wave out included, and why that form was kept; the home-only count stays a measured variant for a later tuning pass with the Easy / Hard rows below as its cases) |
| Found | 2026-10-18-1814, task M5-4g re-check after fix round 2 (c76bc5f) |
| System | AI reserve gate (`AiPlayer.MayReserve` / `ArmySize`) against `MilitaryManager.TryStart` (wave units) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReservePairingsStressTests.Pairing" --logger trx`.
2. Each `ROW` line now has `reserveWaveOut N`: the thinks that named a reserve while `Board.WaveActive` was true.
   Sum it against `reserveThinks`.

## Expected
docs/02 "AI opponent" (fix round 2): "every wave, not just the first, is rebuilt before the AI saves". docs/03
Implementation (M5-4g): "after a wave the army is rebuilt to the wave's size before the AI saves again".

## Actual
`ArmySize` counts every live non-worker unit from the census, including the units walking to or fighting at the enemy
base. So the wave that just left keeps the gate open, and production is held while it is out. When the wave dies the
gate shuts, and only then does production rebuild the army. On the 27 pairing runs (Normal v Normal, 20 minutes):

- 7,976 thinks named a reserve. **6,112 of them (76.6 %) had a wave out.** 50 of 54 sides did this.
- Examples: `standard` v `holy_war` seed 6, Malazan. The first wave left at 6:19 and Age II (400 / 200) was reserved from
  6:35 to 7:28 while the wave was out. The side then sent no other wave and was defeated at 15:39. On the base it sent 4
  waves and survived. Seed 1 Malazan `standard`: 254 of 260 reserve thinks had a wave out. Whirlwind `standard` seed 32:
  268 of 306.

This is not a determinism or crash problem, and the round-2 totals still meet the wave targets: Malazan waves 81 against
79 on the base, and Malazan / Whirlwind defeats 5 / 0 against 8 / 5. But the mechanism isn't what the docs describe. The
"rebuild before saving" protection only works for an army that is at home.

## Notes
- Two ways to settle it. Either the docs are reworded to say "while the army, home or on a wave, is wave-sized", or the
  gate counts only the units that `TryStart` would count (the free ones, not on the wave). The second option may trade
  Age II for waves again (see the variants table in docs/03), so measure it first. This is the Producer's call.
- Same file, outside criterion 3's 27 runs (Easy / Hard, seeds 1-3, printed rows):
  - Hard seed 3 Whirlwind `holy_war` sends **no wave**. On the base it sent one at 15:38.
  - Easy seed 2 Malazan: 5 waves and alive on the base, now 2 waves and defeated at 17:00.
  - Easy seed 3 Whirlwind and Hard seed 2 Malazan are alive at 20:00 without Age II. Both were defeated on the base.
