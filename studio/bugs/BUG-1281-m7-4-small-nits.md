# BUG-1281: M7-4 nits: a docs/03 claim about the building scale view, and two factor-less damage overloads

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-12-0546, task M7-4 (QA full) |
| System | docs/03, combat (`CombatSystem`) |
| Fixed by | |

## Repro
1. docs/03 "Implementation (M7-4)", the `buildings.json scale` bullet: "the view reads it as it reads `UnitDef.Scale`".
   `grep -rn "\.Scale" game/scripts` finds only `UnitDef.Scale` reads (`UnitViews`, `CombatViews`, `ProjectileViews`);
   nothing reads `BuildingDef.Scale`.
2. `CombatSystem.DamageToUnit(World, int attackerType, int owner, int j)` and
   `DamageToBuilding(World, int attackerType, int owner, int j)`: after M7-4 they have no caller anywhere in the repo
   (sim, tests, game), and they compute the hit without the attacker's status damage factor.

## Expected
1. The docs say what is true today: the view does not draw buildings at `scale` yet (the view half of Request 47 is a
   separate task), so the data track's `1.25` rows will be drawn at 1.0 until it lands.
2. No dead overload that gives a non-Frenzy number: a later caller (an AI estimate, a tooltip) picking the short form
   would silently miss the bonus. Remove them, or give them the factor.

## Actual
1. Present-tense claim with no code behind it.
2. Two unused overloads that ignore `StatusSystem.DamageFactor`.

## Notes
Neither affects play today. Found while checking where the factor is read (QA: every live damage path, the melee blow
in `QueueHit`, a shot's landing, both splashes, reads it; ability damage, pulses and towers do not, as the brief says).
