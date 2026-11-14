# BUG-1380: M7-V4 build is not 0 / 0: two CS1998 warnings in the new BuildingScaleViewTest

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (verified by QA re-check, 2026-11-14-0616, fix round 1) |
| Found | 2026-11-14-0616, task M7-V4 (QA) |
| System | view tests (`game/tests/BuildingScaleViewTest.cs`), build hygiene |
| Fixed by | 8c34277 (M7-V4 fix round 1) |

## Repro
1. Worktree `studio/2026-11-14-0616-view` at 112b9af.
2. `dotnet build RtsGame.sln --no-incremental`

## Expected
Acceptance criterion 9 of M7-V4: "Build 0 / 0". The developer's report claims "build 0/0".

## Actual
```
game\tests\BuildingScaleViewTest.cs(218,24): warning CS1998: This async method lacks 'await' operators and will run synchronously. [game\RtsGame.csproj]
game\tests\BuildingScaleViewTest.cs(420,24): warning CS1998: This async method lacks 'await' operators and will run synchronously. [game\RtsGame.csproj]
    2 Warning(s)
    0 Error(s)
```
Line 218 is `private async Task SiteRise(...)` and line 420 `private async Task RememberedGhosts(...)`: neither awaits
anything. Both warnings come from the file this task added; the base commit 43ab896 builds 0 / 0. An incremental
build that doesn't recompile `RtsGame` hides them, which is probably how the report's "0/0" came about.

## Notes
A one-line fix each: drop `async` and return `Task.CompletedTask` (or make them plain `void` / `bool` methods and drop the
`await` at the call sites). Not a behaviour problem; filed S2 only because it is a stated acceptance criterion.

## Verification (QA re-check, fix round 1)
`SiteRise` and `RememberedGhosts` are now `private void`, with the two `await`s at their call sites removed. Neither
method awaited anything, so the old call already finished synchronously: the order and the checks are unchanged.
`dotnet build RtsGame.sln --no-incremental`: 0 Warning(s), 0 Error(s). The `BuildingScaleViewTest` log still prints
the site rise (100 frames, 0.9 to 5.955 m, 20 frames 0 bytes) and both ghost rows; PASS in the 104/104 scene loop.
