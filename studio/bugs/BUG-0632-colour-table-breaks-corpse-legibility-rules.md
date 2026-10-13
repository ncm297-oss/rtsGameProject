# BUG-0632: Two menu colour picks break CombatViewTest's corpse legibility rules: Crimson's corpse fill is below luminance 0.2, and Iron v Jade corpses are 0.22 apart (rule 0.25)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-13-0146, task M6-V3 (QA round 1 re-check) |
| System | view: `ui.json` `setup.colour_*` table vs `CombatViews` corpse colours (BUG-0160 item 3 rules) |
| Fixed by | 650d423 (M6-VH1): `ui.json` Crimson `#A3192B` -> `#B82633`, Jade `#2E8B57` -> `#2EA35A`; a `ShellTest` row checks all 8 fills and 28 pairs. QA 2026-10-13-0527: lowest luminance Cobalt 0.210, closest pair Cobalt / Violet 0.253; CombatViewTest green with each colour on slot 1 |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V3Test.tscn` (row `CorpseRulesOverTable`: a real `CombatViews`
   bound to all eight table colours) prints:
   - `corpse rules, colour_3 Crimson: fill luminance 0.185  <- KNOWN BUG-0632 probe: below 0.2 or rim not darker`
   - `KNOWN BUG-0632 probe: corpse fills of colour_1 and colour_5 only 0.222 apart`
   (all other colours are 0.210 to 0.705 luminance, all other pairs at least 0.25 apart).
2. End to end: a copy of `game/` with the table rows swapped so slot 1 draws Crimson (#A3192B) and slot 2 Jade (the same
   thing the menu gives for picks Crimson / Jade): `CombatViewTest` prints
   `COMBAT VIEW TEST FAIL: seed 1: player 0 corpse fill (0.543, 0.083, 0.143, 1) reads black (luminance 0.19)` (and seed 6).

## Expected
BUG-0160 item 3's rules, enforced by `CombatViewTest.CheckCorpseColours`: each team's corpse fill (owner colour x
`CorpseShade` 0.85) has luminance >= 0.2, the rim is darker, and the two teams' fills are >= 0.25 apart. Since M6-V3 the
owner colour is any of the 8 table entries the menu offers, so every entry (and every pair of distinct entries) should meet
them. `CombatViewTest` only ever sees the defaults, colours 1 and 2.

## Actual
Crimson (#A3192B) gives a 0.185-luminance corpse fill; Iron (#4B4F55) v Jade (#2E8B57) corpse fills are 0.222 apart.
Nothing crashes; it is legibility of corpses for two of the 8 x 8 pick combinations (the Iron / Jade one is a pair a
player can easily pick).

## Notes
Fix options (Producer's call): retune `ui.json` Crimson a little brighter and Iron or Jade a little further apart (data
only), or check the rules in `UiText.ReadColours` / a ShellTest row over the whole table. When fixed, flip the
`QaM6V3Test.CorpseRulesOverTable` probes to Checks. Cobalt (0.210) and Cobalt v Violet are close to the limits.

## Producer (2026-10-13-0527 ACCEPT)
**Fixed** in M6-VH1 (data first: two `ui.json` values, names unchanged; then the `ShellTest` guard row and `QaM6V3Test.CorpseRulesOverTable` as Checks). The new values are under For your review for the owner.
