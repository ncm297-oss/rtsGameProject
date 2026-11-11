# BUG-1147: M5-V2 difficulty tips: typed think intervals can drift, the brief's 33.33 example

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (item 1: M7-VH1, verified by QA 2026-11-11-0739; the 33.33 item was the brief's slip, nothing to fix) |
| Found | 2026-11-07-0926, task M5-V2 |
| System | skirmish setup (difficulty tooltips), `ui.json` |
| Fixed by | 6ec217d (M7-VH1): `{1}` in `setup.difficulty_easy_tip` / `_hard_tip` = `ThinkTicks / 20` in seconds (`UiText.Seconds`, `0.##`), load rule: Easy / Hard must name `{1}`, no `{2}`. Proof: `DifficultyTipTest` (a copy of `ai.json` at 7 / 2 / 0.333: "7", "0.35"), `QaM7Vh1Test` tips at 0.333 / 2 / 7, `QaM5V2Test` load rows. Shipped: "Thinks every 2 seconds" (as before), "thinks every 0.5 seconds" (was "half second"; accepted by the Producer). Residual: BUG-1200 ("1 seconds" at exactly 1 s, a stale comment) |

## Repro
1. Read `game/data/common/ui.json` `setup.difficulty_easy_tip` / `difficulty_hard_tip`: "Thinks every 2 seconds" and
   "thinks every half second" are typed text.
2. Change `game/data/factions/*/ai.json` `difficulties.hard.thinkInterval` to 1: the Hard tip still says "every half
   second". No test pins the typed interval to the data (only the gather bonus is formatted in, per the brief's scope).
3. `UiText.Percent(0.333f)` returns `"33.3"` (QaM5V2bTest pins it); the brief's QA focus said 0.333 "shows 33.33".

## Expected
1. Player-facing numbers that exist in data come from data (docs/02 "AI opponent" text is the source today, but
   `thinkInterval` is the data's own number), or a data-validation check fails when the tip and the data disagree.
2. The brief's example matches the spec it gives (`0.##`): 0.333 is 33.3%; 0.3333 is 33.33%.

## Actual
1. The interval words can go stale on a tuning change with every test green. The developer flagged it; docs/03
   "Implementation (M5-V2)" records it as out of scope.
2. The code follows the `0.##` spec correctly; only the brief's example arithmetic is off. Nothing to fix in code.

## Notes
- Item 1's cheapest fix: `{1}` = the difficulty's `thinkInterval` in seconds in each tip (with a load rule like `{0}`'s),
  or a content test comparing the tip's number with `ai.json`.
- The tooltip panel is still translucent over the Add / Remove player buttons (the screenshot of M5-V2 shows the buttons'
  text through it); that is the known no-shell-theme note in docs/03 (M6-VH5 area), not new here.
