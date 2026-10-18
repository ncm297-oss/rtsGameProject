# BUG-0950: A clone without LFS objects prints 64 ERROR lines per match start (the clips' failed loads): smoke FAIL, every match scene red

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-17-2344, task M6-V12 |
| System | view: `game/scripts/Sfx.cs` (`LoadVariants`) |
| Fixed by | |

## Repro
1. `GIT_LFS_SKIP_SMUDGE=1 git clone --branch studio/2026-10-17-2344-view <repo> nolfs` (every `.wav` is a 131-byte LFS pointer).
2. `powershell -File nolfs/tools/qa/smoke.ps1` -> **FAIL** (the menu pass is clean; the `--skirmish` pass prints the errors).
3. Or `& $env:GODOT --headless --path nolfs/game --quit-after 120 -- --skirmish`: 64 `ERROR` lines, 12 `Sfx: no clip file` warnings.
4. Or `nolfs/tools/qa/scene-loop.ps1 -Filter QaM26*` -> `SCENE LOOP FAIL: 1 of 1: QaM26Test` (any scene that starts a `Match`).

Control: the same no-LFS clone at base b30462a: smoke **PASS**.

## Expected
Brief (M6-V12 Part 1): "a missing file falls back to today's synthesized tone with one warning, so a clone without LFS still
boots"; watch-out: "the SFX fallback tone must keep every other scene green in that state"; docs/03 "Implementation (M6-V12)":
"A clone without `git lfs pull` so boots and every scene but `LookTest` stays green."

## Actual
The fallback itself works (each event plays its tone, one warning each), but before it, for each of the 16 clips:
```
ERROR: Cannot open file 'res://.godot/imported/select_1.wav-fe04...sample'.
ERROR: Failed loading resource: res://.godot/imported/select_1.wav-fe04...sample.
ERROR: Failed loading resource: res://assets/audio/sfx/ui/select_1.wav.
ERROR: Error loading resource: 'res://assets/audio/sfx/ui/select_1.wav'.
```
(`--import` already failed on the pointer: "Not a WAV file ... found 'vers'", "Can't save empty resource".) smoke.ps1 and
scene-loop.ps1 fail on any `ERROR` line, so the gate the brief names goes red in exactly the state it was meant to survive.

## Notes
`LoadVariants` guards with `ResourceLoader.Exists(path)`, which is true whenever the `.import` file exists (it is committed),
so `ResourceLoader.Load` runs and Godot logs the failure. A check that doesn't log is needed first, e.g. the imported
`.sample` path from the `.import`'s `dest_files` existing (`FileAccess.FileExists`), or the source starting with `RIFF`
rather than `version https://git-lfs` (read 4 bytes with `FileAccess`). `SfxTest`'s fallback row moves the file **and its
`.import`** aside, which is the one case that doesn't log, so it can't see this. A regression row: a no-LFS-style pointer
(a text file with the LFS header) in place of one clip, asserting no `ERROR` in the log (or a smoke run on a
`GIT_LFS_SKIP_SMUDGE` clone, as QA did).
