# Teblor

**Faction id:** `teblor` · **Milestone:** M7 · **Role:** giants

## Lore hook

Mountain clans of giants, twice the height of lowlanders, who live by raiding, feuds, and an
honor code that outsiders find terrifying. Their warriors carry huge swords carved from
bloodwood and rub blood-oil into their skin before battle. They count their kills, prize their
war-dogs and their massive horses, and consider the "children" of the lowlands to be weak,
numerous, and contemptible.

## Playstyle

Few, huge, slow to build, very hard to kill. A Teblor army is a handful of giants that regenerate
between fights and crush anything that meets them in melee. Each loss hurts. They win by making
every engagement a brawl on their terms and by using Blood-oil Frenzy at the decisive moment.
They lose to kiting, focus fire, and magic.

## Faction bonus: Children of the Mountains

- Units are **1.5× scale** (visual; collision radius 0.75 m, still under the half-cell cap).
- Combat units cost **2-3 population**; workers cost 1.
- All units **regenerate 1 HP/s** after 6 s without taking damage.
- Units **train 25% slower** than the template (already in the table).
- Combat units use the **Giant** armor class: pierce and magic deal ×1.25 to them.

## Faction upgrade: Blood-oil Anointing

Age II, at the Bloodwood Carver, 200 G / 150 W, 45 s. Warriors and Destrier Riders gain
**+2 armor**; Blood-oil Frenzy **duration +4 s**.

## Units

| Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Speed | Sight | Cost (G/W) | Pop | Train | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Gatherer | 70 | 0 | Light | 6 | Melee | 1.5 | melee | 4.0 | 14 | 75 / 0 | 1 | 16 | Longhouse |
| Line | Warrior | 320 | 2 | Giant | 22 | Melee | 1.8 | melee | 3.4 | 16 | 120 / 30 | 2 | 28 | War Lodge |
| Ranged | Javelin Thrower | 140 | 1 | Giant | 12 | Pierce | 2.6 | 11 | 3.4 | 18 | 70 / 50 | 2 | 26 | Javelin Ground |
| Shock | Destrier Rider | 360 | 2 | Mounted | 24 | Melee | 2.0 | melee | 5.6 | 16 | 160 / 60 | 3 | 36 | Stables & Kennels |
| Caster | Shaman | 130 | 0 | Giant | 10 | Magic | 2.4 | 10 | 3.4 | 16 | 130 / 60 | 2 | 36 | Shaman's Circle |
| Siege | Breaker | 380 | 3 | Giant | 40 | Siege | 3.0 | melee | 3.0 | 14 | 180 / 120 | 3 | 45 | Breaker's Hall |
| Unique | War-dog pack (3 dogs) | 70 each | 0 | Mounted | 7 | Melee | 1.0 | melee | 6.4 | 16 | 120 / 0 per pack | 1 each | 24 | Stables & Kennels |

### Unit notes

- **Gatherer:** carries 15 instead of 10. Expensive, tough enough to survive a light raid.
- **Warrior:** ×1.25 vs Mounted. The core of the army: long strides make it faster than human
  infantry, and it out-trades two or three template line units.
- **Javelin Thrower:** weak, short-ranged, but lets Teblor hit flying units and kite a little.
- **Destrier Rider:** ×1.5 vs Light. A giant on a huge horse; armor class is Mounted, so line
  infantry counters it.
- **Shaman:** splash 1.5 m, detector 10 m. Signature ability **Blood-oil Frenzy**.
- **Breaker:** a giant with a bloodwood maul. Siege damage; can also hit units (at siege
  multipliers, so poorly). No siege engine needed.
- **War-dog pack (unique, Age II):** one queue item spawns 3 normal-sized dogs (1 pop each).
  Passive **Hamstring**: each bite Slows the target 20% for 2 s, so the pack pins kiting units
  for the giants to catch.

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Duration | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Blood-oil Frenzy | Shaman | Self / aura | — | 8 m | 0.5 s | 40 s | 10 s | Allied units in the radius are Frenzied (+40% attack speed, +20% damage). When it ends they are Slowed 20% for 4 s |

