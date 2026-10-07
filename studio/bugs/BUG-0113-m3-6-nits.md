# BUG-0113: M3-6 nits: type-mismatch errors read "malformed JSON ... Nullable`1[Int32]", the 10k-unit load test now times a failing load

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-07-1415, task M3-6 |
| System | data loader, QA load test |
| Fixed by | |

## Repro
1. In `common/techs.json` set `age_ii.requiresAnyOf.count` to `"2"`, `2.5` or `1e10`. The one error reads:
   `common/techs.json: $.techs[0].requiresAnyOf.count: malformed JSON at line 11, byte 38: The JSON value could not be
   converted to System.Nullable`1[System.Int32]. Path: ... | LineNumber: 10 | BytePositionInLine: 37.`
   The file is well-formed; the value has the wrong type. (Same for every int field; the new field inherits it.)
2. `QA/DataLoaderQaTests` 10k-unit load (Perf): since BUG-0010 (one unit per template slot) the 10,000 bulk copies
   are each a slot error, so the test now asserts 10,000 errors and times a load that never builds a `GameData`. The
   scale of `GameData` construction, `ContentHash` and the per-building tables over 10k units is no longer measured.

## Expected
1. A message an author can act on: "expected a whole number" at `techs[0].requiresAnyOf.count`, without CLR type names.
2. A valid large load is timed: e.g. 1,430 generated factions x 7 units (needs `faction.json` per folder), or the
   10k units spread over slots with the slot check measured separately.

## Actual
As above. Neither is wrong behaviour; both are polish.

## Notes
- Also noted for the view (not a bug, the brief chose this order): `CanResearch` reports `Requires` ahead of
  `AlreadyResearched`, so a researched Age II whose halls are later lost reads "Locked" rather than "Researched" if the
  card asks `CanResearch` for a done tech.
