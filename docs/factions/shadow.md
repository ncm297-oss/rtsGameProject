# Shadow (Tiste Edur / Hounds of Shadow)

**Faction id:** `shadow` · **Milestone:** M8 · **Role:** ambush and raids

This page is written ahead of the roster (D-R38, 2026-11-14), in the shape the content pins read, as teblor.md was in
D-R36: `Content/PagePinReadinessTests` checks every section and column below (`ContentFactions.PagesAhead`), and the
files under `game/data/factions/shadow/` are copied from it the session the schemas below exist. Nothing under
`game/data/` carries the Shadow yet. Where the design asks for something no loader field holds, the page says so next to
it and lists it under "Schema gaps (D-R38)"; the player-facing words are under "Owner review (D-R38, proposed)", and the
Producer's answers to the page's open questions under "Decisions (D-R38)" at the end.

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

The rule is built (M8-1, docs/03 "Implementation (M8-1)") and read as docs/01 "2026-11-14 (d)" and "(e)" settle it: "next
to a forest cell" is the unit's own cell or one of the 8 round it holding a standing tree; "taken damage" is any loss of
hit points (a hit that takes none does not count); the 3 s counts from the moment an attack lands or a cast resolves;
gathering is not attacking, so a Thrall chopping at a forest edge stays hidden. In `faction.json` it is exactly the block
docs/03 "For the data track (M8-1)" gives:

```json
"forestStealth": { "status": "stealthed", "delay": 3, "exceptSlots": ["siege"] }
```

`stealthed` is the shipped status (M4-5), the delay the 3 s above (its first balance fix is 5), and `siege` the Edur Ram's
slot. The Wraith needs no entry: a summoned unit is never hidden by the rule.

## Faction upgrade: Wraith-binding

Age II, at the Blackwood Forge, 200 G / 150 W, 45 s. K'risnan Sorcerers gain **Summon Wraiths**.

## Units

| Slot | Unit | Id | HP | Armor | Class | Attack | Type | CD | Range | Targets | Speed | Sight | Cost (G/W) | Pop | Train | Spawn | Scale | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Thrall | `shadow_thrall` | 40 | 0 | Light | 4 | Melee | 1.5 | melee | all | 4.0 | 14 | 50 / 0 | 1 | 12 | 1 | 1 | Edur Longhall |
| Line | Edur Warrior | `shadow_edur_warrior` | 115 | 2 | Heavy | 11 | Melee | 1.5 | melee | all | 3.4 | 14 | 60 / 20 | 1 | 18 | 1 | 1 | Warrior Lodge |
| Ranged | Shadow Archer | `shadow_archer` | 50 | 0 | Light | 9 | Pierce | 2.0 | 15 | all | 3.4 | 18 | 45 / 50 | 1 | 22 | 1 | 1 | Archer's Shade |
| Shock | Aptorian Stalker | `shadow_aptorian_stalker` | 140 | 1 | Mounted | 12 | Melee | 1.6 | melee | all | 6.4 | 20 | 95 / 30 | 2 | 26 | 1 | 1 | Shadow Kennels |
| Caster | K'risnan Sorcerer | `shadow_krisnan_sorcerer` | 60 | 0 | Light | 9 | Magic | 2.2 | 12 | all | 3.2 | 16 | 100 / 60 | 2 | 30 | 1 | 1 | K'risnan Sanctum |
| Siege | Edur Ram | `shadow_edur_ram` | 220 | 6 | Heavy | 55 | Siege | 3.0 | melee | buildings | 2.6 | 10 | 150 / 120 | 3 | 36 | 1 | 1 | Ram Shed |
| Unique | Hound of Shadow | `shadow_hound` | 220 | 2 | Mounted | 16 | Melee | 1.4 | melee | all | 6.8 | 18 | 150 / 50 | 3 | 32 | 1 | 1 | Shadow Kennels |

