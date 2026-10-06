# BUG-0071: In the shove pass a cached plug answer can depend on which member was asked first (stale spatial hash)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-06-0905, task M1-9 |
| System | movement (plug test cache, BUG-0044) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HardeningQaTests.PlugCache_StaleHash_ConstructedPair_AnswerDoesNotDependOnQueryOrder"`
   (skipped under this bug; remove the Skip to run it).
2. Setup: 2-cell corridor (y 6-10 m). Player 1's m (r 0.4) at (33, 6.5) touches the south wall; player 1's
   e (r 0.9) is in the spatial hash at (33, 8.62) and has since moved 0.03 m to (33, 8.59) (as a unit that
   stopped this tick has, by the time the shove pass runs). Gap m-e is now 0.79 m, under a r-0.4 walker's
   0.8 m diameter, so {m, e} spans the corridor wall to wall.
3. Ask `IsPlug` for a player-0, r-0.4 unit in one pass: m first, then e; and in a new pass: e first, then m.

## Expected
docs/03 "Implementation (M1-9)" and the `IsPlug` remarks: the answer is "a set property (which units are
linked, how many, which walls they touch), so the visiting order changes nothing".

## Actual
```
m asked first: m False, then e False; e asked first: e True, then m True
```
Asked from m, the search can't see e (its hashed point, 2.12 m away, is past m's query radius
r_m + MaxUnitRadius + pass = 2.1 m) and caches "no" for m; asked next, e finds m already answered and
copies "no". Asked from e first, the search finds m (m didn't move) and both get "yes". Before M1-9 each
root got its own answer every time (no order dependence); the cache makes the first unit asked decide
for the whole cluster, so in the shove pass (`ShovedIntoEnemy`, called in slot order) a plug can go
unrecognized depending on slot order.

## Notes
- Plan is not affected: there the hash and the positions agree (start of tick). QA's differential test
  `PlugCache_AnyQueryOrder_EqualsAFreshSearch` (5 random worlds, 1,635-3,168 pairs each, 3 shuffled
  orders) matches a fresh search on every pair; random 0-speed jitter after the hash
  (`PlugCache_StaleHash_QueryOrderDependence_Report`) showed 0 order-dependent pairs, so it takes the
  constructed geometry (a wide member that stopped this tick within a hair of the link distance).
- Deterministic (same slots, same answer), so no hash-twin risk; it adds a slot-order dependence of the
  BUG-0046 kind and makes the docs' claim false in the shove pass.
- Likely fix: in the shove pass widen `SearchPlug`'s query by `world.MaxUnitSpeed` (as `SettleBackedOff`
  does) and keep the exact gap test, so discovery is symmetric again; or rebuild the hash before the
  shove pass if that is affordable.
