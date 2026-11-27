# BUG-1421: D32 small findings: the expansion's reserve starves the Shaman step behind it, the tooltip's claims hold in a minority, unpinned measured figures, layout nits

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D38, 2026-11-26-0717: item 1 closed with figures; on the shipped per-difficulty orders the expansion holds the reserve in none of the thinks after the Circle stands, on Easy, Normal and Hard). Before: open, item 1 only (Producer, 2026-11-18-0926 ACCEPT: item 1 waited for Request 55 with BUG-1612; item 2 fixed by D34's softened tooltip; item 5 wontfix, the reason is on teblor.md; items 3-4 fixed by D-R39, session 2026-11-15-1536) |
| Found | 2026-11-14-1246, task D32 (QA) |
| System | data (`game/data/factions/teblor/ai.json` `standard`), docs (`docs/factions/teblor.md`), tests (`Content/Fixtures/pre_d32/`) |
| Fixed by | items 3-4 D-R39; item 2 D34; item 1 D38 (the M7-8 switches in the shipped orders: `throughReserve` on the Shaman step of Normal's M1RS and Easy's K9KC10S) |

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

## D38 (2026-11-26-0717): item 1 re-run on the shipped orders; closed
`QA/Content/D32OpeningQaStressTests.ShamanStep_AfterTheCircle_ThinkByThink_Report` (its two rows plus a Hard row D38
added; each seat reads its own list, `AiPlayer.Steps`), from the Circle's completion to 15:00:
- Normal, Pale Hills, the Teblor mirror, seed 1 (M1RS, Shaman step 27 `throughReserve`): Circle up 10:34, the Shaman step
  first queued 10:35; 266 thinks: the reserve held by the Breakers Hall (step 23) in 169, the Anointing (step 20) in 29,
  nothing in 68, **the expansion in none** (D32's opening: the expand step in 239 of 303, never queued).
- Easy, Raraku against the Malazan, seed 1 (K9KC10S, Shaman step 22 `throughReserve`): Circle up 11:54, queued 12:06;
  93 thinks: the Anointing in 43, nothing in 50, the expansion in none (Easy never expands before 15:00).
- Hard, Pale Hills, the Teblor mirror, seed 1 (QRD1, Shaman step 27, no switch): Circle up 10:30, queued 14:26; 539
  thinks: nothing in 520, the Anointing in 19, the expansion in none: the wait is the bank, not the reserve.
- The Shaman step no longer waits behind the expansion's reserve on any difficulty. teblor.md "The unit mix, shipped
  (BUG-1612, D38)".
