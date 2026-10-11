# BUG-0482: A stealthed attacker that already carries 8 statuses is never Revealed (the reveal is dropped as a ninth status)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-10-1954, task M4-5 |
| System | stealth (`StealthSystem.OnAttackOrCast`, `StatusStore.Apply`) |
| Fixed by | M4-H3 (sim): a reveal always fits |

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

## Fix (M4-H3, 2026-10-11)
`StealthSystem.OnAttackOrCast`: on a unit already carrying `StatusStore.PerUnit` (8) statuses and no Revealed, the reveal
replaces the oldest entry that is not a Stealthed one (`StatusSystem.RemoveEntry`, which recomputes the speed for a slow and
the blind in force for a blind). The one exception to "a ninth status is dropped" (docs/03 "Implementation (M4-4a)"
Statuses, "Implementation (M4-5)" Reveal). QA's `AStealthedAttackerWithAFullStatusStore_IsStillRevealed` un-skipped and green.
