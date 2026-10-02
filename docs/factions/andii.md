# Tiste Andii (Darkness)

**Faction id:** `andii` · **Milestone:** M9 · **Role:** elite

## Lore hook

Children of Mother Dark: an ancient, tall, dark-skinned people, nearly immortal and weary of the
world after hundreds of thousands of years. Few in number, each one a veteran of wars older than
human cities. Their sorcerers wield Kurald Galain, the warren of Darkness itself, in which
ordinary eyes see nothing. Great Ravens, huge and clever, fly with them.

## Playstyle

Quality over quantity. Andii field fewer units that cost more, hit harder, outrange, and
regenerate. They fight from strong positions, use Darkness to blind the enemy at the decisive
moment, see everything with their Ravens, and withdraw to heal rather than trade. Every unit lost
is a real setback; a careless Andii player runs out of army.

## Faction bonus: Children of Mother Dark

- All units **regenerate 1.5 HP/s** after 5 s without taking damage.
- Units cost about **30% more** than the template and combat units use **1.5× population**
  (already applied in the table; pop uses half-steps).

## Faction upgrade: Mother Dark's Blessing

Age II, at the Night Forge, 200 G / 150 W, 45 s. Regeneration **+1 HP/s** (to 2.5 HP/s);
Darkness **duration +5 s**.

## Units

| Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Speed | Sight | Cost (G/W) | Pop | Train | Trained at |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Worker | Steward | 50 | 0 | Light | 5 | Melee | 1.5 | melee | 4.0 | 14 | 65 / 0 | 1 | 14 | Hall of Night |
| Line | Andii Blade | 170 | 3 | Heavy | 15 | Melee | 1.5 | melee | 3.4 | 14 | 80 / 25 | 1.5 | 24 | Blade Hall |
| Ranged | Andii Longbow | 75 | 1 | Light | 11 | Pierce | 2.0 | 17 | 3.4 | 20 | 55 / 60 | 1.5 | 26 | Longbow Gallery |
| Shock | Andii Rider | 200 | 2 | Mounted | 16 | Melee | 1.8 | melee | 6.0 | 16 | 120 / 40 | 3 | 32 | Night Stables |
| Caster | Galain Sorcerer | 80 | 0 | Light | 12 | Magic | 2.2 | 12 | 3.2 | 16 | 130 / 65 | 3 | 36 | Galain Sanctum |
| Heavy | Eleint-blooded Champion | 600 | 4 | Heavy | 40 | Siege | 2.5 | melee | 3.0 | 16 | 300 / 150 | 4.5 | 50 | Champion's Hall |
| Unique | Great Raven | 90 | 0 | Light (flying) | — | — | — | — | 7.0 | 22 | 80 / 40 | 1.5 | 25 | Galain Sanctum |

### Unit notes

- **Andii Blade:** ×1.5 vs Mounted. Beats any template line infantry one-on-one.
- **Andii Longbow:** the longest standard range in the game (17 m) and long sight.
- **Andii Rider:** ×1.5 vs Light. Expensive and rare (see the shock-slot decision below).
- **Galain Sorcerer:** splash 1.5 m, detector 10 m. Signature ability **Darkness**.
- **Eleint-blooded Champion:** the Andii heavy slot is a warrior, not an engine. Siege damage with
  ×2 vs all unit classes, so it is effective against units too (40 vs Light, 57 vs Heavy
  infantry with 3 armor, 115 vs a Town Hall per swing). **At most 2 alive.**
- **Great Raven (unique, Age II):** flying scout and detector (14 m). No attack. Only pierce,
  magic, and towers can hit it.

## Abilities

| Ability | Unit | Kind | Range | Radius | Cast | Cooldown | Duration | Effect |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Darkness (Kurald Galain) | Galain Sorcerer | Target ground (zone) | 16 | 7 m | 1.5 s | 50 s | 15 s | Enemy units inside are Blinded. Enemy players can't see into the zone. Andii units inside regenerate at double rate |

Darkness is larger and longer than the Whirlwind's Sandstorm but doesn't slow. Its synergy is
with regeneration: fight inside the dark, heal inside the dark.

## Shock slot (decided 2026-10-02: Andii Rider)

