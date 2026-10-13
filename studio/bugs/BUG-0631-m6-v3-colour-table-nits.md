# BUG-0631: M6-V3 colour nits: the loader accepts a colour table with two equal colours; four view tests still pin faction primaries; both slots may pick one colour

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-0146, task M6-V3 (QA) |
| System | view: `UiText.ReadColours`, `SkirmishSetup` colour lists, `game/tests` colour rows |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V3Test.tscn` prints
   `PROBE colour table: colours 1 and 2 with the same rgb load clean (a default match would draw both sides alike)`:
   `ui.json` with `setup.colour_2.rgb` set to `colour_1`'s value parses with no error.
2. `grep -l PrimaryColor game/tests/*.cs`: `AbilityViewTest`, `CombatViewTest`, `QaM24Test`, `QaVh3Test` compare drawn colours
   with the faction's `palette.primary`. The match now draws `ui.json` `setup.colour_1` / `colour_2`, which equal the Malazan /
   Whirlwind primaries only by choice of values (the dev's report calls this out).
3. Skirmish setup: pick the same colour in both slots (e.g. Iron / Iron), Start: both sides draw alike (docs/03
   "Implementation (M6-V3)": "two slots may pick the same colour").

## Expected
1. The table's job is to tell players apart: two equal entries (or one inside BUG-0431's 0.4 gap from the own zone rim) are
   a data-entry error the loader could report as one error, like the other `ui.json` rules. Today only `ShellTest` checks the
   shipped file, so the rule is a test, not a load check.
2. A data-only recolour of `ui.json` (CLAUDE.md rule 8 spirit: names and look from data) should not turn four unrelated view
   tests red; they should compare against `Match.PlayerRgb` (as `MinimapTest` and `AiSeatTest` now do).
3. A design call for the Producer / owner: docs/02 "Game flow" 2 lists colour per slot but says nothing on duplicates; most RTS
   setups refuse a colour already taken. Allowing it re-opens BUG-0601's symptom by choice.

## Actual
As in Repro. Nothing crashes; all three are robustness / wording items.

## Notes
Item 3 may be intended (the dev documented it); record the decision in docs/02 either way.
