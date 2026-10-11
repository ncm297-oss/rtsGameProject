# BUG-0531: D12 nit: docs/03 "For the data track (M4-5)" still says the casters' 10 m and the stealth text pins are pending

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D12 round 1, session 2026-10-10-2334; verified by QA) |
| Found | 2026-10-10-2334, task D12 |
| System | docs/03 "For the data track (M4-5)" |
| Fixed by | D12 345ac3d: docs/03 "For the data track (M4-5)" paragraph rewritten as shipped (Cadre Mage + Priest of the Whirlwind 10 m, pinned by `Content/UnitContentTests`; Stealthed/Revealed out of `PendingStatuses`, pinned by `StealthedAndRevealed_AreLoaded_AndStrictlyPinned`) |

## Repro
1. `docs/03-technical-design.md` lines ~2960-2966: "the shipped casters' 10 m ... is D12, with the page rows and one golden
   regen" and "they are in `AbilityContentTests.PendingStatuses` until the data track pins their texts against docs/02's rows".

## Expected
CLAUDE.md DoD 4: docs updated in the same commit when behavior changed. After D12 both casters ship `detector: 10`
(pinned by `Content/UnitContentTests` I/J/K/K2/K3) and Stealthed / Revealed are out of `PendingStatuses` (pinned by
`AbilityContentTests.StealthedAndRevealed_AreLoaded_AndStrictlyPinned`).

## Actual
The paragraph reads as future work. The developer flagged it in the report but did not edit it (docs/** outside
docs/factions is shared; a one-line past-tense edit was allowed).

## Notes
Also mentions Galain Sorcerer / Shaman (unshipped factions): keep those as future. Fix at integration or in the next data task.
