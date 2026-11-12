# BUG-1231: Three view-owned scenes are red once the real Teblor roster is in the shipped data

| Field | Value |
| --- | --- |
| Severity | S3 (blocks D28's landing with BUG-1222: `main`'s scene loop would go red; nothing plays wrong) |
| Status | fixed (M7-V3b, d57a7f2, session 2026-11-11-2256; verified by QA and on the Producer's three-way scratch clone) |
| Found | 2026-11-11-1317 ACCEPT (Producer, the scene loop on the three-way merged scratch clone) |
| System | view tests: `game/tests/QaM7V3aTest`, `game/tests/ThreeFactionTest` (+ `ThirdFactionForge`), `game/tests/ReplayPlaybackTest` |
| Fixed by | M7-V3b: `ThirdFactionForge` cuts its copy to the order's first two factions (`ShippedKept`) before adding `third`; `QaM7V3aTest` forges `MaxFactions - shipped` clones (`qf<index>`; long = first, regen = third, the 4-seat match the first and last); `ReplayPlaybackTest`'s 3-opening fixture and its check are Malazan's and Whirlwind's (`FixtureFactions`), the four-seat row pinned to them |

## Repro
1. A scratch clone of `studio/2026-11-11-1317-sim` (de0a2a6) with `studio/2026-11-11-1317-view` (ceda412) and
   `studio/2026-11-11-1317-data` (5aa5e1c) merged in that order (copy the LFS assets in, or the import hangs: BUG-0860).
2. `powershell -File tools/qa/smoke.ps1` (PASS), then `powershell -File tools/qa/scene-loop.ps1`.

## Expected
The scene loop is green with a third faction in the shipped data (96 / 96 on sim + view without the data branch).

## Actual
`SCENE LOOP FAIL: 3 of 96: QaM7V3aTest, ReplayPlaybackTest, ThreeFactionTest`:
- `QaM7V3aTest`: "forged data failed to load: common/factions.json: order: 9 factions is above the maximum 8" (the scene
  forges six more factions on top of the shipped ones to reach `DataLimits.MaxFactions`; with three shipped it makes nine).
- `ThreeFactionTest`: "forged factions: malazan, whirlwind, teblor, third" and "the added third slot: count 3, faction 2
  (want 3, s mod factions)" (the forge appends a faction to whatever ships and the scene expects exactly three).
- `ReplayPlaybackTest`: "the fixture holds not three openings per faction" (`ThreeOpenings()` asserts every faction's
  `ai.json` has three openings; the Teblor ship one, `standard`).

## Notes
- The M7-V3a brief staged everything on "two shipped + one forged"; the real roster makes that three + one. Same family as
  BUG-1222 on the sim side.
- Means that fit: the forge cuts its copy to the first two factions of the order before adding its own (as the sim's
  `TestDataDir.CopyOfShippedTwoFactions` does), or counts from what ships (`MaxFactions - shipped`); `ReplayPlaybackTest`'s
  fixture reads the two factions it was written for (or "at least one opening, three for the factions that ship three").
- Proof: the scene loop green on a scratch clone with the data branch merged, and green on the branch itself.

## Proof (M7-V3b, developer)
- Scratch clone of `studio/2026-11-11-2256-view` (b54dbeb) + the brief's recipe from 5aa5e1c (`TeblorPageParseQaTests.cs` deleted), LFS
  assets copied in: smoke PASS; the three scenes with base b54dbeb's files: `QaM7V3aTest FAIL` ("order: 9 factions is above the
  maximum 8"), `ThreeFactionTest FAIL` ("forged factions: malazan, whirlwind, teblor, third"; "the added third slot: count 3,
  faction 2 (want 3, s mod factions)"), `ReplayPlaybackTest FAIL` ("the fixture holds not three openings per faction"): the repro.
- Same clone with the fix: all three PASS (QaM7V3aTest's note "3 factions ship; clones qf3, qf4, qf5, qf6, qf7 (long qf3, regen
  qf5)"); full scene loop 93 / 96 with two loops running at once, the three red rows timing-only (`BenchTest` 9.5 s > 7 s,
  `DebugOverlayTest` 1.172 ms > 1 ms, `MinimapTest` 0.879 ms > 0.5 ms), each PASS re-run alone: 96 / 96.
- The branch itself (two factions): smoke PASS; scene loop 95 / 96 under the same load, `MinimapTest` (0.860 ms > 0.5 ms)
  PASS re-run alone: 96 / 96.
