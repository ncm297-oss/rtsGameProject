# BUG-0481: Data with a stealthed status and no revealed status loads clean; attacks and casts from stealth then never reveal

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-1954, task M4-5 |
| System | data loader (statuses), stealth |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~QA.StealthQaTests.StealthedWithoutARevealedStatus_IsRefusedAtLoad"`
   with the `Skip` removed.
2. The test copies the shipped data and deletes the `revealed` entry from `common/statuses.json`.

## Expected
The loader refuses it with a clear error (docs/02 "Stealth and detection": attacking or casting reveals for 3 s; the
QA rule "missing references ... rejected, never silently default"). The loader already refuses a *second* revealed status.

## Actual
`DataLoader.LoadAll` returns `Ok`. `World.RevealedStatus` is -1, so `StealthSystem.OnAttackOrCast` returns at once and a
stealthed unit can attack and cast for ever without being revealed. `rules.json` `stealth.revealSeconds` is still required
and loaded but has no effect.

## Notes
Not reachable with shipped data (it ships `revealed`). A one-line rule fixes it: when any status is of kind `stealthed`,
exactly one of kind `revealed` is required (error at `statuses`, naming the missing kind).