**Targets** is `attack.targets` ("buildings" for the Edur Ram, as for the Whirlwind's Battering Ram; "all" for the rest,
which the file leaves out). **Spawn** is `spawnCount` (1 everywhere: no pack item) and **Scale** is `scale` (1 everywhere:
the Shadow are drawn at the template's size). The Hound of Shadow needs Age II (`requires` `["age_ii"]`, as every unique).

### Unit notes

- **Edur Warrior:** ×1.5 vs Mounted (docs/02's Line rule). Slightly lighter and faster than template line infantry.
- **Shadow Archer:** Passive **Shadowed**: becomes Stealthed after 3 s without moving or attacking, anywhere (not just
  forests). The first shot from stealth deals +50% damage. Neither is in the data yet (Schema gaps G1, G2).
- **Aptorian Stalker:** ×1.5 vs Light. A demon scout with long sight (20 m). Raids and spots.
- **K'risnan Sorcerer:** splash 1.5 m, detector 10 m. Signature ability **Shadow-step**; gains **Summon Wraiths** with
  Wraith-binding (below).
- **Edur Ram:** attacks buildings only (`attack.targets` `buildings`). Shares the Battering Ram's base model, dressed in
  blackwood and dark sailcloth (saves art time).
- **Hound of Shadow (unique, Age II):** Passive **Pack Hunter**: +10% damage for each other Hound within 8 m (max +30%).
  **At most 7 Hounds alive** at once, as in the legend. Neither is in the data yet (Schema gaps G3, G4).

### Summoned unit

The Wraith is not one of the seven template slots: Summon Wraiths makes it, nobody trains it. `units.json` holds one unit
per slot today and every unit needs a `trainedAt`, so it has no row yet (Schema gap G6). Its stats, for the row it gets:

| Unit | Id | HP | Armor | Class | Attack | Type | CD | Range | Targets | Speed | Sight | Lifetime | Summoned by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Wraith | `shadow_wraith` | 40 | 0 | Light | 8 | Magic | 1.5 | melee | all | 4.5 | 12 | 20 s | Summon Wraiths |

Temporary (20 s, the spawn effect's `lifetime`), no cost or pop (a summon charges neither), can't be stealthed (a summoned
unit never is, M4-5): all three hold today through the summon rules (docs/03 "Implementation (M4-4b-3b)").

### Against docs/02's template

Every number of the Units table equals docs/02 "Template baseline stats" for its slot except the ones below; the Thrall
is the template worker exactly, and the Unique (Hound of Shadow) and the Wraith have no template row. The numbers are the
page's since 2026-10-02; the "why" is the data track's reading (D-R38) for the owner to confirm.

| Unit | Stat | Page | Template | Why |
| --- | --- | --- | --- | --- |
| Edur Warrior | HP / Attack / Speed | 115 / 11 / 3.4 | 120 / 10 / 3.2 | Lighter and quicker line: an ambush closes from the forest edge before the enemy forms up |
| Shadow Archer | HP | 50 | 55 | Fragile in the open: its protection is stealth, not hit points |
| Shadow Archer | Attack / Range | 9 / 15 | 8 / 14 | Holds a forest edge and opens the fight from one step beyond the template archer's reach |
| Shadow Archer | Cost / Train | 45 / 50, 22 s | 40 / 45, 20 s | The stand-still stealth and first-shot bonus are paid for in cost and time |
| Aptorian Stalker | HP / CD | 140 / 1.6 | 150 / 1.8 | A raider, not a line breaker: less to kill, faster hits on workers |
| Aptorian Stalker | Speed / Sight | 6.4 / 20 | 6.0 / 16 | The faction's scout: sees and leaves before it is seen |
| Aptorian Stalker | Cost | 95 / 30 | 90 / 30 | Pays 5 gold for the sight and speed |
| K'risnan Sorcerer | Cost | 100 / 60 | 100 / 50 | Shadow-step and Summon Wraiths outweigh a damage spell |
| Edur Ram | Armor / Attack / CD / Range / Speed / Sight / Train | 6 / 55 / 3.0 / melee / 2.6 / 10 / 36 | 4 / 50 / 5.0 / 24 / 2.2 / 18 / 40 | A ram, not an engine: the Whirlwind Battering Ram's pattern (6 / 60 / 3.0 / melee / 2.4 / 10 / 36), a little lighter; no minimum range or splash, buildings only |
| Edur Ram | Cost | 150 / 120 | 200 / 150 | The same pattern: the Battering Ram costs 160 / 120 |

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Duration | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Shadow-step | K'risnan Sorcerer | Target ground | 12 | 4 m | 1.0 s | 40 s | — | The caster and own non-siege units within 4 m (the gather radius) teleport to the target point, keeping their relative positions (blocked cells snap to the nearest free cell) |
| Summon Wraiths | K'risnan Sorcerer | Summon | — | — | 1.0 s | 50 s | — | Summons 3 Wraiths beside the caster for 20 s (needs Wraith-binding) |

In `abilities.json`, as the schema holds them today:

- **Shadow-step:** `kind` `targetGround`, `range` 12, `radius` 4, `castTime` 1.0, `cooldown` 40, `affects` `own_units`, one
  effect `{ "kind": "teleport" }`. The loader refuses `teleport` ("not supported yet", `PlannedAbilityEffectKindIds`), and
  the siege exception, the kept formation and the snap have no field (Schema gap G5).
- **Summon Wraiths:** `kind` `summon`, no `range` and no `radius` (a summon whose effects are all `spawn`), `castTime` 1.0,
  `cooldown` 50, no `affects` (`own_units`), one effect `{ "kind": "spawn", "unit": "shadow_wraith", "count": 3, "lifetime": 20 }`
  (docs/03 "For the data track (M4-4b-3b)"). The 20 s is the spawn's `lifetime`, so the Duration column is "—". Two parts
  wait: the Wraith's unit row (G6) and the ability coming only with Wraith-binding (G7).

## Buildings

| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | Edur Longhall | `shadow_longhall` | 2400 | 2 | 275 / 275 | 90 | 4×4 | 12 | +10 pop, drop-off, trains Thrall, researches Age II | — |
| House | Hearth-house | `shadow_hearth_house` | 500 | 1 | 0 / 50 | 20 | 2×2 | 12 | +8 pop | — |
| Camp | Thrall Camp | `shadow_thrall_camp` | 600 | 1 | 0 / 75 | 25 | 2×2 | 12 | Drop-off | — |
| Infantry Hall | Warrior Lodge | `shadow_warrior_lodge` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Edur Warrior | — |
| Ranged Hall | Archer's Shade | `shadow_archers_shade` | 1200 | 2 | 0 / 150 | 40 | 3×3 | 12 | Trains Shadow Archer | — |
| Shock Hall | Shadow Kennels | `shadow_kennels` | 1200 | 2 | 75 / 150 | 45 | 3×3 | 12 | Trains Aptorian Stalker, Hound of Shadow | Warrior Lodge |
| Forge | Blackwood Forge | `shadow_blackwood_forge` | 1000 | 2 | 100 / 100 | 40 | 3×3 | 12 | Upgrades, Wraith-binding | — |
| Caster Hall | K'risnan Sanctum | `shadow_sanctum` | 1200 | 2 | 150 / 150 | 50 | 3×3 | 12 | Trains K'risnan Sorcerer | Age II |
| Siege Works | Ram Shed | `shadow_ram_shed` | 1400 | 2 | 150 / 200 | 55 | 3×3 | 12 | Trains Edur Ram | Age II |
| Watch Tower | Shadow Spire | `shadow_spire` | 800 | 2 | 50 / 125 | 35 | 2×2 | 24 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II |

Building stats are the template's in [02-game-design.md](../02-game-design.md#buildings), unchanged: HP, armor, cost,
build time, footprint, sight and the Watch Tower's attack and detector, in the slot order of the file. No Scale column:
the buildings are drawn at the template's size (`scale` absent, 1).

## Techs

| Id | Name | Researched at | Cost (G/W) | Time (s) | Requires | Effects |
| --- | --- | --- | --- | --- | --- | --- |
| `shadow_wraith_binding` | Wraith-binding | Blackwood Forge | 200 / 150 | 45 | Age II | K'risnan Sorcerer gains Summon Wraiths |

The faction upgrade is the only faction-specific tech, at docs/02's faction-upgrade price (200 G / 150 W, 45 s). Its one
effect, an ability granted to a unit, is no tech effect today (`stat` is `attack`, `armor`, `range`, `hp`,
`abilityCooldown` or `statusDuration`): Schema gap G7. The common techs are every faction's: Age II at the Edur Longhall,
and the six shared Forge upgrades at the Blackwood Forge ([02-game-design.md "Forge upgrades"](../02-game-design.md#forge-upgrades)).

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

## AI build order (Normal)

The AI opponent's `game/data/factions/shadow/ai.json` (docs/03 "For the data track (M5-1)" and "Implementation
(M5-8a)") will hold one **opening**, read the same way as malazan.md "AI build order (Normal)" (steps run side by side,
one command per step per think, in this order; **Count** is the `train` commands the step queues over its life). Easy,
Normal and Hard all play it, since there is one. The name and description below are what the player may read. The AI
starts with 5 Thralls, 3 on gold and 2 on wood, and keeps 20 workers in Age I and 28 in Age II (the sketch's). Not
measured: no opening is played before the roster lands, so every number below is the sketch's or the template rule's.

| # | Id | Name (shown to the player) | Description (shown to the player) |
| --- | --- | --- | --- |
| 1 | `standard` | Forest ambush | Warrior Lodge, Archer's Shade and Shadow Kennels first; a wave at 18 army pop, then Age II brings K'risnan Sorcerers and Hounds. |

Not in the file, because the schema has no field for it (Schema gaps G9-G13): the raid at about 5:00 (three Aptorian
Stalkers on the enemy wood line, falling back when an army appears), the Archers' ambush posts in forests on the enemy's
likely path, the expansion's condition and place (the sketch's "next to a forest when army ≥ 14 pop"; the step fires at its
time, near the nearest free gold mine), "prefers to engage near forests", "disengages if losing and re-hides", and
"Shadow-steps onto enemy casters and siege" (the AI's cast rules know no teleport). The Blackwood Forge step (22) is not in
the sketch: Wraith-binding is researched there, so a step must place it.

- **Retreat:** a wave that has lost 50% falls back; a unit under 30% hp counts as wounded (`retreat`; Hard pulls it
  back). The sketch gives none: these are the Malazan page's (the data track's proposal).
- **Later waves:** one wave at a time; a new one starts when none is out, at least `ai.waveCooldown` after the last
  began, at the opening's threshold, and never while the base is defended.
- **High starting resources** (docs/02 "Game flow" 2, docs/03 "Implementation (M6-S6)"): the AI
  plays the same opening as at Standard, but while it holds more gold + wood than the preset's spend-down threshold
  (`rules.json` `startingResources` `high` `aiSpendAbove`) it brings its next timed step forward, trains at every
  producer and leans its gatherers toward the kind it holds less of; Standard has no threshold, so it plays exactly as
  before. Not measured for the Shadow (not pinned): `standard` at High.
- **Defense** (the shared rule in Shadow names; docs/02 "AI opponent", docs/03 "Implementation (M5-4b)" and "(M5-4f)"): a
  threat starts when an own building (a site too) within the defend radius of the Edur Longhall is hit, or an enemy
  soldier (not a worker) is seen within it (`rules.json` `ai.defendRadius` 48 m; the threat holds `ai.defendFor` 20 s
  after the last sign). The AI answers with defenders worth `ai.defendRatio` 1.5 times the intruders' gold + wood cost, at
  least `ai.defendMinValue` 150 (a building hit with nobody seen gets 150), taken from the army at home first (nearest the
  defense point first), then from the members of a wave out (nearest the Edur Longhall first), each only while the
  members left keep the wave's start threshold (`attack.minArmyPop` and `minArmyCost` times the difficulty's wave size ×,
  so a wave sent above it gives its surplus). One Raider (64 gold + wood) is answered with 150 worth, e.g. two Edur Warriors from home (160), and a wave out keeps going.
  A raid worth more than the home army and the wave's spare units together calls the whole wave home (it ends like a
  retreat) and every free army unit answers. A hit that leaves the Edur Longhall below `ai.recallHallHp` 0.5 of its max hp
  does the same, however small the raid. Thralls within `ai.workerFleeRadius` (16 m) of the threat flee to the nearest
  finished drop-off away from the threat (the Edur Longhall or a Thrall Camp, whichever is nearest among those more than
  16 m from the threat; else the nearest one).

#### How the AI adapts

Three rules sit on top of every opening above, the counter shift, the starved charge and the far expansion, and two
notes go with the first: what answers what, and where the shift doesn't show (docs/02 "AI opponent"; docs/03
"Implementation (M5-9a)" and "(M5-9b)").

- **Counter shift:** the AI remembers how many enemy soldiers of each slot it has seen at once (workers never count, and
  it sees only what its own fog of war shows), and that memory fades to nothing over `rules.json` `ai.counterMemory`
  180 s unless a sighting at least as big renews it. When it picks its next unit, each unit of the opening's army mix
  weighs more by the share of that memory its slot answers, times the difficulty's `counterStrength` in `ai.json`: Easy
  0 (an Easy AI never adapts), Normal 0.5, Hard 1.0. A Shadow AI that has seen only cavalry weighs its Edur Warriors
  ×1.5 on Normal and ×2 on Hard. The shift tilts the army, it doesn't replace it: no unit is weighted below the
  opening's mix, and a unit the mix doesn't list is never added.
- **What answers what** (`rules.json` `ai.counters`, the "Countered by" column of docs/02 "Faction template", in this
  faction's units): enemy Line → K'risnan Sorcerer, Edur Ram; enemy Ranged → Aptorian Stalker; enemy Shock → Edur Warrior;
  enemy Caster → Aptorian Stalker, Shadow Archer; enemy Siege → Edur Warrior, Aptorian Stalker; enemy Unique → none.
- **Starved charge:** when the AI cannot pay for the unit it would train next and no income can close the gap, and it
  has at least `ai.starvedMinArmy` 1 combat unit free, then after `ai.starvedWaveAfter` 60 s in that state it sends
  every free combat unit at you whatever its wave threshold (or hunts, if it knows none of your buildings), again after
  each wave's cooldown, at every difficulty. A raid on its base resets the clock.
- **Far expansion:** when its own gold is spent (short of gold for its next unit, no Thrall on gold and no gold mine it
  knows), it sends a Thrall to look at the start locations nobody took. Once it has seen a free mine (at one of those
  starts or anywhere else) within `ai.farExpandReach` 384 m of its Edur Longhall, with no enemy drop-off by it, it
  builds a Thrall Camp (`shadow_thrall_camp`, 0 gold / 75 wood) beside it and moves gatherers there;
  an Edur Longhall only if it can pay for one. One at a time. The reach holds for every mine, a start location's
  included: a free start farther than that from the hall is not walked to, and a mine beyond it is never taken.
- **Where the shift doesn't show (BUG-1156):** a Warrior Lodge that already takes an Edur Warrior every time its queue
  empties cannot train more of them for the heavier weight, so against cavalry the shift shows only when the bank, not
  the lodge, is the limit (the Malazan pattern; not measured for the Shadow). An enemy Line army seen in Age I shifts
  nothing, since no opening's Age I mix lists a K'risnan Sorcerer or an Edur Ram.

"What answers what" is the shared rule applied to the Shadow units. The data track's proposal for Shadow-only answers is
under "Owner review (D-R38, proposed)" at the end of this page.

### Opening 1: `standard` (Forest ambush)

The sketch of 2026-10-02 as steps the `ai.json` schema holds: three halls in Age I (the Warrior Lodge and the Archer's
Shade are the two kinds Age II needs; the Shadow Kennels trains the raiders), Age II at 7:00, then a second Edur Longhall,
the K'risnan Sanctum, the Blackwood Forge, the Ram Shed and Wraith-binding. Hearth-houses as the Malazan page builds
them: two early, then one every 7 pop from pop 20, twelve in all.

| # | At | Do | Id | Count | Why |
| --- | --- | --- | --- | --- | --- |
| 1 | time 0 | train | `shadow_thrall` | 15 | Workers to 20 in Age I (5 at the start + 15) |
| 2 | pop 6 | build | `shadow_hearth_house` | 1 | First Hearth-house |
| 3 | pop 8 | build | `shadow_thrall_camp` | 1 | Thrall Camp by the nearest known forest |
| 4 | pop 10 | build | `shadow_warrior_lodge` | 1 | Warrior Lodge |
| 5 | pop 12 | build | `shadow_archers_shade` | 1 | Archer's Shade, Age II's second building kind |
| 6 | pop 13 | build | `shadow_hearth_house` | 1 | Second Hearth-house |
| 7 | pop 15 | build | `shadow_kennels` | 1 | Shadow Kennels (needs the Warrior Lodge): Aptorian Stalkers now, Hounds in Age II |
| 8 | pop 20 | build | `shadow_hearth_house` | 1 | From pop 20 a Hearth-house every 7 pop |
| 9 | pop 27 | build | `shadow_hearth_house` | 1 | |
| 10 | pop 34 | build | `shadow_hearth_house` | 1 | |
| 11 | pop 41 | build | `shadow_hearth_house` | 1 | |
| 12 | pop 48 | build | `shadow_hearth_house` | 1 | |
| 13 | pop 55 | build | `shadow_hearth_house` | 1 | |
| 14 | pop 62 | build | `shadow_hearth_house` | 1 | |
| 15 | pop 69 | build | `shadow_hearth_house` | 1 | |
| 16 | pop 76 | build | `shadow_hearth_house` | 1 | |
| 17 | pop 83 | build | `shadow_hearth_house` | 1 | The 12th Hearth-house: Longhall 10 + 12 × 8 = 106, past the 100 cap |
| 18 | time 420 | research | `age_ii` | 1 | Age II at 7:00 (needs two kinds: the Warrior Lodge and the Archer's Shade; the Kennels is a third) |
| 19 | time 420 | train | `shadow_thrall` | 8 | Workers to 28 in Age II |
| 20 | time 480 | expand | — | 1 | 2nd Edur Longhall near the nearest free gold mine, at Normal's `expandAfter` (480 s); the first Age II step, so it holds the reserve (teblor.md, D32) |
| 21 | time 510 | build | `shadow_sanctum` | 1 | K'risnan Sorcerers, once Age II (60 s) is in |
| 22 | time 540 | build | `shadow_blackwood_forge` | 1 | The Forge, for Wraith-binding (not in the sketch) |
| 23 | time 570 | build | `shadow_ram_shed` | 1 | Edur Rams |
| 24 | time 630 | research | `shadow_wraith_binding` | 1 | The faction upgrade, at the Blackwood Forge (40 s to build) |

**Army composition (`compositions`), weights by pop.** Age I lists only the units the Age I steps can train (the Hound
waits for Age II, the K'risnan Sorcerer and the Edur Ram for their halls), so its weights sum to 65. Hounds at 20% of an
army of 70 pop are about 5 (15 pop), under the cap of 7, which no field holds yet (G4).

| Unit | Age I | Age II |
| --- | --- | --- |
| `shadow_edur_warrior` | 25 | 25 |
| `shadow_archer` | 30 | 30 |
| `shadow_aptorian_stalker` | 10 | 10 |
| `shadow_krisnan_sorcerer` | — | 10 |
| `shadow_hound` | — | 20 |
| `shadow_edur_ram` | — | 5 |

- **Composition target (by pop):** 25% Edur Warrior, 30% Shadow Archer, 10% Aptorian Stalker,
  10% K'risnan Sorcerer, 20% Hound of Shadow, 5% Edur Ram.
- **Attack waves:** first at 18 army pop and 1,516 gold + wood (`attack`; the cost of 18 pop of the Age I mix, rounded
  down: Edur Warrior 80 a pop, Shadow Archer 95, Aptorian Stalker 62.5, weighted 25 / 30 / 10). Prefers to engage near
  forests; Shadow-steps onto enemy casters and siege; disengages if losing and re-hides (none of the three is in the file:
  G10, G12, G13).

## Difficulty

The `difficulties` block of the same `game/data/factions/shadow/ai.json` (docs/02 "AI opponent", docs/03 "For the data
track (M5-1)"): the same rows as every other faction's. Times are in seconds as the file writes them. **Build-order
slack** is how long a step waits after it is due; **wave size ×** scales the attack wave's thresholds above; **gather
bonus** is extra gather rate (docs/02: Hard's 40%); **expands after** is the earliest match time the AI expands. The
`counterStrength` of each row (Easy 0, Normal 0.5, Hard 1) is in "How the AI adapts" above.

| Difficulty | Thinks every (s) | Build-order slack (s) | Wave size × | Gather bonus | Focus fire | Pulls back wounded | Uses abilities | Expands after (s) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Easy | 2 | 30 | 0.6 | 0% | no | no | yes | 900 |
| Normal | 1 | 5 | 1 | 0% | no | no | yes | 480 |
| Hard | 0.5 | 0 | 1.25 | 40% | yes | yes | yes | 300 |

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

## Schema gaps (D-R38)

What each mechanic on this page needs from the loader on `main` (2026-11-14). Every mechanic of the faction bonus, the unit
notes, the summoned unit, the abilities, the techs and the AI sketch is listed once: either the field that holds it today,
or a gap row below with the field the data track would want (a proposal; the sim track names the real one).

| Mechanic | Where on the page | Today |
| --- | --- | --- |
| Forest stealth: non-siege units in or beside a forest, 3 s without attacking or losing hp | Faction bonus | `faction.json` `forestStealth` `{ status, delay, exceptSlots }` (M8-1) |
| Detection by detectors and enemies within 3 m | Faction bonus | `rules.json` `stealth.proximity`; units' and buildings' `detector` (M4-5) |
| ×1.5 vs Mounted (Edur Warrior), ×1.5 vs Light (Aptorian Stalker) | Unit notes | `units.json` `attack.bonusVs` |
| Long sight 20 m (Aptorian Stalker) | Unit notes | `units.json` `sight` |
| Splash 1.5 m, detector 10 m (K'risnan Sorcerer) | Unit notes | `attack.splash`, `detector` |
| Attacks buildings only (Edur Ram) | Unit notes | `attack.targets` `buildings` (M4-2a) |
| Hound needs Age II, trained at the Shadow Kennels | Units | `requires` `["age_ii"]`, `trainedAt` `shadow_kennels` |
| Stand-still stealth anywhere (Shadow Archer) | Unit notes | gap G1 |
| First shot from stealth +50% (Shadow Archer) | Unit notes | gap G2 |
| Pack Hunter: +10% per other Hound within 8 m, max +30% | Unit notes | gap G3 |
| At most 7 Hounds alive | Unit notes | gap G4 |
| Shadow-step: group teleport, siege excepted, formation kept, snap to free cells | Abilities | gap G5 (`teleport` is planned, refused) |
| Summon Wraiths: 3 units beside the caster for 20 s, 1.0 s cast, 50 s cooldown | Abilities | `kind` `summon`, effect `spawn` `{ unit, count, lifetime }` (M4-4b-3b) |
| Wraith has a unit row with no slot and no producer | Summoned unit | gap G6 |
| Wraith: no cost, no pop | Summoned unit | the summon rule (a summoned unit is charged nothing) |
| Wraith can't be stealthed | Summoned unit | the stealth rule (`IsSummoned`, M4-5 / M8-1) |
| Wraith-binding grants Summon Wraiths | Faction upgrade, Techs | gap G7 |
| The AI casts Summon Wraiths | AI build order | gap G8 |
| The AI's raid, ambush posts, expansion condition and place, forest fights, disengage, Shadow-step targets | AI build order | gaps G9-G13 |
| A Shadow-only counter answer (Hounds against Ranged and Casters) | Owner review | gap G14 |
| Workers, houses, halls, Age II, the expansion at 8:00, the army mix, the wave | AI build order | `ai.json` `buildOrder` (`train` / `build` / `research` / `expand`), `compositions`, `attack` |

| Gap | Mechanic | Why no field holds it | Field wanted (proposal) |
| --- | --- | --- | --- |
| G1 | Shadow Archer: Stealthed after 3 s without moving or attacking, anywhere | `forestStealth` is faction-wide and needs a tree; no unit field gives a stealth (docs/01 2026-11-14 (d) (e): the next slice, M8-2) | `units.json` `stillStealth { "status": "stealthed", "delay": 3 }`: held while the unit has not moved, attacked or lost hp for `delay` (the forest rule's timers, plus a last-moved tick) |
| G2 | Shadow Archer: the first shot from stealth deals +50% | No attack field reads the attacker's stealth; `damageBonus` is a status's (frenzied kind) | `units.json` `attack.fromStealthBonus` 0.5: a hit whose attack was started while the attacker was Stealthed deals ×1.5 |
| G3 | Pack Hunter: +10% damage per other Hound within 8 m, max +30% | Passive triggers are `whenBelowHp`, `onDeath`, `onHit`; nothing counts nearby friends | a passive `{ "trigger": "nearbyAllies", "units": ["shadow_hound"], "radius": 8, "damageBonus": 0.1, "maxStacks": 3 }`, the count taken when the attack lands |
| G4 | At most 7 Hounds alive at once | No per-type cap; only the population cap limits training | `units.json` `maxAlive` 7: live units plus queue items count, a train command at the cap is refused (and the AI skips the unit while at it) |
| G5 | Shadow-step: the caster and own non-siege units within 4 m move to the point, keeping their formation; blocked cells snap to the nearest free one | `teleport` is in `PlannedAbilityEffectKindIds`: refused "not supported yet" | effect `{ "kind": "teleport", "exceptSlots": ["siege"] }` on a `targetGround` ability, its `radius` the gather radius, `affects` `own_units`; formation kept and the snap as the rule, not fields |
| G6 | The Wraith as a unit type with no slot, no producer, no cost or train time | `units.json` takes one unit per template slot (`CheckSlots`) and requires `trainedAt`, `cost`, `pop`, `trainTime`; docs/03's M4-4b-3b example names `shadow_wraith` as "an ordinary units.json entry", which the slot rule refuses | `"slot": "summoned"`: outside the one-per-slot rule, no `trainedAt` / `cost` / `pop` / `trainTime` (refused if given), usable only as a `spawn` effect's `unit` |
| G7 | Wraith-binding: K'risnan Sorcerers gain Summon Wraiths | Tech `stat`s are `attack`, `armor`, `range`, `hp`, `abilityCooldown`, `statusDuration`; an ability has no `requires` | `abilities.json` `requires` `["shadow_wraith_binding"]` (the cast refused, the button disabled, until researched), as units' `requires` works |
| G8 | The AI casts Summon Wraiths | The AI casts a summon when `ai.castMinTargets` enemies are within its `radius`, and a spawn-only summon has no `radius` (refused), so it is never cast | a summon's AI reach: enemies within the caster's sight (or a `rules.json` `ai.summonReach`) |
| G9 | Raid at ~5:00: 3 Aptorian Stalkers hit the enemy wood line and fall back when an army appears | An opening has one wave threshold and target rule; no raid party | an opening `raids: [{ "at": { "time": 300 }, "unit": "shadow_aptorian_stalker", "count": 3, "target": "wood" }]` |
| G10 | Archers wait in forests on the enemy's likely path; the army prefers to engage near forests | The AI rallies at a point by the hall and attacks the nearest known building | a `rules.json` / opening rally preference `rallyNear: "forest"` |
| G11 | 2nd Edur Longhall next to a forest when army ≥ 14 pop | An `expand` step fires at its trigger (time, pop or workers) near the nearest free gold mine | a step condition `armyPop`, and an expansion site preference `near: "forest"` |
| G12 | Shadow-steps onto enemy casters and siege | The AI's cast rules know no teleport; its ground rule aims at clumps of enemies | a teleport cast rule: the nearest seen enemy caster or siege within `range`, with own units to carry |
| G13 | Disengages if losing and re-hides | The retreat goes home at a loss share; it doesn't look for a forest | `retreat.to: "forest"` (fall back to the nearest forest cell instead of the hall) |
| G14 | Hounds answer enemy Ranged and Casters | `rules.json` `ai.counters` is one rule by slot for every faction and gives the Unique slot nothing (the Teblor's same request, D-R36) | a per-faction `ai.json` `counters` override (the faction's own answer list for an enemy slot). A `unique` answer in the shared `rules.json` `ai.counters` loads today (an answer list takes any slot id), but it is not enough: the shared rule would send every faction's unique (the Sapper, the War-dog, the Zealot) against enemy Ranged and Casters too |

## Owner review (D-R38, proposed)

The words the Shadow files would carry, for the owner to read (codenames, as the other factions' files; the release names
are the table above, and none of them appears here). Each description is one or two short sentences of at most 144
characters, names only Shadow things, and says "needs X" exactly for a building's requirements (the content pins' rules
for every faction). The Archer's, the Hound's and the two abilities' numbers are the page's; they are copied only once
their schema exists (G1-G7).

| File | Id | Name (`displayName`) | Description (`description`) |
| --- | --- | --- | --- |
| `faction.json` | `shadow` | Shadow | Grey-skinned sea clans and the Hounds of their dusk realm. They hide in forests, strike from stealth and run down whoever flees. |
| `faction.json` bonus | — | Children of Shadow | Units other than siege that stand in or beside a forest and have not attacked or lost hit points for 3 seconds become Stealthed. |
| `units.json` | `shadow_thrall` | Thrall | Gathers gold and wood, raises buildings, and repairs them. Stays hidden at a forest edge while it chops. |
| `units.json` | `shadow_edur_warrior` | Edur Warrior | A tall, grey-skinned spearman, lighter and quicker than most line infantry, who holds the front and cuts down cavalry. |
| `units.json` | `shadow_archer` | Shadow Archer | Vanishes once it has stood still for 3 seconds, forest or not. Its first shot from hiding deals 50% more damage. |
| `units.json` | `shadow_aptorian_stalker` | Aptorian Stalker | A long-limbed demon that sees far, raids worker lines and runs down archers and casters. |
| `units.json` | `shadow_krisnan_sorcerer` | K'risnan Sorcerer | Shadow-steps nearby soldiers across the field in an instant and blasts clumped foes with dusk magic. Detects hidden units. |
| `units.json` | `shadow_edur_ram` | Edur Ram | A blackwood ram that smashes buildings and cannot strike soldiers. Easy prey for melee troops. |
| `units.json` | `shadow_hound` | Hound of Shadow | A horse-sized hound: each other Hound within 8 m adds 10% damage, up to 30%. No more than 7 can live at once. |
| `units.json` | `shadow_wraith` | Wraith | A spirit bound to fight for 20 seconds. It costs nothing, takes no population, and can never hide. |
| `buildings.json` | `shadow_longhall` | Edur Longhall | The heart of an Edur village. Thralls bring gold and wood here, and it trains new Thralls. |
| `buildings.json` | `shadow_hearth_house` | Hearth-house | A clan hearth under a turf roof for another family, raising your population cap by 8. |
| `buildings.json` | `shadow_thrall_camp` | Thrall Camp | A forward store where Thralls drop off gold and wood, so they spend less time walking. |
| `buildings.json` | `shadow_warrior_lodge` | Warrior Lodge | Trains the Edur Warrior, the spearman who holds the line and stops cavalry. |
| `buildings.json` | `shadow_archers_shade` | Archer's Shade | Trains the Shadow Archer, who disappears wherever it stands still. |
| `buildings.json` | `shadow_kennels` | Shadow Kennels | Trains the Aptorian Stalker and the Hound of Shadow; needs a Warrior Lodge. |
| `buildings.json` | `shadow_blackwood_forge` | Blackwood Forge | Forges blackwood weapons and armor for the clans, and Wraith-binding once you reach Age II. |
| `buildings.json` | `shadow_sanctum` | K'risnan Sanctum | Trains the K'risnan Sorcerer, who steps soldiers through shadow and binds Wraiths; needs Age II. |
| `buildings.json` | `shadow_ram_shed` | Ram Shed | Trains the Edur Ram, which batters down buildings; needs Age II. |
| `buildings.json` | `shadow_spire` | Shadow Spire | A dark spire that watches the forest edge, spots hidden enemies and shoots at intruders; needs Age II. |
| `techs.json` | `shadow_wraith_binding` | Wraith-binding | Binds the restless dead to the K'risnan: Sorcerers gain Summon Wraiths. Needs Age II. |
| `abilities.json` | `shadow_step` | Shadow-step | Moves the caster and own soldiers within 4 m to a point up to 12 m away in an instant, keeping their formation. Siege stays behind. |
| `abilities.json` | `summon_wraiths` | Summon Wraiths | Summons 3 Wraiths beside the caster. They fight for 20 seconds, then fade. |
| `ai.json` opening | `standard` | Forest ambush | Warrior Lodge, Archer's Shade and Shadow Kennels first; a wave at 18 army pop, then Age II brings K'risnan Sorcerers and Hounds. |

The opening's description promises only what its steps and mix make sure of (BUG-1390's lesson); it is re-read against
the first measurement before it ships.

**Counter answers (the data track's proposal, the owner's call).** The shared rule gives the Shadow the answers in "How the
AI adapts". The proposal adds the Hound of Shadow (the Unique slot) where the design sends it, "run down anything that
tries to flee" and the soft back line: enemy Ranged and enemy Caster. It needs gap G14.

| Enemy slot | The shared rule gives the Shadow | Proposed |
| --- | --- | --- |
| Line | K'risnan Sorcerer, Edur Ram | K'risnan Sorcerer, Edur Ram |
| Ranged | Aptorian Stalker | Aptorian Stalker, Hound of Shadow |
| Shock | Edur Warrior | Edur Warrior |
| Caster | Aptorian Stalker, Shadow Archer | Aptorian Stalker, Shadow Archer, Hound of Shadow |
| Siege | Edur Warrior, Aptorian Stalker | Edur Warrior, Aptorian Stalker |
| Unique | none | none |

## Decisions (D-R38)

The Producer's answers to the eleven open questions D-R38 returned (Q1-Q11, which lived only in that task's report), as
docs/01 "2026-11-15 (a)" item (3) settles them: Producer decisions the owner may revisit. One line a question, in docs/01's
order; the last is Q4. Where the page already reads that way, the line says where.

1. "Taken damage" is any loss of hit points. Already so: "Faction bonus".
2. `exceptSlots` is `["siege"]` only; the sim's summoned-unit task must show a Wraith is never hidden, else `summoned` joins
   the list. Already so: "Faction bonus" (the `forestStealth` block and the sentence under it).
3. A Thrall stays hidden while it chops at a forest edge; flagged for the M8 balance pass.
   Already so: "Faction bonus" and the Thrall's Owner review description.
4. The Edur Ram is a melee ram that hits buildings only. Already so: "Units", "Unit notes", "Against docs/02's template".
5. The Hound of Shadow's `scale` is 1. Already so: the Units table's Scale column.
6. Losing hit points breaks the Shadow Archer's stand-still stealth, and the +50% first shot is the Archer's alone, from
   either stealth (forest or stand-still). Already so: Schema gaps G1 and G2.
7. The 7-Hound cap counts Hounds alive plus queued. Already so: Schema gap G4.
8. Shadow-step carries own units only: siege stays, Wraiths go. Already so: "own non-siege units" (Abilities, G5).
9. The faction's `displayName` is "Shadow" and its bonus "Children of Shadow" (the owner's taste: it starts like the
   Teblor's "Children of the Mountains"). Already so: "Owner review (D-R38, proposed)".
10. The opening builds a Blackwood Forge because Wraith-binding is researched there. Already so: opening step 22.
11. (Q4) The content checks that refuse another faction's names blank out the faction's own names first ("Edur Warrior"
    and "Warrior Lodge" contain the Teblor's "Warrior"); the units keep their names. Done in D-H9: `UnitContentTests` E
    and `BuildingContentTests` F.
