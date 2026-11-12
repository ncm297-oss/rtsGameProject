# BUG-1300: D29's hand-over opening (V4) wins the 10:00 count but plays worse to 15:00: no expansion, no Age II halls, more Teblor defeats; its new description does not hold

| Field | Value |
| --- | --- |
| Severity | S2 (criterion 4: the opening named "to land" and what the player reads; the Producer's QA focus "worse anywhere / expand, every hall, Anointing by 15:00") |
| Status | open |
| Found | 2026-11-12-0546, task D29 (QA) |
| System | data: the D29 fixture `sim/Rts.Sim.Tests/Content/Fixtures/teblor_standard_d29.json` (the proposed Teblor `standard` opening); AI opening play |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D29OpeningQaStressTests.ShippedAndFixture_Seeds1_4_5_To15Minutes_Report|FullyQualifiedName~D29OpeningQaStressTests.Fixture_Seed1_VatharVMalazan" --logger "console;verbosity=detailed"`
   (QA's eight Teblor pairings, the shipped opening V0 and the fixture V4 on data copies, seeds 1, 4 and 5, played to 18,000 ticks).

## Expected
The opening D29 hands over is the best one "ready to land" (brief), and the player-facing description it brings ("War Lodge
and Javelin Ground first, then Age II; two Warriors raid at 4 army pop, then Javelin Throwers, Shamans, Breakers, Destrier
Riders and War-dogs join in Age II") describes what it does. Getting to Age II earlier should not cost the rest of the
plan (expansion, the Age II halls, the Anointing) or more lost matches.

## Actual
By 10:00 V4 is better, as the developer measured (Age II 6 / 6 / 5 of 8 on seeds 1 / 4 / 5, a wave 8 / 8 / 8; V0 1 / 2 / 1
and 4 / 3 / 2). No pairing that reached Age II or waved on V0 loses it on V4. But to 15:00 (24 pairings per variant):

| by 15:00 | V0 shipped | V4 fixture |
| --- | --- | --- |
| expanded (a second finished Longhouse) | 2 / 24 | **0 / 24** |
| every building kind of the opening finished at least once | 2 / 24 | 1 / 24 |
| Breakers Hall ever finished | 2 / 24 | 1 / 24 |
| Shamans' Circle ever finished | 5 / 24 | 6 / 24 |
| Blood-oil Anointing researched | 3 / 24 | 1 / 24 |
| Teblor defeated | 10 / 24 | **14 / 24** |

Five pairings are lost by 15:00 on V4 and not on V0 (seed 1 Raraku v Malazan 14:29, Pale Hills v Malazan 14:59; seed 4
Raraku v Malazan 14:42, Vathar v Malazan 14:49; seed 5 Raraku v Whirlwind 11:36); one the other way (seed 5 Raraku v
Malazan, V0 14:48).

Seed 1, Vathar Crossing v Malazan on V4 (Age II at 7:42, the best kind of row), minute by minute:

| time | gold / wood | Gatherers | army pop | age | waves | gate open / thinks | defending / thinks |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 8:00 | 35 / 165 | 14 | 6 | 2 | 2 | 60 / 60 | 0 / 60 |
| 9:00 | 15 / 35 | 14 | 10 | 2 | 2 | 60 / 60 | 0 / 60 |
| 10:00 | 35 / 65 | 14 | 4 | 2 | 2 | 7 / 60 | 53 / 60 |
| 11:00 | 30 / 145 | 7 | 4 | 2 | 2 | 0 / 60 | 60 / 60 |
| 12:00 | 5 / 15 | 4 | 4 | 2 | 3 | 2 / 60 | 58 / 60 |
| 15:00 | 30 / 75 | 1 | 0 | 2 | 3 | 0 / 60 | 60 / 60 |

The 14-worker economy (9 trained, not 11; the eight more only at 10:00) pays for Age II and a trickle of two-Warrior waves,
then the purse is empty (under 40 gold every minute from 8:00), the army is 4-10 pop when the opponent's attack lands at
10:00, and the Gatherers go 14, 7, 4, 1. The Breakers Hall, Stables, the eight Gatherers, the expansion and the Anointing
are still waiting at 15:00. So "Shamans, Breakers, Destrier Riders and War-dogs join in Age II" is what the player would
read and almost never see (Breakers need the Breakers Hall: 1 of 24; Destrier Riders and War-dogs the Stables, due only
at 10:00).

## Notes
- The developer's report stops at 10:00 (the brief's 12,000 ticks), so its "the file is enough" holds for the count it
  measured; the cost lands after it. The Producer's QA focus asked for 15:00.
- Likely cause: V4 buys Age II by cutting the economy (9 starting Gatherers instead of 11, the eight more moved from 7:00
  to 10:00) and spends the rest on 4-pop waves; nothing in the file makes the extra Gatherers or the Age II halls come
  sooner once Age II is in. A variant that brings the eight Gatherers back right after Age II completes (a `time` step
  just after the Age II step, e.g. 420) was not measured by D29.
- Not a sim defect: the opening is data; nothing shipped changed this session.