The aftermath slow is the trade-off: Frenzy wins fights, but a Frenzied army can't chase or
escape right afterward.

## Ranged unit or pure melee? (decided 2026-10-02: Javelin Thrower)

Kept for the record, in case playtests reopen it.

| Option | Pros | Cons |
| --- | --- | --- |
| **Javelin Thrower, weak (chosen)** | Template stays complete (Ranged Hall, AI comps, UI); Teblor can hit the Andii Great Raven; some kiting answers | Less distinctive |
| No ranged unit at all | Strong asymmetry, very Teblor | Empty Ranged Hall slot needs special cases in UI/AI; only Shamans and towers can hit flying units; hard to answer kiting |

## Buildings

| Slot | Name | Id |
| --- | --- | --- |
| Town Hall | Longhouse | `teblor_longhouse` |
| House | Hut | `teblor_hut` |
| Camp | Woodcutters' Lodge | `teblor_woodcutters_lodge` |
| Infantry Hall | War Lodge | `teblor_war_lodge` |
| Ranged Hall | Javelin Ground | `teblor_javelin_ground` |
| Shock Hall | Stables & Kennels | `teblor_stables` |
| Caster Hall | Shaman's Circle | `teblor_shamans_circle` |
| Siege Works | Breaker's Hall | `teblor_breakers_hall` |
| Forge | Bloodwood Carver | `teblor_bloodwood_carver` |
| Watch Tower | Stone Cairn | `teblor_stone_cairn` |

Building stats follow the template. Models are 1.25× scale visually but keep template footprints.

## Strengths and weaknesses

- **Strong:** wins any even melee fight; regenerates between fights, so harassment doesn't stick;
  Frenzy turns close fights into routs.
- **Weak:** few units, so losing one is expensive; kiting ranged armies, magic, and focus fire;
  slow to rebuild after a lost fight; big units are easy to see coming.

## AI build order sketch (Normal)

```
Workers: 16 in Age I, 24 in Age II.
Start:   3 workers on gold, 2 on wood.
pop 6    Hut
pop 7    War Lodge
pop 9    Woodcutters' Lodge
pop 11   Hut
~5:00    First attack: 4 Warriors, target the nearest enemy production building
pop 15   Bloodwood Carver -> Armor I
pop 16+  a Hut every ~6 pop
~7:00    Age II
Age II   Shaman's Circle, Stables & Kennels, Breaker's Hall, Blood-oil Anointing
Expand   2nd Longhouse when army >= 16 pop
```

- **Composition target (by pop):** 45% Warrior, 15% Javelin Thrower, 15% Destrier Rider,
  10% Shaman, 10% Breaker, 5% War-dogs.
- **Attack waves:** first at 8 army pop (4 Warriors); then whenever army pop ≥ 24. Retreat to
  regenerate when the army is below 40% total HP. Shamans cast Frenzy at engagement.

## Art notes

- **Palette:** earth brown, bloodwood red-brown, bone white, slate grey, furs. Team color on
  painted skin patterns and hair bindings.
- **Silhouettes:** 1.5× scale is the identity. Long hair, bare arms, huge single-edged wooden
  swords. Destrier Riders on oversized horses. Shamans with antlers or bone headdresses.
  Breakers carry a log-sized maul. War-dogs are big, shaggy, normal-scale dogs.
- **Likely sources:** KayKit Adventurers Barbarian scaled 1.5× and recolored, Quaternius horse
  scaled up, war-dogs from the Quaternius Ultimate Animated Animal Pack if it has a suitable dog
  or wolf, otherwise AI-generated. Details in [04-art-pipeline.md](../04-art-pipeline.md).

## Codename → release name

| Internal codename | Proposed release name |
| --- | --- |
| Teblor | The Bloodwood Clans |
| Warrior | Clan Warrior |
| Blood-oil Frenzy / Blood-oil Anointing | Rage-oil Frenzy / Rage-oil Anointing |
| Gatherer, Javelin Thrower, Destrier Rider, Shaman, Breaker, War-dog, all buildings | Keep (generic) |
