# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-09-0724** (all three tracks accepted; integration sim db39a28 →
view 0dd8952 → data 62e52f7). The next session's PLAN replaces the "Current session plan" sections.

## Where we are

- M0-M3 Done. **M4: 6 / 10** once this integration is on `main`: criterion 5 (fog) complete (M4-V5 drew the ghosts). Left:
  6 abilities (slice 1 M4-4a landed; slice 2 = zones, summons, self / aura, autocast, `abilityCooldown`), 7 stealth (M4-5),
  8 the four signature abilities (Telas Fire works; Sharpers / Cusser, Sandstorm, Zealot passives need slice 2 + D10),
  10 the fog-on sandbox Playable.
- Hardening counters after this session: sim **2 / 4**, view **2 / 4**, data **0 / 4**.
- Open S1 / S2: **none.** Open S3: BUG-0311 (sim, ghost-attack order ends early; Requests 27), BUG-0310 (view, ghost flash),
  BUG-0270, BUG-0241, BUG-0251, BUG-0157, BUG-0144, BUG-0151. S4 of note: BUG-0302 b + c (sim), BUG-0281 (view), BUG-0275 1-2.
- The owner's balance answer (D6 entry under For your review) is still wanted; the data track changes no number until it comes.
- Bug ids next session: sim from **BUG-0330**, view from **BUG-0340**, data from **BUG-0350**.
- Weekly usage: 91 % of the 95 % stop at this ACCEPT; the stop resets 2026-10-10 02:00 local. **If the next PLAN runs before
  the reset, size every task to half the usual and tell the devs to ship what is green.** After the reset, normal sizes.
