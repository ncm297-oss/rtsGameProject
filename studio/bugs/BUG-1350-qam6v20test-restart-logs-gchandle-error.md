# BUG-1350: QaM6V20Test logs "Handle is not initialized" ERROR after a forced-collection restart sample (1 in 20)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-13-2306, task M6-VH7 (QA) |
| System | view tests (QaM6V20Test row 8), Godot .NET GC handles |
| Fixed by | |

## Repro
1. Fresh clone of `studio/2026-11-13-2306-view` at 4b8f1b6, `dotnet build RtsGame.sln`.
2. Run `& $env:GODOT --headless --path game res://tests/QaM6V20Test.tscn` 20 times (6-9 other studio
   `testhost` / Godot processes were running).
3. 19 of 20 clean. Run 10 printed `QA M6-V20 TEST PASS` and exited 0, but logged an ERROR line, so the
   scene loop counts it as a red row.

## Expected
Criterion 1 of M6-VH7: `QaM6V20Test` green every run. No `ERROR` line.

## Actual
Right after `restart 2: nodes 555, resources 80, objects 3251, orphans 0` (that is, after the new
`GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` + 2 frames), the next match start logs:

```
ERROR: System.InvalidOperationException: Handle is not initialized.
   at System.Runtime.InteropServices.GCHandle.FromIntPtr(IntPtr value)
   at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(IntPtr oldGCHandlePtr, IntPtr* outNewGCHandlePtr, godot_bool createWeak)
   ...
   [5] Godot.Resource Godot.ResourceLoader.Load(string, string, Godot.ResourceLoader+CacheMode)
   [7] T Godot.GD.Load<T>(string)
   [8] Godot.ShaderMaterial Rts.Game.FogOfWar.TerrainMaterial() (game/scripts/FogOfWar.cs:129)
   [9] void Rts.Game.TerrainView.Build(...) (game/scripts/TerrainView.cs:36)
```

The object counts were exactly flat (3,251) in all 4 samples of all 20 runs, so the BUG-1323 count
fix itself holds. Log: QA scratchpad `v20runs\run10.log`.

## Notes
- The ERROR comes from Godot's C# bridge swapping the GC handle of a cached resource (`terrain_fog.gdshader`)
  whose wrapper was just collected. The forced collection added for BUG-1323 is the most likely trigger, but it
  is **not proven**: a scratch probe that loads that shader, drops it, force-collects and reloads 300 times
  (cache held / not held, 0 or 2 frames between) logged 0 errors. It has not been seen in any earlier report.
- If it is the forced collection, the brief's other option (sample 12+ cycles, compare the halves' minimums,
  no forced GC) avoids it. If it is a general Godot .NET race, it could also fire in a real match start
  after a natural GC; then it is not a test-only problem.
- Seen only under heavy load so far; not measured with nothing else running.
