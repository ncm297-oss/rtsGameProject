# BUG-1044: M6-S4 team nits: ally friendly-fire kills count as kills, ViewApi enemy pickers misfiled, untested enemy sites

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-27-0158, task M6-S4 |
| System | Teams (stats, docs/03 table, tests) |
| Fixed by | |

## Repro
1. **Ally friendly-fire kill credit.** Teams 1, 1, 0. Player 0's Catapult fires at an enemy Laborer; player 1's (the ally's)
   Laborer, set to 1 hp, stands 0.5 m from the target. The stone kills it: `World.Kills` = 1, 0, 0, `UnitKills` = 1, 0, 0,
   `Losses` = 0, 1, 0 (QA probe on 311bcca). `World.cs:234` passes every non-collapse death to `PlayerLedger.CountDeath`
   with the attacker's owner as the killer, ally or not.
2. **docs/03 "Implementation (M6-S4)" table, `ViewApi/*` row** says "the view's own-player answers: untouched", but two
   ViewApi sites decide *enemy*: `UnitPicker.ResolveEnemy` (`unitOwner[unit] == localPlayer` / `buildings.Owner[building]
   == localPlayer`, the right-click attack pick) and `MinimapRaster.DrawnEnemyDotAt` (`_centreOwner[k] == localPlayer`). In
   a team match a right-click on an ally's unit resolves as an Attack target, which the sim then drops (`MayAttack`), so
   the click does nothing instead of a move. ViewApi is the view track's; the sim track couldn't change it.
3. **Enemy sites still without a test that fails when reverted** (QA mutation run on a scratch clone, each site reverted to
   its pre-M6-S4 owner compare, after QA's new tests):
   - the in-loop allied re-check in `CombatSystem.PickTarget`, `TowerSystem` and `StealthSystem` (only matters for a slot
     freed and refilled since the hash rebuild, the reason the developer gives for keeping it; no test builds that case);
   - `VisionSystem.ZoneHides` (masked: `UnitSeesUnit` falls back to the fog, which `FogStore.HidesFrom` gets right);
   - `StealthSystem` building detectors skipping an ally (only changes `IsDetectedBy` for the ally);
   - `CombatSystem.CollectBuildings` per-side counts (perf only: a side's buildings counted per owner just skip less);
   - `VisionSystem.OnHit` (an ally's friendly fire from high ground revealing the attacker).
   `TeamArchitectureTests` scans `Combat/` only, so a raw owner compare added in `Abilities/`, `Vision/` or `Ai/` isn't caught.

## Expected
1. docs/02 "Game flow" 4: the stats "count only what an enemy destroyed". An ally is not an enemy.
2. The table lists every enemy site; the view's follow-up list names the two pickers.
3. Each enemy site has a test that fails when it regresses (criterion 7's standard, applied to the rest).

## Actual
See Repro.

## Notes
- Item 1 predates teams for the own case: an own friendly-fire kill already counts as a kill for its owner, and
  `QA/CusserQaTests.Cusser_OnItsOwnPosition_KillsTheSapper_CreditsItsOwner_*` pins that (Kills[0] = 1). So this is the
  Producer's call: extend the "enemy only" stats rule to allies (and own), or write the current rule into docs/02.
- QA added tests for the other reverted sites that survived the developer's suite: `FogStore.HidesFrom`, `AbilitySystem.Resolve`'s
  ally-as-own unit and building rules (Cusser, Tela's Fire), Martyrdom, and the AI ability manager's two ally rules (`QA/TeamQaTests`,
  `QA/TeamAiQaTests`).
