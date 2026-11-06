# BUG-1044: M6-S4 team nits: ally friendly-fire kills count as kills, ViewApi enemy pickers misfiled, untested enemy sites

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 1 fixed M6-S5; item 2 built M6-V16; item 3: scan width done in round 1, summary wording M6-SH1; the untested sites and the regex blind spots are left) |
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

## Round 1 (QA, 2026-10-27-0158, fix 0a2b9e2): partly fixed, still open
- Item 2 **fixed**: docs/03 now lists `UnitPicker.ResolveEnemy` and `MinimapRaster.DrawnEnemyDotAt` as enemy sites, allied-as-other,
  view follow-ups.
- Item 3's last line **fixed**: `TeamArchitectureTests.NoRawOwnerCompareMeaningEnemy_InTheEnemySiteFolders` scans `Combat/`,
  `Abilities/`, `Vision/`, `Ai/` and root `World*.cs`. QA reviewed the 14 new allow-list lines in context: FogStore (own eyes,
  `Update` stamps), AiPlayer (gates whose bodies ask `View.IsEnemy`; own producer check), BuildOrderExecutor `HallNear`
  (allied-as-other, documented), PlayerView / ScoutManager (own-only), `World.Defeat.Collapse` (own buildings). None is an
  enemy site wrongly allowed. Planting raw compares in AbilitySystem, VisionSystem.OnHit and AiPlayer makes the scan fail with
  all three named.
- **Still open**: item 1 (an ally's friendly-fire kill credited; Producer's call) and item 3's untested sites (the stale-slot
  re-checks, `ZoneHides`, the stealth building-detector skip, `CollectBuildings`, `OnHit`).
- **New nits (round 1):**
  - The scan's regex misses a compare through a lower-case indexed alias (`int[] owner = u.Owner; ... owner[j] != p`).
    Two such lines exist today, both acceptable: `FogStore` `NextSeenUnit` (`owner[s] == player`, own eyes) and
    `FogStore.UpdateGhosts` (`bool enemy = alive[j] && owner[j] != p;`, allied-as-other in docs/03). A new enemy test
    written that way would pass the scan. QA's `QA/TeamArchitectureQaTests` pins the aliased set to those two.
  - A line that also calls `AreAllied(` / `IsEnemy(` is skipped whole, so a raw enemy compare sharing a line with an unrelated
    predicate call is not seen.
  - `TeamArchitectureTests`' class summary still says "the four listed below" (now 18), and `s_samePlayer` holds
    `BuildOrderExecutor.HallNear`, which docs/03 classes as allied-as-other, not same-player.

## Producer triage (2026-10-27-0158 ACCEPT)
S4, open. **Item 1 decided (Producer decision, owner may revisit, docs/01 change log 2026-10-27):** a friendly-fire kill, by an
own unit or an ally's, is a loss for the victim's owner and **no kill for anyone** (docs/02 "Game flow" 4's "count only what an
enemy destroyed", applied to the kill side as well). This moves the own case too (`QA/CusserQaTests.Cusser_OnItsOwnPosition_*`
pins Kills[0] = 1 today; the pin moves with the fix). Built as a rider of the next sim task (M6-S5): `World.cs:234` passes the
killer only when `IsEnemy(attackerOwner, victimOwner)`; docs/02 gets one sentence. Items 3 (the five untested sites: a
stale-slot summon case, `ZoneHides` on its own, the detector skip through `IsDetectedBy`, `CollectBuildings`' per-side count
through a bucket-walk probe, `OnHit` from high ground), the regex blind spots (lower-case aliases; a line that mixes a raw
compare with a predicate call), the "four listed below" summary and the `s_samePlayer` / `HallNear` misfiling go to the next sim
hardening (M6-SH1), or ride M6-S5 if the dev touches those files.

## M6-V16 (2026-10-27, dev, view): item 2's view follow-up built
The two ViewApi enemy pickers now take the alliance answer: `UnitPicker.ResolveEnemy` and `MinimapRaster.DrawnEnemyDotAt` /
`EnemyDotAt` have overloads reading an alliance table (per player, `World.IsEnemy(local, p)`, built once per match by
`SelectionController.Enemies`, read through `ViewApi/EnemyTable.IsEnemyOwner`), and `SelectionController` / `Minimap` call them, so a right-click on an ally's unit, building, remembered building or minimap dot is
a Move, not an Attack the sim drops. Tests: `ViewApi/UnitPickerTests.ResolveEnemy_WithTheAlliances_*`,
`ViewApi/MinimapRasterTests.AnAllysDot_IsNoEnemyDot_WithTheAlliances`, `ViewApiAllocationTests.EnemyPickers_WithTheAllianceTable_*`.
The old overloads (no table: every other player is an enemy) are kept for the free-for-all callers and QA's suites. Items 1 and 3
are the sim's, unchanged.
## Item 1 fixed (M6-S5, dev)
- `World.RecordDeath` passes the killer to `PlayerLedger.CountDeath` only when `IsEnemy(killerOwner, victimOwner)`; otherwise
  -1 (no player). A friendly-fire death, by an own unit or an ally's, is the victim owner's loss (and unit / building loss)
  and nobody's kill; the `DeathEvent` still names the attacker's owner (the view's event is unchanged). docs/02 "Game flow" 4
  has the sentence.
- Tests: `TeamTests.AFriendlyFireKill_OwnOrAnAllys_IsALoss_AndNobodysKill` (own, ally, and the enemy control row: Kills,
  Losses, `UnitKills`); the pin `QA/CusserQaTests.Cusser_OnItsOwnPosition_KillsTheSapper_CreditsNoKill_StillHitsTheKeep_TwinsEqual`
  (renamed from `..._CreditsItsOwner_...`) moved to Kills[0] = 0, Losses[0] = 1.
- The five free-for-all A/B scenes stay hash-equal to e5c4265 (`Vision/SharedVisionFfaPinTests`): none has an own
  friendly-fire kill in its first 3,000 ticks. A match that has one hashes differently from before (the kill counter is hashed).
- Item 3 / the regex nits: not touched, except that M6-S5 moved the two lower-case aliased compares QA pinned in
  `QA/TeamArchitectureQaTests` (`NextSeenUnit`'s `owner[s] == player`, `UpdateGhosts`' `owner[j] != p`) onto lines that ask
  `AreAllied`, so its known-missed list is now empty (edited by the dev, the test's rule unchanged). Status stays open for the
  rest (M6-SH1).

## M6-SH1 (dev): item 3
- `TeamArchitectureTests` already scanned `Combat/`, `Abilities/`, `Vision/`, `Ai/` and `World*.cs` (round 1). M6-SH1 fixed the
  class summary ("the four listed below") and the allow-list's summary (it holds `BuildOrderExecutor.HallNear`, an
  allied-as-other site, with its comment).
- Not done (budget): the regression rows for the stale-slot allied re-check, `ZoneHides`, the stealth building-detector ally
  skip, `CollectBuildings`, `OnHit`, and the regex blind spots (lower-case aliases, a mixed line). Still open.
