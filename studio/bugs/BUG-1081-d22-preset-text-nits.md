# BUG-1081: D22 preset-text nits (the checker misses a bare faction word; the text's width is unchecked until M6-V17)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (item 1 D23 2026-11-05-2148; item 2 M6-VH5 2026-11-06-0926: `TooltipWrap.Wrap` pre-wraps every description tooltip at 60 characters, `TooltipWrapTest` + QA's `QaVh5Test` fuzz; closed by the Producer) |
| Found | 2026-11-05-1716, task D22 (QA, light) |
| System | sim/Rts.Sim.Tests/Content/StartingResourcesContentTests.cs; game/data/common/rules.json `startingResources[*].description` |
| Fixed by | item 1: D23 (`Content/FactionWords`, shared with `Content/MapTextContentTests`; `StartingResourcesContentTests.ProperNames_HoldTheBareFactionWords` and the `AMutantDescription_IsCaught` rows "a Malazan quartermaster" / "a Whirlwind war chest") |

## Repro
1. Read `StartingResourcesContentTests.ProperNames`: it lists whole display names ("Malazan Empire", "The Whirlwind",
   unit / building / tech / ability / status names) and matches each as a whole phrase.
2. Feed `Problems(data, "high", "Every player starts like a Malazan quartermaster.", 1000, 1000)` or
   "... a Whirlwind war chest ...": no problem is reported ("Malazan Empire" / "The Whirlwind" don't match the bare word).
   Ratio words outside its list ("fivefold", "quintuple", "a dozen") also pass.
3. The two shipped descriptions are 154 and 164 characters. The setup's controls are 240 px wide (`SkirmishSetup`
   `CustomMinimumSize 240`); 164 characters is roughly 5 wrapped lines at 240 px, or one unwrapped Godot tooltip of
   roughly 1,100-1,200 px at the default font, about the width of the 1152 x 648 window.

## Expected
The brief: "no Malazan proper nouns" (a bare "Malazan" is one); the QA focus: the description fits ~240 px / one tooltip
line.

## Actual
1. The shipped text is clean (QA's `QA/Content/D22PresetTextQaTests.ShippedDescriptions_NameNoFactionWord` checks faction
   ids and the capitalised words of the faction display names and passes), but the dev's checker would not catch a
   future edit that adds a bare faction word.
2. The brief's 200-character limit and "one tooltip line at ~240 px" can't both hold; the shipped texts follow the
   200 limit (the longest AI opening description, `cadre_rush`, is 153 and is shown as a tooltip on the stats screen).
   Whether the list wraps or clips is the view's call in M6-V17; check it on the merged tree after view lands.

## Notes
- Fix for 1 (data track, test-only): add each faction's id and the words of its display names to `ProperNames`, plus a
  mutant row "a Malazan quartermaster".
- 2 is a deferred check, not a data defect: if M6-V17 shows the description unwrapped, either it wraps or the text is cut
  to about 60 characters.
- D23 (2026-11-05-2148): item 1 fixed. `ProperNames` now starts from `FactionWords.All` (each faction id and the
  capitalised words of four letters or more of its display and bonus names, "Army" excepted as a generic word); the
  ratio words "fivefold" / "quintuple" / "a dozen" were not part of the fix's note and are not added.
