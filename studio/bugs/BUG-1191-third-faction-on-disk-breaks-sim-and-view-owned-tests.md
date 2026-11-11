# BUG-1191: A third faction in the shipped data breaks tests the data track cannot edit (sim-owned pins and fixtures, one view scene)

| Field | Value |
| --- | --- |
| Severity | S3 (blocks D28's merge: `main` would go red on files outside the data track; nothing plays wrong today) |
| Status | open (sim: M7-H1 item 1; view: M7-V3a) |
| Found | 2026-11-11-1317 PLAN (Producer, trust-but-verify on the handoff's "D28 is unblocked") |
| System | tests: `sim/Rts.Sim.Tests/**` outside `Content/**` and `QA/Content/**`; `game/tests/QaM6V11Test`; the default seat rule `SimConfig.SeatOf` (`p mod factions`) |
| Fixed by | |

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
