# BUG-1200: M7-VH1 nits: a stale comment over `IsTipFormat`, "1 seconds", the Hard tip's wording changed

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (items 1-2: M7-V3a, 493f294, verified by QA; item 3 closed as not a bug, Producer ruling) |
| Found | 2026-11-10-2116, task M7-VH1 (QA, standard) |
| System | view: `game/scripts/UiText.cs`, `game/data/common/ui.json` (difficulty tips) |
| Fixed by | M7-V3a |

## Repro
1. `game/scripts/UiText.cs` above `IsTipFormat`: the old M5-V2 comment ("A difficulty tip format (M5-V2): it formats with
   one number, and with `named` it names it ...") is left in front of the new one; `named` no longer exists.
2. `res://tests/QaM7Vh1Test.tscn` prints `an Easy interval of exactly 1 s reads: 'Thinks every 1 seconds, ...'`. Any
   Easy or Hard `thinkInterval` that rounds to exactly 1 s (20 ticks) reads "1 seconds". None ships today (Easy 2, Hard
   0.5; Normal is 1 s but its tip doesn't name `{1}`). docs/03 "Implementation (M7-VH1)" names it as a known case.
3. The shipped Hard tip changed from "thinks every half second" to "thinks every 0.5 seconds" (`DifficultyTipTest`
   prints it). Criterion 3 says "shipped text reads as before". The brief's own `0.##` format makes this change
   unavoidable for Hard, so it is a note for the Producer, not a missed criterion.

## Expected
1. One comment that describes the method as it is now.
2. Player-facing text that is grammatical for every value the loader accepts, or a load rule or doc note that rules out
   the bad case.
3. The Producer agrees to the wording change.

## Actual
As above. Also by design: `thinkInterval` 0.333 reads "0.35" (7 ticks, the interval the AI really keeps; docs/03
"Implementation (M7-VH1)" records it).

## Notes
- **Producer ruling (2026-11-11-0739, resume of 2026-11-10-2116):** item 3 is accepted (criterion 3 let the wording come
  from the data); it is closed as not a bug. Items 1 and 2 stay open as this S4. Re-confirmed by QA in the resume: the stale
  comment is still at `UiText.cs` above `IsTipFormat`, and `QaM7Vh1Test` still prints "Thinks every 1 seconds".
- Cheapest fixes: for 1, delete the old comment line. For 2, `string.Format` has no plural form, so either word the
  tips so the number needs no plural (for example "every {1} s"), or note in docs/03 that an exact 1 s Easy or Hard
  interval needs a text change.

## Fix (M7-V3a, game-dev)
- Item 1: the stale M5-V2 comment line over `UiText.IsTipFormat` is deleted (one comment left, describing the method).
- Item 2 (Producer ruling): two new `ui.json` keys, `setup.seconds_one` = "{0} second" and `setup.seconds_other` =
  "{0} seconds" (each a load error unless a format naming `{0}`); `UiText.SecondsPhrase(seconds)` picks the first when
  `UiText.Seconds` reads exactly "1"; the tips' `{1}` receives the phrase ("Thinks every {1}, follows ...", "... thinks
  every {1}, focuses fire ...").
- Proof: `DifficultyTipTest` (shipped Easy and Hard tips equal the old strings "Thinks every 2 seconds, ..." and "... thinks
  every 0.5 seconds, ..."; forged Easy / Hard intervals of 20 ticks read "every 1 second,", 19 "every 0.95 seconds,", 21
  "every 1.05 seconds,"); `QaM7Vh1Test` now checks "Thinks every 1 second, " (was a printed "1 seconds" note).
