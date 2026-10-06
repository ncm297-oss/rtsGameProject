# BUG-0056: M1-7 nits: Queued flag accepted on non-order kinds, corridor Hold test misses walkers passing, Hold lasts one tick under a queued order

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-05-2330, task M1-7 |
| System | commands / orders / tests |
| Fixed by | 6d1cbfd (M1-9); see the M1-9 re-check below |

Three small findings, grouped.

## 1. `QueuedFlag` on a SpawnUnit or Noop is valid
`Command.IsValid()` and `Replay.Validate` check only for unknown bits, so `SpawnUnit` or `Noop`
with `Flags = 1` passes, applies (the flag is ignored), and goes into the replay and the hash. It's
harmless, but it means a meaningless value is part of the wire format. Either refuse `Flags != 0`
on a command that isn't a unit order (`!IsUnitOrder`), or document that it's ignored.

## 2. `OrderTests.HoldingUnit_InACorridor_IsNeverPushed_ButAStoppedOneIs` doesn't check passing
The test asserts the holder's position bit for bit, but not that walkers stay behind it, so it can't
see BUG-0055 (10 of 30 own walkers end past a radius-0.9 holder in the same kind of corridor). QA's
`QA/OrderQaTests.FriendlyHoldingPlug_*` and `EnemyHoldingPlug_*` now cover that.

## 3. Unqueued Hold followed by a queued order: Hold lasts one tick
`HoldPosition` (unqueued) clears the queue and holds. A later shift-queued Move is appended, and
since the unit is Idle, phase 7 pops it in the same tick and clears Hold. So "hold, then shift-click
a waypoint" walks off at once. That matches the brief and docs/03 ("a queued Move or AttackMove when
it starts, clears Hold"), and it's the usual RTS behavior, so nothing needs to change. It's worth a
line for the view track (M2-3): the HUD should not show Hold as a persistent stance once an order is
queued after it.

## Producer triage (2026-10-05-2330)
S4 stands; M1 end-of-milestone hardening. Item 1: refuse `Flags != 0` on non-unit-order kinds (with BUG-0054). Item 2: add the walkers-stay-behind check to the dev corridor test once BUG-0055 is fixed. Item 3: by design; the M2-3 brief tells the view not to show Hold as a stance once an order is queued after it.

## Re-check M1-9 (2026-10-06-0905, commit 6d1cbfd): fixed
Item 1: `QueuedFlag` on `Noop` / `SpawnUnit` is refused by `Enqueue` and `Replay.Validate` (new
`SimulationTests` and `ReplayFormatTests` rows fail on base). Item 2: `OrderTests.HoldingUnit_InACorridor_IsNeverPushed_ButAStoppedOneIs`
now asserts walkers stay behind the holder (fails on base). Item 3: by design (Producer triage).
