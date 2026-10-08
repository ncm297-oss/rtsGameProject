# 02 — Game Design

This is the starting design. All numbers are first-pass and will be tuned in playtests; the
structure (slots, damage types, systems) is what matters now. Items tagged **[OPEN]** need the
owner's call. Everything else is the default unless someone objects.

Units: distances in meters (1 map cell = 2 m), time in seconds, speed in m/s. The sim converts
seconds to 50 ms ticks at load time.

## Core loop

1. **Gather.** Workers harvest Gold (finite mines) and Wood (forests) and carry it to a drop-off.
2. **Build.** Spend resources on houses (population), production buildings, and upgrades.
3. **Advance.** Research Age II at the Town Hall to unlock casters, siege, the unique unit, and towers.
4. **Fight.** Build an army whose composition counters the enemy's, take map control, expand to
   new gold, and destroy every enemy building.

A typical Normal match: first skirmishes around minute 5, Age II around minute 7-9, the
decisive fights between minutes 15 and 30.

## Faction template

Every faction fills the same seven unit slots and ten building slots, so the sim, AI, and UI are
written once. Factions differ through stats, one unique unit, one signature caster ability, one
faction bonus, and one faction upgrade. A new faction is JSON plus models, not new systems.

| Slot | Role | Default armor class | Counters | Countered by |
| --- | --- | --- | --- | --- |
| Worker | Gathers, builds, repairs | Light | — | Everything |
| Line infantry | Cheap melee core, holds the front | Heavy | Shock | Casters, siege splash |
| Ranged | Pierce damage from range, fragile | Light | Light units, casters, Giants | Shock |
| Shock | Fast flanker (cavalry or beast) | Mounted | Ranged, casters, workers | Line infantry |
| Caster | Warren magic, splash, one signature ability, detector | Light | Clumped Heavy units | Shock, ranged |
| Heavy / siege | Kills buildings, slow | Heavy | Buildings | Melee units |
| Unique | The faction's identity unit (Age II) | varies | varies | varies |

### Template baseline stats

Faction pages express their units relative to this line. Malazan stays closest to it and is the
balance reference.

| Slot | HP | Armor | Attack | Type | Cooldown | Range | Speed | Sight | Cost (G/W) | Pop | Train |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | 40 | 0 | 4 | Melee | 1.5 | melee | 4.0 | 14 | 50 / 0 | 1 | 12 |
| Line | 120 | 2 | 10 | Melee | 1.5 | melee | 3.2 | 14 | 60 / 20 | 1 | 18 |
| Ranged | 55 | 0 | 8 | Pierce | 2.0 | 14 | 3.4 | 18 | 40 / 45 | 1 | 20 |
| Shock | 150 | 1 | 12 | Melee | 1.8 | melee | 6.0 | 16 | 90 / 30 | 2 | 26 |
| Caster | 60 | 0 | 9 | Magic | 2.2 | 12 | 3.2 | 16 | 100 / 50 | 2 | 30 |
| Heavy / siege | 220 | 4 | 50 | Siege | 5.0 | 24 | 2.2 | 18 | 200 / 150 | 3 | 40 |

Template bonuses: Line deals ×1.5 vs Mounted. Shock deals ×1.5 vs Light. Casters splash
(radius 1.5 m) and detect stealth within 10 m. Siege engines with range have a 6 m minimum range
and 2.5 m splash.

"Melee" range means 0.5 m between the attacker's and target's edges. Unit collision radius is
0.4-1.0 m (never more than half a cell, which keeps pathfinding to one size class). Visual scale
can be larger than the collision radius.

## Economy

**Two resources: Gold and Wood** (decided 2026-10-02). Age of Empires' four (food, wood, gold,
stone) were considered; two keeps the economy UI, AI, and balance small. Factions may rename
resources for flavor in their display text (e.g. Teblor "Bloodwood" for wood).

| Rule | Value |
| --- | --- |
| Starting state | Town Hall, 5 workers, 200 Gold, 200 Wood, pop 5/10 |
| Worker carry capacity | 10 (some factions differ) |
| Gather rate | Gold 0.7/s, Wood 0.6/s while working at the node |
| Gold mine | 2 mines of 2,500 at each start location; 2 mines of 2,000 at each expansion |
| Tree | 100 Wood each; the cell becomes passable when a tree is depleted |
| Drop-off | Town Hall (Gold + Wood) and Camp (Gold + Wood) |
| Population | Town Hall +10, House +8, hard cap 100 |
| Pop costs | Integers or half-steps (Andii use 1.5); stored internally as half-pop integers |
| Mine crowding | No hard cap; workers queue at a mine's edge. Revisit if 20 workers on one mine feels wrong |

