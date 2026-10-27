# BUG-0990: The save's length pre-check (BUG-0960 item 1) leaves out the command, projectile and zone capacities: a forged header still allocates ~170 MB, and a forged command capacity loads

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-18-1236, task M6-S3 |
| System | sim: `Save/SaveHeader.MinBodyBytes`, `Simulation.TryLoad` |
| Fixed by | M5-H4, session 2026-10-26-2108 |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiSaveQaTests+Serial.AForgedHeader_EveryCapacityAtMax_AndEachOneAlone"`
   (prints each case). It takes the small save (human or AI), sets the header's capacities to `SaveFormat.MaxCapacity`
   (1,000,000), recomputes the checksum and loads it, counting this thread's allocation.

## Expected
BUG-0960 item 1: a header that asks for huge stores is refused before `World.ForLoad` allocates them. The brief's
criterion (every capacity at the maximum) is met. A header with only some capacities at the maximum should get the
same treatment, and a header capacity that doesn't match the body should not load.

## Actual
| Forged at 1,000,000 | Result | Allocated | Time |
| --- | --- | --- | --- |
| every capacity | Malformed | 101 KB (AI save: 111 KB) | 0.2 ms |
| unit, resource or building alone | Malformed | 101 KB | 0.2 ms |
| **command alone** | **None (loads)** | 52 MB | 4-14 ms |
| projectile alone | Malformed | 88 MB | 5-14 ms |
| zone alone | Malformed | 42 MB | 2-5 ms |
| command + projectile + zone | Malformed | 182 MB | 20 ms |

- `MinBodyBytes` counts only units (32 B a slot), buildings and resources (16 B each) and grid cells (2 B each). The
  projectile and zone stores (each writes per-slot arrays the reader length-checks) and the command queue are
  allocated at their header sizes before the body is read.
- The command queue's capacity is not a length the body holds (the `CMDS` section is a count and the commands), so a
  forged command capacity passes every check. The loaded sim then has another `CommandCapacity` than the saved one: it
  accepts more pending commands per tick, and its next save writes the forged value.

## Notes
Only a hand-forged file (checksum recomputed) gets here, and 182 MB is far below the 1.4 GB that item 1 was about, so
this is S4. Possible fix: add terms for the projectile and zone slots to `MinBodyBytes` (anything below what their
sections write per slot). For the command capacity, either bound it (e.g. no larger than a constant the game uses) or
accept that it is configuration, not stored state, and say so in docs/03. Related: BUG-0960 items 2-3 (still open).

## Fix (M5-H4, session 2026-10-26-2108)
- `SaveHeader.MinBodyBytes` adds 48 bytes a projectile slot (the store writes 68) and 24 a zone slot (26 plus the per-player
  boxes). The command capacity is configuration, not a body length: `SaveFormat.MaxCommandCapacity` = 65,536 (16x the
  game's 4,096) bounds it, refused `Malformed` with the length pre-check in `Simulation.TryLoad` (no new error code; the
  frozen signatures unchanged). At or below the bound a forged command capacity loads (at most about 4 MB); docs/03 "Save
  format (M6-S2)" says so.
- Tests: `SaveRefusalTests.Serial.AForgedCapacity_AloneOrTogether_IsRefused_UnderAMegabyte` (each capacity alone, the
  command / projectile / zone three, all six; human and AI saves: `Malformed`, under 1 MB allocated; the command, projectile,
  zone and three-together rows failed before), `SaveRefusalTests.AForgedCommandCapacity_IsBoundedByTheFormat` (65,537 and
  1,000,000 refused, 65,536 loads), `SaveRefusalTests.TheLengthPreCheck_PassesARealSaveOfAnEmptyWorld_AtLargeCapacities`
  (100,000 units, 50,000 buildings and nodes, 200,000 projectiles, 20,000 zones, the command bound: loads). QA's
  `AiSaveQaTests.Serial.AForgedHeader_*` row is unchanged and green.
