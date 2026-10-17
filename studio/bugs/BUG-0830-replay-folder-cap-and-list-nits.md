# BUG-0830: Replay folder nits: junk files hold cap slots, future-dated names prune the new recording, no replay marker

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-16-2036, task M6-V9 |
| System | replay folder (`game/scripts/ReplayStore.cs`), Load Game, playback HUD |
| Fixed by | |

## Repro
`& $env:GODOT --headless --path game res://tests/QaM6V9Test.tscn` prints these as `QA M6-V9 NOTE:` lines (notes, not failures):

1. **Junk takes cap slots.** A folder holding 50 dated replays plus `junk.replay` (text) and `zero.replay` (0 bytes);
   a menu match saved into it. `ReplayStore.Prune` keeps 20 `.replay` files, but the two unreadable ones are among them
   (no stamp in the name, so `DateOf` falls back to the write time, which is "now"): **18 playable replays kept**, and the
   junk is not listed, so the player can't Delete it from Load Game. It stays in the cap for good.
2. **Future-dated names.** 20 replays named `2099...-seedN.replay`, then a new recording written with
   `ReplayStore.NewPath(dir, DateTime.Now, 1)` and pruned: **the file just saved is deleted at once** ("Replay saved" is
   still logged). The name stamp is local wall-clock time, so the same happens in small for the DST fall-back hour (a
   match at the second 01:30 sorts before one at the first 01:45), after a time-zone change, or with files copied from
   another machine.
3. **No replay marker.** In playback the screen looks like a live match (screenshot `shell-replay.png` from
   `ShellTest -- --shots`): nothing says "Replay", and right clicks / hotkeys do nothing without feedback.
4. **Load Game reads every file whole when it opens.** `ReplayFormat.TryReadFile` measured at about 20 ms/MB
   (1.0 MB 21 ms, 5.0 MB 100 ms, synthetic 72,000-tick files). At the cap, 20 one-hour files of "a few MB" each
   (docs/03) freeze the menu for 1-2 s each time Load Game opens. The 50 x 9 KB folder opened in 410-740 ms the first
   time (JIT) and in 23-40 ms after that.
5. Dev-log paths mix separators (`...replay-playback-test-5188\20261016-225409-seed1.replay` beside
   `C:/Users/...`): `Files` joins `GlobalizePath`'s forward slashes with `Directory.GetFiles`' backslashes.

## Expected
The brief says to keep at most N files and delete the oldest. That suggests the 20 kept should be the 20 newest
*playable* recordings, and never the one just saved.

## Actual
See the notes above. Every listed replay plays, and the cap never deletes a newer replay in place of an older one.

## Notes
Possible fixes: prune only files `TryReadFile` reads, or only names that match the stamp pattern; never delete the
path just written; take the stamp from UTC, or sort by the write time. A small "Replay" label from `ui.json` in
playback. Reading only the header for the list would remove item 4. None of this blocks M6-V9.
