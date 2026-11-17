# BUG-1522: Hard no longer leads Normal on Pale Hills (4 / 6 / 4); the tally fell from 36 / 3 / 3 to 28 / 10 / 4

| Field | Value |
| --- | --- |
| Severity | S3 (balance) |
| Status | closed by measurement (2026-11-16-2036: Hard v Normal 34 / 7 / 1 after M6-S12, Pale Hills 9 / 4 / 1; nothing was tuned, the matches moved; D33 re-measures as part of the balance pass) |
| Found | 2026-11-15-2136, task M6-SH2 (developer; QA attributed it) |
| System | AI difficulty (data: `ai.json` Hard knobs) on the M6-SH2 economy |
| Fixed by | |

## Repro
`Stress/HardVersusNormalTallyQaStressTests` (42 matches to 72,000).

## Expected
Hard beats Normal on every map (BUG-1163's bar: 32 / 8 / 2 at D27, 36 / 3 / 3 at M6-S10).

## Actual
28 / 10 / 4 (Hard / Normal / none): Raraku 13 / 1 / 0, Vathar Crossing 11 / 3 / 0, **Pale Hills 4 / 6 / 4**.

## Notes
QA: the drop-off rule alone gives Pale Hills 5 / 6 / 3; the retrain plays no part. A finding for the balance pass:
re-measure after the next AI task (it moves every match again), then the data track re-tunes Hard if it still trails.

## M6-S12 (2026-11-16-2036)
Hard / Normal / none over 42 matches to 72,000: 34 / 7 / 1 (was 28 / 10 / 4). Raraku 13 / 1 / 0, Vathar Crossing
12 / 2 / 0, Pale Hills 9 / 4 / 1 (was 4 / 6 / 4). Report only; nothing tuned.
