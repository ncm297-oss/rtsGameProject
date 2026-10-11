# BUG-0482: A stealthed attacker that already carries 8 statuses is never Revealed (the reveal is dropped as a ninth status)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-1954, task M4-5 |
| System | stealth (`StealthSystem.OnAttackOrCast`, `StatusStore.Apply`) |
| Fixed by | |

## Repro
1. `QA/StealthQaTests.AStealthedAttackerWithAFullStatusStore_IsStillRevealed` (remove the `Skip`): a stealthed Heavy
   Infantry carrying 7 test-only slows (8 statuses, `StatusStore.PerUnit`) lands a melee hit on an enemy worker.

## Expected
docs/02 "Stealth and detection": attacking or casting from stealth reveals the unit for 3 s, always.

## Actual
`StatusStore.Apply` drops the Revealed application as a ninth status (`if (n >= PerUnit) return false;`), so the attacker
stays stealthed while it attacks: no enemy sees, targets or retaliates against it.

## Notes
Not reachable with shipped data: six status ids in all, so a unit carries at most six. An M8 edge (Shadow content plus
more statuses). Fix options: Revealed always fits (it replaces the oldest non-stealth entry, or the store keeps a slot for
it), or docs/03 "Implementation (M4-5)" states the exception.

## Producer triage (2026-10-10-1954 ACCEPT)
S4 confirmed (six shipped status ids, so unreachable until M8 adds more). **End-of-M4 sim hardening** (M4-H3) if a few lines: a reveal
replaces the oldest non-stealth entry when the store is full (document the exception in docs/03 either way); un-skip the QA row or keep
it skipped with the documented limit.