Workers return cargo to the nearest drop-off automatically and resume gathering. A worker
ordered to a depleted node looks for the nearest node of the same type within 20 m.

## Buildings

Ten slots per faction, same function across factions, different names and models. The original
plan listed nine; a **Camp** was added because workers need a drop-off near forests and
expansions, and making them build a full Town Hall for that is wrong for an AoE-style economy.

| Slot | HP | Armor | Cost (G/W) | Build | Footprint | Provides | Requires |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Town Hall | 2400 | 5 | 275 / 275 | 90 | 4×4 cells | +10 pop, drop-off, trains Worker, researches Age II | — |
| House | 500 | 3 | 0 / 50 | 20 | 2×2 | +8 pop | — |
| Camp | 600 | 3 | 0 / 75 | 25 | 2×2 | Drop-off | — |
| Infantry Hall | 1200 | 4 | 0 / 150 | 40 | 3×3 | Trains Line | — |
| Ranged Hall | 1200 | 4 | 0 / 150 | 40 | 3×3 | Trains Ranged | — |
| Shock Hall | 1200 | 4 | 75 / 150 | 45 | 3×3 | Trains Shock | Infantry Hall |
| Forge | 1000 | 4 | 100 / 100 | 40 | 3×3 | Upgrades | — |
| Caster Hall | 1200 | 4 | 150 / 150 | 50 | 3×3 | Trains Caster | Age II |
| Siege Works | 1400 | 4 | 150 / 200 | 55 | 3×3 | Trains Heavy/siege | Age II |
| Watch Tower | 800 | 5 | 50 / 125 | 35 | 2×2 | Attack 10 pierce / 2 s, range 18; sight 24; detector 16 m | Age II |

