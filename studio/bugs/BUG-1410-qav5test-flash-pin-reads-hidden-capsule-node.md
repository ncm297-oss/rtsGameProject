# BUG-1410: `QaV5Test` phase 2's flash check reads the slot node, which never draws once the Heavy Infantry is a model

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M7-V5, QA verified 2026-11-14-2156: on a scratch clone with a model's flash overlay never cleared, QaV5Test FAILs "phase 2: new unit inherited a flash"; reverted, PASS) |
| Found | 2026-11-14-1246, task M6-V21 (QA) |
| System | view tests (unit models) |
| Fixed by | M7-V5 (session 2026-11-14-2156, view): `QaV5Test` phase 2 reads `ModelMeshOf(slot) ?? ViewOf(slot)` and `ShownLit`; red on a scratch copy whose model flash is never cleared (the old check stayed green there), green on the branch; `QaV5Test` added to docs/03 M6-V21's list |

## Repro
1. `game/tests/QaV5Test.cs` line 203, phase 2 (a freed slot reused at once by a new Heavy Infantry):
   `Check(!_units.Flash.IsLit(reborn.Index) && view.MaterialOverlay != _units.FlashMaterial, "phase 2: new unit inherited a flash");`
   where `view = _units.ViewOf(reborn.Index)` (line 198) and `_hi = malazan_heavy_infantry` (line 63).
2. Since M6-V21 the Heavy Infantry is drawn as a model. `UnitViews.SetType` clears the slot node's `MaterialOverlay` and
   `Sync` puts the flash on the model's mesh (`ModelMeshOf`), never on the slot node. So the second half of the check is
   always true: it no longer looks at anything that is drawn.

## Expected
The brief's scope (handoff, M6-V21): "any scene that reads one of these four types' slot mesh or material either sets
`UnitViews.UseModels = false` ... or reads `ModelMeshOf`; no pin value changes." `CombatViewTest` was changed this way
(`_units.ModelMeshOf(i) ?? _units.ViewOf(i)`); `QaV5Test` should be too, and docs/03 "Implementation (M6-V21)" should list it.

## Actual
The row still passes, but only `Flash.IsLit` (the flash state, not what's drawn) is actually checked. docs/03's list of
scenes that read the model instead names `CombatViewTest`, `QaM22Test` and `TeblorViewTest` and leaves this one out.

## Notes
- The product is right: QA's `QaM6V21Test` row (5) does this check on the model's mesh. A Heavy Infantry dies while
  flashing, and its slot is reused at once by a Heavy Infantry (same model) and then by a Raider (another model). In
  both cases the new unit's model mesh has no overlay and `ShownLit` is false on its first `Sync`. This bug is only about
  the old pin, which now checks less than it claims to.
- Fix: `MeshInstance3D drawn = _units.ModelMeshOf(reborn.Index) ?? view;` and test `drawn.MaterialOverlay`, plus
  `_units.ShownLit(reborn.Index)`. Add `QaV5Test` to the docs/03 M6-V21 list.
