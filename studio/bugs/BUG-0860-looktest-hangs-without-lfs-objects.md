# BUG-0860: LookTest hangs forever (NRE every frame) when an LFS model isn't pulled

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-VH3, verified by QA 2026-10-17-1624) |
| Found | 2026-10-17-0056, task M6-V10 |
| System | Godot test scenes (`game/tests/LookTest.cs`), `tools/qa/scene-loop.ps1` |
| Fixed by | c0a8d5b (M6-VH3): `LookTest.LoadScene` fails every unloadable model through `Fail(...)`; `scene-loop.ps1 -TimeoutSec` (default 600) kills a hung scene's process tree and prints `FAIL exit timeout`; proving scenes `LookTest` (no-LFS clone) and `LoopTimeoutProbe.tscn` |

## Repro
1. In a scratch clone, replace `game/assets/models/looktest/Superhero_Male_FullBody.gltf` with its LFS
   pointer (`git show HEAD:<path> > <path>`), the state a machine is in after a clone without `git lfs pull`
   (or with LFS skip-smudge).
2. `& $env:GODOT --headless --path game --import`, then
   `& $env:GODOT --headless --path game res://tests/LookTest.tscn` (QA wrapped it in `timeout 60`).

## Expected
The scene prints `LookTest: FAIL <reason>` and exits non-zero, like it does for a missing clip or skeleton.

## Actual
`GD.Load<PackedScene>(CharacterPath)` returns null, `_Ready` throws a `NullReferenceException` before
`_player` is assigned, and `_Process` then throws a `NullReferenceException` every frame. The scene never
reaches `SceneExit.Quit`; it was still running when killed at 60 s (exit 124). `scene-loop.ps1` has no
per-scene timeout, so the whole loop hangs on that machine instead of reporting a FAIL.

```
ERROR: Failed loading resource: res://assets/models/looktest/Superhero_Male_FullBody.gltf.
ERROR: System.NullReferenceException: Object reference not set to an instance of an object.
ERROR: System.NullReferenceException: Object reference not set to an instance of an object.
...
```

## Notes
The owner works from two machines, so a missing `git lfs pull` is a realistic state. The fix could be a
null check on each `GD.Load` result that goes through `Fail(...)` and reaches phase 4, and/or a per-scene
timeout in `scene-loop.ps1` (helps every scene). The same unguarded `_skeleton!` at `LookTest.cs:142`
would throw if the character loaded with no skeleton.

## Verification (QA 2026-10-17-1624, M6-VH3)
- A fresh clone of the branch with `GIT_LFS_SKIP_SMUDGE=1` (all 11 look-test files are pointers), built and imported:
  `scene-loop.ps1 -Filter LookTest -TimeoutSec 120` gives `LookTest FAIL exit 1 0.5 s` with
  `LookTest: FAIL could not load res://assets/models/looktest/Superhero_Male_FullBody.gltf (missing, or an LFS pointer: run git lfs pull and re-import); ...` for all three models. No hang.
- The same clone with base f8036fc's `LookTest.cs` (the old hang) under the new loop, `-TimeoutSec 30`:
  `LookTest FAIL exit timeout 30.6 s timeout after 30 s (killed)`, the NRE lines listed under the row; no Godot process
  left afterwards, no stale temp log.
- `scene-loop.ps1 -Filter LoopTimeoutProbe -TimeoutSec 5`: `LoopTimeoutProbe FAIL exit timeout 5.9 s timeout after 5 s (killed)`, loop exit 1, no Godot process left.
