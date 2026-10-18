# BUG-0950: A clone without LFS objects prints 64 ERROR lines per match start (the clips' failed loads): smoke FAIL, every match scene red

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (runtime path; residual gate quirk is BUG-0955) |
| Found | 2026-10-17-2344, task M6-V12 |
| System | view: `game/scripts/Sfx.cs` (`LoadVariants`) |
| Fixed by | M6-V12 fix round 1 (9555c93): `Sfx.ClipLoadable` (RIFF header + imported file named by the `.import` exist, via `FileAccess`) before `ResourceLoader.Load`; regression row `SfxTest.FallbackWhenMissing` (an LFS pointer with a dangling `.import` under `user://`) |

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

## Re-verify (2026-10-17-2344, fix round 1, QA)
- Fresh `GIT_LFS_SKIP_SMUDGE=1` clone at 9555c93: the menu and `--skirmish` passes of smoke print **0** case-sensitive
  `ERROR` lines (was 64), 12 `Sfx: no loadable clip file` warnings, `Match started:` and the banner.
  `scene-loop -Filter QaM26*` PASS (was the repro's FAIL), `AlertTest` PASS in the same clone.
- With LFS (worktree): smoke PASS, `SfxTest` PASS ("no-LFS Alert pointer -> tone True, 12 warning(s) for 12 events"),
  every real clip `ClipLoadable`.
- **Correction to the control above:** a *true* no-LFS clone of base b30462a (the env var exported for the checkout too;
  QA's first control smudged the looktest art on checkout) fails smoke on its first run as well (37 lines from the
  looktest PNG / FBX / glTF pointers), and passes from the second run on. smoke.ps1 has never been green on a fresh
  no-LFS clone. smoke on the fixed branch still fails in that state on every run, for reasons outside the runtime
  code: filed separately as **BUG-0955 (S3)**. A no-LFS clone is not a supported state (CLAUDE.md: binaries go through
  LFS on both machines; `LookTest` and `SfxTest` need the art), and what the brief asked for (boots, tones, one warning
  per event, no runtime ERROR) holds, so this bug's S2 is closed.
