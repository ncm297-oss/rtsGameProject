# BUG-1440: on a clone whose model files are Git LFS pointers, the import prints glTF `ERROR` lines, smoke FAILs and the `.import` files are left modified

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (M6-SH2, session 2026-11-15-2136: `tools/qa/smoke.ps1` stops before the import when a non-.wav file under `game/` is a Git LFS pointer, exit 3; QA saw it fire on a smudge-skipped clone: "72 file(s) ... run 'git lfs pull' first") |
| Found | 2026-11-14-2156, at PLAN (Producer, scratch clone of `main` f07b849 made with `GIT_LFS_SKIP_SMUDGE=1`) |
| System | view: asset import, `tools/qa/smoke.ps1`, docs (the handoff's and docs/03's "a pointer file gives the capsule and one warning, never an `ERROR`") |
| Fixed by | M6-VH8 (docs) |

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

## QA re-check (2026-11-14-2156, M7-V5 head 13e45c5)
- Clone `GIT_LFS_SKIP_SMUDGE=1 git clone --branch studio/2026-11-14-2156-view file:///C:/Dev/rtsGameProject`, then
  `& $env:GODOT --headless --path game --import` only: exit **0** with **261** `ERROR` lines, and `git status` shows
  **71** tracked `.import` files modified (30 of them the new Teblor ones: 5 `.glb.import` and 25 `.png.import`).
- The M7-V5 textures are LFS too, so besides the glTF parse errors there are image errors now:
  `ERROR: Condition "!success" is true. Returning: ERR_FILE_CORRUPT` and
  `ERROR: Error loading image: 'res://assets/models/teblor/teblor_gatherer/teblor_gatherer_outfit.png'.` (one per pointer PNG).
- The rewrite is the same for every file: `path=` / `path.s3tc=`, `metadata` and `dest_files=` removed, `valid=false`
  added; the uid kept. `git checkout -- game` restores them.
- The same branch cloned with `git lfs pull`: import clean (0 `ERROR`), `git status` clean; a full
  `tools/import-assets.ps1` run (Blender + Godot) on that clone leaves `git status` clean (byte-identical).

## Triage (Producer, 2026-11-14-2156 PLAN)
S4: a fresh clone made the documented way (`git lfs pull`) is unaffected; the studio's worktrees have the real
files. Waits for the view's next hardening; this session's view QA re-checks the no-LFS clone and adds what it sees.

## M6-VH8
docs/03 "Which types" now reads "never an `ERROR` line from the runtime loader" and says the import step on LFS pointer
files does print glTF and image `ERROR` lines and rewrites the `.import` files. Request for the sim track (its file):
`tools/qa/smoke.ps1` could detect a pointer file under `game/assets/` before the import and stop with "run `git lfs pull`
first". Open until that lands.
