# BUG-1171: Tech and ability ids are not under the faction order: a third faction's tech or ability can renumber shipped ones

| Field | Value |
| --- | --- |
| Severity | S3 (latent: bites the day a faction's tech or ability id sorts among the shipped ones) |
| Status | open |
| Found | M7-3 (the developer), 2026-11-10 |
| System | data loader (`DataLoader.TechReader`, `DataLoader.BuildAbilities`), `GameData.Techs` / `Abilities` ids |
| Fixed by | |

## Repro
1. Shipped tech ids (one global ordinal table of `common/techs.json` + every faction's): `age_ii` 0, `armor_1` 1, `armor_2` 2,
   `dryjhnas_prophecy` (Whirlwind) 3, `melee_weapons_1` 4, `melee_weapons_2` 5, `moranth_supply` (Malazan) 6,
   `ranged_weapons_1` 7, `ranged_weapons_2` 8. Abilities: `cusser` (Malazan) 0, `sandstorm` (Whirlwind) 1, `telas_fire` (Malazan) 2.
2. Add a third faction whose ability id sorts before `telas_fire` (any `teblor_*` ability does: 'b' < 'l'), or whose tech id
   sorts before `ranged_weapons_2` (e.g. `blood_oath`).

## Expected
As unit and building types since M7-3: a faction appended to `common/factions.json` moves no shipped id.

## Actual
`telas_fire` (or the later techs) is renumbered, so every state hash, digest and golden `k` line that touches it moves.

## Notes
- No faction-grouped rule reproduces today's tech or ability ids (common and faction techs interleave; Malazan's abilities
  straddle Whirlwind's), so putting them under the order moves every hash once: a deliberate golden regen and pin sweep
  re-pin in one commit (the Producer's call; the M7-3 brief forbade a regen).
- Until then the data track must name a new faction's techs and abilities so they sort after every shipped one (techs:
  after `ranged_weapons_2`; abilities: after `telas_fire`, which no natural Teblor name guarantees). Better to decide the
  regen before D28 ships its Teblor abilities.
- `Data/FactionOrderTests.AddTeblor` works around it on the copy (no abilities, tech `teblor_supply`).
- QA 2026-11-10-1236 (M7-3): confirmed and pinned as a symptom row,
  `QA/FactionOrderQaTests.ATeblorAbility_NamedNaturally_RenumbersTelasFire_Bug1171Symptom`: a full Teblor copy appended
  last with abilities `teblor_telas_fire` / `teblor_cusser` moves `telas_fire`'s id; named `zteblor_*` it doesn't. When
  this is fixed, flip that row's first assertion.
- **Producer decision (ACCEPT 2026-11-10-1236, owner may revisit):** techs and abilities go under the faction order in the
  sim's next task (**M7-3b**): common techs first in ordinal order, then each faction's techs in faction order (ordinal
  within), and the factions' abilities faction by faction likewise; **one deliberate golden regen** (header and `k` lines)
  and a full pin sweep in the same commit, with the reason in `GatherWedgeQaTests.SameGameDataHashes`. The alternative
  (name every new faction's techs and abilities so they sort after the shipped ones) fails at M9 already (`andii_*` sorts
  before `armor_1`) and makes `blood_oil_frenzy` impossible. D28 (the Teblor roster) waits for M7-3b before it ships
  `abilities.json`; its units, buildings and `teblor_blood_oil_anointing` are safe today (that tech sorts after
  `ranged_weapons_2`), but the roster lands whole, after M7-3b.
