# BUG-1178: QaM6V18Test is red on main since M7-1 (save format 10 always writes the map id)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M7-V2, 2026-11-10-1236: `QaM6V18Test` expects format 10's empty map-id field; red with the old file on a clean clone, green with the fix; the scene loop 88 / 88 twice) |
| Found | 2026-11-10-0146, task M7-V1 (pre-existing, not caused by it) |
| System | view tests / save format |
| Fixed by | `M7-V2` (3133c6a) |

## Repro
1. `powershell -File tools/qa/scene-loop.ps1 -Filter QaM6V18Test` in the view worktree at HEAD `96e4a0a`, and in a clean
   clone at the diff base `1eb231d` (before any M7-V1 change).
2. Both: `QA M6-V18 TEST FAIL: the generated save holds something between its preset and its heightmap`,
   `SCENE LOOP FAIL: 1 of 1: QaM6V18Test`. The full loop (`-SkipPlayable`) is 86 / 87 with only this scene red.

## Expected
The scene loop is green on `main`; a save-format change updates the view tests that pin the bytes (as BUG-1108's
patch did for formats 6 / 7).

## Actual
`game/tests/QaM6V18Test.cs` line 103 asserts that a generated map's save has the `HMAP` tag right after the
starting-resources preset string (no map id written, format 4 / 6 / 8). M7-1 (`24cfb35`, `SaveHeader.Write`) makes every
save format 10 and always writes the map id, `""` for a generated map, so an empty string field now sits between
the preset and `HMAP`. The line 98 check passes only because `SaveFormat.HuntMaplessVersion` now aliases `CurrentVersion`.

## Notes
- Last seen green: 2026-11-07-0236 (M5-4d-2 QA). M7-1 was the sim track's task, so the view-side test was not in its
  QA run; the sim/view split leaves view scenes that pin sim bytes unchecked by sim QA (as BUG-1108).
- Fix (view track, test-only): expect the empty map-id string field (`StringField("")`) then `HMAP`, and update the
  comment; check the other rows of the scene that name formats 4-9.
