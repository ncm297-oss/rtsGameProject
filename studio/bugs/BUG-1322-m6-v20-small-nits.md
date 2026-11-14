# BUG-1322: M6-V20 small nits (model node name on reuse, repeated team-less warning, stray empty file in the main checkout)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-13-1118, task M6-V20 (QA) |
| System | unit views (models), studio process |
| Fixed by | |

## Repro and actual
1. **A reused slot's model is not named `Model`.** docs/03 "Implementation (M6-V20)" says the slot node's child is `Model`.
   When a slot switches from one model scene to another, `UnitViews.SetType` calls `QueueFree` on the old child and adds
   the new one in the same frame. The old one still holds the name, so Godot gives the new one an auto name
   (`@Node3D@<n>`). Seen in `QaM6V20Test` row 4 (laborer, then crossbowman, then laborer in one slot). Nothing reads the
   name, so this is cosmetic. Fix: rename or `RemoveChild` the old one before freeing it.
2. **The team-less model warning repeats on every `Bind`.** Found by reading the code, not run. `UnitModels.Load` caches
   a scene that loaded, but `FindTeam` runs (and warns) in every `UnitModels` constructor, that is at every match start.
   docs/03 says "one warning". Only a future model without a `team` material would hit this.
3. **A stray empty file in the owner's main checkout:** `C:\Dev\rtsGameProject\ModelShots.cs` (0 bytes, untracked,
   created 2026-11-13 12:07, during the M6-V20 developer run). It is outside the studio worktree. It looks like a
   redirect or copy that went to the wrong directory. An owner's `git add -A` would commit it. QA did not touch it,
   because it is outside QA's working directory.

## Expected
Docs and behaviour agree; no files outside the worktree.

## Notes
None of these affects play.
