# BUG-1173: M7-3 nits: a pack unit whose worth rounds to 0 is undocumented, a wrong-case faction folder gets a misleading message, ten kept literal pairs could be data-derived

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-10-1236, task M7-3 (QA, full) |
| System | docs/03 "Implementation (M7-3)"; `DataLoader.FactionOrder`; sim test helpers |
| Fixed by | |

## Repro
1. **Zero-worth pack units.** `QA/FactionOrderQaTests.APackOfEight_WorthIsIntegerDivision(7, 0, 0, 0)` and
   `ZeroWorthPackUnits_InTheArmy_AndUnderAttack_CountZero_AndNothingBreaks`: a pack of 8 at 7 / 0 gives each unit
   `UnitWorth` 0. Such units never add toward `attack.minArmyCost`, add nothing to the intruders' worth (the defense then
   answers with `ai.defendMinValue`), and in `DefenseManager.Pick` every one is picked (the "first always" rule holds while
   `picked` stays 0), so a home army of them all answers any threat. Nothing crashes, nothing allocates.
2. **Folder case.** On Windows, a folder `factions/Teblor/` with `"order": [..., "teblor"]` gives (seen in a scratch run):
   `order[2]: faction 'teblor' has no folder factions/teblor/` (the folder exists, case-insensitively),
   `order: folder factions/Teblor/ is not listed`, and the older, clear
   `factions/Teblor/faction.json: id: faction id 'teblor' does not match its folder name 'Teblor'`.
3. **Kept literal pairs.** `Ai/AbilityManagerTests:21`, `Ai/FarExpandTests:39`, `QA/AbilityManagerQaTests:23`,
   `QA/AiOpeningsQaTests:298`, `QA/EndGameH6QaTests:450 / 677`, `QA/FarExpandQaTests:35 / 360`, `QA/TeamAiQaTests:19` keep
   `new[] { "malazan", "whirlwind" }` because each picks a per-faction id with a ternary
   (`f == "malazan" ? "malazan_laborer" : "whirlwind_camp_follower"`).

## Expected
1. docs/03 "Implementation (M7-3)" says what an integer-division worth of 0 means for the AI (an item cheaper than its
   `spawnCount` in a resource counts 0 of it), so the data track doesn't write a cheap pack and wonder why the AI never
   waves with it. The shipped war-dog row (120 / 0 over 3 = 40) is exact and not affected.
2. The two order-file errors say what is wrong ("listed as 'teblor', folder is 'Teblor'"), or are left out when the
   folder-name check already reports it; "has no folder" is false on a case-insensitive file system.
3. Each helper reads the faction's unit / building by slot from the data (`StartBase.UnitOfSlot(data, f, UnitSlot.Worker)`,
   the house by its building slot) over `TestSim.ShippedFactions`, so a third faction's `ai.json` is edited too; today a
   Teblor AI seat in those scenes would keep its shipped opening.

## Actual
As in Repro. None changes a shipped number or a test result today.

## Notes
- Item 3 is the brief's own carve-out ("unless the test is about those two"); the ternaries make them about those two only
  by accident of how they were written. Low priority; worth doing when a Teblor seat is first used in an AI scene.
