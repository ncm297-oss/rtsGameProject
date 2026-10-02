# Shadow (Tiste Edur / Hounds of Shadow)

**Faction id:** `shadow` · **Milestone:** M8 · **Role:** ambush and raids

## Lore hook

The realm of Shadow is a shattered warren of dusk, ruled from a throne of shadow and patrolled
by its Hounds: enormous, near-unkillable beasts that hunt in packs. Its mortal children are the
grey-skinned Tiste Edur, seafaring warrior clans whose sorcerers, the K'risnan, draw on the
fractured power of Emurlahn. Demons serve Shadow too: the long-limbed Aptorians stalk as its scouts.

## Playstyle

Hit-and-run and map control. Shadow forces vanish into forests, wait for the enemy to walk past,
and strike from stealth. Shadow Archers hold forest edges invisibly; Aptorian Stalkers raid and
scout; K'risnan Shadow-step whole squads onto exposed casters and siege; the Hounds of Shadow
run down anything that tries to flee. Head-on, in open ground, Shadow is ordinary or worse.

## Faction bonus: Children of Shadow

Non-siege units that stand **in or next to a forest cell** and haven't attacked or taken damage
for 3 s become **Stealthed**. Standard detection rules apply (detectors, and any enemy within
3 m). See [02 Stealth and detection](../02-game-design.md#stealth-and-detection).

## Faction upgrade: Wraith-binding

Age II, at the Blackwood Forge, 200 G / 150 W, 45 s. K'risnan Sorcerers gain **Summon Wraiths**.

## Units

| Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Speed | Sight | Cost (G/W) | Pop | Train | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Thrall | 40 | 0 | Light | 4 | Melee | 1.5 | melee | 4.0 | 14 | 50 / 0 | 1 | 12 | Edur Longhall |
| Line | Edur Warrior | 115 | 2 | Heavy | 11 | Melee | 1.5 | melee | 3.4 | 14 | 60 / 20 | 1 | 18 | Warrior Lodge |
| Ranged | Shadow Archer | 50 | 0 | Light | 9 | Pierce | 2.0 | 15 | 3.4 | 18 | 45 / 50 | 1 | 22 | Archer's Shade |
| Shock | Aptorian Stalker | 140 | 1 | Mounted | 12 | Melee | 1.6 | melee | 6.4 | 20 | 95 / 30 | 2 | 26 | Shadow Kennels |
| Caster | K'risnan Sorcerer | 60 | 0 | Light | 9 | Magic | 2.2 | 12 | 3.2 | 16 | 100 / 60 | 2 | 30 | K'risnan Sanctum |
| Siege | Edur Ram | 220 | 6 | Heavy | 55 | Siege | 3.0 | melee | 2.6 | 10 | 150 / 120 | 3 | 36 | Ram Shed |
| Unique | Hound of Shadow | 220 | 2 | Mounted | 16 | Melee | 1.4 | melee | 6.8 | 18 | 150 / 50 | 3 | 32 | Shadow Kennels |
| Summoned | Wraith | 40 | 0 | Light | 8 | Magic | 1.5 | melee | 4.5 | 12 | — | 0 | — | Summon Wraiths |

### Unit notes

- **Edur Warrior:** ×1.5 vs Mounted. Slightly lighter and faster than template line infantry.
- **Shadow Archer:** passive **Shadowed**: becomes Stealthed after 3 s without moving or
  attacking, anywhere (not just forests). The first shot from stealth deals +50% damage.
- **Aptorian Stalker:** ×1.5 vs Light. A demon scout with long sight (20 m). Raids and spots.
- **K'risnan Sorcerer:** splash 1.5 m, detector 10 m. Signature ability **Shadow-step**; gains
  **Summon Wraiths** with Wraith-binding.
- **Edur Ram:** attacks buildings only. Shares the Battering Ram's base model, dressed in
  blackwood and dark sailcloth (saves art time).
- **Hound of Shadow (unique, Age II):** passive **Pack Hunter**: +10% damage for each other Hound
  within 8 m (max +30%). **At most 7 Hounds alive** at once, as in the legend.
- **Wraith (summoned):** temporary, 20 s lifetime, no cost or pop, can't be stealthed.

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Shadow-step | K'risnan Sorcerer | Target ground | 12 | 4 m (gather radius) | 1.0 s | 40 s | The caster and allied non-siege units within 4 m teleport to the target point, keeping their relative positions (blocked cells snap to the nearest free cell) |
| Summon Wraiths | K'risnan Sorcerer (after Wraith-binding) | Summon | — | — | 1.0 s | 50 s | Summons 3 Wraiths beside the caster for 20 s |

## Buildings

| Slot | Name | Id |
| --- | --- | --- |
| Town Hall | Edur Longhall | `shadow_longhall` |
| House | Hearth-house | `shadow_hearth_house` |
| Camp | Thrall Camp | `shadow_thrall_camp` |
| Infantry Hall | Warrior Lodge | `shadow_warrior_lodge` |
| Ranged Hall | Archer's Shade | `shadow_archers_shade` |
| Shock Hall | Shadow Kennels | `shadow_kennels` |
| Caster Hall | K'risnan Sanctum | `shadow_sanctum` |
| Siege Works | Ram Shed | `shadow_ram_shed` |
| Forge | Blackwood Forge | `shadow_blackwood_forge` |
| Watch Tower | Shadow Spire | `shadow_spire` |

Building stats follow the template in [02-game-design.md](../02-game-design.md#buildings).

## Strengths and weaknesses

- **Strong:** controls forested maps, chooses its fights, punishes armies that march without
  detection, Hounds finish fleeing units, Shadow-step bypasses front lines.
- **Weak:** open maps (Raraku) take away its bonus; detectors (towers, casters, Ravens) hard-counter
  stealth; template-level stats lose straight-up fights.

### Balance risk to watch

Watch Towers and casters arrive in Age II, so before that only the 3 m proximity rule detects
stealth. If forest stealth dominates the early game in playtests, the first fixes to try are, in
order: move the Watch Tower to Age I for all factions, raise the stealth delay from 3 s to 5 s,
or limit forest stealth to units that are standing still.

## AI build order sketch (Normal)

```
Workers: 20 in Age I, 28 in Age II.
Start:   3 workers on gold, 2 on wood.
pop 6    Hearth-house
pop 8    Thrall Camp
pop 10   Warrior Lodge
pop 12   Archer's Shade
pop 13   Hearth-house
pop 15   Shadow Kennels
~5:00    Raid: 3 Aptorian Stalkers hit the enemy wood line, retreat when an army appears
         Archers take ambush positions in forests along the enemy's likely attack path
~7:00    Age II
Age II   K'risnan Sanctum, Hounds of Shadow, Wraith-binding, Ram Shed
Expand   2nd Longhall next to a forest when army >= 14 pop
```

- **Composition target (by pop):** 25% Edur Warrior, 30% Shadow Archer, 10% Aptorian Stalker,
  10% K'risnan Sorcerer, 20% Hounds of Shadow, 5% Edur Ram.
- **Attack waves:** first at 18 army pop. Prefers to engage near forests; Shadow-steps onto
  enemy casters and siege; disengages if losing and re-hides.

## Art notes

- **Palette:** ash grey skin, slate, dusk violet, dark teal accents, blackwood. Team color on
  cloaks and banners. Stealthed units render as a dark, semi-transparent shimmer for their owner.
- **Silhouettes:** Edur are tall and slender with long dark hair. Aptorians are spindly,
  long-limbed, insect-like demons. Hounds of Shadow are horse-sized dogs with glowing eyes.
  Wraiths are translucent skeletal figures.
- **Likely sources:** KayKit Adventurers recolored grey for the Edur; KayKit Skeletons with a
  ghost shader for Wraiths; Hounds as a Quaternius wolf scaled 1.6× with a dark material if
  it reads well, otherwise AI-generated; Aptorian AI-generated. Details in
  [04-art-pipeline.md](../04-art-pipeline.md).

## Codename → release name

| Internal codename | Proposed release name |
| --- | --- |
| Shadow / Tiste Edur | The Shade Kin |
| Edur Warrior | Shade Warrior |
| Edur Longhall / Edur Ram | Shade Longhall / Shade Ram |
| K'risnan Sorcerer / K'risnan Sanctum | Shade Priest / Shade Sanctum |
| Aptorian Stalker | Shade Stalker |
| Hound of Shadow | Shade Hound |
| Emurlahn (warren) | (drop) |
| Thrall, Shadow Archer, Wraith, Shadow-step, Wraith-binding, other buildings | Keep (generic) |
