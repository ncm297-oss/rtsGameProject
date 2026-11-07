# BUG-1093: `TeamsTest` failed the scene loop once with a GodotSharp "Handle is not initialized" error inside `GD.Load`

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (robust, cause not proven; M6-VH5 2026-11-06-0926: `Main.MatchScene` holds the PackedScene for the process, no `GD.Load` at a match start; two loops, 13 `TeamsTest` and 9 parallel `MapListTest` runs without a recurrence. Reopen on any recurrence) |
| Found | 2026-11-05-2148, task M6-V18 |
| System | Godot side: scene loop, `Main.StartMatch` (engine interop) |
| Fixed by | M6-VH5 ca99057 (`Main.MatchScene`; `MapListTest.MenuMaps` regression row) |

## Repro
1. `powershell -File tools/qa/scene-loop.ps1` on the view worktree at afba408 (81 scenes, 19 m 09 s, while other tracks'
   test hosts were running). `TeamsTest` printed `TEAMS TEST PASS` and exited 0, but the loop failed it on ERROR lines.
2. Alone, `scene-loop.ps1 -Filter TeamsTest` passed 5 / 5 (5.0 s each). It has not reproduced since.

## Expected
No ERROR lines in a scene's log.

## Actual
Three times, right after a match ended and the next `StartSkirmish` began (`TeamsTest.MixedStats` → `StatsOf` → `Start`):

```
Match stopped at tick 264
ERROR: System.InvalidOperationException: Handle is not initialized.
   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)
   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak)
   ...
       [7] T Godot.GD.Load<T>(string)
       [8] void Rts.Game.Main.StartMatch(Rts.Game.LaunchOptions, bool) (game/scripts/Main.cs:535)
       [9] void Rts.Game.Main.StartSkirmish(System.String[]) (game/scripts/Main.cs:527)
       [10] Rts.Game.Match Rts.Game.Tests.TeamsTest.Start(int, System.String[]) (game/tests/TeamsTest.cs:400)
```

## Notes
- M6-V18 doesn't touch `Main.cs` or `TeamsTest.cs`. No build ran during the loop. This looks like a GodotSharp interop race
  when a cached PackedScene's script instance handle is swapped (weak / strong) while the previous match's nodes are being
  freed, possibly made more likely by CPU load. It's in the same family as `FreeForAllTest`'s one-off 0xC06D007F start
  crash (handoff watch-outs).
- Worth watching across loops. If it recurs, check whether a `Match` freed with `QueueFree` is still referenced from C#
  (an event handler or a static) when the scene is loaded again.
