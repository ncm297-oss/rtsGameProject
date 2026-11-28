# BUG-1952: The kit builder fails a part that is not an object, or a role that is a list, with a bare Python error that names no entry or part

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-27-1857, task M6-V32 |
| System | `tools/art/build_building.py` `check_kit` (art tooling) |
| Fixed by | |

## Repro
1. `powershell -File tools/qa/kit-errors-v32.ps1` (needs Blender 5.2). Cases `rolelist` and `partstr`.

## Expected
BUG-1911's rule: a bad kit input gives a `FAIL` line that names the entry and the part, and no model.

## Actual
No model is written and the exit is non-zero (so nothing bad lands), but the lines name nothing:
```
rolelist    FAIL  FAIL TypeError: unhashable type: 'list'
partstr     FAIL  FAIL AttributeError: 'str' object has no attribute 'get'
```
(`rolelist`: a part `{ "shape": "box", "role": ["pole"], ... }`; `partstr`: a kit `[ {...}, "pole" ]`.)
The other fifteen new cases in the same script (negative size, a string for a number, a `rotate` of NaN or a string, two
parts with one bad, a four-number size, a `top` of -1 or NaN, `sides` 4.5 or true, an `axis` on a box, `colours` a list,
a 1e308 size) all give the right line.

## Notes
`check_kit` builds its label with `p.get(...)` before checking that `p` is a dict, and `p.get("role") not in roles` hashes
the role. A `isinstance(p, dict)` check first and a `isinstance(role, str)` check would cover both. Also cosmetic: a
`colours` that is a list reads "the entry has no "colours" map" (it has one, of the wrong kind).
