# BUG-0431: An own storm's rim and a Whirlwind enemy's storm rim are nearly the same colour

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-10-1015, task M4-V6c (filed as BUG-0421 by QA; renumbered to the view track's id range at ACCEPT) |
| System | ability views: zone discs (`AbilityViews.OwnZoneRimColor`, enemy rim = owner's `palette.primary`) |
| Fixed by | afa2661 (M4-VH3): `OwnZoneRimColor` (0.2, 0.8, 0.9), a light teal; test `AbilityViewTest.OwnRimColourRow` (every faction colour at least 0.4 apart in some channel; failed first at 0.064) |

## Repro
1. `AbilityViews.OwnZoneRimColor` is (0.72, 0.50, 0.22). The Whirlwind faction's `palette.primary` is `#C8892E` =
   (0.78, 0.54, 0.18): at most 0.06 apart in any channel.
2. Windowed `AbilityViewTest -- --shots <dir>`: compare `ability-seed1-storm.png` (own) with
   `ability-seed1-storm-enemy.png` (enemy Whirlwind): both rims read as the same orange-gold.

## Expected
The brief: "owner-coloured rim", so the player can tell an own storm from an enemy's at a glance (walk in or not).

## Actual
Only Whirlwind casts Sandstorm today, so an enemy storm always has a Whirlwind-gold rim. When the local player is
Whirlwind (own storms exist) against a Whirlwind AI (mirror match), own and enemy storms look alike. Against Malazan
there is no confusion today (only one side has storms), but the own rim colour carries no "own" meaning.

## Notes
The developer flagged this in the report (taste default until M6). A distinct own-rim colour (e.g. the local player's
UI accent or a white/cyan rim), or a pattern (dashed rim for enemy) would separate them; the mirror match's unit colours
share the same problem, so this may belong to the M6 team-colour pass.

## Verification (QA 2026-10-10-2334, M4-VH3)
- Fixed. Measured from pixels, not the constant: `QaVh3Test.tscn` windowed puts an own and an enemy (Whirlwind) Sandstorm
  side by side on flat ground at zoom 30 and averages 72 still pixels on each rim's centre line. Own rim `#75DDE8`, enemy
  rim `#DCAE55` (Whirlwind `#C8892E` blended over the sand): 0.576 apart in the largest channel, 0.727 Euclidean (the
  criterion asks > 0.06). Each rim also stands out from its fill (own 0.416, enemy > 0.06). Looked at
  `build/shots/qa-vh3/qa-vh3-seed1-rims-own-left-enemy-right.png`: teal beside gold, plainly different.
