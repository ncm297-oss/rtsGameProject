# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-09-1155** (all three tracks accepted; integration sim 1b63697 →
view 3511873 → data 9c21324). The next PLAN replaces this file.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** Criterion 6 (abilities) has slices M4-4a + M4-4b-1 landed and castable from the window
  (M4-V6a); still owed: zones / Blinded (M4-4b-2), summons / self-aura / autocast / passives (M4-4b-3), status markers +
  the resolve flash on screen (M4-V6b). Criterion 8: Telas Fire and the Cusser work; Sandstorm needs zones, the Zealot
  passives need passives. Then 7 stealth (M4-5), 10 the fog-on sandbox Playable.
- `main` after this integration: non-Perf ~4,290 / 11 skipped / 0 failed expected (each branch green alone), scene loop
  37 / 37, smoke PASS. No open S1 / S2 (S3 18, S4 18 by the index).
- **Usage: 94 % of the 95 % weekly stop at this ACCEPT; the studio pauses itself until 2026-10-10 02:00 local.** The next
  session runs at normal sizes (`max_task_lines` 1500 for clear designs; ~800 for uncertain ones like zones).
- Hardening counters: **sim 3 / 4, view 3 / 4, data 1 / 4.** The next session is a feature session for all three; the one
  after is the sim's and the view's hardening (their debt backlogs in STATE are current).
- Golden `data-hash` is 41842085985611BF (in `SameGameDataHashes`). Sessions today: 3 / 8 (the next calendar day resets).
- Bug ids: sim from **BUG-0360**, view from **BUG-0370**, data from **BUG-0380**.
- Standing rules: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full suite incl.
  Perf once (alone) and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the `ui.json`
  `states.*` line itself; (3) whoever moves `data-hash` adds the new hash to `GatherWedgeQaTests.SameGameDataHashes` in
  the same commit when the seed-21 match is unaffected, else re-records; (4) when the view needs a `ui.json` key, the
  brief says so and the sim stays off the file that session.

## Sim track

### Next task candidate: M4-4b-2 · zones, Blinded, Sandstorm · feature · QA full · uncertain design (~800 lines, first slice)

Zones: `createZone` effect (docs/02 "Ability system", docs/03 "Abilities, statuses, zones"): a timed circular area at the
cast point (`duration` from the def) that applies its `applyStatus` to units inside every tick (phase 5, before statuses
pulse) and, when flagged (`blocksVision`, Darkness / Sandstorm), marks a per-player vision-blocker mask the vision pass
honours (units inside see only 2 m? — check docs/02's Sandstorm / Darkness text; the Blinded status carries the sight
rule, so a zone may simply apply Blinded). `ZoneStore` SoA, hashed, freed on expiry; `World.Zones` read-only for the
view. Blinded (`statuses.json` kind `blind`): sight 2 m, acquire / attack ≤ 3 m (docs/02 "Status effects"). Sandstorm
shipped from `whirlwind/abilities.json` (18 / 6 m / 1.2 s / 45 s / 12 s; `affects` non-Whirlwind units per the page) with
the Whirlwind caster's `abilities`. `ContentHash` + golden regen once. **OUT:** summons, self / aura, autocast, passives
(M4-4b-3), stealth (M4-5), the AI's casting, any view file, `ui.json` (no new `UnitState` expected), `docs/factions/**`.

Watch-outs: `World.Neighbors` / `FlowFields.BuildScratch` shared scratch; the vision pass runs every 4 ticks + tick 0,
so a blocker mask must be stamped on the same cadence (hash it only if it is state the view can't derive); Perf margins
`EmptyTick(2500)` 476 / 500 µs and `TightBlob2500` 4.48 / 4.6 ms (a zone scan per tick must be 0 B and cheap when no zone
exists: early-out on `ZoneStore.Count == 0`). BUG-0330 (death list) is a hardening item unless a zone test trips it.

After: M4-4b-3 (summons, aura, autocast, passives), the sim hardening (BUG-0330, BUG-0311, BUG-0241, BUG-0157 kept-chase,
BUG-0270, BUG-0302 b + c, BUG-0275 1-2, BUG-0271 / 0272, BUG-0144, BUG-0142 1-2, BUG-0113 item 2, BUG-0094, BUG-0242,
the docs/02 "allied" wording), M4-5 stealth (BUG-0311 first).

## View track

### Next task candidate: M4-V6b · status markers, the resolve flash, the view nits · feature · QA standard

Burning / Slowed markers over units from `Units.Statuses` (`Count[slot]`, entries at `slot * StatusStore.PerUnit + k`,
`StatusId` → `Data.Statuses[id]`), fog-gated like hp bars, MultiMesh, 0 B per frame; the cast / resolve flash from
`World.AbilityEvents` (like `Impacts` marks); verify the Cusser's button appears on the Sapper in a test scene (data-driven
row order: `Data.Units[type].Abilities`); BUG-0342 (cast bar: draw the fill over the back, or tint the back; Shift + click
keeps targeting armed), BUG-0310 (skip a ghost whose footprint has a visible cell), BUG-0340 + `Minimap.cs:198` string
lookup. Test scene rows for each; `AbilityViewTest` extended or a new `StatusViewTest.tscn`; the scene loop (37 + new).
**OUT:** zone visuals (after M4-4b-2 lands; plan them the session after), stealth visuals, autocast toggles.

Watch-outs: `Fog.Ghosts(player)` changes only on update ticks (`tick % 4 == 1`); BUG-0251 (`EconomyViewTest` FATAL at
shutdown under load, rerun once); the F12 overlay's tick graph moved to y 158 this session (layout tests pin positions).

After: the view hardening (BUG-0251, BUG-0250, BUG-0281 1-3, BUG-0148 item 1, BUG-0126 3 / 5 / 6, export hygiene), zone
visuals, stealth visuals (M4-5), the fog-on sandbox Playable.

## Data track

### Next task candidate: D10b · the Cusser row pinned fully, the statuses' text review · feature · QA light

Inbox requests first (the balance answer, any text tweak). Then BUG-0350: teach `AbilityContentTests` the `damage` row
("<amount> <type> damage in the area" ↔ `Amount` / `DamageType`; "full damage to buildings" ↔ `Buildings`; "friendly fire
at N%" ↔ `FriendlyFire`; "≈355 to a Town Hall" ↔ `DamageCalc` siege x structure 3.0 - armor 5 on the loaded Town Hall) and
drop `Cusser` from `PendingAbilities`; BUG-0351 (the message; the For your review table in the report, this time); review
the Cusser's and the statuses' `description` text against the pages and docs/02 and propose wording in the report (a
data-only edit is allowed only if the sim's brief keeps it off `malazan/abilities.json` and `common/statuses.json` that
session; the Producer names the files in both briefs). Sandstorm's row pins after M4-4b-2 (the session after).
Files: `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`, `docs/factions/**` (text only), bug files. **No `game/data/**`
unless the brief names the file.**

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the scene loop and smoke on each merged result; push only green states.
  Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md` (append all sides); docs/03 one
  subsection per track; docs/01 rows appended.
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
