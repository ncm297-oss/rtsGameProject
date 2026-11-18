# BUG-1580: M8-2 adds three new permanent pins of the shipped data (hash and save bytes); D33 turns them red on merge this session

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-17-0816, task M8-2 |
| System | tests (cross-track merge hazard): `Data/StillStealthLoaderTests`, `Vision/StillStealthSaveBytesTests` |
| Fixed by | |

## Repro
1. Fresh clone of `studio/2026-11-17-0816-sim` (b3a18bf) into a scratch folder.
2. Change one shipped number: `game/data/factions/malazan/units.json`, `malazan_crossbowman` `trainTime` 16 -> 17 (a
   change that D33, this session's balance pass, makes many of).
3. `dotnet build sim/Rts.Sim.Tests`, then
   `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~StillStealthLoaderTests|FullyQualifiedName~StillStealthSaveBytesTests|FullyQualifiedName~TheShippedContentHash_IsTheGoldenHeaders|FullyQualifiedName~TheShippedDataHash_IsTheGoldenHeaders"`

## Expected
Rule 51 and the session plan: when a data task moves the hash, the data developer is handed the sim-owned data-hash
pins **by name** (this session: the golden's header, `FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`,
`QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`), and "two tracks never move the hash in one session". A sim task
that ships test data only should not add new pins that the other track's planned hash move breaks (rule 58's spirit:
sim test data tolerates data-track changes).

## Actual
5 failed / 3 passed. Besides the two known golden pins, three **new** M8-2 rows fail:
```
Failed StillStealthSaveBytesTests.ASaveOnTheShippedData_IsTheBytesMainWrites
  Expected: "094A8AE06379ACEC8FDE745767CD50B9649D80BD1"...   Actual: "1212C4A3E9EFE98F9067F0DE6FD7DC31C0D9BBEE0"...
Failed StillStealthLoaderTests.TheHash_IsUnchangedWithoutTheField_MovesWithIt_AndWithEachField
  Expected: 13208432833924990509 (0xB74DC7AE61B4862D)        Actual: 1762811529505015569
Failed StillStealthSaveBytesTests.ASaveOnTheForestCopy_IsTheBytesMainWrites
  Expected: "788743CA6E1EE1E5587D3B2997AAA11599E320C52"...   Actual: "5FE14F2658B2FBD237A0C27F52B9942ED13A9B940"...
```
D33 moves the data hash this session (handoff "Current session plan", data track). Whichever of the two merges second,
`main`'s default loop goes red with these three rows, and nobody on the data track was told they exist. The two save
digests also move with any future sim change to combat or movement (they pin a 300-tick brawl's bytes), so they act as
a second golden that no brief names.

## Notes
- The criteria (2 and 10, third row) needed checking **against base 10acb53 this session**, which these rows did. As
  permanent pins they are a maintenance trap.
- Cheap fixes (the Producer's call): (a) compare the shipped hash with the value the golden pins already hold (one
  constant, so rule 51's hand-over covers it) instead of a second literal; (b) turn the save-bytes rows into a
  comparison that doesn't depend on the data: e.g. the same brawl saved with `World.Units.Still` forced off vs the
  normal path on data without the field, or a structural check (no `STIL` tag, part list identical to a format-10 save on
  the same data); or (c) keep them and add all three to the rule-51 hand-over list for D33 by name, so the data
  developer re-pins them in the same commit.
- Severity S2 because the session plan makes the red certain, not hypothetical (compare BUG-0124). The Producer may
  re-triage to S3 if the conductor re-pins them at the merge.
