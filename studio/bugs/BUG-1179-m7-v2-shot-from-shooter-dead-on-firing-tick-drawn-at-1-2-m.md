# BUG-1179: A shot whose shooter dies the tick it fires is drawn from 1.2 m, not its shooter's scaled height

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-10-1236, task M7-V2 |
| System | view: ProjectileViews (shot heights, M7-V2) |
| Fixed by | |

## Repro
1. `powershell -File tools/qa/scene-loop.ps1 -Filter QaM7V2Test -LogDir <dir>` and read the `brawl inference` line.
   The scene compares each aimed shot's drawn launch height (`ProjectileViews.ShotFromHeight`) on the tick it is first
   seen with the store's internal ground truth (`ProjectileStore.AttackerType` / `Attacker`, read by reflection), over a
   150 v 150 forged mixed-scale brawl (Desert Archer 3.0, Crossbowman 0.5, Cadre Mage 1.5) plus two watchtowers.

## Expected
docs/03 "Implementation (M7-V2)": an aimed shot leaves at its shooter type's height, `1.2 m x scale`.

## Actual
```
brawl inference: 1411 aimed shots seen on their firing tick; launch height wrong 3 (3 of them by a shooter that died the tick it fired), strike height wrong 0; tower shots 24 (0 wrong) ...
  wrong launch: whirlwind_desert_archer drawn 1.2: 3
```
(an earlier run with no catapults: 2 of 2208). Every wrong one is a shot whose shooter was killed later in the same
tick it fired: the view infers the shooter from live units near the launch point, finds none, and falls back to
scale 1, so a 3.0 archer's last arrow starts at 1.2 m and climbs to its victim's height. Rare (about 0.1-0.2% of shots
in a big fight) and short-lived; nothing in the sim is affected.

## Notes
- The same fallback covers the documented known limit (a shooter that moved more than its radius before the view's
  first look). Both go away if the ViewApi ever exposes the projectile's attacker type (out of scope for M7-V2: no
  ViewApi change), or if the view also accepted a unit that died this tick at the launch point.
- No shipped unit is scaled, so nothing visible changes in the shipped game today.
