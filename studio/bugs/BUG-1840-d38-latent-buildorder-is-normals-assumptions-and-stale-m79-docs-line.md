# BUG-1840: D38 small findings: content-test helpers that assume `buildOrder` is Normal's list, and docs/03's stale "no shipped opening carries the field"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (items 1-2 fixed by QA in the D38 inspection; item 4, the stale docs/03 sentence, fixed by the Producer at the 2026-11-26-0717 ACCEPT and checked at the 1847 PLAN; item 3: `AiContentQaTests` fixed by D39 (2026-11-26-1847), `D21OpeningsQaTests` still open, in the data task after D39) |
| Found | 2026-11-26-0717, task D38 (QA) |
| System | content tests (`sim/Rts.Sim.Tests/QA/Content/`), docs/03 "For the data track (M7-9)" |
| Fixed by | items 1-2: QA D38 inspection (`QA/Content/AiContentQaTests.cs`, `QA/Content/AiHouseRuleReportTests.cs`); item 3, `AiContentQaTests` half: D39 (`QA/Content/AiContentQaTests.cs`) |

## Repro
QA focus (4), the BUG-1771 family. In a scratch clone of D38's head 5c03915d, write the Teblor `standard` opening so that
every seat plays **the same list as shipped**, but `buildOrder` is no longer Normal's list:
`buildOrder` = Easy's K9KC10S, `buildOrders` = { "normal": M1RS, "hard": QRD1 }. The loader refuses an entry for all
three difficulties, so a straight `normal` entry beside `easy` and `hard` cannot be written. Then run
`dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf&Category!=Stress&Category!=Soak"`.

Expected red: the rows that pin the file's layout (the fixtures, the page tables, the hash). Any other red row is a
helper that edits or reads `buildOrder` assuming it is the list a Normal seat plays.

## Expected
Rule 79: a helper replaces every list or removes `buildOrders`, and reads a seat's list by its difficulty.

## Actual
Two rows outside the layout pins go red:
1. `QA/Content/AiContentQaTests.E_AResearchWhoseBuildingNoStepPlaces_FailsByName_AndWaitsAtRunTime(teblor, 0)`. It removes
   the Forge's build step from `buildOrder` and keeps `buildOrders`, so the Normal seat plays the untouched `normal` entry
   and the research is sent. **Fixed by QA:** the copy now removes `buildOrders`.
2. `QA/Content/D20PinQaTests.HouseNote_SaysWhatTheMenuMapRunsShow(teblor)`: "without the 12th Hut step ... the cap reaches
   100 at 1031 s" (the page says 1007 s). `AiHouseRuleReportTests.TwelfthHouseRun` (D38's edit) removes the step from
   `buildOrder` and drops `buildOrders`, so the Normal seat plays Easy's list minus a step. **Fixed by QA:** it now takes the
   step out of the list a Normal seat plays (`buildOrders.normal` if present, else `buildOrder`) and writes that list as
   `buildOrder`. Its precondition reads `BuildOrderFor(Normal)`.

Both rows are correct on the shipped file today (Normal plays `buildOrder`). They would go wrong silently on the first
opening that ships a `normal` entry.

Open:

3. QA's static oracles in `QA/Content/AiContentQaTests` (a-d, g) and `QA/Content/D21OpeningsQaTests` read only
   `o.BuildOrder`. `Content/AiContentTests` C-F, which D38 extended, check every list, so nothing is unchecked today.
   QA's independent oracles do not yet cover Easy's and Hard's lists. QA's new `QA/Content/D38QaTests` covers the page and
   file pins of every list.
4. docs/03 "For the data track (M7-9)" (line ~5918) still says "No shipped opening carries the field." Since D38 the Teblor
   `standard` carries `buildOrders` (easy, hard). This is the sim track's page; the D38 developer raised it as teblor.md
   "Questions for the Producer (D38)" 2.

## Notes
The forge run on 5c03915d: 29 failed / 10,557 passed / 20 skipped. 27 of the failures are layout or hash pins
(`AiContentTests.A`, the 13 `D38ShippedOrdersTests` rows, `ReserveSwitchContentTests.ASwitchedExpandInAnEntry(easy)`,
`TeblorOpeningFixtureTests`, 6 `TeblorRosterPinTests`, `D33QaTests.TeblorStepTable`, the three golden-header rows) plus
BUG-1771's `WorkerCarryTests`. The other two are items 1-2. With QA's edits, both pass on the forged data (10 / 10) and on
the shipped data. BUG-1771's ~100 sim-owned sites outside the content folders (S3, sim track) turned no row red under the
forge.

## Fix (D39, item 3, the `AiContentQaTests` half)
`QA/Content/AiContentQaTests` (a)-(d) and (g), and the oracles of `Shipped_PassesEveryOracle`, now run over every list a
seat can play: `buildOrder` and each `buildOrders` entry (`ListsOf`, theory data `Lists`; a problem in an entry names it,
"teblor standard buildOrders.hard step 3 ..."). `Lists_AreEveryListOfEveryShippedOpening` proves the theory data holds
every seat's list (`BuildOrderFor` of each difficulty), counted from the loaded data. Forged rows on every shipped
opening with entries for one difficulty (`hard`) and two (`normal,hard`, `easy,hard`; any shipped `buildOrders` replaced
whole, rule 79): `AnEntryForOneOrTwoDifficulties_LoadsAndEveryListPassesTheOracles`, `..._ItsHardEntryNamingTheOtherFactionsId_IsRefusedNamingTheEntry`
(check (a), refused at `openings[k].buildOrders.hard[i].id`), `..._ItsHardEntryWithAStepTwiceOrAnOffGridPop_IsRefusedNamingTheEntry`
((c), (d)) and `..._ItsHardEntryPlacingNoAgeIBuilding_FailsTheAgeIOracleNamingTheEntry` ((b) / (g)'s oracle). Three
entries are refused by the loader (ruling (a)), so not rehearsed (rule 80). (e), (f), (h)-(j) are unchanged (not in the
brief). Left open: `QA/Content/D21OpeningsQaTests` (the sim track held it this session, rule 68).
