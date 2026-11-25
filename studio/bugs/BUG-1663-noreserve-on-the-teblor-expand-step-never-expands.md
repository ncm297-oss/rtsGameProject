# BUG-1663: `noReserve` on the Teblor expand step starves the expansion for good: 0 of 24 seats expand by 30:00 (shipped: 22 of 24)

| Field | Value |
| --- | --- |
| Severity | S3 (the switch works as specified; the order docs/03 offers the data track as "B" never expands, and the page does not say so) |
| Status | closed by ruling (Producer, 2026-11-24-1431 ACCEPT: the rule stays as built; documented in docs/03 "For the data track (M7-8)" and docs/01 2026-11-24; no opening ships `noReserve` on an expand step without 30-minute rows) |
| Found | 2026-11-24-1431, task M7-8 |
| System | AI build order (`BuildOrderExecutor.Run`'s `noReserve`), docs/03 "For the data track (M7-8)" |
| Fixed by | docs only (the Producer's line in docs/03 at the ACCEPT); QA's `M78ReserveSwitchQaStressTests.NoReserveOnTheTeblorExpand` keeps the measurement |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M78ReserveSwitchQaStressTests.NoReserveOnTheTeblorExpand"`
   (Stress, about 16 minutes under load): the Teblor mirror, order B (`TeblorSwitchOrders.Data("B")`: `throughReserve` on
   the Rider / War-dog / Shaman train steps, `noReserve` on the expand step) against the shipped order, Pale Hills,
   Raraku and Vathar Crossing x Easy and Normal x seeds 1-2, to 36,000 ticks (30:00).
2. Read the tick each seat's expand step is done and its second Longhouse stands.

## Expected
docs/02 and docs/03: a `noReserve` step "is still bought the moment it can be paid". The data track reads docs/03's
order B as a candidate ("B (the expand never saved for) costs Normal the second Longhouse ... by 15:00").

## Actual
| order | expand step done (seats of 24) | second Longhouse by 30:00 (seats of 24) |
| --- | --- | --- |
| shipped | 22 (8:51-18:09; never: Vathar Normal seat 1 x2) | 20 |
| B | 0 | 0 |

With the expand step unreserved, the bank never holds the Longhouse's 275 / 275: the steps behind it and the production
manager spend everything as it comes in (the production manager spends above a reserve, and with the expand out of the
reserve the reserve is another step's or nothing). Seeds 1 and 2 give the same times per map (different end hashes).

## Notes
- Not a defect of the switch's rule as written (it does what docs/03 says); it is a trap for the data track: a `noReserve`
  expand is in practice "never expand" while the build order and production have something to buy. docs/03 "For the
  data track (M7-8)" should say so in one line (order B is not "costs the second Longhouse by 15:00" but "never expands
  by 30:00 on any map measured"), and the data track should not ship `noReserve` on an expand step without its own
  30-minute rows.
- If the Producer wants a `noReserve` expand to still happen, that is a rule change (e.g. the switch lets the next step
  be reserved only while the expand is not yet affordable within N thinks), not this task's.
