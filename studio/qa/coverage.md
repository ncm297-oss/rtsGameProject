# QA coverage map

Maintained by the QA inspector. One row per game system, one column per kind of test. Every
system should reach at least ✅ in Unit, Invariant fuzz, and Determinism before its milestone is
signed off; Scale and Soak apply from M1 and M5 respectively.

Legend: ✅ covered · 🟡 partial · ❌ missing · — not applicable yet

| System | Milestone | Unit | Scenario | Invariant fuzz | Determinism | Scale/perf | Soak | Visual | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Solution skeleton & build | M0 | ✅ | — | — | — | — | — | 🟡 | `SimInfoTests`; `QA/ArchitectureTests` (no Godot refs in Rts.Sim.dll, strict csproj settings, forbidden-API source scan); `tools/qa/smoke.ps1` gate (build + import + boot + log check). Fresh clone verified 2026-10-03. Visual: windowed boot only, empty scene. |
| Tick loop & command queue | M1 | — | — | — | — | — | — | — | |
| Data loader & validation | M1 | — | — | — | — | — | — | — | |
| Terrain levels & nav grid | M1 | — | — | — | — | — | — | — | |
| Flow fields & steering | M1 | — | — | — | — | — | — | — | |
| Replays | M1 | — | — | — | — | — | — | — | |
| Camera, selection, orders (view) | M2 | — | — | — | — | — | — | — | |
| Economy & buildings | M3 | — | — | — | — | — | — | — | |
| Combat & projectiles | M4 | — | — | — | — | — | — | — | |
| Fog, high ground, stealth | M4 | — | — | — | — | — | — | — | |
| Abilities, statuses, zones | M4 | — | — | — | — | — | — | — | |
| AI opponent | M5 | — | — | — | — | — | — | — | |
| Menus, save/load, export | M6 | — | — | — | — | — | — | — | |

M0-1 (2026-10-03-0826): skeleton row filled. The architecture scan in `QA/ArchitectureTests` covers every future sim file automatically.
