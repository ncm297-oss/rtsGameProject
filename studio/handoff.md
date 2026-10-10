# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-10-0215** (scheduled; base `e6b74ea` = `origin/main` after the
2026-10-09-1155 integration). The ACCEPT of this session replaces this file.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** Criterion 6 (abilities) has M4-4a + M4-4b-1 landed and castable from the window (M4-V6a);
  owed: zones / Blinded (**M4-4b-2, this session**), summons / self-aura / autocast / passives (M4-4b-3), status markers +
  the resolve flash on screen (**M4-V6b, this session**). Criterion 8: Telas Fire and the Cusser work; Sandstorm lands with
  M4-4b-2; the Zealot passives need M4-4b-3. Then 7 stealth (M4-5), 10 the fog-on sandbox Playable.
- `main` at `e6b74ea`: build 0 warnings; the Producer's non-Perf run is recorded in the session log. No open S1 / S2
  (index: S3 14, S4 13 open).
- Usage reset at 02:00: normal sizes (`max_task_lines` 1500 for clear designs; ~800-1,200 for uncertain ones like zones).
- Hardening counters before this session: **sim 3 / 4, view 3 / 4, data 1 / 4.** This is a feature session for all
  three; **the next session is the sim's and the view's hardening** (their debt backlogs in STATE are current).
- Golden `data-hash` is 41842085985611BF (in `GatherWedgeQaTests.SameGameDataHashes`). Sessions today: 1 / 8 (this one).
- Bug ids: sim from **BUG-0360**, view from **BUG-0370**, data from **BUG-0380**.
- Standing rules: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full suite incl.
  Perf once (alone) and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the `ui.json`
  `states.*` line itself; (3) whoever moves `data-hash` adds the new hash to `GatherWedgeQaTests.SameGameDataHashes` in
  the same commit when the seed-21 match is unaffected, else re-records; (4) when the view needs a `ui.json` key, the
  brief says so and the sim stays off the file that session.
