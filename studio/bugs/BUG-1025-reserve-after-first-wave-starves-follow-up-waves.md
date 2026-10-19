# BUG-1025: After the first wave the reserve gate stays open for good: it holds through defenses, follow-up waves drop, and Malazan defeats rise from 8 to 10 while Whirlwind's fall from 5 to 1

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-18-1814, task M5-4g re-check after fix round 1 (57dfa22) |
| System | AI reserve gate (`AiPlayer.MayReserve`: `Board.WavesStarted > 0` opens it permanently) against `DefenseManager` / `MilitaryManager` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReservePairingsStressTests" --logger "trx"` on the branch.
   On the base, run the same file built with `-p:DefineConstants=QA_BASE -p:NoWarn=CS0649` in a clone at f531dd9. Compare
   the `ROW` lines (wave count, defeat, peak army, `hold ... defending N thinks of it`).

## Expected
docs/02 "AI opponent" puts the defense first. The brief's QA focus (c) says a defended base must still recall and place
houses. BUG-1021's second half: the reserve should not keep starving the army while the base is being razed.

## Actual
27 pairing runs, Normal v Normal, menu map, 20 minutes:

| | base f531dd9 | fix round 1 |
| --- | --- | --- |
| Malazan waves (total) | 79 | **55** |
| Malazan sides with 2+ waves | 20 | **12** |
| Malazan peak army (sum, pop) | 903 | **675** |
| Malazan defeats | 8 | **10** |
| Whirlwind waves / 2+ waves | 97 / 24 | 86 / 19 |
| Whirlwind defeats | 5 | **1** |

Malazan sides newly defeated, each holding a reserve while defending until it fell:
- `crossbow_push` v `holy_war` seed 12: before, 3 waves, peak 61 pop, alive. Now 1 wave, peak 17, defeated 14:03. Build
  150 / 200 reserved from 8:52 to 14:03, 312 of those thinks defending.
- `crossbow_push` v `standard` seed 37: before, 5 waves, peak 34, alive. Now 1 wave, peak 16, defeated 14:27. Build
  150 / 200 reserved from 8:45 to 13:09, 265 thinks defending.
- `standard` v `holy_war` seed 6: before, 4 waves, alive. Now 1 wave, defeated 17:13. Build 150 / 200 reserved from
  8:31 to 16:35, 339 thinks defending.
- `standard` v `holy_war` seed 4: defeated at 19:35 before, 14:37 now. Build 150 / 200 reserved from 9:23 to 14:37,
  304 thinks defending.
- `crossbow_push` v `standard` seed 32: defeated at 15:30 before, 12:35 now. Age II 400 / 200 reserved from 7:54 to 11:33
  with 1,046 gold banked, 220 thinks defending.

Before the first wave too: the gate looks at half-pop only. When it opens during a defense, or with the wave's
`minArmyCost` not yet met, the wave can't start but the reserve holds production anyway:
- `cadre_rush` v `raider_rush` seed 7, Malazan (traced): the gate opened at 8:44 while defending (36 / 36 half-pop), and
  150 / 150 was reserved. The army was then cut from 36 to 12 half-pop by 9:31, and the gate closed again. The first wave
  went at **14:08 (8:42 on the base)**.
- `standard` v `raider_rush` seed 2, Malazan: at 6:00 the army was 32 / 32 half-pop but cost 1,233 of 1,235, and the base
  was defended. The gate opened and a reserve was named for one think. The first wave went at 12:13 (6:52 on the base).
  Most of that delay is the opponent's changed play: Whirlwind's own gate opens after its 4:50 wave.

## Notes
- The criterion is met (every side waves at least once, apart from the base's three). The effect is on the game:
  Malazan, whose openings wait for bigger waves, now loses 10 of 27 against 1 for Whirlwind. This is a balance change
  that the data track's pages don't yet show.
- The developer measured "the reserve suspended while defended" on its own (11 sides with no wave). Its combination
  with the gate was not reported. That combination is the obvious next variant to measure, along with closing the gate
  while `Defending` holds or a wave's cost is unmet.
