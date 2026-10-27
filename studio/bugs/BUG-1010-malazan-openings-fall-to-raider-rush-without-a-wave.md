# BUG-1010: Two Malazan openings fall to `raider_rush` without a wave on 6 of 10 seeds (cadre_rush 4 / 5, crossbow_push 2 / 5)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-18-1236, task D-H6 (QA) |
| System | AI behaviour under pressure (sim-side: military manager's wave rule); content balance Malazan v Whirlwind `raider_rush` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DH6ThresholdQaTests" --logger "console;verbosity=detailed"`
2. `Bug0982_KeepEighteen_HoldsOnTwoMoreSeeds` (cadre_rush v raider_rush, Normal v Normal, menu map, 20 min, the
   pairing's 5 smallest seeds in 1-500) prints at the shipped 18 pop:
   ```
   | 18 / 1332 | 7  | 8:42 (3)  | 8:24  | never | 25 |
   | 18 / 1332 | 8  | never (0) | 8:44  | 13:14 | 11 |
   | 18 / 1332 | 14 | never (0) | never | 11:19 | 8  |
   | 18 / 1332 | 18 | never (0) | never | 14:33 | 6  |
   | 18 / 1332 | 23 | never (0) | 8:24  | 14:05 | 11 |
   ```
   (16 and 14 pop give the same outcome on seeds 8, 14, 18 and 23.)
3. `CrossbowPushVersusRaiderRush_FourthAndFifthSeeds` prints
   `seed 25: crossbow_push first wave never (0), defeated 11:26, peak 12`. Seed 19 (the pairing's 3rd, D-H6's
   `KnownBeatenAlive` entry `(1, 1, 19, 0)`) sends no wave and is not defeated (peak 8 pop, 6 pop / cap 16 at 20:00).

## Expected
D21 criterion 2: each Malazan x Whirlwind pairing has both sides send a wave. The malazan page (D-H6) says `cadre_rush`
"can fall to a rush without ever attacking ... on two of three measured seeds".

## Actual
Against `raider_rush`, `cadre_rush` sends no wave on 4 of 5 seeds (all four defeated) and `crossbow_push` on 2 of 5
(one defeated, one beaten but alive). The army never gets past 6-12 pop, so no threshold in the 14-18 range would
help (the D-H6 rule still picks 18 over five seeds; reproduced the dev's 7 / 8 / 14 cells exactly). Seeds 18 and 23
are new data for the page: "two of three" understates a 4-of-5 pattern, and the page does not mention that
`crossbow_push` is affected on a defeated seed too (25), only the alive one (19).

## Notes
- S3, not S2: D-H6's criteria (measure, apply the Producer's rule, write it down) are met, and D21's criterion holds on
  the seeds it named. But `raider_rush` beats two of the three Malazan openings without a fight most of the time,
  which is what a Normal player will see when the seed pairs them.
- This is the tracking id for the dev's `KnownBeatenAlive` allowance in `QA/Content/D21OpeningsQaTests.cs`, which
  says "reported to the Producer (a new bug)" without naming one. Reasonable as a named, single-row allowance (not a
  loosened rule); remove it when this is fixed.
- The lever is sim-side (BUG-0982's notes: "send what you have when the base is being lost", or a defend-then-counter
  rule), likely folding into M5-4g with BUG-0981. Data alternative if the owner wants it: a stronger Age I defence
  in these two openings (e.g. a second Barracks earlier), measured against these 10 seeds.
- Page fix (data track, cheap): quote the 5-seed counts and name seed 25.

## Update (D-R32, 2026-10-26-2108, after M5-4g's reserve)
`cadre_rush` sends no wave on 4 of 5 seeds (8, 14, 23 defeated at 13:25 / 12:33 / 15:36; 18 alive at 20:00, peak 6 pop); `crossbow_push` on 2 of 5 (19 and 25, both alive and beaten down: seed 19 at 15 pop / cap 24 with 910 gold and no Age II, seed 25 at 1 pop; before the reserve seed 25 was defeated at 11:26). Seed 25 is a named allowance in `DH6ThresholdQaTests.KnownBeatenAlive`; seed 19 still needs `D21OpeningsQaTests.KnownBeatenAlive`. The keep-18 rule re-derived on the new table still picks 18, so the lever stays sim-side (M5-4g's defend-then-counter slice was built and backed out: 0 / 6 seeds waved). The malazan page quotes the 5-seed counts and names seed 25 (its page line is done).
