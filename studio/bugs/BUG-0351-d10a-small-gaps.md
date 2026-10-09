# BUG-0351: D10a small gaps: no "For your review" table, odd Duration message

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-09-1155, task D10a |
| System | content tests / studio records |
| Fixed by | |

## Repro
1. Brief scope item (5) asks for "the For your review table"; the developer report says it was not
   written. Nothing in the diff (7a9a0ac) adds one.
2. Scratch clone: add `"duration": 6` to `telas_fire` in abilities.json and run
   `AbilityContentTests.EveryLoadedAbility*`.

## Expected
1. A review table for the owner (Telas Fire's shipped text, the Duration column added to malazan.md).
2. A message like "page '—', data 6 s".

## Actual
1. Missing.
2. `'Telas Fire' Duration: page '—' lacks the unit 's', data 6` (correct ability and field, but the
   wording reads as a formatting error rather than a value mismatch).

## Notes
Both cosmetic; the pin itself catches the mutation.
