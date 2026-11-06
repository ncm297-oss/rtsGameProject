# BUG-1102: D23 map-text nits (British "centre" in American-spelled text; the map checker has no ratio words)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-05-2148, task D23 (QA, light) |
| System | game/data/maps/raraku.json `description`; sim/Rts.Sim.Tests/Content/MapTextContentTests.cs |
| Fixed by | |

## Repro
1. `grep -rnoiE "\bcent(re|er)\b" game/data` gives one hit, `maps/raraku.json:4: centre`. The rest of the player-facing
   text is American ("Armor", "Armory", "armored" in techs / buildings / units), and docs/02's Shipped maps row says
   "Open center", docs/02's biome line "flavor", "favors".
2. `MapTextContentTests.Problems` rejects a digit, "placeholder" and a faction word, but not the ratio words the presets'
   checker rejects (`StartingResourcesContentTests.NumberWords`: "twice", "double", "triple", "half"). "Twice the forests of
   Pale Hills" passes the map checker (QA row `D23MapTextQaTests.NumberWords_AreAllowed_ButFactionWholeWordsStillCount`
   pins that number words like "two mesas" pass, which is the brief's rule).
3. Docs/03's M6-S7 Raraku paragraph (around line 9040) and docs/02 line 341 still say "placeholder" for Raraku; the
   text is no longer a placeholder (the layout still is). Sim / Producer owned.

## Expected
One spelling convention in player-facing text (the shipped data's American); the map checker's "describe, don't count"
rule either includes the ratio words or says in its summary why a map may use them.

## Actual
"wide open centre"; ratio words pass the map checker silently.

## Notes
- Item 1 is a one-word data edit ("centre" -> "center") plus the pin in `MapTextContentTests.Pinned`; it moves the data
  hash, so batch it with the next task that regenerates the golden (or after M6-SH1, when the header hash is informational).
- Not a criterion: the brief says "no number", and the 4 required mutants all fail as asked.
