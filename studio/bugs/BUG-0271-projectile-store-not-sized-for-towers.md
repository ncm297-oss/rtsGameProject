# BUG-0271: The default projectile store is sized for units only; towers share it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-09-0125, task M4-3b (builder's known limit) |
| System | combat (projectiles, towers) |
| Fixed by | |

## Repro
Reasoning, not observed: `SimConfig.ProjectileSlots` defaults to `players x popCap / smallest shooter pop` (200 for two
players), the most unit shots that can be in the air at once. Towers are not population-capped (up to
`BuildingCapacity` = 256 of them), and since M4-3b each can have one shot in the air.

## Expected
Every shot fired is in `World.Projectiles` (docs/03 "Implementation (M4-2b)": a full store loses the shot, which the
default size was chosen never to need).

## Actual
With every population slot a shooter with a shot in flight, a tower's shot is lost. Needs a near-impossible army (200
shooters, all with a shot in the air on the same tick); a normal match never fills the store.

## Notes
Sizing the default up by `BuildingCapacity` costs about 30 KB on the 1,024-cell map with 4,096 unit slots
(`FieldBuildFairnessQaTests.World_1024Map_CacheStays32_MemoryBounded`: 230,726,944 bytes before M4-3b, 230,756,976 after,
bound 230,765,000, so 8 KB left); a smaller term
(towers a player can afford, a data cap on towers) needs a design rule. Producer's call.
