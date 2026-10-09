# BUG-0251: `EconomyViewTest.tscn` sometimes crashes at shutdown under load, after printing PASS (Godot .NET finalizer FATAL, exit -1073741795)

| Field | Value |
| --- | --- |
| Severity | S3 (flaky merge gate: the scene loop goes red) |
| Status | open |
| Found | 2026-10-08-2144, task M4-VH1 (QA); not caused by M4-VH1 as far as QA can tell, see Notes |
| System | view scene test `game/tests/EconomyViewTest.cs`; Godot 4.7.2 .NET shutdown |
| Fixed by | |

## Repro
1. Load the machine (for example `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` in any worktree), then run
   `& $env:GODOT --headless --path game res://tests/EconomyViewTest.tscn` 10-15 times.
2. QA 2026-10-08-2144 at 08dc8e3 under load: the scene loop under load failed `EconomyViewTest` (32 / 33). Separate
   runs crashed 1 of 8, 0 of 10, 2 of 15 and 0 of 15, about 6 % overall. The same HEAD with `SoundGap` put back to the
   old `CreateTimer` wait crashed 1 of 15. The base 4c1f168 ran 0 of 25 (10 + 15, same load).

## Expected
The scene exits 0 after `ECONOMY VIEW TEST PASS` with no ERROR line.

## Actual
```
ECONOMY VIEW TEST PASS
ERROR: FATAL: Condition "csharp_lang && !csharp_lang->script_bindings.is_empty()" is true.
   at: _instance_binding_free_callback (modules/mono/csharp_script.cpp:1220)
ERROR: Leaked unsafe reference to object: (res://scenes/Match.tscn):<PackedScene#-9223372005733038533>
   ... 91 "Leaked unsafe reference" lines (StandardMaterial3D, CapsuleMesh, ...)
Fatal error. System.Runtime.InteropServices.SEHException (0x80004005): External component has thrown an exception.
   at Godot.NativeInterop.NativeFuncs.godotsharp_internal_refcounted_disposed(IntPtr, IntPtr, Godot.NativeInterop.godot_bool)
   at Godot.GodotObject.Dispose(Boolean)
   at Godot.GodotObject.Finalize()
```
A second shape was also seen: `ERROR: FATAL: Condition "!rc_owner" is true.` Exit code -1073741795 (0xC000001D). The
.NET finalizer thread disposes RefCounted wrappers (the Match `PackedScene`, view meshes and materials still referenced
from C# at quit) while Godot is tearing the C# language down.

## Notes
- Not shown to be caused by M4-VH1. The crash happens after the test body, and also with the pre-change timer.
  Base 0 / 25 against head 5 / 64 is not significant enough to blame the change (p about 0.1).
- Possible fixes, all test-side: drop or `Dispose()` the scene's C# references to the Match `PackedScene` and view
  resources, then `GC.Collect(); GC.WaitForPendingFinalizers();` before `GetTree().Quit(...)`. Or free the Match node and
  wait a frame before quitting. Check whether other scenes leak the same way: none crashed in QA's runs, but every
  scene that instances `Match.tscn` probably holds the same references.
- If the scene loop goes red on `EconomyViewTest` with a FATAL after PASS, rerun it once before treating it as a
  regression.
