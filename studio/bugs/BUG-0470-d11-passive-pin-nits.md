# BUG-0470: D11 nits: a second same-trigger passive sentence on a page is never compared; docs/03 and STATE.md still quote the pre-D11 text

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-1314, task D11 |
| System | content tests (`Content/PassiveContentTests.Compare`), docs/03 "Implementation (M4-4b-3a)", `studio/STATE.md` |
| Fixed by | |

Nothing shipped is wrong today: the shipped page and data agree. These are a gap in the new pin and two stale quotes.

## Repro
1. **A contradicting second sentence passes.** `QA/Content/ZealotPassivePinQaTests.ASecondSameTriggerPageSentence_WithOtherNumbers_Fails`
   (skipped with this id). Append to the whirlwind.md Zealot bullet:
   `Passive **Last Rites**: when a Zealot dies, Zealots within 12 m gain Frenzied (+90% attack speed) for 9 s.`
   then `PassiveContentTests.Compare(Data, "whirlwind", lines)`: **0 problems** (checked with a probe in this session).
2. docs/03 "Implementation (M4-4b-3a)", the `onDeath` bullet: "Frenzied +50 % for 5 s (the 0.5 is a Producer default: the
   faction page gives none)". Since D11 the page says "(+50% attack speed)" for Martyrdom.
3. `studio/STATE.md` lines 81, 256, 302 quote the old Frenzied ("Attacks faster until it wears off. ...") and Blinded
   ("can't target anything ...") descriptions.

## Expected
1. The data allows one passive per trigger (`DataLimits.MaxUnitPassives`, `BelowHpPassive` / `DeathPassive`), so two page
   sentences of the same trigger for the same unit can't both be true: the second should fail (or be compared, and fail on
   its numbers).
2-3. The docs and STATE say what the page and data say now.

## Actual
1. Page -> data accepts any sentence whose unit has *a* passive of that trigger (`of(u).Any(p => p.Trigger == trigger)`), and
   data -> page compares only the first matching sentence (`page.FirstOrDefault(...)`), so the second one's numbers are
   never read.
2-3. Stale text (outside the data track's files this session; the developer's report flagged both).

## Notes
Fix for 1: in `Compare`, count page sentences per (unit, trigger) and fail the second naming both passives. Then unskip the
QA test.
