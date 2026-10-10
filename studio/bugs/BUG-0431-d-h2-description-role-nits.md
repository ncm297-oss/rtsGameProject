# BUG-0431: D-H2 nits: a few description wordings bind a number to the wrong role; the bug index still lists BUG-0410 open

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-1015, task D-H2 |
| System | content tests (`Content/AbilityContentTests.DescriptionProblems` / `DurationRole` / `DescriptionNumber`), `studio/bugs/README.md` |
| Fixed by | |

No shipped description uses any of these wordings, so the shipped pin is correct today; they are gaps a future tooltip
could fall into. Numbers below are from QA probes in a scratch copy (Sandstorm: duration 12 s, cooldown 45 s, cast 1.2 s,
range 18 m, radius 6 m, zone statuses linger 1 s; Telas Fire: 10 magic damage a second for 4 s).

## Repro
Each line: `AbilityContentTests.WithDescription(key, text, mutant)` then `DescriptionProblems(Data, abilities, statuses)`.

1. **"cooldown:" with a colon binds to the duration.** Sandstorm "Slowed by 30%" -> "Slowed by 30% (cooldown: 12 s)":
   **0 problems** (a wrong cooldown passes, the BUG-0410 class). "(cooldown: 45 s)", the right number, fails:
   `'45 s' reads as a duration, but its data duration is 12`. The `before` regex in `DurationRole` allows
   `cooldown(s)? (of|is|in)?` then end, not a colon. "(cooldown 45 s)" and "45 s cooldown" work.
2. **Hyphenated durations have no role.** Sandstorm "for 12 seconds" -> "for 18-second": **0 problems** (the bare 18 matches
   the range through the any-role fallback). `DescriptionNumber` needs `\s?` then `s|seconds`, so "18-second" (and
   "18-metre") fall to the bag check.
3. **"dmg" is not a damage word.** Telas Fire "10 magic damage a second" -> "4 magic dmg a second": **0 problems** (4 is the
   duration, any role).
4. **Linger detection is per clause, both ways.** "Blinded for 1 second after a unit leaves." fails
   (`'1 second' reads as a duration`; only "after leaving / it leaves / they leave" are recognized), and "for 12 seconds and
   its effects linger after it leaves" fails (`'12 seconds' reads as a lingering duration`): the whole clause, not the
   number's own phrase, picks Lingering.
5. **Index row stale.** `studio/bugs/README.md` still lists BUG-0410 as `open` though its file says `fixed` (D-H2).

## Expected
Brief D-H2 / BUG-0410 fix: a number is checked against the field its words name; a wrong number in any of these common
wordings fails and the right one passes.

## Actual
As above: items 1-3 are false passes, item 4 false failures.

## Notes
Possible fix: allow `[:\s]*` after "cooldown" / "recharge" in the `before` regex; accept `-` as well as whitespace before the
unit word; add `dmg`; for linger, look only at the words between the previous clause end and the number plus the few words
after it ("after <anything> leaves"). Item 5 is a one-line index edit. Pinned by QA in
`QA/Content/DescriptionRoleQaTests` only for the wordings that work today; these probes are not permanent tests.
