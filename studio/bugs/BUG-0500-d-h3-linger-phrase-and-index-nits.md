# BUG-0500: D-H3 nits: per-phrase linger misses a linger word after the number (a base pass now fails); BUG-0470 indexed fixed with item 3 open

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D12, session 2026-10-10-2334; verified by QA) |
| Found | 2026-10-10-1954, task D-H3 |
| System | content tests (`Content/AbilityContentTests.DurationRole`, `LingerBefore` / `LingerAfter`), `studio/bugs/README.md` |
| Fixed by | D12 8fbede1: `LingerBefore` up to four words, `LingerAfter` accepts the linger noun right after the number; regression `QA/Content/DescriptionWordingQaTests.LingerPhrasings_ThatPassedAtTheBase_StillPass` un-skipped (3 rows) + `Content/AbilityContentTests.BUG0500_ALingerWordAfterTheNumberOrFourWordsBefore_ReadsAsLingering` (7 rows, wrong number still fails); item 3 resolved by setting BUG-0470 to fixed (STATE.md line 280 carries the D11 text, fixed at the 1314 ACCEPT) |

No shipped description uses these wordings: the shipped pin is correct and `DescriptionProblems` is 0 on shipped text.

## Repro
1. Run `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~DescriptionWordingQaTests` with the `Skip` removed from
   `QA/Content/DescriptionWordingQaTests.LingerPhrasings_ThatPassedAtTheBase_StillPass`. Each row appends a sentence to
   Sandstorm's description after "Your own units are unaffected." (zone statuses linger 1 s, zone life 12 s):
   - " A 1 s linger after leaving." -> `'1 s' reads as a duration, but its data duration is 12`
   - " A 1-second linger after leaving." -> `'1-second' reads as a duration, but its data duration is 12`
   - " The effects linger on a unit for 1 s." -> `'1 s' reads as a duration, but its data duration is 12`
2. Same three rows on the diff base dbcc70d: all pass (the clause search found "linger"; "1-second" fell to the any-role
   bag, where 1 is a data number).
3. `studio/bugs/README.md` row BUG-0470 reads `fixed`, while the bug file says item 3 (`studio/STATE.md`) is left for
   the Producer.

## Expected
BUG-0440 item 4 / the D-H3 brief: the linger role read per sentence; the right number in a common linger wording passes.
The index row reflects the file's status.

## Actual
`LingerBefore` only looks back at most two words before the number and `LingerAfter` needs "after" right after the number,
so a linger noun after the number ("a 1 s linger") or three words in between ("linger on a unit for 1 s") reads as the
storm's duration. The right number now fails where it passed before: a false failure, loud rather than silent. The
developer swapped "per sentence" for "per phrase" on purpose (so "for 12 seconds and its effects linger after it leaves"
reads as the duration) and said so in the report.

## Notes
Possible fix: also accept `^[\s-]*linger` right after the number, and allow up to three words in `LingerBefore`. Or
treat a sentence containing "linger" as lingering unless the number is preceded by "for" right after the ability's verb.
For item 3: write `fixed (item 1); item 3 open (Producer)` in the index row, or have the Producer fix STATE.md.
