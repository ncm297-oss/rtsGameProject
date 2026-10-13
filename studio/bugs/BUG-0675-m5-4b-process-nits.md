# BUG-0675: M5-4b nits: an unplanned ui.json edit, a "data-hash unchanged" line that could not hold, and a defendRadius that never waves

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-0916, task M5-4b |
| System | process, data ranges |
| Fixed by | |

## Findings
a. **ui.json.** The brief gave `game/data/common/ui.json` to the view this session ("the sim does not"). The sim added
   `placement.defeated`, which its unplanned `PlacementError.Defeated` forced (the view's `UiText` requires a key for
   every member). `git merge-tree` of 4edcf26 with the view's 442a3e6: `ui.json` auto-merges. The only conflict is
   `docs/01-vision.md`, where both tracks appended a decision row (routine). No harm found; it is a coordination slip.
b. **Golden data-hash.** The brief said the golden `data-hash` must not move. It has to: M5-3 put every `rules.json` `ai`
   field into `ContentHash`, so three new knobs move it (8409DB7517579FEF -> 72EF431E70C49C39). The `k` lines did not
   move. The developer's call was right and the brief's line was wrong.
c. **defendRadius 256** loads, as the brief asks. But on a 128-cell (256 m) map every enemy non-worker the AI ever sees
   is a threat: `DefenseQaTests.TheKnobsAtTheirEdges_*(20, 256)` measured `waves 0/0` in 4,000 ticks (one threat, never
   released). That is valid data that plays as an AI that never attacks: a range or doc question for the data track.
d. **Memory line.** Criterion 6 asked for "one int per building slot per seat". The dev keeps two (hp plus generation, so
   a reused slot doesn't fire a false hit) and itemises 8 B x slots in docs/03. That is correct; the brief's count was short.

## Notes
None of these blocks. Item c is a data-range question for the Producer.
