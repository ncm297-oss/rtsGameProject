# BUG-0123: The selection panel allocates a string every tick while the selected building is repaired; the greyed button looks like an enabled one

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-07-1415, task M3-V3 |
| System | HUD (game/scripts/SelectionPanel.cs, game/scripts/CommandCard.cs) |
| Fixed by | 9998824 (M3-V3b: hp is two labels with an int-string table; greyed name / hotkey at `CommandCard.DimAlpha` 50 %). `QaV3Test` strict row is a `Check`. Verified by QA 2026-10-07-1715: 300 repair ticks with a running queue and a rally: panel, card, strip, rally, minimap and building views 0 B (`QaV3bTest`; the resource bar allocates only when a total changes, see BUG-0126 item 6); greyed cells' name / hotkey `Modulate.A` = 0.5 on every one of 900 cells checked; windowed pixel read: the brightest name pixel is 1.00 enabled and 0.81 greyed |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaV3Test.tscn` (row `Allocation`; `-- --strict` fails it).
   The row selects the own Town Hall, damages it to a third, sends three workers to repair it, and measures only the HUD
   `Sync` calls (panel, card, strip, rally, bar) over 300 ticks.
2. For item 2, look at `hud-card-queue.png` from `ProductionHudTest -- --shots <dir>`: the queued Age II button (disabled,
   "In a queue") next to the enabled Laborer button.

## Expected
Brief M3-V3, "Constraints most at risk": "the panel rebuilds its strings only when a shown value changes: hp changes
every tick under repair, so cache per-value strings or use a numeric label". docs/03 (M2-H2 rule) keeps per-frame HUD
work allocation-free.

## Actual
1. ```
   allocation: idle 0 B; repair 15552 B over 300 ticks (hp 803 -> 1363, 300 text rebuilds)
   ```
   About 52 bytes per tick: `SelectionPanel.SetStat` builds `string.Create($"{hpNow} / {value:0.#}")` on every hp change.
   Idle is 0 bytes (criterion 8 holds). docs/03 "Implementation (M3-V3)" documents it ("one small string per change"),
   but that is the case the brief asked to avoid. Production running with the hall still repaired measured 5232 B over
   100 ticks, the same source.
2. A disabled production button keeps the enabled look: the name, hint and cost are child labels, so Godot's disabled
   style doesn't dim them. Only the small red reason line tells them apart (seen in the screenshot: "Age II / In a
   queue" next to "Laborer / 50 / 0").

## Notes
1. Cheapest fixes: two labels ("now" and "/ max"), with "now" from a cached table of int strings up to the largest
   building hp (built once in `Init`), or `Label.Text` set from a reused `char[]` buffer. Turn the `Known("BUG-0123")`
   row into a `Check` when fixed.
2. Set `Modulate` (for example 60 % alpha) on the cell's labels when `Disabled` changes. That is one write per reason
   change, so it stays allocation-free.
