# Malazan Empire

**Faction id:** `malazan` · **Milestone:** vertical slice (M3-M6) · **Role:** balance reference

## Lore hook

A sprawling, pragmatic empire that conquered half a world with professional soldiers rather than
heroes. Its legions mix heavy infantry, crossbows, mages attached to every army (the cadre), and
marines and sappers armed with alchemical munitions bought from the Moranth. Malazan soldiers
are cynical, tired, and very good at their jobs. Their sappers are famous for blowing up the
enemy, and occasionally themselves.

## Playstyle

The "standard" faction: solid at everything, best at nothing. Disciplined combined arms. Holds a
line with Heavy Infantry, kills from behind it with Crossbowmen, flanks with Wickan Lancers, and
cracks fortifications or clumped armies with Sappers. Stats sit closest to the template so every
other faction can be balanced against it.

## Faction bonus: Imperial Drill

Infantry units (Heavy Infantry, Crossbowman, Sapper) **train 20% faster and cost 10% less Gold**.
Already applied in the table below.

## Faction upgrade: Moranth Supply

Age II, at the Armory, 200 G / 150 W, 45 s. Sapper **Cusser cooldown 45 → 30 s**; Catapult
**range +4 m**.

## Units

| Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Speed | Sight | Cost (G/W) | Pop | Train | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Laborer | 40 | 0 | Light | 4 | Melee | 1.5 | melee | 4.0 | 14 | 50 / 0 | 1 | 12 | Garrison Keep |
| Line | Heavy Infantry | 130 | 3 | Heavy | 10 | Melee | 1.5 | melee | 3.0 | 14 | 54 / 20 | 1 | 14 | Legion Barracks |
| Ranged | Crossbowman | 55 | 0 | Light | 9 | Pierce | 2.2 | 15 | 3.2 | 18 | 36 / 45 | 1 | 16 | Crossbow Range |
| Shock | Wickan Lancer | 150 | 1 | Mounted | 12 | Melee | 1.8 | melee | 6.2 | 16 | 90 / 30 | 2 | 26 | Wickan Corral |
| Caster | Cadre Mage | 60 | 0 | Light | 9 | Magic | 2.2 | 12 | 3.2 | 16 | 100 / 50 | 2 | 30 | Cadre Tower |
| Siege | Catapult | 220 | 4 | Heavy | 50 | Siege | 5.0 | 24 (min 6) | 2.2 | 18 | 200 / 150 | 3 | 40 | Engineers' Yard |
| Unique | Sapper | 70 | 1 | Light | 20 | Siege | 3.0 | 8 | 3.4 | 16 | 72 / 40 | 1 | 20 | Engineers' Yard |

### Unit notes

- **Heavy Infantry:** ×1.5 vs Mounted. Passive **Shield Wall**: +3 armor against pierce attacks
  while not moving. Slightly slower than the template line.
- **Crossbowman:** ×1.3 vs Heavy (bolts punch through armor). Slower rate of fire than a bow.
- **Wickan Lancer:** ×1.5 vs Light. Passive **Charge**: the first attack after moving at least
  8 m deals +10 damage.
- **Cadre Mage:** splash 1.5 m, detector 10 m. Signature ability **Telas Fire** (below).
- **Catapult:** splash 2.5 m, **friendly fire**. Can't fire at targets closer than 6 m.
- **Sapper (unique, Age II):** throws **Sharpers**, small munitions with 2.0 m splash and
  **friendly fire**. Ability **Cusser** (below). Fragile; wants an escort.

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Telas Fire | Cadre Mage | Target ground | 16 | 3 m | 0.8 s | 25 s | Enemy units in the area are Burning: 10 magic damage/s for 4 s. No effect on buildings |
| Cusser | Sapper | Target ground | 6 | 3.5 m | 1.0 s | 45 s | 120 siege damage in the area, full damage to buildings (≈355 to a Town Hall), friendly fire at 50% |

Telas Fire is the faction's signature caster ability: a short, decisive area denial that punishes
clumped Heavy units. Cusser is the unique unit's identity: a satchel charge that deletes
buildings and can wreck your own army if you're careless.

## Buildings

| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Provides | Requires |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | Garrison Keep | `malazan_garrison_keep` | 2400 | 5 | 275 / 275 | 90 | 4×4 | +10 pop, drop-off, trains Laborer, researches Age II | — |
| House | Billet | `malazan_billet` | 500 | 3 | 0 / 50 | 20 | 2×2 | +8 pop | — |
| Camp | Quartermaster's Depot | `malazan_depot` | 600 | 3 | 0 / 75 | 25 | 2×2 | Drop-off | — |
| Infantry Hall | Legion Barracks | `malazan_barracks` | 1200 | 4 | 0 / 150 | 40 | 3×3 | Trains Heavy Infantry | — |
| Ranged Hall | Crossbow Range | `malazan_crossbow_range` | 1200 | 4 | 0 / 150 | 40 | 3×3 | Trains Crossbowman | — |
| Shock Hall | Wickan Corral | `malazan_wickan_corral` | 1200 | 4 | 75 / 150 | 45 | 3×3 | Trains Wickan Lancer | Legion Barracks |
| Forge | Armory | `malazan_armory` | 1000 | 4 | 100 / 100 | 40 | 3×3 | Upgrades, Moranth Supply | — |
| Caster Hall | Cadre Tower | `malazan_cadre_tower` | 1200 | 4 | 150 / 150 | 50 | 3×3 | Trains Cadre Mage | Age II |
| Siege Works | Engineers' Yard | `malazan_engineers_yard` | 1400 | 4 | 150 / 200 | 55 | 3×3 | Trains Catapult, Sapper | Age II |
| Watch Tower | Watchtower | `malazan_watchtower` | 800 | 5 | 50 / 125 | 35 | 2×2 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II |

Building stats follow the template in [02-game-design.md](../02-game-design.md#buildings).

`Content/BuildingContentTests` and `UnitContentTests` pin this page to the data.

## Techs

| Id | Name | Researched at | Cost (G/W) | Time (s) | Requires | Effects |
| --- | --- | --- | --- | --- | --- | --- |
| `moranth_supply` | Moranth Supply | Armory | 200 / 150 | 45 | Age II | Sapper Cusser cooldown 45 → 30 s; Catapult range +4 m |

The faction upgrade is the only faction-specific tech. Age II and the six shared Forge upgrades are in
[02-game-design.md "Forge upgrades"](../02-game-design.md#forge-upgrades). `Content/TechContentTests` pins this
table and the "Faction upgrade" line above to the data.

## Strengths and weaknesses

- **Strong:** flexible, no hard counter. Cheap, fast infantry keeps up pressure. Sappers make
  sieges short.
- **Weak:** no standout mobility (Lancers are only template-fast). Friendly fire punishes sloppy
  play. Crossbows fire slowly, so swarms (Whirlwind) can close the gap.

## AI build order sketch (Normal)

```
Workers: train continuously to 20 in Age I, 30 in Age II.
Start:   3 workers on gold, 2 on wood.
pop 6    Billet
pop 8    Quartermaster's Depot at the nearest forest
pop 10   Legion Barracks (rally to base choke)
pop 12   Billet
pop 14   Crossbow Range
pop 16   Armory -> Melee Weapons I
pop 18+  a Billet every ~7 pop
~6:30    Age II
Age II   Cadre Tower, Engineers' Yard, Wickan Corral, Moranth Supply, Watchtower at the choke
Expand   2nd Garrison Keep when army >= 12 pop and no enemy army near base for 60 s
```

- **Composition target (by pop):** 35% Heavy Infantry, 30% Crossbowman, 10% Wickan Lancer,
  10% Cadre Mage, 10% Sapper, 5% Catapult.
- **Attack waves:** first at 20 army pop; then every 3 minutes or when army pop ≥ 1.3× the last
  wave. Sappers target buildings; Mages cast Telas Fire on the largest enemy clump.
- **Defense:** pull the army home when buildings take damage; Laborers flee to the Garrison Keep.

## Art notes

- **Palette:** dark steel grey, oxblood red cloth, dull bronze trim. Team color on shields and
  cloaks.
- **Silhouettes:** Heavy Infantry carry big rectangular shields; Crossbowmen are lighter with a
  visible crossbow; Wickan Lancers are lean riders with long lances, braids, and feathers; Cadre
  Mages wear hooded robes, no armor; Sappers wear leather aprons with munition satchels and a
  crouched stance.
- **Likely sources:** KayKit Adventurers (Knight for Heavy Infantry, Rogue for Crossbowman, Mage
  for Cadre Mage, Rogue_Hooded for Sapper, plus weapon accessories), a Quaternius horse for the
  Lancer's mount, Kenney Castle Kit siege weapons for the Catapult, Quaternius Ultimate Fantasy
  RTS for buildings. Details in [04-art-pipeline.md](../04-art-pipeline.md).

## Codename → release name

| Internal codename | Proposed release name |
| --- | --- |
| Malazan Empire | The Iron Legions |
| Wickan Lancer | Steppe Lancer |
| Wickan Corral | Steppe Corral |
| Cadre Mage | Battle Mage |
| Cadre Tower | Mage Tower |
| Telas Fire | Wildfire |
| Moranth munitions / Moranth Supply | Alchemist's munitions / Alchemist's Supply |
| Sharper | Fire-pot |
| Cusser | Satchel Charge |
| Heavy Infantry, Crossbowman, Catapult, Sapper, Laborer, all buildings | Keep (generic) |
