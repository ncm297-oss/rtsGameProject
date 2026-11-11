# BUG-1186: 19 Content / QA.Content test files still list `{ "malazan", "whirlwind" }`, and teblor.md lacks the AI bullets they read

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-10-1236, task D-R36 |
| System | Tests: `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`; docs: `docs/factions/teblor.md` |
| Fixed by | |

## Repro
1. `git grep -n -e '"malazan", "whirlwind"' -e 'InlineData("malazan")' -- sim/Rts.Sim.Tests/Content sim/Rts.Sim.Tests/QA/Content`
   on D-R36's HEAD (`b7082a8`).
2. `grep -n "Defense\|High starting" docs/factions/teblor.md`

## Expected
The brief's scope: "Content/** and QA/Content/** read factions from TestSim.Data.Factions instead of the literal pair", so
that when D28 adds `teblor` the existing pins cover it or fail by name. That needs teblor.md to have every section those
pins read.

## Actual
The seven named pin classes iterate `ContentFactions.Keys` (good). These still list the pair, so a third faction's files
would be **silently skipped**, not pinned or failed:

- Content: `AiOpeningPairsReportTests` (150, 169-170), `AiSpendingReportTests` (72-73, 103, 125, 207),
  `BuildingKillTimeReportTests` (92), `CounterTriangleMarginsTests` (115-116).
- QA/Content: `AiHouseRuleReportTests` (106-107), `BuildingRosterQaTests` (93), `D18ReportPreconditionQaTests` (54),
  `D19QaTests` (137), `D20DefenseWordingQaTests` (21-68, 4 theories), `D20PinQaTests` (39-99, 3 theories),
  `D21OpeningsQaTests` (22, 35-36), `DR35ProsePinQaTests` (77-78), `UnitRosterQaTests` (129),
  `ZealotPassivePinQaTests` (19).

Several of the prose pins (D20PinQaTests / D20DefenseWordingQaTests: the `- **Defense**` bullet; the "High starting
resources" bullet) read bullets that teblor.md does not have, so converting them in D28 will also need page text.
`BuildingRosterQaTests` / `UnitRosterQaTests` (one stat changed moves the hash) are cheap to convert and would cover the
new roster as soon as it lands.

## Notes
The developer disclosed "several report/prose tests stay typed to the two shipped pages". The report-category ones (spending,
pairings, kill time) are reasonably two-faction until the Teblor are measured; the roster / prose pins are not. The
acceptance criteria name only the seven pin classes, so this is S3. Suggest D28 converts the roster and prose pins and adds the
Defense / High-starting-resources bullets to teblor.md, or the Producer rules them two-faction by design.