- The unique unit trains at whichever hall fits it (stated on each faction page) and needs Age II.
- Several workers can build one structure. Build time with *n* workers is `t × 3 / (n + 2)`
  (Age of Empires' formula: diminishing returns).
- Workers repair damaged buildings at 50% of the build rate for 25% of the original cost,
  scaled by damage.
- Placement: footprint cells must be passable, unoccupied by buildings, resources, or units that
  can't step aside, and explored by the player.
- A destroyed building leaves rubble (visual only) and frees its cells.
- **Walls are deferred** (see [01-vision.md](01-vision.md#explicitly-out-of-scope-later-maybe)).

## Tech

### Ages

- **Age I** at start. **Age II** researched at the Town Hall: 400 Gold / 200 Wood, 60 s.
  Requires two Age I production buildings or a Forge (any two of Infantry Hall, Ranged Hall,
  Shock Hall, Forge).
- Age II unlocks: Caster Hall, Siege Works, Watch Tower, the unique unit, level-2 Forge
  upgrades, and the faction upgrade.

### Forge upgrades

| Upgrade | Effect | Level 1 | Level 2 (Age II) |
| --- | --- | --- | --- |
| Melee Weapons | +1 / +2 attack for melee units | 100 G / 50 W, 30 s | 175 G / 100 W, 40 s |
| Ranged Weapons | +1 / +2 attack for pierce units and towers | 100 G / 50 W, 30 s | 175 G / 100 W, 40 s |
| Armor | +1 / +2 armor for all non-siege units | 125 G / 50 W, 35 s | 200 G / 125 W, 45 s |
| Faction upgrade | One per faction, see faction pages | — | 200 G / 150 W, 45 s |

## Combat

### Stats

Every attacker has: HP, armor (flat), armor class, attack value, damage type, cooldown, range,
wind-up (time from start of attack to the damage point, default 0.3 s melee / 0.4 s ranged), and
optional splash radius, minimum range, bonus multipliers vs armor classes, and a friendly-fire flag.
What an attack may target is `attack.targets`: `units`, `buildings` or `all` (the default); the Battering Ram is
`buildings` (M4-2a).

### Damage formula

```
raw     = attack × typeMultiplier[damageType][armorClass] × bonusVs[armorClass]
damage  = max(1, round(raw) - armor)      // Melee, Pierce, Siege
damage  = max(1, round(raw))              // Magic ignores armor
```

### Damage type × armor class

| Type ↓ / Class → | Light | Heavy | Mounted | Giant | Structure |
| --- | --- | --- | --- | --- | --- |
| Melee | 1.0 | 1.0 | 1.0 | 1.0 | 0.4 |
| Pierce | 1.25 | 0.6 | 1.0 | 1.25 | 0.2 |
| Siege | 0.5 | 0.75 | 0.5 | 0.75 | 3.0 |
| Magic | 1.0 | 1.25 | 1.0 | 1.25 | 0.4 |

- **Light:** workers, archers, casters, most unarmored units.
- **Heavy:** armored infantry and siege engines.
- **Mounted:** cavalry, beasts, and other fast units.
- **Giant:** Teblor and other huge creatures. Big targets: pierce and magic hurt them more.
- **Structure:** buildings and towers.

Worked example: a Crossbowman (9 pierce, ×1.3 vs Heavy) shooting Raider line infantry (Heavy,
armor 1): `9 × 0.6 × 1.3 = 7.0 → 7 - 1 = 6` damage per bolt.

### Projectiles

Ranged attacks spawn a projectile at the damage point. Arrows and bolts fly at 25 m/s toward
the target's position *at the moment of firing*. On arrival, the projectile hits if the target is
still within its collision radius + 0.3 m of the impact point, otherwise it misses and lands.
Fast units moving across the line of fire can dodge long shots; slow units almost never do: a
shot at a unit moving no faster than the projectile's lead speed (data, 5 m/s: foot units, not
cavalry) is led to where that unit will be when it lands, and kept on it in flight
(2026-10-08, BUG-0183, pending the Producer's review; see docs/03 "Implementation (M4-2b)").
Catapults and thrown munitions are ground-targeted lobs (12 m/s) that always explode at the
impact point.

### Splash and friendly fire

Splash damage is 100% within 40% of the radius and falls off linearly to 50% at the edge. Attacks
flagged **friendly fire** (Sappers, Catapults) also hit allied and own units in the radius, at 50%
damage. Friendly fire never damages buildings.

### Flying units

Only the Andii Great Raven flies in v1. Flying units ignore terrain and pathing, and can only be
attacked by pierce attacks, magic, and towers (no melee, no siege). They use the Light armor class.

### Death

Dead units are removed from the sim at once. Views play a death animation and leave a corpse for
10 s (visual only). Killing blows emit an event used for stats and for "on death" effects
(e.g. Zealot martyrdom).

## Abilities and status effects

### Ability system

Every caster has one signature ability; some units and upgrades add more. In v1 abilities have a
cooldown but **no mana**.

| Kind | Targets | Examples |
| --- | --- | --- |
| Target unit | One unit in range | (reserved for later heroes) |
| Target ground | A point in range, effect in a radius | Telas Fire, Sandstorm, Darkness, Shadow-step, Cusser |
| Self / aura | Area around the caster | Blood-oil Frenzy |
| Summon | Spawns temporary units | Summon Wraiths |

Each ability is data: `kind, range, radius, castTime, cooldown, duration, effects[]`. Effects are a
small fixed vocabulary: `damage`, `applyStatus`, `createZone`, `teleport`, `spawn`. New abilities
combine these; a new effect type is a code change and needs a design reason.

Casting: the player selects the ability (hotkey or button), then clicks a target. If several
casters are selected, only the one nearest the target casts. Abilities flagged `autocast` can be
toggled with right-click on the button (AI always manages its own casting).

### Status effects

| Status | Effect | Typical source |
| --- | --- | --- |
| Stealthed | Invisible and untargetable to enemies without detection | Shadow forest stealth, Shadow Archer |
| Revealed | Cannot be stealthed (overrides Stealthed) | Attacking from stealth, detector abilities |
| Slowed | Movement speed × (1 - magnitude) | Sandstorm, war-dog bite, Frenzy aftermath |
| Frenzied | Attack speed and/or damage up | Blood-oil Frenzy, Zealot passive |
| Regenerating | +HP per second | Andii and Teblor passives |
| Blinded | Sight radius 2 m; can't acquire or attack targets more than 3 m away | Sandstorm, Darkness |
| Burning | Damage over time (magic) | Telas Fire |

Stacking rule: reapplying the same status refreshes its duration and keeps the stronger
magnitude. Different statuses stack freely.

### Zones

Zones are persistent ground areas (circle, duration, owner) that apply statuses to units inside
every tick and can modify vision. Darkness (Andii) and Sandstorm (Whirlwind) are zones.

## Stealth and detection

- A stealthed unit is invisible and cannot be targeted by an enemy player unless it stands inside
  the detection radius of one of that player's detectors **and** in a cell that player can see.
- Detectors: Watch Towers (16 m), all casters (10 m), the Andii Great Raven (14 m).
- **Proximity:** any enemy unit within 3 m of a stealthed unit detects it. This keeps stealth
  beatable before Age II, when no faction has towers or casters yet.
- Attacking or casting reveals the attacker for 3 s (the Revealed status).
- Stealth sources: Shadow's faction bonus (forest stealth), the Shadow Archer's stand-still
  stealth. Other factions have no stealth.
- Stealthed friendly units render semi-transparent for their owner.

## Vision and fog of war

- Three states per cell, per player: **unexplored** (black), **explored** (terrain and last-seen
  buildings shown, darkened), **visible** (everything shown).
- Vision is circular from each unit and building (sight radius). No line-of-sight blocking by
  terrain in v1.
- Enemy buildings seen once stay visible as "last known" ghosts in explored fog until the cell is
  seen again.
- Vision modifiers: Darkness and Sandstorm zones hide their contents from enemies outside them
  and blind enemies inside them. High ground limits vision from below (next section).

### High ground

**StarCraft 2 style, in v1** (decided 2026-10-02).

- Terrain has discrete **elevation levels** (4 m apart; maps use levels 0-2). Plateaus connect
  through **ramps** (slopes of 30° or less, passable). Plateau edges steeper than 30° are
  impassable and read as cliffs.
- Every cell stores its level in the map data. Ramp cells count as the **lower** level they
  connect to.
- A unit or building sees a cell only if that cell's level is **at or below its own level**.
  Low ground can't see up onto high ground.
- Exceptions: flying units see every level; a cell within 4 m of the viewer is always visible
  (you can see the lip of the ramp you're standing under); vision from your own units already on
  the high ground works normally.
- Attacking from high ground **reveals the attacker** to the target's owner for 2 s, so the low
  side can shoot back or retreat instead of dying to an invisible enemy.
- No damage bonus in v1. An Age of Empires-style elevation damage bonus is a possible tuning lever
  later; it would be a single number in `rules.json`.
- Consequences to design around: towers and ranged units on high ground are strong; spotting
  high ground (Great Raven, Aptorian Stalker, a unit walking up the ramp) matters; the AI scouts
  ramps before attacking up them.

## Map and terrain

| Rule | Value |
| --- | --- |
| Size | 128 × 128 cells, 1 cell = 2 m (256 m × 256 m) |
| Elevation | Heightmap with discrete levels (0-2, 4 m apart) joined by ramps; slopes steeper than 30° are impassable, so plateau edges act as cliffs and ramps as chokepoints (discrete terraces: a level change without a ramp is a cliff) |
| High ground | Low ground can't see up (see [High ground](#high-ground)) |
| Trees | Block movement, choppable for wood, grouped into forests |
| Water | Optional lakes: impassable cells, decorative |
| Gold | 2 mines per start location, 2 per expansion, 2-3 expansions per player |
| Layout | Symmetric for 2-4 players (mirror or rotational) |
| Maps | Seeded procedural generator + 3 hand-tuned maps shipped |

Biomes give flavor through palette, props, and tree types: **Raraku** desert (sand, scrub,
rock outcrops, sparse palms), **Genabackis** forest (dense conifers, hills), **Seven Cities**
steppe (grass, rolling ground, river beds).

Shipped maps (working titles):

| Map | Players | Biome | Character |
| --- | --- | --- | --- |
| Raraku | 2 | Desert | Open center, few forests, long sightlines; favors Whirlwind |
| Pale Hills | 4 | Forest | Dense woods with paths between; favors Shadow ambushes |
| Vathar Crossing | 2-4 | Steppe | A river of slopes with fords as chokepoints |

## Controls and camera

| Input | Action |
| --- | --- |
| Left-click / drag | Select / box select |
| Shift + click / drag | Add to or remove from selection |
| Double-click, or Ctrl + click | Select all units of that type on screen |
| Right-click | Context command: move, attack, gather, build/repair, set rally point |
| A + click | Attack-move |
| S | Stop |
| H | Hold position |
| M + click | Move (ignore enemies) |
| P + click | Patrol |
| Shift + any command | Queue the command |
| Ctrl + 1-9 | Assign control group |
| Shift + 1-9 | Add selection to control group |
| 1-9 | Select group; double-tap to center the camera on it |
| Tab | Cycle subgroups within a mixed selection (command card follows) |
| Q W E R | Abilities of the active subgroup |
| B / V | Worker build menu: basic (Age I) / advanced (Age II) |
| F1 | Select idle worker (repeat to cycle) |
| F2 | Select all army units |
| Space | Jump camera to the last alert |
| Backspace | Cycle Town Halls |
| Esc | Cancel current targeting / close menus |
| F10 | Game menu (pause, save, load, settings, quit) |

**Grid hotkeys:** building production cards and worker build menus use the full 5×3 grid
(`Q W E R T / A S D F G / Z X C V B`). They never conflict with A/S/H because those cards have no
unit commands. All keys are rebindable.

**Camera:** fixed pitch about 55°, no rotation (readability), zoom with the scroll wheel between
about 20 m and 60 m from the ground. Pan with screen edges, **arrow keys**, and middle-mouse drag.
(The plan said WASD; that collides with A, S, and grid hotkeys, so arrows it is. Rebindable.)

**Minimap:** bottom-left. Left-click jumps the camera, right-click issues a move or attack order,
Alt + click pings. Shows terrain, fog, resources, units as dots in player colors, and alerts.

**HUD layout (StarCraft style):** resources and population top-right, game clock and menu
top-center, minimap bottom-left, selection panel bottom-center (portrait, stats, multi-select
grid, production queue), command card bottom-right (5×3). Alerts ("Your base is under attack")
show text, a minimap ping, and a sound.

## AI opponent

- Three difficulties:
  - **Easy:** thinks every 2 s, follows its build order slowly, attacks with small waves, doesn't
    micro, rarely uses abilities, expands late.
  - **Normal:** fair play at a relaxed pace, follows the build order on time, uses abilities.
  - **Hard:** fair play with tighter decisions (thinks every 0.5 s, focus fire, pulls back wounded
    units, good ability use) plus a **25% gather bonus**, shown in the skirmish setup.
- Behaviors in priority order: defend the base when attacked → follow the build order → keep
  producing workers and army → expand when safe → scout → send attack waves → rebuild losses.
- Build orders, army compositions, and attack thresholds are per-faction data (`ai.json`), not code.
- The AI sees only its own fog-of-war view. It never reads hidden enemy state.

## Game flow

1. **Main menu:** Skirmish, Load Game, Settings, Quit.
2. **Skirmish setup:** map, 2-4 player slots (each: human/AI, faction, difficulty, color, team),
   starting resources (standard / high), game speed. Default: free-for-all.
3. **Match.** Pause any time (single-player). Game speeds: Slow 0.75×, Normal 1×, Fast 1.5×.
4. **Victory/defeat:** a player is defeated when they have no buildings left. The last player or
   team standing wins.
5. **Post-match stats:** resources gathered, units trained/lost/killed, buildings built/lost,
   army value over time (graph), time to Age II.
6. **Save/load** any time during a match. **Replays** are recorded for every match.
7. **Settings:** resolution, window mode, VSync, graphics quality, master/music/SFX volume, edge
   pan speed, keybinds.

## Resolved open items

All open items from the planning phase were decided by the owner on 2026-10-02. New open items
get added here with an **[OPEN]** tag until decided.

| Item | Decision | Where |
| --- | --- | --- |
| Number of resources | 2 (Gold, Wood) | [Economy](#economy) |
| High-ground vision advantage | **Yes**, StarCraft 2 style | [High ground](#high-ground) |
| Teblor ranged unit | Weak Javelin Thrower | [factions/teblor.md](factions/teblor.md) |
| Hero units | Not in v1 | [01-vision.md](01-vision.md#explicitly-out-of-scope-later-maybe) |
| Andii shock slot | Andii Rider (expensive, elite) | [factions/andii.md](factions/andii.md) |
