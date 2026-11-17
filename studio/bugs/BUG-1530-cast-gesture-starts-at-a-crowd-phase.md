# BUG-1530: The cast gesture starts partway through its clip and loops on the longer casts

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (M6-V24, 2026-11-16-2036, be6b218: the cast clip starts at 0 and follows the cast once; `CastClipTest` rows 1b / 1c, QA's `QaM6V24Test` at 0.5x / 1x / 2x, paused, Stop, recast, slot freed) |
| Found | 2026-11-15-2136, task M6-V23 |
| System | unit models / animation (view) |
| Fixed by | M6-V24 (be6b218): `CastClipTest` rows 1b / 1c, QA's `QaM6V24Test` |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V23Test.tscn`. Row (C) prints
   `attack_cast: 0.50 s, loop mode Linear` and the cast times: `blood_oil_frenzy 10 ticks (0.50 s)`,
   `telas_fire 16 ticks (0.80 s)`, `cusser 20 ticks (1.00 s)`, `sandstorm 24 ticks (1.20 s)`.
2. Read `UnitViews.PlayClip`. Every clip, the cast clip included, starts at `slot * 0.37 s % length`, so the crowd
   doesn't move in step.

## Expected
The cast is one gesture: the thrust in `attack_cast` (UAL `Spell_Simple_Shoot`) lands about when the ability goes
off, the way a single cast reads in other RTS games.

## Actual
- A Shaman in slot 1 starts the 0.5 s clip at 0.37 s, near its end. It plays the end of the motion, wraps, and the
  thrust lands at a time set by the slot number, not by the cast.
- With the M6-V23 rule, every model type plays `attack_cast` while it is Casting. A model caster with a 0.8-1.2 s cast
  (Cadre Mage, the Whirlwind caster, when they get models) would loop the 0.5 s thrust 1.6-2.4 times.
- The 0.15 s default blend uses 30 % of a Frenzy cast.

Cosmetic only. Today the one shipped model caster (Teblor Shaman) has a 0.5 s cast and a 0.5 s clip, so the whole
motion shows, but it starts at a phase that depends on the slot.

## Notes
A possible fix: start the Cast slot at 0 (no crowd phase; casts are rare and don't line up in a crowd), and play it at
`length / castTime` speed, or don't loop it, so one thrust spans the cast. Leave this until a second model caster
lands, then decide together with the Malazan / Whirlwind casters' clip maps (M6-V23 rule-67 question 3).

## Fixed (M6-V24, 2026-11-16-2036)
The cast clip starts at its first frame on the tick the unit is first seen casting and stands where the cast is
(`UnitViews.CastProgress`), so it spans the cast once and never wraps; other clips keep the crowd phase. Producer:
the gesture spans the cast as seen (one tick shorter than `castTime`) and the 0.15 s blend stays (Q2, Q4 accepted).
