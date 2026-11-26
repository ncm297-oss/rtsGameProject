# BUG-1770: A `null` or `[]` entry in `buildOrders` also counts toward ruling (a), so the file gets a second, misleading error

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-26-0207, task M7-9 |
| System | data loader (`ai.json` `buildOrders`) |
| Fixed by | |

## Repro
1. In a copy of the shipped data, give the Malazan `standard` opening
   `"buildOrders": { "easy": [ <a valid step> ], "normal": null, "hard": [ <a valid step> ] }`
   (or `"normal": []`).
2. `DataLoader.LoadAll(dir)`.
   Test: `QA/M79PerDifficultyLoaderQaTests.MixedValidAndInvalidEntries_ReportOnlyTheBrokenOnes` (prints the errors).

## Expected
Ruling (b): an entry that is `null` or `[]` is one error at `openings[i].buildOrders.normal`. The file has two real
entries, so ruling (a) ("an entry for every difficulty") does not apply.

## Actual
Two errors:

```
openings[0].buildOrders.normal: null is not a build order (leave the key out for a normal seat to play buildOrder)
openings[0].buildOrders: an entry for every difficulty: buildOrder could never be played (write one of them as buildOrder)
```

The second tells the author to move a list into `buildOrder`, while fixing the first error the way it suggests (leaving
the key out) clears both. The file is rejected either way, so nothing wrong loads.

## Notes
`DataLoader.BuildAiBuildOrders` counts `entries++` for every key that is present, before it checks for `null` / `[]`.
Counting only entries that hold steps (or skipping the ruling (a) check when an entry already errored) gives one error.
Polish only: the loader already rejects the file.
