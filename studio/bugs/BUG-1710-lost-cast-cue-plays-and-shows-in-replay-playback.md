# BUG-1710: The lost-cast cue plays and shows in replay playback

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-25-0924, task M6-V27 (view developer, by reading the code) |
| System | `AbilityViews` lost-cast cues (M5-V1) in replay playback |
| Fixed by | |

## Repro
1. Play a live match with a selected Cadre Mage whose walk-in cast is lost (ringed in, 3 s), so the Refused sound plays and
   the `hud.cast_lost` words show over it.
2. Load Game -> Watch that match's recording.

## Expected
The same rule as M6-V27's order notices (brief scope item 4: nothing shown or played while watching a replay), or a ruling
that a replay shows the lost casts as they happened.

## Actual
`AbilityViews.CollectTick` collects `LostCastCues` for `SelectionController.LocalPlayer` on every tick, live or playback
(`Match.Wire` binds it the same way for a replay), so the playback plays the Refused sound and shows the red disc and words
for player 0's lost casts. M6-V27's `OrderNoticeView` is off in playback (`Enabled = live`). Not run in a scene; read from
the code (`AbilityViews.cs` `CollectTick`, `Match.cs` `Wire`).

## Notes
- A cure is one line in `Match.Wire` (an `Enabled` flag on the lost-cast collect, false for a replay) plus a playback row
  in `AbilityViewTest` or `QaM5V1Test`. docs/03 "Implementation (M6-V27)" question 4 asks the Producer which rule holds.
