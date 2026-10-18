# BUG-0910: BUG-0791's no-site wait also holds a build step whose search never ran (requires unmet): its Build goes 1-5 thinks late

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (verified by QA 2026-10-17-2344) |
| Found | 2026-10-17-1624, task M5-H3 (QA, full tier) |
| System | AI: `Ai/BuildOrderExecutor` (`StepNoSiteUntil`, `NoSiteRetryThinks`), `AiPlayer.TryBuild` / `FindSite` |
| Fixed by | 13d9ad6 (M5-8a): `TryBuild` sets `NoSiteFound` only when `SiteSearches` moved; regression rows `QA/SimHardeningM5H3QaTests.ABuildStepWaitingOnARequirement_*` (Hard) and `QA/AiOpeningsQaTests.ARequirementBlockedBuild_GetsNoWait_*` / `ARealNoSiteSearch_StillWaits*` (Easy, Normal, Hard) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningM5H3QaTests.ABuildStepWaitingOnARequirement"`
   (skipped with this id; remove the `Skip` to run it).
2. The scene: a Malazan Hard AI seat on the flat AI scene, gold and wood 5,000, build order `time 0: build malazan_wickan_corral`
   (it requires a Legion Barracks). There is no Barracks for 2-6 thinks. Then a finished Barracks is spawned, and the test counts
   the thinks until the Corral's Build is sent.
3. Thinks from the Barracks to the Corral's Build:
   - head d048a21: 4, 3, 2, 1, 5
   - base bf8026e: 2, 1, 1, 1, 1

## Expected
Brief M5-H3: "no behaviour change beyond the bug fixes". BUG-0791's fix is about cost: a site search that scanned the whole
placement radius and found nothing should not run again at every think (docs/03: "a step whose search found no anchor").
A step whose `FindSite` returned at the cell-free test (`PlayerView.CanPlaceAnywhere`, BUG-0572: requires unmet, or a full
building store) cost nothing, so it has no reason to wait. It should act at the next think after the requirement is met, as before.

## Actual
`AiPlayer.TryBuild` sets `NoSiteFound` whenever `FindSite` returns -1, and that includes `FindSite`'s first line
(`if (!View.CanPlaceAnywhere(type, out _)) return -1;`). The executor then sets `StepNoSiteUntil = Now + 5 thinks`. A step
due before its requirement therefore re-checks only every 5th think, and its Build goes out up to 5 thinks after the
requirement stands: 2.5 s on Hard, 5 s on Normal, 10 s on Easy.

## Notes
- This hits shipped build orders whenever a step falls due before its requirement. Examples: the Age II buildings at
  450-570 s (`malazan_cadre_tower`, `malazan_engineers_yard`, `whirlwind_shrine`, ...) when Age II is still researching,
  and a Corral or Range step while its Barracks is still a site.
- The menu-map spending cells did not move because of it. With only BUG-0790 reverted on head, the faction pages' numbers
  come back, so the cells QA measured there are unaffected. Other matches can be affected.
- Fix idea: set `NoSiteFound` only after `FindSite` actually ran its ring search. For example, `FindSite` can report "searched"
  (it already counts `SiteSearches` past the cell-free test), or `TryBuild` can call `CanPlaceAnywhere` first.
- BUG-0791's own note proposed this exact case ("when `PlayerView.CanPlaceAnywhere` / the site search finds nothing").
  The cell-free half of that idea is the part that changes behaviour for no gain in cost.
