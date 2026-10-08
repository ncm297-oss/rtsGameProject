# BUG-0139: The Battering Ram attacks units; the Whirlwind page says it attacks buildings only

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-07-2014, task M4-1 |
| System | combat target rules (`CombatSystem.CanFight` / `PickTarget`) vs unit data |
| Fixed by | `9be1991` (M4-2a, session 2026-10-08-0313): `attack.targets` schema field (`AttackDef.Targets`, `all` / `units` / `buildings`, default `all`, bad value = `DataError`), respected by `PickTarget` (a buildings-only unit goes straight to the building list), retaliation, holders and `MayAttack`; the Battering Ram's `units.json` says `"targets": "buildings"` (golden `data-hash` regenerated once with replay format 4). Proof: `BatteringRam_AttacksBuildingsOnly_NeverAUnit` un-skipped and green, `AttackTargetsLoaderTests`, QA `AttackOrderQaTests` ram paths (never takes a unit by scan, Hold, attack-move or retaliation over 400 ticks of being hit; hits a Tent) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~BatteringRam_AttacksBuildingsOnly_NeverAUnit"`
2. A `whirlwind_battering_ram` stands 1.5 m from an enemy Laborer that holds position. Within 400 ticks the Laborer is
   dead (60 siege x 0.5 vs Light = 30 per hit, 40 hp).

## Expected
docs/factions/whirlwind.md: "**Battering Ram:** attacks buildings only." (docs/factions/shadow.md says the same of the
Edur Ram.)

## Actual
M4-1 lets every unit whose attack has a value and no projectile scan and swing (`CanFight`); the ram has a 0.5 m melee
attack and no projectile, so it scans for units first (they outrank buildings in the priority), kills workers and
soldiers, and also counts as a tier-1 "can attack" target for the enemy's priority.

## Notes
The data has no field saying "buildings only", so this needs a generic data hook (e.g. an attack `targets` list or
`buildingsOnly` flag in `units.json`, data track + loader) or, for this slice, a Producer call. Rule 6 forbids a
hard-coded id check. Until fixed, a Whirlwind ram in a real match is a 240-hp, armor-6 melee unit.

## Re-check 2026-10-07-2315 (QA, fix round 1)
Still reproduces (row stays skipped); docs/03 "The ram" records the deferral.
