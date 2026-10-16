# BUG-0720: D17 Defense wording nits: docs/03 still quotes the old page line; "more than 16 m from it" reads as "from the Keep"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-13-1616, task D17 |
| System | docs (faction pages, docs/03 M5-4b) |
| Fixed by | item 1: ba37fa4 (the Producer's docs-drift fix at the 1616 ACCEPT; the stale quote is gone from docs/03); item 2: c6bb467 (D18, verified by QA). Status set by the Producer at the 2026-10-15-2325 PLAN |

## Repro
1. `grep -n "Laborers flee to the Garrison Keep" docs/03-technical-design.md docs/factions/malazan.md`
2. Read the last sentence of the "Defense" bullet in `docs/factions/malazan.md` (line ~169) and `docs/factions/whirlwind.md` (line ~172).

## Expected
- docs/03 "Implementation (M5-4b)" → Defense quotes the faction pages accurately, or doesn't quote them.
- The page sentence names the threat as the thing the drop-off must be 16 m away from (docs/03: "the nearest own finished
  drop-off farther than `workerFleeRadius` from the point").

## Actual
1. docs/03 line 6185 still says `(the faction pages: "Laborers flee to the Garrison Keep")`. D17 removed that phrase from
   malazan.md, so the quote is now stale (and whirlwind.md never said it). docs/03 belongs to the sim track, so D17 couldn't
   fix it.
2. Both pages end with "flee to the nearest finished drop-off away from the threat (the Garrison Keep or a Quartermaster's
   Depot more than 16 m from it; else the nearest one)". The nearest noun before "it" is the drop-off or the Keep, so a
   reader can take it as "a Depot more than 16 m from the Keep". The rule means 16 m from the threat. Suggested wording:
   "... (the Garrison Keep or a Quartermaster's Depot, whichever is nearest among those more than 16 m from the threat;
   else the nearest one)". Do the same for the Holy Camp / Supply Cache line.

## Notes
Wording only, no rule or data impact. The rest of both lines matches docs/03 point for point: the trigger (an own building,
a site too, within `defendRadius` of the hall hit, or an enemy non-worker seen within it), `defendFor` 20 s after the last
trigger, a wave out ending like a retreat, and workers within `workerFleeRadius` 16 m. The knob values match
`common/rules.json` (48 / 20 / 16), and the drop-off display names match `buildings.json` (`dropOff: true`: Garrison Keep,
Quartermaster's Depot; Holy Camp, Supply Cache).

QA 2026-10-14-0946 (D18): item 2 verified fixed in c6bb467. Both pages now carry the suggested wording, which matches
`DefenseManager.NearestDropOff` (the nearest to the worker among the drop-offs more than `workerFleeRadius` from the
defense point, else the nearest). `AiContentTests` and `NormalMicroPhraseQaTests` are green. Item 1 (docs/03, sim track) is still open.
