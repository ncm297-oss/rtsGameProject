# BUG-1421: D32 small findings: the expansion's reserve starves the Shaman step behind it, the tooltip's claims hold in a minority, unpinned measured figures, layout nits

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (items 1, 2, 5: D33; items 3-4 fixed by D-R39, session 2026-11-15-1536: the measured figures pinned or labelled as dated, the long lines wrapped, the fixture renamed `save_teblor_frenzy.b64`) |
| Found | 2026-11-14-1246, task D32 (QA) |
| System | data (`game/data/factions/teblor/ai.json` `standard`), docs (`docs/factions/teblor.md`), tests (`Content/Fixtures/pre_d32/`) |
| Fixed by | |

## Repro
1. **The expansion's reserve gates the Shamans.** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D32OpeningQaStressTests.ShamanStep_AfterTheCircle_ThinkByThink_Report" --logger "console;verbosity=detailed"`.
   Normal, Pale Hills, the Teblor against the Teblor, seed 1, seat 0: the Circle stands at 9:57; the expand step (step
   18) holds the reserve (275 g / 275 w) in 239 of 303 thinks from then to 15:00 (until about 13:45). The bank often
   holds a Shaman's 130 g / 60 w (11:12: 340 g / 170 w; 13:42: 285 g / 220 w), but step 21 (train 2 Shamans) comes after
   the reserved step in file order, so it only spends above the reserve and is never queued. In the mirror row on Pale
   Hills, 0 of 16 Teblor seats field a Shaman; against the Malazan / Whirlwind, 9 of 16. Easy, Raraku against the
   Malazan: the Circle stands at 11:06; after that no step holds the reserve in 105 of 117 thinks, the base is "defended" at
   every sampled think from 11:36, and the bank holds 15-60 gold, so again no Shaman. That is the economy, not the
   order.
2. **The tooltip's two new claims hold in a minority.** "then Javelin Throwers and Shamans join them and a second
   Longhouse goes up". QA's rows (`D32OpeningQaStressTests.Opening_Row_Report`), matches with a Shaman / a second
   Longhouse finished by 15:00, of 64: Normal 27 / 18; Easy 39 / 0 (Easy's `expandAfter` is 900 s, so the step can't
   act before 15:00); Hard 31 / 23; mirror Easy 41 / 0, Normal 26 / 19, Hard 58 / 43. Unlike BUG-1390, nothing is
   false in every match; the text describes the plan.
3. **The page's measured figures are not pinned.** In a clone, "a Shaman fielded in 27" -> 28 and "a second Longhouse
   finished by 15:00 in 18" -> 19 in teblor.md "The opening, measured (D32)" pass the whole Content / QA.Content /
   hash-pin subset (2,181 tests). D31's measured paragraph was the same.
4. **Layout.** teblor.md "Houses at the cap" now has two unwrapped lines (up to 458 characters; the page wraps at about
   120). `Content/Fixtures/pre_d32/save_teblor_ai.b64` is 192 KB (`pre_d31`'s save is 16 KB; the developer did not find
   why) and is named "ai" although it holds a Frenzy scene with no AI seat.
5. **The build order is no longer in time order.** The Carver (10:00, step 19) comes before the Circle (9:00, step 20).
   File order sets priority, so this is legal. But BUG-1050's spending-down rule pulls "the first time step not yet due,
   in file order" forward, so a rich seat pulls the 10:00 Carver before the 9:00 Circle.

## Expected
1-2: The opening does what its text says in most matches, or the text says "may". 3: A figure the page prints is
either pinned or labelled as a dated measurement that isn't checked (as it is now). 4-5: Tidy.

## Actual
As above. Nothing crashes, and D32's four Normal figures are met.

## Notes
- Item 1 is the order trade-off the developer chose (X7 keeps the expansion first). X8 / X9 (Circle and Shaman step
  before the Carver) were measured too. A sim-side option is a train step that may spend through the reserve. That
  would be a new rule, so it is a Request for the sim track, not data.
- Item 2 is the Producer's / owner's taste call ("opening text: ..." inbox note).
