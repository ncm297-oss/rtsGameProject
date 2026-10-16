# BUG-0760: The AI's no-stacking record of its own failed Sandstorm blocks the judging rule's retries for the whole storm window

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-16-0847, task M5-H2 (QA) |
| System | AI ability manager (`Ai/AbilityManager.cs`, BUG-0702 item 2's no-stacking rule) |
| Fixed by | ac80818 (`AbilityManager.ForgetEnded` / `NeverRan`); regression `QA/SimHardeningM5H2QaTests.ASandstormThatNeverLands_IsSentAgain_UpToMaxTries` (red on 47ca4a2, green on ac80818) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningM5H2QaTests.ASandstormThatNeverLands_IsSentAgain_UpToMaxTries"`
2. The scene: one AI Priest beside four enemy Heavy Infantry (the `AbilityManagerQaTests` scene, Whirlwind); the test sends
   the Priest a Stop on every tick it is casting, so no storm ever lands (the same staging as
   `AbilityManagerTests.ACastThatNeverLands_IsSentMaxTriesTimes_ThenAgainWhenThePointMoves`, which uses Telas Fire).

## Expected
docs/03 "Implementation (M5-4c)", Judging: "A cast that didn't land (refused, dropped, cancelled) is sent again at the next
think, at most `AbilityManager.MaxTries` (3) times at the same point". The no-stacking rule (M5-H2) is about a storm that
*runs*: "not sent within its radius of a point a caster was sent the same ability at whose effect still runs".

## Actual
Head 47ca4a2: **1 send in 12 thinks** (0 landed). Base 8c39c08, the same test: **3 sends** (MaxTries), as documented.

```
a storm that never lands: 1 sends in 12 thinks (0 landed); window sent + cast + duration = 264 ticks, think 20 ticks
```

`AbilityManager.Casters` records every ground send of a non-stacking ability in `Blackboard.RecentCast*` with an end tick of
`sent + CastTicks + DurationTicks`, whether or not it lands, and `SentNear` then refuses any send of that ability within its
radius, the caster's own retry included. So a storm that was cancelled (a re-order, the caster hit out of its cast), refused
or dropped blocks itself and every other caster at that point for 264 ticks (13 thinks), the whole window of a storm that
never came. The judging rule's retries (`same && CastTries < MaxTries`) can't happen for any non-stacking ability.

## Notes
- Possible directions (the dev's call): drop the record when the judging step finds the cast didn't land (the caster's
  ready tick didn't move), or record the storm from its resolve (the zone exists) rather than from the send.
- Regression pinned red on head, green on base: `QA/SimHardeningM5H2QaTests.ASandstormThatNeverLands_IsSentAgain_UpToMaxTries`.
- Shipped data: Sandstorm is the only ground ability with no damage and no spawn effect, so the Priest is the one caster
  affected today.

## Verification (QA re-check, 2026-10-16-0847, fix round 1)
- The regression row prints `3 sends in 12 thinks (0 landed)` on ac80818 (1 on 47ca4a2): fixed.
- No stacking came back: `AbilityManagerQaTests.ThreePriests_OneStormOnOnePoint_TheOthersHoldTheirs` green; new
  `ThreePriests_TheFirstStormCancelled_IsSentAgain_ThenNoSecondStormOnTheLandedOne` (cancelled at 22, re-sent at 41, landed
  at 65; the next storm on the point at 321, after the 264-tick window); new seeded fuzz
  `PriestsWithRandomCancels_NeverStackAStormOnALandedOrRunningOne_TwinsEqual` (60 scenes, 207 storm sends, 74 landed, 115
  cancels, 195 sends near a cancelled one; oracle at every send, twins equal every tick); `TheRecentCastArrays_StayOutOfTheStateHash`.
- Three mutants of `NeverRan` in a scratch copy (forget every record; ignore the ready-tick check; forget while casting /
  walking) each turn the three-Priest row, the new row and both fuzz rows red.
- Why the moved ready tick is a sound "landed" signal: `AbilitySystem.Run` sets `AbilityReadyTick = tick + CooldownOf` only at
  the resolve, `CooldownOf` is at least 1 and the loader's `cooldown` floor is 1 tick, so a landed cast always moves it past
  the send's stamp; a cancel or drop never touches it.
