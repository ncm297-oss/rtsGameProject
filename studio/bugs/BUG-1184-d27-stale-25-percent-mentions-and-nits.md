# BUG-1184: D27 nits: docs/03 still states Hard's 25% in four places; the pre-map replay fixture's "recorded on ab54ddb" comments; mojibake arrows in the BUG-1163 patch; test edits outside the brief's file list

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (items 1-3, D27 fix round 1, 6b72729; item 4 is a brief-template note for the Producer; verified by QA re-check round 1) |
| Found | 2026-11-10-0146, task D27 (QA standard) |
| System | Docs (`docs/03-technical-design.md`), test comments (`Save/PreMapFixtures`, `ReplayFormatTests`, `Ai/HuntTests`), the developer's BUG-1163 patch |
| Fixed by | 6b72729 (D27: fix QA findings (round 1)) |

## Repro
1. `docs/03-technical-design.md` on 3d25e9d, after D27 moved Hard's `gatherBonus` to 0.4:
   - line ~7060 (M5-V2): quotes docs/02 as "a 25% gather bonus" (docs/02 now reads 40%);
   - line ~7212 (the `ai.json` schema example): `"hard": { ... "gatherBonus": 0.25, ...` (the shipped value is 0.4);
   - line ~7786 (M5-4c slice 2 header): "docs/02 'AI opponent': Hard '... plus a 25% ...'";
   - line ~7941: "the difficulty table (still true: Hard 25%, focus fire yes, ...)".
   The developer flagged two of these as history. The schema example and the "still true" claim read as current facts.
   `docs/01-vision.md` line 218 (the 2026-11-07 decision row, "a 25% gather bonus") also has no D27 row yet (developer left it
   for the Producer, as the brief allows).
2. `sim/Rts.Sim.Tests/Replays/premap/replay_ai_ffa_standard.replay` (seat 1 Whirlwind AI Hard) was re-recorded in D27
   (commands 84 -> 81) and before that in M5-4c-2 (947a273). Its users still say it was recorded on ab54ddb:
   `Save/PreMapFixtures.cs` lines 14 / 33, `ReplayFormatTests.cs` line 931, `Ai/HuntTests.cs` line 633
   ("a recording made before M5-4d ... recorded on ab54ddb"). The re-recording is not mentioned in any comment or in the
   docs/03 D27 note, unlike the golden's header.
3. The developer's BUG-1163 append patch (scratchpad `d27/BUG-1163-append.md`) has the arrows as mojibake (`0.25 â†’ 0.4`,
   UTF-8 read as Windows-1252). Applied as is, BUG-1163's file gets garbage characters.
4. The diff edits sim-owned test folders (`Ai/`, `Economy/`, `Data/`, `Vision/`, `QA/`, `Replays/`) that the brief's
   "Files (the only ones)" list does not name. Every edit is a Hard-seat re-pin (checked: `ReserveTwinTests` rows each have a
   Hard seat; `ai_seed4_4seats_Normal` seat 1 is Hard; the scout row is a Hard seat; the premap replay's seat 1 is Hard) or
   the golden header, which the brief's Scope requires, so the brief's file list and its Scope contradict each other.
   Not a track breach in substance; recorded so the Producer's next Hard-tuning brief lists the pin folders.

## Expected
Current-fact sentences in docs/03 say 0.4 / 40% (or are dated as history); the fixture comments say when the replay was last
re-recorded and why; the patch in UTF-8 arrows (or ASCII `->`); the brief's file list names the Hard-pin test folders.

## Actual
As above. Nothing fails.

## Notes
Docs / comments only. Fold into the next data-track hardening (items 1, 3) and the Producer's brief template (item 4);
item 2 is a sim-track test comment.

## Re-check round 1 (2026-11-10-0146, QA)
1. docs/03: the schema example reads `"gatherBonus": 0.4`; the field description reads "Hard 0.4 since D27, 0.25 before it";
   the M5-V2 and M5-4c slice 2 quotes are marked "25% before D27, 40% now"; line ~7943 reads "still true then: Hard 25% (40%
   since D27)". `grep -n "25%" docs/03-technical-design.md` leaves only those dated mentions. The docs/01 decision row is still
   the Producer's (allowed by the brief).
2. `Save/PreMapFixtures.cs` (class summary: the exception for `replay_ai_ffa_standard.replay`, re-recorded in M5-4c slice 2
   947a273 and D27, header byte for byte ab54ddb; the save digests "never re-recorded"), `ReplayFormatTests.cs` and
   `Ai/HuntTests.cs` now say so. Checked against `git log` of the fixture (69d5673, 947a273, 3d25e9d) and the D27 diff
   (`commands 84` -> `81`, header lines unchanged).
3. The BUG-1163 append patch (scratchpad `d27/BUG-1163-append.md`) is pure ASCII now (`->`; 0 non-ASCII bytes).
4. Not a code fix: still a note for the Producer's next Hard-tuning brief (list the Hard-pin test folders).
- Also: the old test name `TheScout_LeavesAtScoutAfter_*` has no remaining reference outside the first QA report.
