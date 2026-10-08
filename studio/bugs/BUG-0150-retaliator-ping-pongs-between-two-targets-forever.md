# BUG-0150: A retaliator ping-pongs between two targets forever (give-up never builds); four movement rows were switched to combat off to hide it

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-08-0313, task M4-2a (reported by the developer, reproduced and characterised by QA) |
| System | sim: combat chase / give-up memory (`Combat/CombatSystem.cs`: `Acquire`, `Engage`, `PickTarget`), sim track |
| Fixed by | `fea7963` (`CombatSystem.Acquire`: a switch away from a target out of scan range while `ChaseStall > 0` gives it up); rows `CombatTests.TargetLostFromSightMidStall_IsGivenUp...`, `GridChangeFuzzStressTests` back on combat |

## Repro
1. Un-skip `sim/Rts.Sim.Tests/QA/CombatPingPongQaTests.cs` (both rows) and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CombatPingPongQaTests"`:
   - `FourGroups_CombatOn_EverybodyComesToRest`: closings every 1 / 2 / 3 ticks leave 6 / 20 / 22 units Moving after
     3,000 ticks (combat off: 0).
   - `MoreGoalsThanCacheSlots_Seed110_CombatOn_AllTerminate`: seed 110 leaves 5 units Moving after 3,000 ticks.
2. Also seen in `GridChangeFuzzStressTests` seed 3 with combat on (`seed 3: units still Moving 4000 ticks after the last
   order`, unit 55) and in `ShoveQaTests.BuildCap_500Units...(players: 2)`.

## Expected
docs/03 "Implementation (M4-1)": every engagement ends; a chase that gets no closer for `GiveUpScans` (10) scans is
given up (BUG-0137), and a switch from one target to another keeps the stall count (BUG-0143).

## Actual
GridChangeFuzz seed 3, combat on, unit 55 (player 1 Heavy Infantry, mode `Retaliate`, anchor (140.3, 110.8)) from tick
~6,930 to the end, period 8 ticks:
```
t6930 Moving Retaliate pos 139.00,115.66 tgt 107 stall 0 best 13.20 | 107: whirlwind_priest Idle d14.06 | 86: malazan_laborer Idle d7.41
t6933 Moving Retaliate pos 139.00,116.11 tgt 107 stall 0 best 13.20 | 107 d14.26 | 86 d7.85
t6934 Moving Retaliate pos 139.00,115.96 tgt 86  stall 1 best 7.05  | 107 d14.19 | 86 d7.70
t6937 Moving Retaliate pos 139.00,115.51 tgt 86  stall 1 best 7.05  | 107 d14.00 | 86 d7.26
t6938 Moving Retaliate pos 139.00,115.66 tgt 107 stall 0 best 13.20 | ...   (repeats to tick 7,002 and beyond)
```
The priest (a "can attack" target, priority tier 1) sits at the edge of sight (14 m) and is reached by a detour whose
first steps lead away from it (+y). The unit chases it, steps out of sight, the scan drops to the nearer laborer (tier 2)
and Engage sets `ChaseBest` to the laborer's gap; the chase toward the laborer brings the priest back into sight, the
next due tick counts that as progress on the laborer (gap below `ChaseBest - 0.1`), resets the stall to 0, and the scan
re-picks the priest with `ChaseBest` reset to the priest's gap. Each target in turn shows "progress" against a best that
was just reset, so the stall never passes 1, the give-up never fires, the leash never pulls (it stays 5 m from its
anchor), and the unit walks a 0.6 m loop forever without fighting.

## Notes
- Not introduced by M4-2a's code: QA ran the same four rows on the M4-2a head with the ram's `attack.targets` set back to
  `all` and combat on, and all pass; with the ram as `buildings` (the shipped data) they fail. The ram change only changes
  who is in the brawls; the loop is in M4-1's switch handling (`Engage` resets `ChaseBest` on every switch while the
  stall count is kept: BUG-0143's rule covers the count but not the best).
- M4-2a switched four rows to `combat: false` / `ConfigNoCombat` (ShoveQaTests BuildCap 2-player, CrowdRowSweep seeds
  81-140, GridChangeQaTests FourGroups, GridChangeFuzzStressTests) with a comment pointing here. That is acceptable for
  movement rows, but the combat-on versions are the only rows that found this; keep `CombatPingPongQaTests` until fixed.
- Related: BUG-0149 (stall count across a switch gives up a reachable enemy), the opposite failure of the same rule.

## Re-check (2026-10-08-0313, round 1, QA)
- **The real loop is fixed.** `GridChangeFuzzStressTests` seed 3 with combat on and the settle window raised to 40,000
  ticks (scratch copy): on `fea7963` it settles 1,540 ticks after the last order. With only the new give-up lines
  removed it is still Moving at tick 43,002 (unit 55, `Retaliate`, target 107): a true infinite loop, now gone.
  Seeds 1-12 combat on all settle (1,540-2,284 ticks; bound 4,000), twins equal every tick.
- **Correction to this report:** the two `CombatPingPongQaTests` rows (and the CrowdRowSweep / FourGroups combat-off
  switches) were *not* this loop. Run to rest with no tick limit, on both `9be1991` (pre-fix) and `fea7963`, with
  identical numbers: seed 110 ends at tick 3,759, four-groups at 6,618 / 5,686 / 5,057 (closings every 1 / 2 / 3 ticks), every
  time with player 1 wiped. Hp is lost in every 500-tick window, so no livelock. Player 1 there has only Heavy Infantry
  and Zealots that fight (MoveScenario gives player 0 the even type ids and player 1 the odd ones). Its rams, catapults,
  sappers, archers and priests can't fight back in this slice, so 20 of player 0's melee units crowd round a few large
  targets as BUG-0143 back-rankers, switching targets (558 switches after tick 3,000) until the last one dies. Of the 60 seeds
  81-140, seed 110 is the only one over 3,000 ticks. The skipped rows' 3,000-tick bound is a walking bound; they are
  replaced by `CombatPingPongQaTests` combat-on rows with a combat bound (rest within 10,000 ticks, no 1,000-tick window
  without hp lost while anyone moves), and the old rows were removed.
