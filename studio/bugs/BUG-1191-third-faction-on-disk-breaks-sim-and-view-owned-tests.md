# BUG-1191: A third faction in the shipped data breaks tests the data track cannot edit (sim-owned pins and fixtures, one view scene)

| Field | Value |
| --- | --- |
| Severity | S3 (blocks D28's merge: `main` would go red on files outside the data track; nothing plays wrong today) |
| Status | fixed (sim part: M7-H1, 1a49bbc; view part: M7-V3a, 493f294; both verified by QA; what the real roster still breaks is BUG-1222) |
| Found | 2026-11-11-1317 PLAN (Producer, trust-but-verify on the handoff's "D28 is unblocked") |
| System | tests: `sim/Rts.Sim.Tests/**` outside `Content/**` and `QA/Content/**`; `game/tests/QaM6V11Test`; the default seat rule `SimConfig.SeatOf` (`p mod factions`) |
| Fixed by | M7-H1 (sim part): `tools/qa/third-faction-copy.ps1`; the sim-owned tests made third-faction-ready by the four means of the brief (docs/03 "Implementation (M7-H1)"); `KillCounterOracleTests` (the sim's counter is right). See "M7-H1" below |

## Repro
1. A scratch clone of `main` (451284f). Put a third faction on disk the way `Data/FactionOrderTests.AddTeblor` does on a
   temp copy: copy `game/data/factions/malazan/*` to `game/data/factions/teblor/`, replace `malazan_` → `teblor_`,
   `moranth_supply` → `teblor_blood_oil_anointing`, `"telas_fire"` → `"blood_oil_frenzy"`, `"cusser"` → `"teblor_cusser"`,
   set `faction.json` `id` to `teblor`, and write `common/factions.json` `{ "order": ["malazan", "whirlwind", "teblor"] }`.
2. `dotnet build RtsGame.sln`, then the default loop
   (`dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf&Category!=Stress&Category!=Soak"`), `tools/qa/smoke.ps1`,
   `tools/qa/scene-loop.ps1 -SkipPlayable`.

## Expected
M7-3 / M7-3b's promise: a third faction appended to the order moves no Malazan v Whirlwind id, digest or outcome, so
D28 (data only) can land with every test outside the data track's folders green and unedited.

## Actual
The ids hold, but tests outside the data track's folders assume two factions:
- **By reading (certain):** `Data/FactionOrderTests.TheShippedIds_*` asserts `ShippedFactions == { malazan, whirlwind }`
  and the whole unit / building / tech / ability tables (not their prefix); `ABrokenOrder_IsOneErrorAtItsEntry`'s rows
  build on a copy of the shipped data (`["malazan"]` then leaves two folders unlisted, `Assert.Single` fails; the
  "teblor has no folder" row loads clean); `AddTeblor` (used by `FactionOrderTests`, `FactionOrderQaTests`,
  `TechAbilityIdRuleQaTests` and others) writes a Malazan copy over a real `factions/teblor/`.
- **The default seat rule:** `SimConfig.SeatOf` gives player p faction `p mod factions`. With three factions every
  3-4 player row on default seats gets a Teblor seat 2 and a Malazan seat 3 (was Malazan, Whirlwind), so its pinned
  digests and outcomes move (the FFA / 2v2 criterion rows, the four-seat hunt rows and the like).
- **Measured on the copy (Producer, three suites at once on the PC):** smoke PASS; scene loop 89 / 91: `DebugOverlayTest`
  (the known 1 ms frame flake under load) and **`QaM6V11Test`: "at the end (tick 21600): row 3 UnitsKilled '68' (oracle
  '72')"** in its four-seat match (`seats malazan human, whirlwind ai hard, teblor ai normal, malazan ai hard`). Either
  the sim's kill counter or the scene's oracle is wrong once seat 2 plays a third faction; not diagnosed.
- **The default loop on the copy (Producer, 54 m beside two other suites): 8,604 / 345 / 23 of 8,972** (`main` itself
  the same hour: 8,631 / 0 / 23 of 8,654). 279 failures are in 24 classes under `Content/**` and `QA/Content/**` (the
  copy is Malazan's numbers under the Teblor page: the real roster is what makes those green, D28's job). **66 failures
  are in 22 sim-owned classes:** `QA.TeamQaTests` 16, `QA.FactionOrderQaTests` 15, `FactionOrderTests` 8,
  `TechLoaderTests` 3, `DataValidationTests` 2 (`ShippedData_TrainedAt_ResolvesForAll14Units`,
  `ShippedData_Buildings_TownHallOfEachFaction_MatchesDocs02`), `AbilityLoaderTests` 2,
  `Stress.RequirementHostileFuzzStressTests` 2, `QA.TechDataQaTests` 2, `Save.SaveFormatTests` 2
  (`AGeneratedMatch_SavesFormat4_ByteForByte_AsBeforeMapFiles`: the save is 560 bytes longer), `SeatConfigTests` 2, and
  one each in `EveryUnitFightsTests` ("14" units), `QA.RequirementQaTests`, `QA.TrainedAtLoaderQaTests`,
  `MovementSystemTests`, `ProjectileLoaderTests`, `QA.ProjectileLoaderQaTests`, `StateHashTests`, `ReplayFormatTests`,
  `TowerAttackLoaderTests`, `QA.PackQaTests`, `BuildingSightLoaderTests`, `DataConversionTests`. Typical messages:
  "Expected: 14 Actual: 21", "Expected [malazan, whirlwind] Actual [malazan, whirlwind, teblor]", `Assert.Single` on
  an error list that now has one more faction's error, "seat 3 whirlwind human normal 2" not found in a replay header.
  Not run on the copy: the Stress, Soak and Perf categories (the 3-4 player pin rows live there).

## Notes
- Fix on the sim side (M7-H1): pins about the two shipped factions pin a prefix; fixtures that need "a faction the data
  lacks" pick an id the data lacks; rows that mean Malazan v Whirlwind say so with explicit `Seats` (explicit default
  seats hash the same as the defaults, docs/03, so no pinned value changes); a sim-side oracle row for the kill counter
  with a third faction in a four-seat match.
- Fix on the view side (M7-V3a): `QaM6V11Test`'s oracle against a forged three-faction copy.
- D28 merges only when the merged tree is green; its own branch may be red in sim-owned or view-owned tests only.

## M7-H1 (sim part, game-dev)
- **The tool:** `powershell -File tools/qa/third-faction-copy.ps1 -Repo <scratch clone> [-Id <id>]` (names `<id>_upgrade`,
  `<id>_fire`, `<id>_cusser`; refuses a `.claude\worktrees` path, a linked worktree, the main checkout, a non-snake_case id,
  an existing `factions/<id>` and an id already in the order). Applied as `andii` then `zz_horde` on one clone (four factions):
  smoke PASS (the clone's first smoke run fails only on the LFS-pointer looktest art, as `smoke.ps1` says; the second PASS).
- **Default loop on a clone with `teblor` (the tool's copy), before:** 8,605 / 344 / 23 of 8,972; **65 rows in 22 sim-owned
  classes** (the Producer's list, plus `MovementSystemTests.MoreGoalsThanCacheSlots_*` (41-80), `QA.PackQaTests` and
  `FactionOrderTests`' golden-header rows); 279 rows in 24 content classes.
- **After:** the same clone with the branch: 8,674 / 293 / 19 of 8,986: sim-owned red = the two golden-header rows
  (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA.PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`:
  green once D28 regenerates the header, rule 51) and 12 `Cli` rows of an intermediate edit since reverted (the CLI's own
  `k mod types` mirror). On a four-faction clone (`andii`, `zz_horde`) with the golden header regenerated there:
  8,656 / 631 / 19 of 9,306, sim-owned red 2 (`DataValidationTests` hall prefix with an underscore id, `DataConversionTests`
  "andii" as the missing id), both fixed and re-run green there (534 / 534 in five classes). Content classes still red
  (the data track's): AbilityContentTests 22, AbilityPinQaTests 7, AiAdaptsContentTests 1, AiContentFileSideQaTests 77,
  AiContentQaTests 99, AiContentTests 3, AiHouseRuleReportTests 4, BuildingContentTests 9, BuildingKillTimeReportTests 1,
  BuildingRosterQaTests 3, D20DefenseWordingQaTests 1, D20PinQaTests 3, D21OpeningsQaTests 1, DR35ProsePinQaTests 1,
  PagePinCultureQaTests 12, PassiveContentTests 1, SandstormBlindedPinQaTests 4, SharedTechTextQaTests 1,
  TeblorPageParseQaTests 1, TechContentTests 4, TechRosterQaTests 1, UnitContentTests 19, UnitRosterQaTests 3,
  ZealotPassivePinQaTests 1.
- **Stress on the `teblor` clone, before:** 525 / 2 / 4 of 531: `StandardPresetPinQaTests.EverySaveScene_*(defeated_seat)`
  (default seats: seat 2 became the copy) and `TechAbilityIdRuleQaTests.ACastingMatch_*` (an order file over a copy with a
  third folder); skips = BUG-1156, BUG-1151, the Pale Hills hunt stall, BUG-1149. **After:** 532 / 1 / 4 of 537 (six new rows): the one red, `TechAbilityIdRuleQaTests.ACastingMatch_*`, was fixed while the run went (copies cut to two factions) and the class re-run green on the same clone and on the four-faction clone (8 / 8 with its Stress rows); the same four skips. On the branch (two factions) the Stress pin classes (`OutcomePinM73bQaTests`, `FactionOrder*`, `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, `SharedVisionFfaPinTests`, `ReserveTwinTests`, `MatchCriterionTests`, `HuntStressTests`, `MapSlice2StressTests`, `CounterCompositionTests.TheNonHardPins_*`, `RegenQaStressTests`, `KillCounterOracleTests`, `TechAbilityIdRuleQaTests`): 259 / 0 / 1 (the Pale Hills hunt stall). No pinned value edited.
- **The kill counter (scene finding):** `KillCounterOracleTests` (Stress), four seats on a 128 map (idle human, AI Hard /
  Normal / Hard, faction `p mod factions`), 21,600 ticks, seeds 11-13, three factions and two: every seat's unit and building
  kills and losses equal a `World.Deaths` oracle (6 / 6 green). Seed 11 with the third faction reproduces the scene's row
  exactly: seat 3 (Malazan, Hard) unit kills **68**, plus **4 own-unit kills** the sim (rightly, M6-S5: a friendly-fire death
  is nobody's kill) does not count: tick 19954 its `malazan_laborer` at (146.7, 137), 19956 `malazan_heavy_infantry` at
  (147.5, 134.9), 20177 at (142.4, 137.5), 20193 at (150, 136.3). 68 + 4 = the scene oracle's 72: the scene's oracle counts
  `KillerOwner >= 0` without the enemy check; the sim's counter is right.

## View half (M7-V3a, game-dev; applied by the Producer at the 2026-11-11-1317 ACCEPT)
- **Cause of 68 v 72: the scene's oracle, not the sim.** With the third faction on disk seed 11's default seats are malazan,
  whirlwind, teblor, malazan (`p mod 3`): seat 3 (Hard) plays Malazan. Four of its own units die to its own army's splash
  (Sappers x4 and Cadre Mages x3-4 beside them): tick 19,954 a `malazan_laborer`, ticks 19,956, 20,177 and 20,193 a
  `malazan_heavy_infantry` each, owner 3, killer owner 3. Since M6-S5 `World.RecordDeath` counts a friendly-fire death as the
  victim's loss and nobody's kill, so the stats row's 68 is right; the oracle credited every death with a killer (72).
- **Fix:** `game/tests/QaM6V11Test.cs` `Observe` applies `World.IsEnemy(killer, victim)` and prints every friendly-fire death.
- **Proof:** QA's scratch clone with the recipe: `QaM6V11Test` PASS (278 s), 4 friendly-fire deaths; PASS on the shipped data.
  Sim side: `KillCounterOracleTests` (6 rows) and QA's `M7H1KillCounterQaStressTests` (20 rows) equal a death-list oracle.

## Closed (Producer, 2026-11-11-1317 ACCEPT)
The Malazan-copy readiness holds (QA: copies `aardvark` and `zulu`, only the two golden-header rows red outside the data
folders, by design). With the real roster 28 sim-owned rows are still red: BUG-1222 (the sim's next task; D28 is held for it).
