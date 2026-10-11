# BUG-0481: Data with a stealthed status and no revealed status loads clean; attacks and casts from stealth then never reveal

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-10-1954, task M4-5 |
| System | data loader (statuses), stealth |
| Fixed by | M4-H3 (sim): the statuses loader rule |

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

## Producer triage (2026-10-10-1954 ACCEPT)
S3 confirmed (a silent default, against the loader rule); not reachable with shipped data. **End-of-M4 sim hardening** (M4-H3): the
one-line rule (a `stealthed` kind requires exactly one `revealed` kind, error at `statuses`), a `Data/StealthLoaderTests` row, the QA row
un-skipped, docs/03 "Implementation (M4-5)" data bullet.

## Fix (M4-H3, 2026-10-11)
`DataLoader.BuildStatuses`: when any status is of kind `stealthed`, exactly one of kind `revealed` is required; the error is
at `common/statuses.json` `statuses`, naming the stealthed status and the missing kind. The shipped data minus `revealed`
now gives exactly one error (`Data/StealthLoaderTests.AStealthedStatusWithoutARevealedOne_IsRefused_AtStatuses_WithOneError`;
data with neither kind loads). It also exposed a second, misleading error, now gone: with `statuses.json` failed, a zone
status written without a `magnitude` (Sandstorm's `blinded`) was reported "missing required field"; the magnitude check now
defers to the statuses file's own error. QA's `StealthedWithoutARevealedStatus_IsRefusedAtLoad` un-skipped and green.
docs/03 "Implementation (M4-5)" data bullet.
