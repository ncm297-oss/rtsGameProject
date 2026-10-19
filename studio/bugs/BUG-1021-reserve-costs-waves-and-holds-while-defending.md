# BUG-1021: With the reserve, 11 of 54 pairing sides send no wave (3 before); the reserve keeps starving the army while the base is under attack

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-18-1814, task M5-4g (QA) |
| System | AI: build-order reserve (`BuildOrderExecutor` / `AiPlayer.CanAffordAboveReserve` / `ProductionManager`) against the defense |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReservePairingsStressTests" --logger "console;verbosity=detailed"`
   (QA's `Stress/ReservePairingsStressTests`: the 9 pairings x their 1st-3rd smallest seeds, Normal v Normal, menu map,
   20 min; BUG-1010's six seeds; Easy and Hard on seeds 1-3). Every `ROW` line gives Age II, the first wave, the defeat,
   the peak army and the reserve's longest hold on one step with the thinks spent defending during it.
2. The same file on the diff base f531dd9 (reserve fields compiled out) for the before numbers.
3. The data-owned rows the brief names fail the same way: `Content/AiOpeningPairsReportTests` pairings (0,1) seed 2,
   (2,1) seed 7, (1,2) seed 12; `QA/Content/D21OpeningsQaTests.EveryPairing_SecondSeed_*` (0,0) seed 22, (2,0) seed 16.

## Expected
Brief criterion 3: "both sides still wave within 20 minutes on every pairing (D21 criterion 2, the existing named
allowances allowed)". docs/02 "AI opponent" puts the defense first.

## Actual
27 runs, 54 sides (independently measured; matches the developer's own counts exactly):

| | base f531dd9 | M5-4g |
| --- | --- | --- |
| sides alive at 20:00 without Age II | 23 / 41 | **0 / 43** |
| Malazan alive at 20:00, no wave | 1 (seed 19) | **3** (seeds 12, 22, 16) |
| Malazan defeated, no wave | 2 (seeds 8, 14) | **7** (2, 7, 10, 8, 6, 19, 14) |
| Whirlwind defeated, no wave | 0 | **1** (`holy_war` seed 4: wave 9:10 and alive before; no wave, defeated 12:50 now) |
| Malazan defeats | 8 | 8 |

Peak Malazan army on the no-wave seeds drops sharply, e.g. `crossbow_push` v `holy_war` seed 12: peak 61 pop / 3 waves ->
peak 10 / none; `standard` v `standard` seed 22: 39 / 5 waves -> 13 / none; `cadre_rush` v `standard` seed 16: 34 / 2 -> 10 / none.

BUG-1010's six seeds get worse, not better: all six Malazan sides send no wave and **all six are defeated** (before: five
defeated, `crossbow_push` seed 19 alive). Peak army 2-8 pop (before 6-12). Seed 19 is now defeated at 10:07 with a peak
army of 2.

Easy v Easy and Hard v Hard on seeds 1-3 (printed by the same file): Hard seed 3 Malazan `standard` had 4 waves and stood
at 20:00 before; now no wave, defeated 16:06 (Hard seed 2 Malazan sends no wave either way, defeated 12:45 before and
17:27 now). Easy seed 2 Malazan: 5 waves and standing before; 1 wave, defeated 17:41 now.

**Why (traced):** the reserve keeps holding while the base is being lost. The longest reserve hold on one step, per side:

- `holy_war` seed 4: step 21 Build 150 / 150 reserved from 8:46 to the defeat at 12:50, **245 thinks of it defending**;
  the bank never passes 42 / 115, so production may spend nothing below 150 / 150 the whole time (trains 31 -> 35 in four
  minutes, the army 27 -> 0 pop). Before that, the Age II reserve (400 / 200) froze production 7:00-7:45 as the Malazan
  wave (6:51) arrived.
- `cadre_rush` v `raider_rush` seed 18: Build 150 / 150 reserved 6:53 to the defeat at 12:21, 329 thinks defending.
- seed 23: Build 150 / 200 reserved 8:05 to the defeat at 12:34, 266 thinks defending.
- seed 8: Expand 275 / 275 reserved 8:55 to the defeat at 15:16, 305 thinks defending.
- `crossbow_push` seed 19: Age II 400 / 200 reserved 7:05-9:40, 156 thinks defending; defeated 10:07.
- Hard seed 2 Malazan: Build 150 / 0 (wood guarded out) reserved 9:20 to 17:27, 638 thinks defending, 833 gold banked at the end.

A defended base under a raid is exactly when army production matters most; holding a 150-400 gold reserve for a
building or an expansion the base can't use while it is being razed converts a beaten-but-alive side into a defeated one.

## Notes
- The developer reports criterion 3's wave half NOT met (STATUS PARTIAL) and attributes it to the openings' thresholds
  ("a data-track question"). The traces point at the reserve rule itself: it ignores `Board.Defending`. A sim-side lever to
  measure: no reserve (or the reserve dropped for the production manager) while `Board.Defending` holds, or while the
  army is under some fraction of the wave threshold. That is a design choice (docs/02 says "build order -> army"; docs/02
  also puts the defense first): the Producer's / owner's call.
- It is the reserve, not the gather shift: with `ai.reserveShift` 0 (the reserve kept) the same 27 runs give 12 sides with
  no wave and 15 defeats (11 and 11 with the shipped 0.25; BUG-1022).
- Slice 2 (counter-wave) was built and backed out at 0 / 6 per the developer; not inspected (not in the diff).
- Not a determinism problem: twins on all 27 runs equal every 50 ticks.
