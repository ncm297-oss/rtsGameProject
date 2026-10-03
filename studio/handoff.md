# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-03 08:26 (session 2026-10-03-0826, PLAN)._

## Where we are

Planning is done and all planning-phase open items are decided. The repo holds docs only. No
`sim/`, `game/`, or solution exists yet. Toolchain verified live on 2026-10-03: .NET SDK
8.0.425, Godot 4.7.2.stable.mono (console exe in `$env:GODOT`), Git 2.53, Git LFS 3.7.1.

## Current session plan

**TASK_ID:** M0-1 · **Title:** Create the solution skeleton and verify the toolchain commands

**Goal:** Turn the docs-only repo into a building, testing, headless-booting skeleton: a pure
.NET sim library, its xUnit tests, and a Godot 4.7 C# project that references the sim. This is
the whole of M0; every later milestone builds on this split.

**Scope:**
- `RtsGame.sln` at the repo root containing the three projects below.
- `sim/Rts.Sim/Rts.Sim.csproj`: `net8.0` class library, `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>12</LangVersion>`, no
  package or project references. One file `SimInfo.cs` with `public static class SimInfo` and a
  `public const string Version = "0.0.1";` plus a `///` summary.
- `sim/Rts.Sim.Tests/Rts.Sim.Tests.csproj`: xUnit, references `Rts.Sim`. One test asserting
  `SimInfo.Version` is non-empty / equals the expected value.
- `game/project.godot`: Godot 4.7 config, Forward+ renderer, C# enabled,
  `dotnet/project/solution_directory=".."`, main scene `res://scenes/Main.tscn`.
- `game/RtsGame.csproj`: `Godot.NET.Sdk/4.7.2`, `net8.0`, nullable on, `ProjectReference` to
  `../sim/Rts.Sim/Rts.Sim.csproj`.
- `game/scenes/Main.tscn` (text format) with `game/scripts/Main.cs` that calls
  `GD.Print($"Rts.Sim {SimInfo.Version}")` in `_Ready()`.
- Run `--import` once, then the headless smoke run; commit the resulting `project.godot`
  formatting once. `.godot/`, `bin/`, `obj/` must not be committed (already gitignored).
- Verify every command in CLAUDE.md's Commands block except the windowed run and
  `tools/export.ps1`. Fix wrong ones in CLAUDE.md in the same commit.
- Update `docs/03-technical-design.md` only if the real csproj/Godot settings differ from what
  it says (e.g. exact Sdk version string).
- OUT of scope: any gameplay code, `World`, RNG, data folders, `tools/`, the Godot MCP server,
  `tools/export.ps1`, installing anything, NuGet packages beyond the xUnit template defaults
  and `Godot.NET.Sdk`.

**Acceptance criteria:**
1. `dotnet build RtsGame.sln` exits 0 with zero warnings from `Rts.Sim`.
2. `dotnet test sim/Rts.Sim.Tests` passes (1 or more tests, 0 failed).
3. `sim/Rts.Sim/Rts.Sim.csproj` has `net8.0`, `Nullable` enable, `TreatWarningsAsErrors` true,
   and no `PackageReference`/`ProjectReference`; the built `Rts.Sim.dll` references no
   `GodotSharp` assembly.
4. `& $env:GODOT --headless --path game --import` exits 0.
5. `& $env:GODOT --headless --path game --quit-after 600` exits 0, the log contains
   `Rts.Sim 0.0.1`, and no line contains `ERROR`.
6. `git status` is clean after the smoke run (no untracked generated files, no rewritten
   `project.godot`).
7. CLAUDE.md Commands block matches what actually works; roadmap M0 criteria 1-7 are tickable
   with the above as evidence.

**Design references:** `docs/03-technical-design.md` "Platform and versions" and the project
table (lines 6-14, 51-59); `docs/05-roadmap.md` M0; CLAUDE.md architecture rule 1 and the
coding conventions (file-scoped namespaces, one type per file, `Rts.Sim.*` namespaces).

**Tests required:** `sim/Rts.Sim.Tests/SimInfoTests.cs` with one xUnit fact on
`SimInfo.Version`. No other tests exist yet, so no determinism/golden runs apply.

**Constraints most at risk:** rule 1 (no Godot in `Rts.Sim`), warnings-as-errors must be on
from the first commit, `dotnet add package` needs approval nobody can give (edit csproj by
hand; `dotnet new` templates are fine), `Godot.NET.Sdk` must be exactly 4.7.2, commit subject
`M0: ...`. Never install software; if a tool is missing, stop and report.

## QA focus

- Fresh `git clone` of the session branch into a temp folder: restore + build from scratch,
  `dotnet test` green, headless boot exits 0 and prints the version.
- Inspect `Rts.Sim.dll` references (e.g. `System.Reflection.Metadata` or `ildasm`-free check
  via a small script) for any `Godot*` assembly. Confirm `TreatWarningsAsErrors` really bites:
  add a throwaway unused-variable warning in a scratch copy and confirm the build fails.
- Run `--import` and the smoke run twice in a row; `git status` must stay clean (idempotent).
- Grep the smoke log for `ERROR`, `WARNING`, and `Unhandled`.
- Confirm `.godot/`, `bin/`, `obj/` are absent from the commit and `.gitattributes` LFS
  patterns still apply (`git lfs track`).

## Watch out for

- Godot generates `.godot/` and may rewrite `project.godot` formatting on first open; commit
  the result once, then keep it stable.
- `Godot.NET.Sdk` version must match the installed editor exactly (4.7.2).
- `.claude/settings.local.json` is absent in the studio worktree (gitignored); the session
  env still provides `$env:GODOT`. Fall back to
  `[Environment]::GetEnvironmentVariable('GODOT','User')` if the shell lacks it.
