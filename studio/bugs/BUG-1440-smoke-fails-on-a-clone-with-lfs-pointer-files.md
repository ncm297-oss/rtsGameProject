# BUG-1440: on a clone whose model files are Git LFS pointers, the import prints glTF `ERROR` lines, smoke FAILs and the `.import` files are left modified

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-14-2156, at PLAN (Producer, scratch clone of `main` f07b849 made with `GIT_LFS_SKIP_SMUDGE=1`) |
| System | view: asset import, `tools/qa/smoke.ps1`, docs (the handoff's and docs/03's "a pointer file gives the capsule and one warning, never an `ERROR`") |
| Fixed by | |

## Repro
1. `GIT_LFS_SKIP_SMUDGE=1 git clone <repo> x` (the `.glb` files are pointer text).
2. In `x`: `powershell -File tools/qa/smoke.ps1`.

## Expected
What the handoff and docs say: capsules and one warning per model, no `ERROR` line; or a clear "run `git lfs pull`
first" message and a non-zero exit.

## Actual
```
FAIL: log: ERROR: glTF: Error parsing .gltf JSON data: Expected 'true', 'false', or 'null', got 'version' at line: 0
FAIL: log: ERROR: Error importing 'res://assets/models/malazan/malazan_crossbowman/malazan_crossbowman.glb'.
```
for every model and the animation library; exit 1. The failed import also rewrites tracked `.import` files
(`git status` shows them modified), so after a later `git lfs pull` smoke still fails ("Failed loading resource")
until `git checkout -- game` and `game/.godot` is deleted. With the real files on a clean tree smoke is PASS.

## Notes
- The "never an `ERROR`" claim holds for the runtime loader (QA, M6-V20), not for the import step.
- Smallest fix: `smoke.ps1` (the sim's file; a Request) or the import tooling's docs detect a pointer file first and
  say "run `git lfs pull`"; and SETUP.md says so. Correct the sentence in docs/03.

## Triage (Producer, 2026-11-14-2156 PLAN)
S4: a fresh clone made the documented way (`git lfs pull`) is unaffected; the studio's worktrees have the real
files. Waits for the view's next hardening; this session's view QA re-checks the no-LFS clone and adds what it sees.
