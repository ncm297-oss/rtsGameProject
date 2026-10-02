# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-02 (studio setup)._

## Where we are

Planning is done and all planning-phase open items are decided (high ground: yes; everything
else: default). The repo holds docs only. No `sim/`, `game/`, or solution exists yet.

## Next task: M0-1, environment check and solution skeleton

**Precondition:** `dotnet --list-sdks` shows 8.0.x and `& $env:GODOT --version` shows 4.7.x
mono. Both were installed on 2026-10-02 (.NET SDK 8.0.425, Godot 4.7.2 .NET, `GODOT` user
variable set). If a check fails anyway, STOP and put it on "Waiting on you". Never install
software from an unattended session.

**Scope** (M0 acceptance criteria in [docs/05-roadmap.md](../docs/05-roadmap.md#m0--environment--skeleton)):

1. `RtsGame.sln` at the repo root.
2. `sim/Rts.Sim` (`net8.0` class library, nullable, warnings as errors, no Godot reference) with
   a `SimInfo.Version` constant.
3. `sim/Rts.Sim.Tests` (xUnit) with one test for `SimInfo.Version`.
4. `game/project.godot` (Godot 4.7, Forward+, C#, `dotnet/project/solution_directory` = `..`),
   `game/RtsGame.csproj` (Godot.NET.Sdk matching the installed Godot version) referencing
   `Rts.Sim`, and `game/scenes/Main.tscn` with a script that prints `SimInfo.Version` on ready.
5. Verify the build, test, import, and headless-smoke commands in CLAUDE.md's Commands block;
   fix the ones that don't work. Skip the windowed run (it never exits unattended) and
   `tools/export.ps1` (arrives in M6).
6. If `$GODOT` is empty in your shell, read it with
   `[Environment]::GetEnvironmentVariable('GODOT','User')` and report it (the session env is
   supposed to provide it via `.claude/settings.local.json`).

Wire project references by editing the `.csproj` files directly (`dotnet add package` asks for
approval and nobody is there to answer; `dotnet new` templates are fine).

**QA focus:** a fresh `git clone` of the branch into a temp folder builds from scratch, `dotnet test`
green, headless Godot boot exits 0 and prints the version, `Rts.Sim` really has no Godot
reference (inspect the csproj and the built assembly's references), warnings-as-errors is on.

**Done when:** the 7 required M0 checkboxes can be ticked, with evidence in the QA report.
(The Godot MCP item is optional and doesn't count toward completion.)

## Watch out for

- Godot generates `.godot/` and may rewrite `project.godot` formatting on first open; commit the
  result once, then keep it stable.
- `Godot.NET.Sdk` version must match the installed editor exactly (4.7.x).
