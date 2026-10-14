# BUG-0704: M5-4e win-rule nits (stale "no buildings left" texts, an ambiguous "camps", weaker re-staged rows, the keeper seam)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-14-0946, task M5-4e |
| System | defeat rule docs, tests, dev console line |
| Fixed by | |

## Repro
Read the files named below at `947a61a`.

## Expected
The texts say what the new rule does; the re-staged rows still test what their names say.

## Actual
1. **Stale text (view-owned)**: `game/scripts/Match.cs:233` prints `Match over: player p has no buildings left (tick n)`
   for every `DefeatEvent`; since M5-4e a defeated player can have a House, Forge, tower or site standing. docs/03
   lines ~5412 and ~5664 quote the same line. Dev console only (not player-facing UI), so S4; the view track owns the fix.
2. **"camps" is ambiguous**: docs/02 "Game flow" 4 and docs/01's 2026-10-14 row say "houses, camps, the Forge and towers
   need not fall". The Whirlwind's **Holy Camp**, **Raider Camp** and **Archer Camp** are a town hall and two unit halls
   and do keep a side in; only the camp *slot* (Depot / Supply Cache) doesn't. Suggest "drop-off camps (Depot, Supply
   Cache)".
3. **Re-staged rows weaker than their names**: in `QA/DefeatRuleQaTests.EveryCommandKind_*` the unit orders now name
   dead units and the Cancel / Train / Research / rally rows target player 1's buildings, so they would be dropped
   without the door too; only SpawnUnit and SpawnBuilding still need it. `AbilityManagerQaTests.ADefeatedSeat_NeverCasts`
   has no caster left, so it can't fail. Both describe states the new rule makes impossible, which is why QA accepts
   them; the door on what a defeated player still owns (its site, its Forge) is now covered by
   `QA/WinConditionQaTests.ADefeatedPlayersCommandsOnItsStandingBuildings_AreDropped` (with a control showing the same
   commands apply to a side still in).
4. **The oracle is one-way**: `DefeatCountsFuzzTests` checks "a defeat event's player has 0 of both" but not the other
   direction (a player that built and has nothing left is defeated that tick), and doesn't require any defeat to happen.
   `QA/WinConditionQaTests.CheckRule` checks both ways on real AI matches.
5. **The keeper seam**: `TestSim.KeepIn` spawns a summon with `ExpiresAtTick = int.MaxValue`, so `SummonedUnits > 0`
   for the whole scene and `SummonSystem.Run` scans every unit slot every tick; it also gives the player sight of the far
   corner. Harmless in the 20-odd rows that use it today, but a Perf or fog row that picks it up would measure something
   else. A note in its summary would do.

## Notes
None of these change the rule's behaviour; items 1 and 2 are wording, 3-5 test hygiene.

## Round 1 status (QA, 9793159)
- Item 1: docs/03 now marks the console line as stale and the view's to reword; `game/scripts/Match.cs:233` is unchanged
  (view-owned). Open.
- Item 2: fixed. docs/01 and docs/02 name the Holy / Raider / Archer Camp as halls and the drop-off camp (Depot / Supply
  Cache) as not keeping a side in; docs/03 matches.
- Item 3: open (accepted by QA; covered by `QA/WinConditionQaTests`).
- Item 4: open (`DefeatCountsFuzzTests` still one-way).
- Item 5: fixed. `TestSim.KeepIn` documents its side effects and gained `nearCorner` so two keepers in a combat scene
  don't share a cell. New tiny nit: `TestSim.cs` line ~85 `int worker =Rts.Sim...` lost its space after `=`.
