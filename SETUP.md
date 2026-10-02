# Setup

How to get a Windows machine ready to work on this project. Written so the owner can follow it by
hand, or so Claude Code on that machine can run each command with the owner's approval.

Package ids were checked against winget on 2026-10-02. Versions move on; the ids don't.

| Tool | winget id | Version checked | Needed for |
| --- | --- | --- | --- |
| Git | `Git.Git` | 2.55 | Everything |
| Git LFS | `GitHub.GitLFS` | 3.8 | Binary assets (Git for Windows usually bundles it already) |
| Godot .NET build | `GodotEngine.GodotEngine.Mono` | 4.7.2 | M0 onward |
| .NET 8 SDK | `Microsoft.DotNet.SDK.8` | 8.0.425 | M0 onward |
| Node.js LTS | `OpenJS.NodeJS.LTS` | 24.x | Optional: Godot MCP server |
| Blender | `BlenderFoundation.Blender` | 5.2 | Optional: asset touch-ups (M6) |
| GitHub CLI | `GitHub.cli` | 2.10x | Optional: PRs and issues from the terminal |

## 1. Location: keep the repo out of sync folders

Clone to **`C:\Dev\rtsGameProject`**, not inside OneDrive, Dropbox, or Documents. Godot's `.godot/`
cache and the .NET `bin/`/`obj/` folders create thousands of small files that sync clients fight
over. GitHub is the source of truth between machines.

## 2. Git and Git LFS

```powershell
winget install --id Git.Git --exact --source winget
winget install --id GitHub.GitLFS --exact --source winget
```

Open a **new** terminal (so `PATH` refreshes), then set up LFS and your identity once per machine:

```powershell
git lfs install
git config --global user.name "Nick Mitchell"
git config --global user.email "you@example.com"   # the email on your GitHub account
git config --global init.defaultBranch main
```

## 3. Clone

Skip this on the machine where the repo was created (it's already at `C:\Dev\rtsGameProject`).

```powershell
git clone https://github.com/ncm297-oss/rtsGameProject.git C:\Dev\rtsGameProject
```

The repo is private: the first clone or push opens a browser window from Git Credential Manager
to sign in to GitHub. That's expected.

## 4. .NET 8 SDK

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact --source winget
```

New terminal, then check:

```powershell
dotnet --list-sdks     # expect a line starting with 8.0.
```

## 5. Godot 4.7 (.NET build)

```powershell
winget install --id GodotEngine.GodotEngine.Mono --exact --source winget
```

This is a portable zip install. Without admin rights winget can't create a `godot` command alias,
so point a `GODOT` environment variable at the **console** executable (it prints logs to the
terminal, which Claude needs for headless runs):

```powershell
$exe = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter "Godot_v4.7*_mono_win64_console.exe" | Select-Object -First 1 -ExpandProperty FullName
[Environment]::SetEnvironmentVariable("GODOT", $exe, "User")
$env:GODOT = $exe
& $env:GODOT --version   # expect 4.7.x.stable.mono...
```

The windowed editor is the same file name without `_console`, in the same folder. Pin it to the
Start menu if you want to open the editor by hand (not required: Claude edits scene files as text).

### Export templates (needed at M6, not before)

Exporting a Windows build needs the matching .NET export templates. Easiest: open the Godot
editor once → **Editor → Manage Export Templates → Download and Install**. Claude can script it
instead when the time comes.

## 6. Optional tools

### Godot MCP server (lets Claude launch the game, read its output, take screenshots)

Evaluate at M2, when there's something on screen to look at. The game will also have its own
`--screenshot` flag, so this is a convenience, not a requirement. Needs Node.js:

```powershell
winget install --id OpenJS.NodeJS.LTS --exact --source winget
```

Then, from a terminal where the `claude` CLI is available (on native Windows, `npx` servers need
the `cmd /c` wrapper):

```powershell
claude mcp add godot -e GODOT_PATH="$env:GODOT" -- cmd /c npx -y @coding-solo/godot-mcp
```

Candidates to compare at M2: [Coding-Solo/godot-mcp](https://github.com/Coding-Solo/godot-mcp)
(the one above: run project, debug output, scene tools) and others with runtime screenshot and
input support. Pick one; don't stack several.

### Blender (asset touch-ups, M6)

```powershell
winget install --id BlenderFoundation.Blender --exact --source winget
```

### A code editor for reading along

Any of: VS Code with the C# Dev Kit extension, Visual Studio 2022 Community, or JetBrains Rider
(free for non-commercial use). Not required for Claude's workflow.

## 7. Verify

Run from `C:\Dev\rtsGameProject` in a new terminal:

```powershell
git --version
git lfs version
git lfs track            # lists the model/texture/audio patterns from .gitattributes
dotnet --list-sdks
& $env:GODOT --version
```

All five should print versions or patterns without errors. Then open the folder in Claude Code and
start **M0** from [docs/05-roadmap.md](docs/05-roadmap.md).

## Daily routine across two machines

```powershell
git pull                 # before starting
# ... work ...
git add -A; git commit -m "M1: describe the change"; git push   # before stopping
```

If you forget to push on one machine, the other can't see the work. If both machines changed
the same file, git asks you to merge on pull; Claude can resolve it.
