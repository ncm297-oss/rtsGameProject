# BUG-1555: `PlayerView.IsKnownClosed` reads a building razed out of sight (ghost still remembered) and a node felled out of sight as open: the rally search sees through the fog

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-S14 item C, session 2026-11-26-1847; QA verified, accepted 2026-11-26-1847) |
| Found | 2026-11-16-2036, task M6-12 (M6-S12), QA full |
| System | AI `PlayerView` (the rally search's map read, `MilitaryManager.NarrowWithin` / `Acceptable`) |
| Fixed by | M6-S14 (C) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M6S12AiRulesQaTests.IsKnownClosed_FollowsTheFog_ButARazeOutOfSight_Leaks_Bug1555"`.
2. Player 1's hall on ground player 0 never explored: `IsKnownClosed` false (right). A unit of player 0 sees it: true (right).
   The unit dies (a ghost): true (right). The hall is razed while player 0 cannot see it: player 0 still remembers its ghost
   (`Fog.Ghosts(0)[k].Generation` unchanged), but `IsKnownClosed` now reads **false**.

## Expected
CLAUDE.md rule 3 and the brief's constraint: "the AI reads the world only through its fog-filtered view ... the rally search
may read terrain walkability only as far as the AI already may". `IsKnownClosed`'s own summary says it counts "a ghost of
the same building: the rule `CanPlace` uses", and `CanPlace` judges a ghost's remembered footprint as closed, "also of a
building gone unseen". The answer for that cell should stay "closed" until player 0 sees the ground again.

## Actual
`IsKnownClosed` tests the live `NavFlags.Building` bit first and returns open when it is clear, so the ghost is never
consulted: the rally search can learn of a death it did not see. The same shape for nodes (a node felled out of sight reads
open) is documented as intended (docs/03 M6-S12 Q3); the ghost case is not documented and contradicts the summary.

## Notes
- Practical effect is small (the search reads only cells within `ai.rallyReach` + 2 cells of its own rally point, near its
  base), which is why it is S3 as the earlier AI leaks were (BUG-0540, BUG-0280). The Producer may rate it higher under rule 3.
- Cure: for a cell with no `Building` bit, also answer true when a ghost of player 0 covers it (the `CanPlace` loop over
  ghosts), and for nodes read the per-player node memory (`FogStore.NodeAt`), as `CanPlace` does.
- The row pins today's answer (`Assert.False(razedUnseen)`) and flips with the cure.

## Fix (M6-S14)
`PlayerView.IsKnownClosed` closes a remembered ghost's or node's footprint until a fog update shows that ground again; `KnownClosedWindow` for the rally search. The QA row flipped (`IsKnownClosed_FollowsTheFog_AndARazeOutOfSight_StaysClosedUntilSeen_Bug1555`); the node: `Ai/KnownClosedMemoryTests`. docs/03 M6-S12 Q3 corrected.
