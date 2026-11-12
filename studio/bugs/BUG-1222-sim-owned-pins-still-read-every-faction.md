# BUG-1222: Sim-owned pins that still read every faction: red on a Whirlwind-shaped copy and with the real Teblor roster

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-11-1317, task M7-H1 (QA full) |
| System | sim test suite (third-faction readiness, BUG-1191 follow-up) |
| Fixed by | |

## Repro
1. **Whirlwind-shaped third faction** (QA focus item 1's hand-made copy): on a scratch clone of `studio/2026-11-11-1317-sim`
   (1a49bbc), copy `game/data/factions/whirlwind/` to `factions/mirage/` with `whirlwind_` → `mirage_`,
   `"dryjhnas_prophecy"` → `"mirage_prophecy"`, `"sandstorm"` → `"mirage_sandstorm"`, the faction id set, and append
   `"mirage"` to `common/factions.json` (the same recipe as `Stress/M7H1KillCounterQaStressTests.AddWhirlwindCopy`).
   Smoke PASS. Default loop: 8,586 / 376 / 19 of 8,981; outside `Content/**` / `QA/Content/**` 4 red: the two golden-header
   rows (by design until D28 regenerates the header) and:
   - `AttackTargetsLoaderTests.ShippedData_TheRamAttacksBuildingsOnly_EveryOtherUnitAll`: "Expected: All Actual: Buildings"
     (`mirage_battering_ram`; the row reads `TestSim.Data` and names only `whirlwind_battering_ram`);
   - `PassiveLoaderTests.Shipped_Zealot_HasFrenzyOfTheApocalypse_AndMartyrdom_AndNoOtherUnitHasPassives`: "mirage_zealot has
     passives".
   Neither fails on `tools/qa/third-faction-copy.ps1`'s Malazan copy (Malazan has no ram and no passive), which is why
   BUG-1191's list and M7-H1's runs missed them.
2. **With the real roster** (the data branch `studio/2026-11-11-1317-data` at 7093dec merged into the sim branch on a
   scratch clone): the default loop is 8,807 / 28 / 19 of 8,854, all 28 outside the data folders (0 in `Content/**` /
   `QA/Content/**`):
   - 23 fixture rows whose copy adds an `onHit` passive to every unit and now collides with `teblor_war_dog`'s own
     ("teblor_war_dog already has a 'onHit' passive (passives[0]); a unit has at most one per trigger"):
     `OnHitPassiveTests` x9, `PackMatchTests` x3, `QA.PackQaTests` x11; and `PassiveLoaderTests.Shipped_Zealot_*`
     ("teblor_war_dog has passives");
   - `DataConversionTests.Scale_AbsentIsOne_AndOneAndAHalfLoads` (6 of 21 units have a scale) and
     `SpawnCount_AbsentIsOne_ThreeLoadsAsThree` (1 of 21);
   - `SelfAuraTests.TheTestAbilities_LoadAsSelfAura_WithNoRange` ("Expected: TargetGround Actual: SelfAura");
   - `QA.FieldBuildFairnessQaTests+Serial.World_1024Map_CacheStays32_MemoryBounded`: 232,643,520 bytes, deterministic
     (re-run alone: the same); 232,509,584 on the branch and 232,512,448 with the Malazan copy: the Teblor tables add
     ~134 KB, past the row's 72 KB margin.

## Expected
The M7-H1 goal: every test outside the data track's folders holds "for any appended faction". A pin about the two shipped
factions pins them (`TestSim.TwoFactionData` / the first two factions), and a fixture that edits "every unit" either cuts its
copy to the first two factions or skips units that already use the hook.

## Actual
As above: 2 rows on a Whirlwind-shaped copy, 28 with the real Teblor roster (27 + the memory bound).

## Notes
- The 27 Teblor rows (all but the memory bound) are the "32 fixture rows that assume no shipped unit uses an M7 hook" of the data track's list, less
  `RegenTests.ShippedData_NoFactionRegenerates` and the four `DataConversionTests` regen rows that M7-H1 did fix; the 57
  two-faction rows and the 11 behaviour / bound rows other than the memory bound are gone on the merged tree.
- Means that fit: `AttackTargetsLoaderTests` / `PassiveLoaderTests.Shipped_*` / `DataConversionTests.*_AbsentIsOne` /
  `SelfAuraTests` on `TestSim.TwoFactionData`; the `OnHitPassiveTests` / `PackMatchTests` / `PackQaTests` copies built on
  `TestDataDir.CopyOfShippedTwoFactions()`. The memory bound needs a Producer ruling (re-measure with the roster, or measure
  on the two-faction data); QA does not loosen it.
- Two rows are QA's (`QA.PackQaTests`, `QA.FieldBuildFairnessQaTests`); the sim track may edit them under the same means.