The plan left open whether Andii get a shock unit at all. Kept for the record, in case playtests
reopen it.

| Option | Pros | Cons |
| --- | --- | --- |
| **Andii Rider, expensive and elite (chosen)** | Template complete (UI, AI, counters all work); Andii can chase | Andii are infantry in the books; cavalry feels generic |
| No shock unit | Truer to the lore; stronger identity | Empty Shock Hall slot needs UI/AI special cases; no answer to raids except Longbows |
| Replace with something non-cavalry (e.g. fast "Night Hunters" on foot) | Lore-friendly and keeps the slot | One more unique model and concept |

## Buildings

| Slot | Name | Id |
| --- | --- | --- |
| Town Hall | Hall of Night | `andii_hall_of_night` |
| House | Quarters | `andii_quarters` |
| Camp | Steward's Store | `andii_stewards_store` |
| Infantry Hall | Blade Hall | `andii_blade_hall` |
| Ranged Hall | Longbow Gallery | `andii_longbow_gallery` |
| Shock Hall | Night Stables | `andii_night_stables` |
| Caster Hall | Galain Sanctum | `andii_galain_sanctum` |
| Siege Works | Champion's Hall | `andii_champions_hall` |
| Forge | Night Forge | `andii_night_forge` |
| Watch Tower | Raven Spire | `andii_raven_spire` |

Building stats follow the template in [02-game-design.md](../02-game-design.md#buildings).

## Strengths and weaknesses

- **Strong:** wins efficient trades, outranges everything, regeneration makes harassment
  pointless, Ravens give full map awareness and detection, Darkness neutralizes enemy ranged
  armies.
- **Weak:** low unit count means swarms (Whirlwind, Zealots) and splash can overwhelm them;
  expensive losses; slow to rebuild; a lost Champion is a huge setback.

## AI build order sketch (Normal)

```
Workers: 20 in Age I, 28 in Age II.
Start:   3 workers on gold, 2 on wood.
pop 6    Quarters
pop 8    Steward's Store at the nearest forest
pop 10   Blade Hall
pop 13   Quarters
pop 14   Longbow Gallery
pop 16   Night Forge -> Armor I
pop 18+  Quarters every ~7 pop
~6:00    Age II (fastest of the five)
Age II   Galain Sanctum (first Great Raven scouts at once), 2 Raven Spires at the base,
         Champion's Hall, Mother Dark's Blessing
Expand   2nd Hall of Night at ~10:00 with a Raven Spire beside it
```

- **Composition target (by pop):** 35% Andii Blade, 30% Andii Longbow, 5% Andii Rider,
  15% Galain Sorcerer, 10% Champion, 5% Great Raven (one Raven per army group).
- **Attack waves:** first at 24 army pop. Retreats to regenerate when the army is below 50%
  total HP. Sorcerers cast Darkness on the enemy's ranged units.

## Art notes

- **Palette:** black, deep midnight blue, silver, pale moonlight accents. Team color on cloaks and
  blade trims.
- **Silhouettes:** tall (about 1.1× human scale), dark-skinned, long silver or white hair. Long
  slender blades, tall longbows. Galain Sorcerers trail darkness (particles). The Champion is a
  huge armored warrior with draconic hints (horns, scales). The Great Raven is a black bird with
  a wingspan wider than a horse.
- **Likely sources:** KayKit Adventurers recolored and scaled 1.1×; Champion as the Knight scaled
  1.3× with added horns, or AI-generated; Great Raven AI-generated (Meshy/Tripo) unless a CC0
  bird turns up. Details in [04-art-pipeline.md](../04-art-pipeline.md).

## Codename → release name

| Internal codename | Proposed release name |
| --- | --- |
| Tiste Andii | The Nightborn |
| Andii Blade / Andii Longbow / Andii Rider | Night Blade / Night Longbow / Night Rider |
| Galain Sorcerer / Galain Sanctum | Night Sorcerer / Night Sanctum |
| Darkness (Kurald Galain) | Veil of Night |
| Eleint-blooded Champion | Dragon-blooded Champion |
| Mother Dark's Blessing | Blessing of Night |
| Steward, Great Raven, other buildings | Keep (generic) |
