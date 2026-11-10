# BUG-1162: With the hunt's free starts first, a lone worker at its own start is not seen in an hour; other hides 10,000 ticks later

| Field | Value |
| --- | --- |
| Severity | S3 (a hide-and-seek regression on a scene; every measured match row still ends) |
| Status | open |
| Found | 2026-11-09-1336, task M5-H6 (QA inspection, QA focus "hide-and-seek rows") |
| System | AI hunt: `MilitaryManager.NextSweep` / `NextHuntStart` (M5-H6 lever (i)) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~EndGameH6QaTests.ALoneWorker_OnTheSecondHunt_IsFound"`
   (Stress; the "own start 1" row is skipped with this id: remove its `Skip`).
2. The scene is the developer's own (`EndGameHardeningTests.AHunt_SweepsTheFreeStartsFirst_*`): a 512 m flat map with
   four starts (0 the hunter's, 1 the remnant's, 2 and 3 free), player 1 reduced to one camp follower, player 0 hunting
   with four Heavy Infantry from round (40, 60) m. Each row runs the same scene twice: `HuntsStarted` preset to 1 (this hunt
   is the match's second: the free starts first) and to 0 (the first hunt: nearest-first, as before M5-H6), to 72,000 ticks.

## Expected
A remnant that survived the first hunt is found at least about as soon as before; above all, a lone worker standing at
its own start (the most natural place for a beaten side's last unit) is seen inside the hide rows' 72,000-tick bound.

## Actual
Ticks from the hunt's start to the first sighting (second hunt / first hunt):

| Hide | Second hunt (M5-H6) | First hunt (no lever) |
| --- | --- | --- |
| own start 1, (258, 482) | **never** (pass 1, 231 targets) | 39,797 |
| far corner, (500, 500) | 17,921 | 7,441 |
| interior quarter, (166.5, 344.7) | 51,545 | 41,289 |
| free start 3's centre, (82, 402) | 60,945 | never |

Output (own start 1): `second hunt seen never; pass 1, hunts 2, 231 targets: 20:<76.8, 384> 2260:<409.6, 409.6> 4520:<384, 384> ...`
/ `first hunt seen after 39797; pass 0, hunts 1, 155 targets`.

Every hunt from the second on first walks to each free start's proxy (here two cross-map trips, about 4,500 ticks) and
then re-plans nearest-first from there, so the whole route changes. A remnant anywhere but a free start is seen about
10,000 ticks later on these rows, and the home-start worker (on a spot only the odd pass's edge midpoints see) is not seen
at all in the hour. The one row the lever exists for (a worker at free start 3) gains, but is still found only at 60,945,
because the proxy it walks to is 17.6 m from the start's centre (BUG-1161 item 2).

## Notes
- The match rows the lever was measured on (Pale Hills 1 / 2 / 5, the five Hard-seat-0 seeds) all end, and QA's probe
  (`Stress/EndGameH6ProbeQaStressTests`) reproduces the developer's end ticks with the hunt's fog oracle clean; there the
  remnant really is at a far depot. This row is the cost side the developer's table doesn't show.
- The developer's report says lever (i) was limited to the second hunt on because, applied to every hunt, it sank QA's
  `HuntSweepRound2HideQaTests` Pale Hills AI-seat-1 row; this is the same fragility one hunt later.
- Possible directions (the Producer's call): look at a free start only when the remnant could be there (the beaten side
  was seen building or gathering away from home, or holds a far depot the seat once saw), or insert the starts into the
  nearest-first order by distance (the brief's own alternative in (i)) instead of walking them all first.
- Related: BUG-1116 (the sweep order), BUG-1157 (the late finish the lever fixes), BUG-1161 item 2.
