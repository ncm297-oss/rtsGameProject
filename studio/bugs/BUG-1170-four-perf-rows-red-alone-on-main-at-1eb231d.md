# BUG-1170: Four wall-clock Perf rows fail alone (rule 42) on `main` at 1eb231d on this PC, before any M7-2 change

| Field | Value |
| --- | --- |
| Severity | S3 (Perf gates red on `main` on the studio's own machine; no gameplay effect) |
| Status | open (machine state; not a code regression; no bound changed) |
| Found | 2026-11-10-0146, task M7-2 (QA's Perf run on a clean clone of 1eb231d, twice, and on the branch) |
| System | Perf tests: `Ai/AiPerfTests`, `Vision/VisionGatePerfTests`, `QA/PassiveScaleQaTests`, `QA/EndGameH6Round2QaTests` |
| Fixed by | |

## Repro
1. Clean clone of 1eb231d (or of the merged `main` after this session), no other testhost or Godot running, 30 s idle.
2. `dotnet test sim/Rts.Sim.Tests --filter "Category=Perf"`: 4 rows fail (QA, two runs at the base and one at the branch head):
   - `AiPerfTests.EightHardSeats_MidWave_*` 1.056 ms (base 1.126 / 1.040 ms; bound 1 ms; last green avg 0.525 ms, p95 0.863 at M5-H6),
   - `VisionGatePerfTests.Brawl250v250_*` 5.26 ms (base 4.81 / 5.22 ms),
   - `PassiveScaleQaTests.EmptyTick_2500OneOwner_*(zealot, True)` 532 us (base 504 us on run 2),
   - `EndGameH6Round2QaTests.ASweepRetarget_*` 67 us (base 55.8 / 69.1 us; bound 50; measured 16.4 us at M5-H6, 35.2 us worst case).

## Expected
Every Perf row green alone on `main` (rule 42), as at the 2026-11-09-2226 ACCEPT (QA: `AiPerfTests` 6 / 6, `CombatPerfTests` 5 / 5 alone).

## Actual
The same four fail at the diff base and at the M7-2 head, so M7-2 did not cause them; the machine is running slower (the
session ran three tracks' testhosts for hours; the PC's power plan slows it to about 0.6x under a sustained load, BUG-0764, and
the slowdown may outlast the load). The developer measured 203 / 0 Perf earlier in the same session.

## Notes
- Not a code regression as far as the evidence goes: the M7-2 diff adds one `HitPassive < 0` read per direct hit and the
  pack loop only at a spawn; none of the four rows trains packs or bites.
- Next sim session: rerun the four alone first (cold); if green, close as a machine state with the numbers; if red, profile
  before touching a bound (the sweep re-target at 67 us against 50 is the most suspicious: it had 35.2 us of headroom).
- M7-3 step 0 (2026-11-10, before any M7-3 change, the four rows alone, filter on the four names): **red**, all four.
  The machine was not idle: the data and view tracks' builds were running (3 MSBuild nodes + a 2.4 GB VBCSCompiler, CPU
  28-46 % busy before the run), so a true cold idle was not available in a parallel session.
  - `AiPerfTests.EightHardSeats_MidWave_*`: red (number in the run's output, above the 1 ms bound),
  - `VisionGatePerfTests.Brawl250v250_DesignBudget_FreshProcessShortRun_Under4Ms`: 5.79 / 6.12 / 6.51 ms (bound 4),
  - `PassiveScaleQaTests.EmptyTick_2500OneOwner_*(whirlwind_zealot, True)`: 567.4 us (its two other rows green: raider
    351.6 us, zealot full hp 443.3 us),
  - `EndGameH6Round2QaTests.ASweepRetarget_WithEveryStartVisited_CostsUnder50Us`: 77.9 us (bound 50).
- Profiled the sweep re-target first, as asked: `git diff fe6ca00 HEAD -- sim/Rts.Sim/Ai` is **empty** (no AI code changed
  since the M5-H6 QA re-check that measured 16.4 us). The M5-H6 tree itself (`git archive fe6ca00`, built in the
  scratchpad) run alone, interleaved with the head, on the same machine at the same time: **57.6 / 51.4 us** at fe6ca00
  against **45.8 / 77.9 us** at the head. Identical code, the same 3-5x slower than at M5-H6: the machine, not the code.
  The call is `NearestUnvisited`'s two passes over 225 hunt points (a 512 m map at `ai.scoutGrid` 56: 10 x 10 centres,
  81 corners, 44 ring points), `HuntPoint` each.
- Re-run at the M7-3 head (machine at about 15 % CPU, the other tracks quieter): `EightHardSeats_MidWave` green (avg
  0.680 ms, p95 1.380 ms), `Brawl250v250_DesignBudget_FreshProcessShortRun` green (3.56 / 3.55 / 3.52 ms),
  `EmptyTick_2500OneOwner(zealot, True)` green (495.1 us), `ASweepRetarget` **red** (70.5 us). So three of the four follow
  the machine's load; the sweep row is red on identical code (fe6ca00 measures the same now), so its 16.4 us at M5-H6
  was the outlier or the machine has slowed since. Worth a QA look at the row on an idle machine before any re-bound.
- Verdict (rule 42): no re-bound (no regression to explain). Re-measure on an idle machine (no other track building), e.g.
  at the start of a single-track session; close as a machine state if green then.
- QA 2026-11-10-1236 (M7-3, full): the four alone after a 30 s idle, then rerun interleaved with a `git archive` of the
  diff base (dacd5b9) built in the scratchpad. The machine was **not** idle: 31-52 % CPU from non-studio apps (Spotify,
  Creative Cloud, Epic launcher, Chrome) with no other track's testhost running. All four red at both trees, the same
  numbers within noise:
  - `EightHardSeats_MidWave`: head 1.362 / 1.144 / 1.263 ms avg, base 1.167 / 1.120 ms (bound 1 ms),
  - `Brawl250v250_DesignBudget_FreshProcessShortRun`: head 5.46-6.50 ms, base 5.64-6.71 ms (bound 4),
  - `EmptyTick_2500OneOwner(zealot, True)`: head 574.4 / 572.0 / 575.0 us, base 575.3 / 588.2 us,
  - `ASweepRetarget`: head 79.6 / 78.0 / 79.8 us, base 78.0 / 79.9 us (bound 50).
  The whole Perf category on the head under the same load: 193 / 10 / 3; the six beyond these four (`AbilityPerfTests`
  500-unit zones, `EightAiSeats_Over2500Units`, `GatherPerfTests` 200 workers, `PropsMeasureTests` minimap 0.557 ms,
  `ZoneScalePerfTests` 64 blockers, `SimHardeningQaTests` 400 stacked) were rerun alone at head and base: base failed all
  six, head failed four and passed two. No M7-3 regression; allocation probes 0 B throughout. Still a machine state; the
  idle-machine re-measure stays open.
- Producer, ACCEPT 2026-11-10-1236: QA's cold rerun was made with **31-52 % CPU from the owner's own apps** (Spotify, Creative
  Cloud, Epic, Chrome), so rule 42's "alone" was not met and the numbers are not a verdict either way. Stays open as a
  machine-state record, no re-bound; the sim's next hardening (counter 4 / 4) measures once more with the PC idle and
  profiles `ASweepRetarget` only if it is still red then. An item for the owner is in STATE's "Wanted".
- Producer, ACCEPT 2026-11-11-0739 (M7-3b, QA's numbers): the dead session's Perf-alone run at about 00:xx on 2026-11-11 was
  **203 / 0 / 3**, every row green incl. these four. The same morning at ~09:00 with the PC idle (0-11 % load, only QA's testhost)
  Perf alone failed 15 and then 13 rows on the branch and 29 on de591f0, different rows each run, 9 min a run against 4 m 44 s;
  `ASweepRetarget` 82.4 us (branch) / 95.5 us (base) against 16.4 us when recorded. So the rows pass when the PC runs at full
  speed and fail when it runs slow, with no load to blame: a power or thermal state, not code and not the owner's apps alone.
  No re-bound. M7-H1 runs Perf once first: green means close this as machine state with the two runs as the record; red at
  base and head alike means record the numbers and move on (an owner item is in STATE's Wanted, item 8).

- QA M7-H1 (2026-11-11, 21:31-21:36, the sim branch at 1a49bbc on a scratch clone): `Category=Perf` alone, started after every test host and Godot (all tracks) had ended, CPU 3-5 % in the seconds before: **203 / 0 / 3 of 206 in 4 m 44 s**; `ASweepRetarget` 15.81 us (bound 50; the recorded speed), every-start variant 34.65 us. The developer's 191 / 12 / 3 the same evening (~18:45, idle, 85.5 us) and this green run on the same code bracket the pattern: the PC's slow state, not the code. QA agrees with closing it as machine state.
