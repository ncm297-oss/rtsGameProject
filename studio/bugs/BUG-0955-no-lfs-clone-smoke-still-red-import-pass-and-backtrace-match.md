# BUG-0955: A clone without LFS objects still fails smoke.ps1 on every run: the wav importer retries the 16 pointers (48 ERROR lines per `--import`) and smoke counts warning backtrace frames

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-17-2344, task M6-V12 (fix round 1 re-verify; residual of BUG-0950) |
| System | Godot import of `game/assets/audio/sfx/**/*.wav` pointers; `tools/qa/smoke.ps1` log match |
| Fixed by | M6-VH4 (2026-10-26-2108): `tools/qa/smoke.ps1` matches as `scene-loop.ps1` (`-cmatch`, `[n]` frames skipped) and skips only the pointer clips' `--import` lines, with a WARN |

## Repro
1. `GIT_LFS_SKIP_SMUDGE=1 git clone --branch studio/2026-10-17-2344-view <repo> nolfs2` (9555c93).
2. `powershell -File nolfs2/tools/qa/smoke.ps1` -> FAIL; again with `-SkipBuild` -> FAIL (72 problem lines on the second run).
3. Of those: 48 from the `--import` pass, on every run (16 x "Not a WAV file ... found 'vers'", 16 x "Can't save empty
   resource to path 'res://.godot/imported/<clip>.sample'", 16 x `Condition "f.is_null()" is true`); 24 are backtrace frames
   of the 12 `Sfx: no loadable clip file` warnings (`[4]` / `[13] ... godot_variant_call_error ...`), which smoke's
   case-insensitive `-match 'ERROR'` counts. The menu and `--skirmish` passes have **0** case-sensitive `ERROR` lines
   (BUG-0950's runtime part is fixed), `Match started:` and the banner print.

Control (corrects the BUG-0950 report): a true no-LFS clone of base b30462a (`GIT_LFS_SKIP_SMUDGE=1` exported for the
checkout too) also fails smoke on its **first** run (37 lines: the `looktest` PNG / FBX / glTF pointers' import errors),
and passes from the second run on, because the texture and scene importers write `valid=false` into the `.import` and
stop retrying. The wav importer writes nothing, so it retries and fails on every run. QA's earlier "base PASS" was a
second run.

## Expected
Not a supported state: CLAUDE.md puts every binary through Git LFS and both of the owner's machines pull LFS objects;
`LookTest` needs the LFS art regardless; base already failed smoke on a fresh no-LFS clone's first run. What the brief
asked for, "a clone without LFS still boots" with tones and one warning per event, holds. So this is a gate quirk, not a
player-facing bug. docs/03 "Implementation (M6-V12)" states it under **Open** correctly.

In the same clone the scene loop: `QaM26Test` PASS and `AlertTest` PASS (both red/at risk before the fix), `SfxTest`
FAIL ("the real res://assets/audio/sfx/combat/cast_1.wav counts as not loadable"): it checks the real clips, so like
`LookTest` it needs the LFS objects. docs/03 names only `LookTest` there; `SfxTest`'s clip rows belong in that sentence.

## Notes
Options, none needed for M6-V12: (a) smoke.ps1 matches as `scene-loop.ps1` does (`-cmatch` and skip `^\s+\[\d+\]` frame
lines), which by itself would not make the no-LFS run green because of the import pass; (b) the import pass in smoke
could skip lines naming a file that is an LFS pointer (a `git lfs` aware filter in QA tooling); (c) `importer="keep"` for
the clips plus `AudioStreamWav.LoadFromBuffer` at runtime (a pipeline change, docs/04). Changing smoke's match to
case-sensitive is a loosening of an existing gate and needs the Producer's call, so QA has not edited it.

## Fix (M6-VH4, 2026-10-26-2108, dev)
- Option (a), the 2344 decision: `Test-Problem` matches `ERROR|Unhandled|Exception` case-sensitively and skips
  `^\s+\[\d+\]` backtrace frames, as `scene-loop.ps1` does.
- The import pass, without an importer change (option (c) stays declined): smoke finds the `.wav` files under
  `game/assets` that are LFS pointer text, and only when there are any it skips, in the `--import` output only, the lines
  `ERROR: Not a WAV file ... found 'vers'`, `ERROR: Can't save empty resource to path 'res://.godot/imported/<clip>.wav-<md5>.sample'`
  naming a pointer clip, and as many unnamed `Condition "f.is_null()" is true` lines as samples it skipped (the import is
  threaded, so they can't be paired by position). It prints `WARN: 16 .wav clips are LFS pointers ...: 48 of their
  --import error lines skipped (BUG-0955)`. A normal clone has no pointer and no line is skipped.
- The looktest art's pointers still fail a fresh no-LFS clone's **first** `--import` (Godot records their failure and
  doesn't retry); from the second run on smoke passes. That is base's behaviour, left as is: those errors are anonymous
  (`Condition "!success"`, `ufbx`, glTF) and the state is unsupported.
- docs/03 "Implementation (M6-V12)" **Open** paragraph now says fixed, and "Implementation (M6-VH4)" describes the skip.
