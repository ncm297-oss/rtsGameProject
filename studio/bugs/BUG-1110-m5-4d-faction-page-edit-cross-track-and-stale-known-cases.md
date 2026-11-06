# BUG-1110: M5-4d edits the data track's `docs/factions/malazan.md` (D-H7 edits the same file this session) and leaves its seed 19 / 25 text and both `KnownBeatenAlive` lists stale

| Field | Value |
| --- | --- |
| Severity | S2 (track ownership: `docs/factions/**` is the data track's, producer.md "Tracks"; the Producer may judge the re-print brief-sanctioned, but the stale text below needs a data-track re-print either way) |
| Status | open |
| Found | 2026-11-06-0926, task M5-4d (QA full; conductor question (b)) |
| System | docs / content tests: `docs/factions/malazan.md`, `QA/Content/D21OpeningsQaTests.KnownBeatenAlive`, `QA/Content/DH6ThresholdQaTests.KnownBeatenAlive` |
| Fixed by | |

## Repro
1. `git diff 1e9e1e8...b7a3f83 -- docs/factions/` (sim branch): the Age II paragraph + two table cells re-printed (`crossbow_push` v `raider_rush`
   seed 19 "never (defeated 10:24)", v `standard` seed 32 "never (defeated 14:48)").
2. `git -C .claude/worktrees/studio-data diff 1e9e1e8 -- docs/factions/malazan.md` (D-H7, data branch, same session): edits the same page,
   including the `crossbow_push` v `raider_rush` paragraph ~10 lines above the sim's hunk: "re-measured on 2026-11-06 ... both still fail it
   with the same numbers, so both entries stay".
3. On the sim branch: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DH6ThresholdQaTests|FullyQualifiedName~D21OpeningsQaTests.EveryPairing_ThirdSeed"`.

## Expected
A sim task leaves `docs/factions/**` to the data track (or the brief names the file); after the merge every sentence of the page matches the runs.

## Actual
- Step 3 passes (12 / 12) but prints, for both known cases:
  `the known case seed 19 player 0 now passes the rule: take it out of KnownBeatenAlive (BUG-1010)` (Malazan `crossbow_push` defeated 10:24,
  876 thinks defending, 0 pop at 20:00) and `the known case seed 25 now passes the rule: take it out of KnownBeatenAlive (BUG-1010)`
  (defeated 12:34). So the lists don't fail; they are just stale (and would hide a regression back to "beaten alive").
- `docs/factions/malazan.md` (the `crossbow_push` v `raider_rush` paragraph, sim branch and main): "both are still standing at 20:00, beaten
  down: on seed 19 ... at 20:00 it has 15 pop with a cap of 24 ... on seed 25 it ... is down to 1 pop at 20:00": false since M5-4d (both
  defeated). D-H7's added sentence ("both still fail it ... so both entries stay") becomes false the moment the branches merge.
- The two branches' hunks in `malazan.md` are separate (no textual conflict expected), so the contradiction would merge silently.

## Fix
Data track (next data task, or the conductor at the merge if the Producer prefers): re-print the `crossbow_push` v `raider_rush` paragraph
(seeds 19 / 25 defeated 10:24 / 12:34 without a wave: BUG-1010's "falls without attacking" still holds, "standing at 20:00" does not), drop
D-H7's "so both entries stay" sentence, and take seed 19 / 25 out of the two `KnownBeatenAlive` lists (data track's test folder).
