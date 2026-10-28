# BUG-1060: M6-VH4 nits: bug index not updated, BUG-0521's status is not open / fixed / wontfix, a fresh no-LFS clone's first smoke is still red

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-26-2108, task M6-VH4 (QA) |
| System | `studio/bugs/README.md` index, `studio/bugs/BUG-0521-m4-vh3-nits.md`, `tools/qa/smoke.ps1` on a `GIT_LFS_SKIP_SMUDGE` clone |
| Fixed by | Item 1: the Producer at the 2026-10-26-2108 ACCEPT (the index rows). Item 2: M6-V16, BUG-0521's status word is now `wontfix`. Item 3: decided in M6-V16, wontfix by the brief's word: "from the second run is enough" (see "M6-V16" below) |

## Repro
1. `grep -nE "BUG-(0521|0922|0954|0955|0970|1002|1030|1031|1032)" studio/bugs/README.md`: every row still says `open`,
   although the nine files say `fixed` (or closed). The dev's report says so (deviation g). It is the same slip as
   BUG-1032 item 2.
2. `studio/bugs/BUG-0521-m4-vh3-nits.md` Status: `closed (item 1 fixed M5-V1, item 3 fixed M6-VH1, item 2 wontfix: unreachable)`.
   The README allows `open` -> `fixed` or `wontfix` "with the Producer's reason". The brief delegated the close ("close with
   reason"), and the reason is written down, but the status word is neither of the two.
3. `GIT_LFS_SKIP_SMUDGE=1 git clone <studio-view worktree> viewqa2108-nolfs` (01fc036), then
   `powershell -File tools/qa/smoke.ps1` twice. The first run FAILs with 30 `log:` lines, all from the `looktest`
   PNG / FBX / glTF pointers (`Error loading image`, `ERR_FILE_CORRUPT`, `ufbx ... Unrecognized file format`, glTF JSON).
   The second run PASSes with `WARN: 16 .wav clips are LFS pointers (no 'git lfs pull'): 48 of their --import error lines skipped (BUG-0955)`.

## Expected
1. The index matches the files.
2. `wontfix` for item 2, with the reason (or the Producer's word).
3. The brief's criterion 4 says "smoke PASS (incl. no-LFS clone after BUG-0955)". Only the second run passes.

## Actual
See Repro. Item 3 is documented on purpose (docs/03 "Implementation (M6-VH4)", BUG-0955 "Fix"): base was red on a fresh
no-LFS clone's first run too, and the brief excluded importer changes. QA records it so the Producer can decide whether
"from the second run" is enough for criterion 4.

## Notes
- The wav skip only switches on when a `.wav` under `game/assets` is LFS pointer text (under 1 KB and starts with
  `version https://git-lfs`). On a normal clone the list is empty and nothing is skipped, so the gate is unchanged there.
- Brief item 5 listed BUG-0743 item 3 as a wontfix candidate. It was already fixed in M6-VH2 (2a822ca, verified by QA
  2026-10-16-0847), so nothing was needed. The dev's report doesn't mention it.

## M6-V16 (2026-10-27, dev, view)
- **Item 2 fixed:** `BUG-0521`'s Status is `wontfix` (items 1 and 3 were fixed in M5-V1 and M6-VH1; item 2 is the wontfix, its reason
  in that file's "Close (M6-VH4)"); its index row says the same.
- **Item 3 decided, not changed: a fresh no-LFS clone's `smoke.ps1` passes from its second run, and that is enough.** The M6-V16
  brief said to fix it only if it took a few lines of `smoke.ps1`, else to write this down and close it. It is not a few lines:
  the first run's 30 lines come from three importers (the image loader, ufbx, the glTF reader) on the `looktest` pointers, and
  some of them (`ERR_FILE_CORRUPT`, the glTF JSON parse errors) don't name the file, so they can't be tied to a pointer the way
  the wav lines are (BUG-0955); skipping them by text alone would also hide a real corrupt asset on a normal clone. Godot writes
  the failed imports' records on the first `--import`, so the second run is clean (QA's repro step 3). `tools/` is also the sim
  track's, so a bigger change there belongs to a sim or tools task. A clone without LFS objects is unsupported (CLAUDE.md puts
  every binary through LFS); the smoke prints its BUG-0955 WARN on such a clone so the cause is visible.
