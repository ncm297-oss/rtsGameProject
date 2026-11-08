# BUG-1123: After the Hard knobs (M5-4c slice 2 + fix round 1), Hard loses to Normal more often than it beats it (5 wins / 10 losses in 18 matches; 12 / 2 before the slice)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-07-0926, task M5-4c-2 (QA re-verify round 1; the developer proposed it in the fix report) |
| System | AI difficulty (MilitaryManager pull-back / focus, gather bonus), game balance |
| Fixed by | |

## Repro
A scratch harness, not committed. It plays a whole match from the map helpers to 72,000 ticks or to the end, Hard in
seat 0 or 1 and Normal in the other, and prints the winner:
```csharp
Simulation sim = map == "raraku" ? MapFileTests.RarakuMatch(seed, false, out _, a, b)
                                 : MapFileTests.MapMatch(MapFileTests.PaleHills /* or VatharCrossing */, seed, false, out _, a, b);
while (sim.TickNumber < 72_000 && !sim.MatchOver) sim.Tick();
```
Run it on seeds 1-3 x 3 maps (Raraku, Pale Hills, Vathar Crossing) x both seats = 18 matches per tree. The trees: `git archive`
exports of 6f6414b (before the slice), 947a273 (the slice) and 9f7423b (fix round 1), plus 9f7423b with
`"pullBackWounded": false` in both factions' `ai.json` (a data copy).

## Expected
docs/02 "AI opponent": Easy < Normal < Hard. Hard is "fair play with tighter decisions (thinks every 0.5 s, focus fire,
pulls back wounded units ...)". The slice was meant to make Hard harder: Hard must beat Normal at least as often as it
did before the slice, when it was only Normal with 0.5 s thinks and a 1.25 wave multiplier.

## Actual
Hard wins / Normal wins / no result by 72,000 ticks (Raraku + Pale Hills are 12 matches, Vathar 6):

| Tree | Raraku + Pale Hills | Vathar Crossing | All 18 |
| --- | --- | --- | --- |
| 6f6414b, before the slice | 6 / 2 / 4 | 6 / 0 / 0 | **12 / 2 / 4** |
| 947a273, the slice | 1 / 9 / 2 | (not run) | |
| 9f7423b, fix round 1 | 3 / 6 / 3 | 2 / 4 / 0 | **5 / 10 / 3** |
| 9f7423b, `pullBackWounded` off | 6 / 3 / 3 | 3 / 3 / 0 | 9 / 6 / 3 |

The Raraku + Pale Hills rows match the developer's table in docs/03 to the match. Fix round 1 recovers part of the loss.
But Hard now loses 10 of 18 to Normal and wins 5, where before the slice it won 12 and lost 2. Vathar is the clearest
case: Hard won all 6 before the slice and wins 2 now.

## Notes
- Pull-back is the largest single drag, as the developer says: turning it off moves the fix from 5/10 to 9/6. It is not
  the only one: with it off Hard still trails the pre-slice tree (9/6 against 12/2), so focus fire and/or the per-member
  reach still cost something in long matches (Vathar 3/3 against 6/0).
- Likely mechanism for the pull-back (from the developer, consistent with the counts): nothing heals, so a wounded unit
  never rejoins a wave. A Hard army that pulls back 15-40 units per seat in 20,000 ticks (4-seat Pale Hills seed 1:
  30/40/15/17) attacks with fewer units over a long match.
- Not a determinism or crash issue: every row is deterministic, and the two-process Hard rows are green.
- Whether this blocks the slice or becomes a tuning follow-up (pull-back only with healing or a regroup, gating it on
  the army's size, ...) is the Producer's call. QA files it S2 because the slice inverts the difficulty order it was
  meant to sharpen.