- **File ownership this session (no two tracks on one file):** sim edits `game/data/common/statuses.json`,
  `game/data/factions/whirlwind/abilities.json` (new) and `whirlwind/units.json` (the Priest's `abilities` line only);
  view may edit `game/data/common/ui.json` (sim stays off it); data edits **no `game/data/**` file** (the sim moves the
  golden `data-hash` this session, so a data edit would collide on the golden file and `SameGameDataHashes`).

## Sim track

### Current session plan: M4-4b-2 · zones, Blinded, Sandstorm · feature · QA full · ~1,000 lines (uncertain design; up to 1,200 with tests)

**Goal.** Make zones real so Sandstorm, the Whirlwind signature, works from data: a `createZone` effect leaves a timed
circle at the cast point that applies statuses to the units inside every tick and, when flagged, hides its contents from
enemies outside it. Ship the Blinded status (docs/02 "Status effects": sight 2 m, no acquiring or attacking beyond 3 m).
This advances M4 criteria 6 and 8.

**Scope.**
- `common/statuses.json`: kind `blind` with data fields `sight` (m) and `reach` (m); shipped `blinded` (2 / 3,
  `displayName` "Blinded", a one-line `description`). No stat in C#.
- `abilities.json`: the `createZone` effect (`DataLimits.PlannedAbilityEffectKindIds` loses it): `blocksVision` (bool)
  and `statuses` (a list of `applyStatus`-shaped entries: `status`, `magnitude`, `duration`). The ability's `duration`
  (s, required when an effect is `createZone`) is the zone's lifetime; the zone's radius is the ability's `radius`,
  its centre the cast point, its owner the caster's owner. Validation: `duration` above 0 with a zone, at least one
  status, the DoT whole-seconds rule applies to zone statuses too, `blocksVision` only on `createZone`.
- `ZoneStore` (SoA, fixed capacity from `SimConfig`, hashed: owner, centre, ability id, ticks remaining) + `ZoneSystem`
  in phase 5 before the status pulses: every live zone counts down and applies its statuses (refreshing their
  `duration` each tick, so a unit keeps them for `duration` after it leaves) to every unit its ability's `affects`
  allows whose centre is within the radius (`SpatialHash.QueryRadius`, exact test). Freed on expiry. Early-out when
  `Count == 0` (the empty-tick budget is thin: `EmptyTick(2500)` 476 / 500 µs).
- **Vision blocker** (`blocksVision`): for every player other than the zone's owner, cells within the zone are not
  visible from outside it: after the normal stamp, such a cell is visible to that player only if one of that player's
  live units stands inside the zone and the cell is within that unit's current (Blinded) sight. The owner's side sees
  into the zone normally. Explored stays explored. Stamped on the vision cadence (`tick % 4 == 1` + the tick-0 stamp);
  the mask itself is derived (the visible bits it changes are already hashed).
- **Blinded:** a derived per-unit sight (like `Speed`, recomputed when the status set changes, not hashed) that the
  fog stamp uses (`FogStore` needs a mask for every `blind` status's `sight`, so add the statuses' sights to the distinct
  radii at construction) and a reach cap: a Blinded unit acquires nothing beyond `reach` and does not swing or fire at a
  target further than `reach` (walks closer if ordered; a ranged unit effectively fights at 3 m). Towers are never
  Blinded (buildings have no statuses).
- `whirlwind/abilities.json` (new): `sandstorm` (18 m / 6 m / 1.2 s cast / 45 s cooldown / 12 s duration,
  `affects: enemy_units`, `createZone` with `blocksVision: true`, statuses Blinded 1 s + Slowed 0.3 for 1 s), a
  `description` that says what the page says; the Priest's `abilities: ["sandstorm"]` in `whirlwind/units.json`.
  Dryjhna's Prophecy's existing `abilityCooldown` must make it 30 s (600 ticks).
- `World.Zones` read-only for the view (`Count`, per-slot `Alive`, `Owner`, `Center`, `Radius`, `TicksRemaining`,
  `AbilityId`, `BlocksVision`), documented in docs/03 "For the view (M4-V6c)".
- `ContentHash` covers the new fields; golden `data-hash` regenerated once (rule 3).
- Docs: docs/03 "Implementation (M4-4b-2)" + "Data format" + "For the data track (M4-4b-2)" (the exact JSON shape);
  docs/02 "Zones" one sentence if the rule needs it; docs/01 rows for the Producer decisions below.
- **OUT:** summons / `spawn`, `selfAura` / `targetUnit`, `autocast`, passives (M4-4b-3); stealth / detection and the
  Revealed status (M4-5); the AI's casting; any view file; `ui.json` (no new `UnitState`); `docs/factions/**`
  (the data track aligns the Sandstorm row's wording when it pins it next session); `malazan/abilities.json`.

**Producer decisions (producer_default; record in docs/01 as "Producer decision, owner may revisit"):**
(a) Sandstorm `affects` is `enemy_units`: the page's "Non-Whirlwind units" reads as "enemy" while v1 has no alliances.
(b) Zone statuses linger 1 s after a unit leaves (the `duration` on each zone status; data, not code).
(c) The vision rule above (enemies outside see nothing inside; an enemy inside sees its Blinded 2 m; the owner sees
normally). (d) Blinded's 2 m / 3 m live in `statuses.json`, not C#.

**Acceptance criteria.**
1. Loader: `createZone` loads with the shape above and refuses: a zone ability without `duration`, `blocksVision` on a
   non-zone effect, an empty `statuses` list, a `blind` status without `sight` / `reach`, a fractional-second DoT inside
   a zone (`AbilityLoaderTests`, `StatusLoaderTests`; each error at its field path).
2. Zone life: a Sandstorm cast at tick T resolves and a zone exists for exactly 12 s (240 ticks) then is freed; an
   enemy unit inside has Blinded and Slowed 0.3 every tick while inside and loses both 1 s after stepping out; a
   Whirlwind (owner's) unit inside is untouched; a zone-store-full cast is handled by a documented rule
   (`Abilities/ZoneSystemTests`).
3. Blinded: a Blinded Desert Archer (range 7) does not acquire an enemy at 5 m and does not fire at an ordered target
   until within 3 m; it acquires one at 2.5 m; its fog circle is 2 m (cells 3 m away are not stamped) and returns to its
   type's sight when the status ends (`Combat/BlindedTests` or in `AbilitySystemTests`).
4. Vision blocker: a Malazan Crossbowman 10 m from a Sandstorm's centre (sight 16) has `Fog.CanSeeUnit` false for a
   Whirlwind Raider standing inside it and cannot acquire it; a Malazan unit standing inside sees a Raider 1.5 m from it
   and not one 4 m away; the Whirlwind player sees the whole zone; the zone's cells are explored for the Malazan player
   afterwards; no blocker effect once the zone expires (`Vision/ZoneVisionTests`).
5. Determinism + hash: `StateHashTests` cover every zone field (a zone with one tick left vs. none differ); the fuzz
   (`Stress/AbilityFuzzStressTests` extended: Priests casting Sandstorm, 3 players, 3 seeds x 2,000 ticks, twin worlds
   equal every tick, replay round trip); golden `data-hash` moved once with every `k` line equal (or re-recorded with
   the reason) and the new hash in `SameGameDataHashes`.
6. Perf alone: `EmptyTick(2500)` stays under 500 µs, `TightBlob2500` under 4.6 ms, a zone row (500 units, 8 live zones,
   two blockers) in `AbilityPerfTests` is 0 B per tick; full suite incl. Perf green alone; smoke PASS; the merged scene
   loop green; docs/03 / docs/02 / docs/01 as listed.

**Design references.** docs/02 "Ability system" (the effect vocabulary), "Status effects" (Blinded: sight 2 m, 3 m),
"Zones", "Vision and fog of war" (vision modifiers); docs/factions/whirlwind.md "Abilities" (18 / 6 / 1.2 / 45 / 12;
Blinded + Slowed 30 %; "Enemies outside can't see into the storm") and the Dryjhna's Prophecy row (45 → 30 s); docs/03
"Abilities, statuses, zones", "Vision, detection, fog" (the stamp, the masks, the cadence, what is hashed), tick phases
5 / 6 / 12.

**Tests required.** `Data/AbilityLoaderTests` + `Data/StatusLoaderTests` rows, `Abilities/ZoneSystemTests`, the Blinded
tests, `Vision/ZoneVisionTests`, `StateHashTests` zone rows, `Stress/AbilityFuzzStressTests` extended, `AbilityPerfTests`
zone row, `DataContentHashTests` for the new fields, golden regen.

**Constraints.** No Godot in `Rts.Sim`; every number in data (`statuses.json` for 2 m / 3 m, `abilities.json` for
Sandstorm); no allocation per tick (`QueryRadius` into scratch; the blocker pass over the zone's bounding box only, no
full-map pass per zone); hash order fixed (slot order, no dictionaries); `World.Neighbors` / `FlowFields.BuildScratch`
shared scratch is in use by the vision pass; the derived sight must never be read for the hash.

**QA focus.** The blocker vs. the high-ground rule and the 4 m lip (a Sandstorm on a cliff edge; a blinded unit on a
ramp); a unit on the zone's exact radius (6.0 m in, 6.01 m out) for statuses and for the mask; two overlapping
Sandstorms of two different Whirlwind players over a Malazan unit; a zone expiring on an update tick vs. between
updates; a Blinded caster (can it cast at 16 m? the cast range is not a target acquisition: it should); Blinded and
a tower's target; twin-world equality with casts on the vision cadence; the empty-tick cost with `ZoneStore.Count == 0`
(0 B, under 5 µs added); store-full behaviour; the replay round trip with 50+ Sandstorms.

After: M4-4b-3 (summons, aura, autocast, passives), then the sim hardening (BUG-0330, BUG-0311, BUG-0241, BUG-0157
kept-chase, BUG-0270, BUG-0302 b + c, BUG-0275 1-2, BUG-0271 / 0272, BUG-0144, BUG-0142 1-2, BUG-0113 item 2, BUG-0094,
BUG-0242, the docs/02 "allied" wording), M4-5 stealth (BUG-0311 first).

## View track

### Current session plan: M4-V6b · status markers, the resolve flash, the ability-UI nits · feature · QA standard · up to ~1,000 lines

**Goal.** Finish the view half of M4 criterion 6 for what the sim has on `main`: a player can see which units are
Burning or Slowed, and where a spell landed. Close the three small view bugs the last session left (BUG-0342,
BUG-0310, BUG-0340) and verify the Cusser's button appears on the Sapper without any new view code.

**Scope.**
- Status markers: a small marker over each unit per active status (Burning: a flame-coloured mark; Slowed: a
  blue-grey mark; colours as constants in `AbilityViews.cs` or a new `StatusViews.cs`), read from `Units.Statuses`
  (`Count[slot]`, entries at `slot * StatusStore.PerUnit + k`, `StatusId` → `Data.Statuses[id]`), fog-gated like hp
  bars (`Fog.CanSeeUnit`), MultiMesh, 0 B per frame at 500 units; a hover tooltip or the selection panel line may show
  the status `displayName` from data (no literals). A `ViewApi` helper (`StatusMarkers`, read-only, spans in) is fine.
- The resolve flash: from `World.AbilityEvents` (`Resolved == true`): a brief ground burst of the ability's radius at
  `Point` (like `ImpactMarks`), fog-gated by the point's cell; the cast start may show a small ring (optional).
- The Cusser: in `AbilityViewTest.tscn` (or a new row) select a Sapper and assert the card shows "Cusser" on the first
  free hotkey from `Data.Units[type].Abilities` order; a screenshot looked at.
- BUG-0342: the cast bar readable from the first tick (draw the fill over a tinted back, taller, or a contrasting
  outline); Shift + click stays armed in targeting so several casts can be queued (right-click / Esc ends it).
- BUG-0310: `FogView.CollectGhosts` (or `BuildingViews`) skips a ghost whose footprint has a visible cell; the `TargetRing`
  clears with it; un-skip the QA row `ABuildingDestroyedInSight_IsNeverAGhostOverVisibleGround`.
- BUG-0340: cached "+N" strings in `SelectionPanel`; `Minimap.cs:198`'s `IsActionPressed("order_queue")` through the
  cached `StringName`; put the panel back into `AbilityViewTest`'s 0 B span.
- Test scene rows (`AbilityViewTest.tscn` extended or `StatusViewTest.tscn`), xUnit tests for any `ViewApi` helper, the
  scene loop (43 scenes on disk now; all green). docs/03 "Implementation (M4-V6b)".
- `game/data/common/ui.json`: allowed for the view this session (a tooltip key if needed; the sim stays off it).
- **OUT:** zone visuals (M4-4b-2 lands this session; zone discs and the storm's look are M4-V6c next feature session),
  stealth visuals, autocast toggles, the Blinded marker (its status lands this session; add it with the zone visuals
  if the data is on `main` by then, else it draws with the generic path).

**Acceptance criteria.**
1. In the test scene, enemies hit by Telas Fire carry a Burning marker for exactly as long as `TicksRemaining` says
   and lose it on expiry; a Slowed unit shows the Slowed marker; a unit with both shows both side by side; markers hide
   under the fog with the unit (`FogViewTest`-style row).
2. A resolve flash appears once per resolved cast at the cast point with the ability's radius and fades within ~0.5 s;
   none for a cast the fog hides; none for a cast start (unless the optional ring is implemented and documented).
3. A Sapper's card shows "Cusser" with its hotkey, read from data; pressing it enters targeting with a 6 m ring and
   a 3.5 m circle (screenshot).
4. BUG-0342 fixed: the bar reads as a bar in the first tick's screenshot; Shift + click queues two casts in a row with
   one Q (scene row counts two `UseAbility` commands, `queued` true on the second).
5. BUG-0310 fixed: the QA row un-skipped and green; BUG-0340 fixed: `SelectionPanel` + minimap right-click 0 B rows.
6. Scene loop all green; 0 B per frame at 500 units with 100 statuses live; build 0 warnings; smoke PASS; no
   player-facing literal in C#; docs/03 updated.

**Design references.** docs/02 "Status effects" (names), "Ability system" (casting UX); docs/03 "For the view (M4-V6)",
"Implementation (M4-V6a)" (what exists), "Dumb views" rule (CLAUDE.md 7).

**Tests required.** `ViewApi/StatusMarkersTests` (if a helper is added), the scene rows above, QA's own scene, the
un-skipped `GhostQaTests` row.

**Constraints.** View holds no gameplay state (markers are rebuilt from the snapshot each frame or on `Ticked`); no
allocation in `_Process` (cached `StringName`s, pre-sized MultiMesh); fog gating uses the sim's `Fog` only; no
`ui.json` fallback literals.

**QA focus.** 500 units with 8 statuses each (the store's cap) for the marker pool; a unit dying mid-status (marker gone
with the corpse); statuses on a unit under the fog edge across an update tick; 200 resolves in one tick (a flash pool
cap, no leak); Shift-queued casts with a caster dying mid-queue; the 0 B rows for `_Process`, `Sync`, panel, minimap.

After: the view hardening (BUG-0251, BUG-0250, BUG-0281 1-3, BUG-0148 item 1, BUG-0126 3 / 5 / 6, export hygiene), then
M4-V6c zone visuals (the Sandstorm disc + Blinded marker), stealth visuals (M4-5), the fog-on sandbox Playable.

## Data track

### Current session plan: D10b · the Cusser row pinned fully, text reviews · feature · QA light · ~300 lines

**Goal.** Retire the Cusser's pin allowance so its numbers are checked by name like Telas Fire's (BUG-0350), fix the two
D10a gaps (BUG-0351), and review the player-facing text of the shipped abilities and statuses against the pages,
proposing wording in the report. No inbox answer on the balance proposal is pending, so this is the roadmap item.

**Scope.**
- BUG-0350: `Content/AbilityContentTests` learns the `damage` row: "<amount> <type> damage" ↔ `Amount` / `DamageType`;
  "full damage to buildings" (or the page's wording) ↔ `Buildings`; "friendly fire at N%" / "own units take half" ↔
  `FriendlyFire`; the "No effect on buildings" check applies only when `Buildings` is false; "Enemy units"-first wording
  generalised. Drop `Cusser` from `PendingAbilities`; the Malazan page's Cusser row may be re-worded to the parsable
  form (`docs/factions/malazan.md` is the data track's). A mutant per new field fails by name.
- BUG-0351: the Duration message reads "page '—', data 6 s"; the For your review table goes in the report this time.
- Text review (report only, no data edit): the Cusser's and Telas Fire's `description`, Burning's and Slowed's
  `description` against docs/02 and the pages; propose wording per entry with a reason; the Producer plans any change
  as a request to the sim (common / malazan files) next session.
- **Keep `Sandstorm` in `PendingAbilities` and `Blinded` in `PendingStatuses`:** the sim lands both this session in
  parallel; a landed entry must be reported, not failed, so the integration stays green. Pin them next session (D10c).
- Files: `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, `docs/factions/malazan.md` (text), bug
  files. **No `game/data/**`, no `docs/02`, no `whirlwind.md`.**

**Acceptance criteria.**
1. With the real Cusser on `main`, `AbilityContentTests` passes with `Cusser` out of `PendingAbilities`; a mutant on each
   of `amount`, `type`, `buildings`, `friendlyFire` fails naming 'Cusser' and the field; a page mutant on each fails.
2. Telas Fire's row still pins "No effect on buildings"; a Telas Fire with `buildings: true` would fail by name.
3. Sandstorm / Blinded remain allowances: a scratch run with a `sandstorm` entry + a `blinded` status present reports and
   passes (QA checks on a scratch copy of the sim branch if available, else with a hand-made entry).
4. BUG-0351's message reads as a value mismatch; the report carries the For your review table (what changed, quoted text).
5. Green, 0 warnings, no `game/data/**` diff, no `data-hash` move.

**Design references.** docs/factions/malazan.md "Abilities" (the Cusser row: 6 / 3.5 / 1.0 / 45, 120 siege, buildings,
own units half); docs/02 "Ability system" (the `damage` sentence), "Status effects"; docs/03 "Implementation (M4-4b-1)"
("For the data track (M4-4b-1)": `buildings`, `friendlyFire`).

**Tests required.** `Content/AbilityContentTests` rows per field; QA's `Content` mutants.

**Constraints.** No C# outside the two test folders; player-facing text proposals in the report, not in data this session.

**QA focus.** Mutants per new field (both directions); a page row re-ordered; the allowance path with a landed Sandstorm;
"friendly fire at 50%" vs "own units take half" wording robustness.

After: D10c (Sandstorm's row + Blinded pinned, the `whirlwind/abilities.json` and `blinded` text review, the Sandstorm
page wording aligned with `affects: enemy_units`), then the text tweaks the owner asks for, the full balance pass (QA
standard) once the fog-on sandbox gives numbers, `ai.json` build orders (M5), M7-M9 faction data.

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the scene loop and smoke on each merged result; push only green states.
  Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md` (append all sides); docs/03 one
  subsection per track; docs/01 rows appended.
- **Golden:** only the sim moves `data-hash` this session. The data track's `AbilityContentTests` must stay green with
  Sandstorm + Blinded landed (allowances kept).
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
