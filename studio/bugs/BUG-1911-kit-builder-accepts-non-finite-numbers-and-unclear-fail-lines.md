# BUG-1911: The kit builder writes a model from a part with NaN / Infinity (the part silently vanishes); three kit errors read unclearly

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-27-0817, task M6-V31 |
| System | import tooling: `tools/art/build_building.py` `check_kit` / `build_building` (M6-V31 kit path) |
| Fixed by | |

## Repro
`powershell -File tools/qa/kit-errors-extra.ps1` (Blender 5.2; scratch manifests, nothing under the repo is written):

```
nocolours   ok    FAIL KeyError: 'colours'
badhex      ok    FAIL ValueError: invalid literal for int() with base 16: 'zz'
nan         FAIL  RESULT (exit 0, model written: True) size 2.5 x 2.5 x 2.0
inf         FAIL  RESULT (exit 0, model written: True) size 2.5 x 2.5 x 2.0
noat        ok    FAIL whirlwind_tent: kit part 1 (box, role pole): at None is not three numbers
sides2      ok    FAIL a cylinder part needs at least 3 sides, has 2
turned      ok    FAIL whirlwind_tent: kit part 0 (box, role cloth): 0.654 m outside its 4 x 4 m footprint less 0.25 m
notbuilding ok    FAIL whirlwind_bazaar: not a building of ...\game\data\factions\whirlwind\buildings.json
unusedcol   ok    FAIL whirlwind_tent: manifest names material brass, not in the source
typo        info  RESULT (exit 0, model written: True) size 2.5 x 2.5 x 2.0
boolsize    info  RESULT (exit 0, model written: True) size 2.5 x 2.5 x 2.0
```

## Expected
The brief's QA focus 4 and docs/04: a bad kit entry is "refused with a FAIL line, no model written", "a clear failure,
not a silent empty model".

## Actual
1. **Non-finite numbers pass.** A part whose `size` holds `NaN` or whose `at` holds `Infinity` (both read by Python's
   `json.load` and by PowerShell 5.1's `ConvertFrom-Json`, so `tools/import-assets.ps1` would pass the manifest on) clears
   `check_kit` (every comparison with NaN is false, so "over 0", "under the ground" and "outside the footprint" all pass),
   and the builder exits 0 with a model that is the other parts only (2.5 x 2.5 x 2.0 m: the ridge tent; the bad part is
   gone). A silent wrong model.
2. A kit entry with no `colours` key fails with a bare `FAIL KeyError: 'colours'` (no id, no hint).
3. A colour no kit part uses, on an entry with no source, fails with "manifest names material brass, not in the source"
   (there is no source; "no kit part has role brass" is what happened).
4. Measured, not counted as failures: an unknown part key (`"turn"` for `"rotate"`) is ignored silently (the part is built
   unturned), and a `true` size is read as 1.

## Notes
Inputs 1 and 4 are outside what docs/04 documents (an absurd parameter), so S4. The cure is a few lines in `check_kit`:
`math.isfinite` on every size / at / rotate number and a known-keys check per part; `b.get("colours", {})` with a named
FAIL; a kit-specific message for an unused colour.
