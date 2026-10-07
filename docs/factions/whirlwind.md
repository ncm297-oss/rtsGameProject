# The Whirlwind (Seven Cities / Army of the Apocalypse)

**Faction id:** `whirlwind` · **Milestone:** vertical slice (M3-M6) · **Role:** swarm

## Lore hook

In the deserts of Seven Cities, a prophesied uprising against the Malazan occupiers rises around
the goddess of the Whirlwind, whose storm wraps the holy desert of Raraku. Tribes, bandits,
priests, and fanatics gather into the Army of the Apocalypse: huge, fast, poorly armored, and
burning with faith. The vertical slice pits them against the Malazans, the clash at the heart of
the uprising.

## Playstyle

Numbers and speed. Cheap, slightly fragile units that arrive early and keep coming. Raids worker
lines with Horse Raiders, overwhelms with Raiders and Zealots, and blinds the enemy with
Sandstorms so they can't focus fire. No ranged siege: buildings fall to Battering Rams under the
cover of the swarm.

## Faction bonus: Army of the Apocalypse

- All units **cost 20% less** (Gold and Wood).
- All units have **10% less HP** than their template equivalent.
- Workers **gather 15% faster**.

Cost and HP changes are already applied in the table below. The gather bonus is a faction
modifier in `faction.json`.

## Faction upgrade: Dryjhna's Prophecy

Age II, at the Smithy, 200 G / 150 W, 45 s. Zealots **+20 HP**; Sandstorm **cooldown 45 → 30 s**.

## Units

| Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Speed | Sight | Cost (G/W) | Pop | Train | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Camp Follower | 36 | 0 | Light | 4 | Melee | 1.5 | melee | 4.0 | 14 | 40 / 0 | 1 | 12 | Holy Camp |
| Line | Raider | 108 | 1 | Heavy | 11 | Melee | 1.5 | melee | 3.4 | 14 | 48 / 16 | 1 | 18 | Raider Camp |
| Ranged | Desert Archer | 50 | 0 | Light | 7 | Pierce | 1.8 | 14 | 3.6 | 18 | 32 / 36 | 1 | 20 | Archer Camp |
| Shock | Horse Raider | 135 | 0 | Mounted | 11 | Melee | 1.6 | melee | 6.6 | 18 | 72 / 24 | 2 | 26 | Horse Lines |
| Caster | Priest of the Whirlwind | 54 | 0 | Light | 9 | Magic | 2.2 | 12 | 3.2 | 16 | 80 / 40 | 2 | 30 | Shrine of the Whirlwind |
| Siege | Battering Ram | 240 | 6 | Heavy | 60 | Siege | 3.0 | melee | 2.4 | 10 | 160 / 120 | 3 | 36 | Ram Yard |
| Unique | Zealot | 63 | 0 | Light | 9 | Melee | 1.0 | melee | 4.4 | 14 | 30 / 10 | 1 | 10 | Raider Camp |

### Unit notes

- **Raider:** ×1.5 vs Mounted, ×1.2 vs Heavy (flails wrap around shields). Lighter armor and a
  bit faster than template line infantry.
- **Desert Archer:** fires faster but hits softer than the template. Fast on foot.
- **Horse Raider:** ×1.5 vs Light. The fastest template unit in the vertical slice; built to raid
  worker lines and run.
- **Priest of the Whirlwind:** splash 1.5 m, detector 10 m. Signature ability **Sandstorm**.
- **Battering Ram:** attacks buildings only. Very high armor shrugs off arrows (pierce does
  1 damage); melee units kill it.
- **Zealot (unique, Age II):** cheap, fast, and frenzied. Passive **Frenzy of the Apocalypse**:
  below 50% HP gains Frenzied (+50% attack speed). Passive **Martyrdom**: when a Zealot dies,
  Zealots within 6 m gain Frenzied for 5 s.

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Duration | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Sandstorm | Priest of the Whirlwind | Target ground (zone) | 18 | 6 m | 1.2 s | 45 s | 12 s | Non-Whirlwind units inside are Blinded and Slowed 30%. Enemies outside can't see into the storm. Whirlwind units are unaffected |

Sandstorm is the signature: drop it on the enemy army as the swarm engages, so enemy ranged units
can't shoot beyond 3 m and the army can't retreat quickly.

## Buildings

| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Provides | Requires |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | Holy Camp | `whirlwind_holy_camp` | 2400 | 5 | 275 / 275 | 90 | 4×4 | +10 pop, drop-off, trains Camp Follower, researches Age II | — |
| House | Tent | `whirlwind_tent` | 500 | 3 | 0 / 50 | 20 | 2×2 | +8 pop | — |
| Camp | Supply Cache | `whirlwind_supply_cache` | 600 | 3 | 0 / 75 | 25 | 2×2 | Drop-off | — |
| Infantry Hall | Raider Camp | `whirlwind_raider_camp` | 1200 | 4 | 0 / 150 | 40 | 3×3 | Trains Raider, Zealot | — |
| Ranged Hall | Archer Camp | `whirlwind_archer_camp` | 1200 | 4 | 0 / 150 | 40 | 3×3 | Trains Desert Archer | — |
| Shock Hall | Horse Lines | `whirlwind_horse_lines` | 1200 | 4 | 75 / 150 | 45 | 3×3 | Trains Horse Raider | Raider Camp |
| Forge | Smithy | `whirlwind_smithy` | 1000 | 4 | 100 / 100 | 40 | 3×3 | Upgrades, Dryjhna's Prophecy | — |
| Caster Hall | Shrine of the Whirlwind | `whirlwind_shrine` | 1200 | 4 | 150 / 150 | 50 | 3×3 | Trains Priest of the Whirlwind | Age II |
| Siege Works | Ram Yard | `whirlwind_ram_yard` | 1400 | 4 | 150 / 200 | 55 | 3×3 | Trains Battering Ram | Age II |
| Watch Tower | Lookout Tower | `whirlwind_lookout_tower` | 800 | 5 | 50 / 125 | 35 | 2×2 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II |

Building stats follow the template in [02-game-design.md](../02-game-design.md#buildings).
`Content/BuildingContentTests` and `UnitContentTests` pin this page to the data.

## Strengths and weaknesses

- **Strong:** early pressure, map-wide speed, cheap reinforcements, Sandstorm neutralizes ranged
  armies and casters.
- **Weak:** splash damage (Telas Fire, Catapults, Sappers) shreds clumped low-HP units. No ranged
  siege, so a well-defended choke with towers is hard to crack. Loses long, even trades.

## AI build order sketch (Normal)

```
Workers: 18 in Age I, 26 in Age II (fewer workers, more army).
Start:   3 workers on gold, 2 on wood.
pop 6    Tent
pop 8    Raider Camp
pop 9    Supply Cache at the nearest forest
pop 11   Tent
pop 12   Archer Camp
pop 14   Horse Lines
~4:30    First raid: 6 Raiders + 2 Horse Raiders, target workers, retreat at 50% losses
pop 16+  a Tent every ~7 pop
~7:30    Age II (later than Malazan; spends on army first)
Age II   Shrine, Ram Yard, Dryjhna's Prophecy, Zealots from 2 Raider Camps
Expand   2nd Holy Camp at ~9:00 regardless of threat (relies on army presence)
```

- **Composition target (by pop):** 30% Raider, 20% Desert Archer, 15% Horse Raider, 20% Zealot,
  10% Priest, 5% Battering Ram.
- **Attack waves:** first at 14 army pop; then continuous pressure, a new wave every 2 minutes.
  Priests cast Sandstorm on the enemy army's center as the melee engages.
- **Defense:** counter-attack the enemy base with Horse Raiders when its army is away.

## Art notes

- **Palette:** ochre, sand, sun-bleached white robes, crimson sashes and paint. Team color on
  sashes, banners, and shields.
- **Silhouettes:** lean and loose: veils, head-wraps, curved blades and flails. Horse Raiders on
  small, light horses. Priests in tall headdresses with a staff trailing ribbons. Zealots
  bare-chested, red-painted, twin knives, so they read differently from Raiders at a glance.
  Battering Ram as a hide-covered ram on wheels.
- **Likely sources:** KayKit Adventurers (Barbarian and Rogue bases recolored), Quaternius horse,
  Kenney Castle Kit / Quaternius Ultimate Fantasy RTS for buildings (tent and mudbrick variants
  may need AI generation or Blender kit-bashing). Details in [04-art-pipeline.md](../04-art-pipeline.md).

## Codename → release name

| Internal codename | Proposed release name |
| --- | --- |
| The Whirlwind / Army of the Apocalypse | The Storm Faithful |
| Seven Cities | The Sun Coast |
| Priest of the Whirlwind | Storm Priest |
| Shrine of the Whirlwind | Storm Shrine |
| Dryjhna's Prophecy | The Prophecy |
| Raraku | The Holy Desert |
| Raider, Desert Archer, Horse Raider, Battering Ram, Zealot, Camp Follower, other buildings | Keep (generic) |
