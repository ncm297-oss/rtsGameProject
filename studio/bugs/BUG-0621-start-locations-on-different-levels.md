# BUG-0621: On 128 maps about 1 seed in 7 puts one base on a plateau and the other on low ground, and bases can start 70 m apart

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-13-0146, task M5-4a |
| System | map generator (start rule, `MapGenerator.FindStarts`), design conformance |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StartLocationQaTests.Survey_StartLevelsAndSeparation" --logger "console;verbosity=detailed"`
2. Output (seeds 1-100, default params):
   ```
   128: bases on different levels 15/100, flat fallback 0/100, apart min 70 m (seed 21) mean 177 m
   192: bases on different levels 1/100, flat fallback 0/100, apart min 206 m (seed 72) mean 308 m
   256: bases on different levels 1/100, flat fallback 0/100, apart min 350 m (seed 17) mean 437 m
   ```
3. The default skirmish map, 128 seed 1: player 0 at (45, 61) on **level 1**, player 1 on **level 0**, 75 m apart (the dev's
   report says "37 cells apart near centre, player 0 on a plateau"); seeds 10 and 12 also start 76-89 m apart.

## Expected
docs/02 "Map and terrain": "Layout | Symmetric for 2-4 players (mirror or rotational)", and "High ground" gives a real vision
advantage (low ground can't see up). The brief asks for point-mirrored start *positions* "on level 0 or one plateau" per location;
it doesn't say both bases share a level, nor set a least distance between them. Whether that is what "symmetric" means is the
Producer's call.

## Actual
`FindStarts` mirrors only the centre cell; the terrain is not mirrored, so the mirrored cell can be on another level. With the
"nearest the middle of the west band" rule, a crowded 128 map pushes player 0 toward the centre (the band x ≤ 53 is all that's
allowed), giving 70-90 m rush distances on some seeds against about 180 m typical.

## Notes
- Options: require `Level` equal for all locations (redraw otherwise; costs some 128 layouts); prefer level 0 for both; add a
  least separation (for example half the map width); or mirror the terrain (M6 work). The AI's scout and the M5-5 20-minute
  winner test will play these maps, so a lopsided seed can skew the win rates.
- Not an acceptance criterion failure: criterion 1 (mirror, mines and forest on each base's own level, flat clear disc) holds on
  all 36 pairs.

## Producer triage (2026-10-13-0146 ACCEPT)
S3 confirmed, ruled: docs/02 "Symmetric" is met for *positions* (exact point mirror; criterion 1 holds on all 36 pairs); mirrored *terrain* is M6 criterion 7's generator work (docs/03 "Water, symmetry and hand-made maps"), not a regression. But two bases on different levels is a real fairness gap (high ground sees down, low ground can't see up), so the **sim's hardening next session** adds a same-level rule to `FindStarts` (`Fits` requires every position's `Level` equal; a layout with no such centre is redrawn like one without room) and a least separation of half the smaller map side between any two centres; re-run the survey and record the new fallback rates for 128 (expect more flat fallbacks on crowded 128 layouts: report the number, don't tune the generator to it). The golden (seed 1, cross-map) may move: seed 1's player 0 is on level 1 today, so a same-level rule picks another centre or redraws; regenerate once, say so.
