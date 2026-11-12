# BUG-1190: M7-3b nits (seed21 recording's Research ids, seventy-techs hash loop stops below the top ids)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (M7-H1, 1a49bbc, verified by QA and by the Producer at the 2026-11-11-1317 ACCEPT) |
| Found | 2026-11-10-2116, task M7-3b |
| System | tests (replay fixture, tech data QA) |
| Fixed by | M7-H1: (1) new `sim/Rts.Sim.Tests/Seed21ResearchTests` asserts the recording holds Research (4348, 0) and (5556, 4), and that the click at 5556 is refused (`ResearchRefused`) and tech 4 never applies, so the dependency is explicit; (2) `TechDataQaTests.SeventyTechs_*`'s hash loop runs `t = 60 .. d.Techs.Length - 1` (ids 70 and 71 included), on a copy cut to the two shipped factions (BUG-1191) |

## Repro
1. `sim/Rts.Sim.Tests/Replays/seed21_playable.replay`: `awk '$1=="c" && $5==15' seed21_playable.replay` shows two
   Research commands, `c 4348 0 30 15 0 ...` (age_ii either way) and `c 5556 0 31 15 4 ...`. Before M7-3b tech 4 was
   `melee_weapons_1`; on the branch it is `melee_weapons_2`.
2. `sim/Rts.Sim.Tests/QA/TechDataQaTests.SeventyTechs_UseASecondFlagWord_AndEveryFlagIsHashed_AndResearchable`: the
   hash loop runs `t = 60 .. 69`; the data now has 72 techs (63 extras), so the two highest ids (70, 71, the two faction
   techs, in flag word 1) are not hash-checked.

## Expected
1. A recording replayed under data it was not recorded with names the techs it named when it was recorded; the
   recording is the only one in the suite that skips the data-hash check (M6-SH1), so nothing refuses it.
2. "Every flag is hashed" covers the top of word 1, as it did before the edit (ids 64-69 were the last six then).

## Actual
1. The row passes because the click at tick 5556 was refused on both trees (all 7,682 checkpoints hold, and the tech
   flags are hashed by id, so no research by that id succeeded either way). The developer's report and the
   `GatherWedgeQaTests` comment say so; the file's meaning still changed silently, and a future data edit that lets
   that click succeed would make the row fail for a reason that has nothing to do with gathering.
2. The loop's range wasn't moved with the count: ids 70 and 71 are never set and hashed in this test (the bitset code is
   generic, so this is coverage, not a defect).

## Notes
Fix suggestions: (1) re-record `seed21_playable.replay` the next time it must be re-recorded anyway, or have the row
assert the click at tick 5556 is refused, so the dependency is explicit; (2) run the hash loop to `d.Techs.Length`.
Neither moved an outcome: QA's `OutcomePinM73bQaTests` and the 42-match tally are identical to de591f0.
