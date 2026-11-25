# BUG-1661: a hunt that finds and razes a building forgets its sweep; the next hunt starts over at the free starts

| Field | Value |
| --- | --- |
| Severity | S3 (an AI match that is decided runs on for tens of thousands of ticks; BUG-1610's length) |
| Status | open (traced in M7-8; the cure waits for the AI-play session, docs/01 2026-11-24 (2)) |
| Found | 2026-11-24-1431, task M7-8 (the developer's trace of BUG-1610 / BUG-1621; QA re-ran the trace and filed it) |
| System | AI end game (`MilitaryManager.BeginHunt` / `NextSweep` / `NextHuntStart`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~VatharStallTraceTests"`; read the timeline from 30,000.
2. Seat 3's hunt 5 starts at 38,061 toward (128, 128) m, then (384, 384) m; it finds a depot at (40, 138) m at 66,921;
   hunt 6 starts from scratch and reaches sweep point 31 (7 m from seat 2's Keep at (70, 182) m, 40 m from the razed
   depot) only at 88,441 (QA's re-run: `88441 seat 3: ... sweep 31 ... target (77,179)`; the Keep falls at 88,998).

## Expected
A remnant next to what the hunt just found is looked at next (docs/02 "AI opponent": the hunt finishes the match).

## Actual
`BeginHunt` clears the visited marks and, from the second hunt on, `NextSweep` sends the army to the free starts first,
whatever the last hunt found; each find near the remnant costs a whole lap of the map (21,000-29,000 ticks here).

## Notes
- Cure (recommended, not built): when a hunt turned into a wave on a found building and that wave razed it, the next hunt
  keeps the marks and resumes nearest-first from the building; the free starts first only for a hunt with no find within
  `ai.huntMemory`. Small (`MilitaryManager`), no format change if the resume is derived from the wave's last target.
  Moves every AI match with two or more hunts (docs/03 "Implementation (M7-8)" lists the pin classes).
