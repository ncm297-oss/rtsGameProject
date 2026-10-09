# BUG-0148: M3-V4 nits: the pick's circles take ~1 % facet-sliver pixels for the tree; the Playable scene doesn't check the "+1" is green or that the rally laborer chops the rally forest; a non-step exception saves no replay

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | partly fixed: items 2, 3 and 4 fixed; item 1 open |
| Found | 2026-10-07-2014, task M3-V4 (QA) |
| System | right-click node pick (`ViewApi/ResourcePicker.PickRay`), M3 Playable scene (`game/tests/M3PlayableTest.cs`) |
| Fixed by | items 2, 4: ec28ee4; item 3: 08dc8e3 (M4-VH1, `M3PlayableTest.TrainedAndChopping` checks the rally tree or a tree within `nodeSearchRadius` of it) |

## Repro / Actual
1. **Facet slivers.** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PickRayV4QaTests" --logger
   "console;verbosity=detailed"`: per seed and ray family, 20-51 of 3,000 rays (0.7-1.7 %) pass inside the trunk / cone
   circle the pick tests but outside the drawn 6- / 7-sided mesh. The pixel shows the ground (or the tree behind)
   and the click names the tree. The gap is at most 8 cm (a facet's middle), about one pixel at zoom 60. Documented
   in docs/03 as a choice. The exact polygon test would remove it, but it costs more than it's worth until the M6 art
   pass replaces the props.
2. **"+1" colour not checked.** Brief scope 1: "the panel reads attack with the green '+1'". Step 16 checks
   `StatBonus(1).Visible` and its text, not its colour (`AddThemeColorOverride` / the panel's bonus colour).
3. **Rally walk not checked.** Step 6 ("a new Laborer walks there and gathers") passes as soon as any laborer born
   after the rally is `Gathering` *any* wood node. It doesn't check the rally tree or its forest (a rally tree felled
   before the laborer is born makes it idle, see the scene's own re-task note).
4. **No replay on a non-step exception.** `_Ready` saves the replay only for `StepFailed`. Any other exception (a null
   reference, an index out of range, `Enum.Parse` on an empty grid key) prints `exception ...` with no repro file.

## Expected
1: a click that shows ground is never a node (criterion 3's spirit), within reason. 2-3: the scene checks what the
brief's script says. 4: every failure leaves a repro.

## Notes
None of these blocks M3-V4.

## Re-check round 1 (QA 2026-10-07-2315)
- Item 2 **fixed**. Step 18 reads `StatBonus(1).GetThemeColor("font_color")` and requires G > 0.5, G > R + 0.3 and
  G > B + 0.3. The panel's `BonusColor` is (0.35, 0.95, 0.45), which passes. A label left at the default theme colour
  (white) would fail. All 10 QA runs passed the check.
- Item 4 **fixed**. The generic `catch` now saves `m3playable-seed<N>-exception-tick<T>.replay` and guards the save with
  its own try/catch. Checked by reading the code only; no exception was injected.
- Items 1 (facet slivers, a documented approximation) and 3 (the rally walk isn't checked against the rally forest)
  remain open.

## Re-check (QA 2026-10-08-2144, M4-VH1)
- Item 3 **fixed**. Step 6 now requires the new laborer's wood node to be the rally tree, or one within
  `rules.json nodeSearchRadius` of it (where a Gather on a felled tree resolves), and prints which. Both scene loops:
  "laborer 10 gathers the rally tree" on seeds 1 and 6.
- Item 1 (facet slivers) is still open, as before (a documented approximation until M6).