- Standing rules for briefs: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full
  suite incl. Perf once and the merged scene loop before it reports; (2) **a sim task that adds a `UnitState` ships the
  `ui.json` `states.*` line itself** (named in the brief as the one allowed view-owned edit; the 0724 ruling); (3) the data
  hash: whoever moves `data-hash` adds the new hash to `GatherWedgeQaTests.SameGameDataHashes` in the same commit if the
  seed-21 match is unaffected (BUG-0303's lesson), else re-records.

## Sim track

### Next task candidate: M4-4b · abilities slice 2 · feature · QA full

Zones (`createZone` effect: a timed area at the point with a radius and a status it applies **every tick** to units inside,
through `StatusSystem.Apply`, so the pulse clock keeps Burning honest; Darkness / Sandstorm also mark a per-player
vision-blocker mask the vision pass honours: docs/03 "Abilities, statuses, zones" `ZoneSystem`, docs/02 the Sandstorm /
Darkness rows), summons (`spawn` effect: units of a type for the caster's owner at free cells near the point, timed or not
per docs/02), the `selfAura` and `targetUnit` kinds, `autocast`, the `abilityCooldown` tech effect wired (`TechState` →
`CooldownTicks`), the **whole-seconds DoT loader rule** (Producer decision 0724: a `damageOverTime` `applyStatus` duration
must be whole seconds ≥ 1 s; docs/01 row). Keep the schema additive (the data track's D10 fills content after). Uncertain
system: size to the zone + vision-blocker part first if the budget is tight; summons / aura can be slice 3.
**Fold in BUG-0311 only if the vision rules are touched** (`UnitSeesFootprint` vs `fog.SeesFootprint`); else it is M4-5's
first item.

Then: M4-5 stealth / detection (BUG-0311 first; `detector` is stored on towers; docs/02 "Stealth and detection"), the
fog-on sandbox Playable (sim side: a CLI scenario with both factions and fog on).

### Watch-outs (sim)

- `World.Neighbors` / `FlowFields.BuildScratch` are shared scratch; `AbilitySystem.Resolve` uses `Neighbors` in phase 6; the
  fog's layers lie over `BuildScratch` in phase 12. A `ZoneSystem` in phase 5 may use `Neighbors` too (nobody walks then).
- `UnitState.Casting` is planted like `Attacking` in every `MovementSystem` wall test; a new state needs the same sweep.
- Golden `data-hash` is 7E04011FC88881F3; `GatherWedgeQaTests.SameGameDataHashes` must get every new hash (see rule 3).
- Thin Perf margins (pass alone): `EmptyTick(2500)` ~0.49 of 0.5 ms, `TightBlob2500` 4.52 of 4.6 ms; a slice that adds a
  per-tick scan over unit slots should gate on a count like `UnitsWithStatuses` / `CasterCount`.
- Next sim hardening (counter 2 / 4): BUG-0311 (if still open), BUG-0241, BUG-0157's kept-chase rule, BUG-0270, BUG-0302 b + c,
  BUG-0275 items 1-2, BUG-0271 / 0272 (decide or wontfix), BUG-0144, BUG-0142 items 1-2, BUG-0113 item 2, BUG-0094, BUG-0242.

## View track

### Next task candidate: M4-V6 · ability feedback · feature · QA standard

Requests 26 lists the surface (all on `World`, docs/03 "For the view (M4-V6)"). The command card's ability button for a
selected caster (hotkey from `ui.json` grid, the cooldown as a sweep / seconds, `Data.Abilities[id].DisplayName` /
`Description` in the tooltip), a targeting mode like A + click: the range ring around the caster and the radius circle at
the cursor (`Range` / `Radius`), left-click = `Command.UseAbility` from **the nearest selected caster only** (docs/02; Shift
queues), the "Casting" label (the key exists), a cast bar over the caster (`CastTicks` / `CastTicks` total from the def),
Burning / Slowed markers over units from `Units.Statuses` (fog-gated like bars), a flash at the point on `AbilityEvents`
resolve, F12 lines. A test scene (`AbilityViewTest.tscn`): the button casts, the circle sizes, 0 B at 500 units, twins
equal. **BUG-0310 if a few lines** (skip ghost entries with a visible footprint cell). `ui.json` keys for the new text
(view-owned). No sim file outside `ViewApi/` (read-only).

Then: zone visuals (after M4-4b), stealth visuals (M4-5), the fog-on sandbox Playable (the window side).

### Watch-outs (view)

- `Fog.Ghosts(player)` changes only on update ticks (`tick % 4 == 1`); ghosts are collected every refresh (BUG-0310's cause).
- BUG-0251: `EconomyViewTest` can FATAL at shutdown after PASS under load (rerun once).
- Next view hardening (counter 2 / 4): BUG-0310 (if still open), BUG-0251, BUG-0250 (3 items), BUG-0281 items 1-3, BUG-0148
  item 1, BUG-0126 items 3 / 5 / 6, export hygiene (M6).

## Data track

### Next task candidate: D9 · the towers' Attack / Detector columns · feature · QA light

An "Attack" (10 pierce / 2 s / 18 m) and "Detector" (16 m) column (or one "Defence" column) in the Buildings tables of
`docs/factions/malazan.md` and `whirlwind.md`, pinned both ways by `BuildingContentTests` (the K rows from D-H1 cover the
text; add the number pins), docs/02 "Buildings" row cross-checked. No number change. **An inbox answer on the balance
proposal comes first** (then its tweaks are the task). Files: `docs/factions/**`, `sim/Rts.Sim.Tests/Content/**`,
`QA/Content/**`, bug files, `studio/qa/**`.

Then **D10** (abilities content): an "Abilities" table on both pages pinned to `abilities.json` / `statuses.json`; review
Telas Fire's shipped text; the Whirlwind file `{ "abilities": [] }` only if the loader needs it; Sharpers / Cusser /
Sandstorm / Zealot entries as soon as M4-4b's kinds load (STOP on those until then). Then the full balance pass once the
fog-on sandbox gives numbers.

### Watch-outs (data)

- `game/data/common/**`, `*/units.json`, `*/abilities.json` are the sim's while M4-4b is in flight; D10 edits
  `abilities.json` only in a session where the sim's task doesn't (name the files in both briefs).
- Any text edit moves `data-hash`: golden regen + the `SameGameDataHashes` row in the same commit (rule 3 above).

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the 35-scene loop (36 with `QaGhostViewTest`) and smoke on each **merged**
  result; push only green states. Expected conflicts: `studio/bugs/README.md` (union by id) and `studio/qa/coverage.md`
  (append all sides); docs/03 one subsection per track; docs/01 rows appended.
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name; QA reruns `SerialCollectionTests`
  after committing its own files.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
