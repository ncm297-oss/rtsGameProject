# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-10-1015** (scheduled; third full session of 2026-10-10, cap 8).
Base `5582a9e` = `origin/main` after the 0624 integration (sim M4-H2 → view M4-VH2 incl. the BUG-0390 fix round → data D10c).
All three tracks run: sim **M4-4b-3a**, view **M4-V6c**, data **D-H2**.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** Criterion 6 (abilities, statuses, zones) owes the Frenzied status, unit passives, self / aura
  (this session's M4-4b-3a), then summons / autocast / target-unit (M4-4b-3b) and the zone discs + Blinded marker on
  screen (this session's M4-V6c); criterion 8 owes the Zealot passives (M4-4b-3a). Then 7 stealth (M4-5), 10 the fog-on
  sandbox Playable, the end-of-M4 hardening, sign-off.
- `main` at PLAN: build 0 warnings; non-Perf suite run by the Producer (see the session log); **no open S1 / S2**; S3 12 /
  S4 20 open (BUG-0401 is the new S3: a player's Attack on a reachable building dropped after 2 s when the path first
  leads away; sim, end-of-M4 hardening, Producer direction in the bug file).
- Counters at PLAN: sim 0 / 4, view 0 / 4, data 3 / 4. After this session: sim 1 / 4, view 1 / 4, data **0 / 4** (D-H2 is
  the data hardening, taken one session early because its feature work is blocked by the sim's files this session).
- Golden `data-hash` is **EA5CB5A0AFCEEDBE** (D10c). **This session only the sim moves it** (`statuses.json` `frenzied` + the
  Zealot's `passives`): regen once, every `k` line equal, add the new hash to `GatherWedgeQaTests.SameGameDataHashes` (rule 3;
  no Zealot and no Frenzied in the seed-21 match). View and data touch no `game/data/**` file.
- Bug ids: sim from **BUG-0420**, view from **BUG-0430**, data from **BUG-0440**.
- Standing rules: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full suite incl. Perf
  once (alone) and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the `ui.json`
  `states.*` line itself (none expected here); (3) whoever moves `data-hash` adds the new hash to `SameGameDataHashes` in the
  same commit when the seed-21 match is unaffected, else re-records (BUG-0275 item 1, `BuildingGhost.Site`, still waits for a
  re-record); (4) when the view needs a `ui.json` key, the brief says so and the sim stays off the file (none this session);
  (5) when a sim brief changes a behaviour a view QA scene asserts on, the same session's view brief carries a named item for
  that scene (checked at PLAN: no view scene spawns a Zealot or reads `StatusKind` beyond a default arm; `statuses.json`
  entries are **appended**, so the Burning / Slowed / Blinded ids keep their values).
- **Memory bound margin is 18.9 KB** (231,843,112 of 231,862,000, BUG-0302 b): M4-4b-3a's per-type / per-unit passive state
  re-baselines it with every byte itemised (M4-H2's +35,016 are; M4-4b-2's +18,536 are not yet; both go in the note).
- `EmptyTick(2500)` has ~15 µs of headroom (483-485 of 500 µs paired medians): passives must add no per-tick work for units
  without one (a per-type flag and a count of live units whose type has a passive; the pass skips when the count is 0).

## Sim track

### Current session plan: M4-4b-3a · Frenzied, unit passives (the Zealot), the `selfAura` kind · feature · QA full · ~800 lines

**Goal.** Give the ability system the pieces criterion 6 still lacks on the status side and make the Zealot's two passives
(criterion 8) work from data: a `frenzied` status kind that shortens the attack cooldown, a small fixed vocabulary of unit
passives (`whenBelowHp`, `onDeath`) with data hooks rather than code per unit, and the `selfAura` ability kind (effects
resolve around the caster, no target). A new system with real uncertainty, so this is slice a; slice b (summons `spawn` +
temporary units, `targetUnit`, `autocast`) is the next sim task.

**Scope.**
1. `common/statuses.json` kind **`frenzied`** (append it to `DataLimits.StatusKindIds` / `StatusKind` after `blind`; append the
   entry after `blinded` so the existing ids keep their values). Its applied `magnitude` is the attack-speed increase as a
   fraction (0.5 = +50 %): the attack cooldown becomes `max(1, round(cooldownTicks / (1 + magnitude)))` while in force; the
   wind-up is unchanged (Producer default: "attack speed" is the swing rate). Of several Frenzied entries the **largest
   magnitude** counts (docs/02 stacking rule; like `slow`). Loader: `magnitude` required and above 0 where applied; a
   `frenzied` status refuses `sight` / `reach` / `damageType`. Shipped entry: `frenzied`, displayName "Frenzied", a short
   description with no number in it (the data track pins and rewords in D11). The "damage up" half of docs/02's "and/or"
   is **not** in this slice (no shipped user before M7's Blood-oil Frenzy); say so in docs/03.
2. **Unit passives** (`UnitDef.Passives`, at most `DataLimits.MaxUnitPassives` = 2, each a `{ "trigger", ... }` object in
   `units.json`):
   - `whenBelowHp` `{ "hpFraction": 0.5, "status": "frenzied", "magnitude": 0.5 }`: while the unit's hp is **below**
     `hpFraction` x its max hp (max hp incl. tech bonuses: Dryjhna's Prophecy's +20), the status is on it with that
     magnitude (the sim may re-apply each tick with a 1-tick duration: `StatusStore.Apply` never shortens an existing
     entry, so a longer Martyrdom Frenzied is kept). At exactly the fraction (hp 32 of 63 is 50.8 %, hp 31 is 49.2 %;
     the boundary is `hp * 1 < fraction * maxHp` in whatever integer form the dev picks, documented) it is **not** on.
   - `onDeath` `{ "status": "frenzied", "magnitude": 0.5, "duration": 5, "radius": 6, "units": ["whirlwind_zealot"] }`:
     when the unit dies (phase 11, in death order), every **live own** unit whose type is in `units` and whose centre is
     within `radius` of the dying unit's position (the edge counts, like zones; the dying unit itself excluded) gets the
     status for `duration` (same stacking rule). Units dying in the same tick but processed later are still live when an
     earlier death resolves: that is the rule (document it).
   - Loader: trigger one of `DataLimits.PassiveTriggerIds` (`whenBelowHp`, `onDeath`); each field required for its trigger
     and refused on the other (`duration` / `radius` / `units` on `whenBelowHp`; `hpFraction` on `onDeath`); `hpFraction`
     in (0, 1); `radius` above 0, at most 64; `units` non-empty, own-faction ids, no repeats; `status` an id in
     `statuses.json`; `magnitude` by the status kind's rules. Errors name the path (`units[k].passives[j].field`).
   - The Whirlwind Zealot gets both passives in `factions/whirlwind/units.json` (Frenzy of the Apocalypse: below 50 %
     Frenzied +50 %; Martyrdom: Zealots within 6 m, Frenzied +50 % for 5 s; docs/factions/whirlwind.md lines 55-57; the
     magnitude 0.5 for Martyrdom is a Producer default, the page gives none). No other unit changes.
   - **Cost rule:** a per-type "has a `whenBelowHp` passive" flag and a world count of live units of such types; the
     per-tick pass runs only when the count is above 0 and visits only those units (the dev may keep a compact slot list
     or scan with the flag; either way 0 B and no work in the no-passive case). `onDeath` work happens only in the death
     path. Per-unit arrays added for this (if any) are itemised in the memory re-baseline.
3. **`selfAura` ability kind** (`DataLimits.PlannedAbilityKindIds` loses it): no target; `Command.UseAbility`'s point is
   ignored (the view keeps sending one); the cast runs as for `targetGround` (walk: none; cast timer; `UnitState.Casting`;
   cancelled by any order) and the effects resolve around the **caster's position at the resolve** with the ability's
   `radius`; `range` is refused on a `selfAura` ability (or must be 0: pick one, document it). Every existing effect works
   in it (`damage`, `applyStatus`, `createZone`). **Test-only ability** in the test fixtures (`TestSim` / in-memory data), not
   in any shipped `abilities.json` (a shipped ability needs a faction-page row for the data track's pins).
4. **Memory re-baseline**, itemised byte by byte (BUG-0302 b: the note covers M4-4b-2's +18,536 too), in the task's docs/03
   section and the test's comment.
5. **Golden:** `data-hash` moves once (the `frenzied` entry + the Zealot's `passives`); every `k` line equal; the new hash in
   `SameGameDataHashes` in the same commit (rule 3).
6. Docs: docs/03 "Implementation (M4-4b-3a)" (the kind, the passives' phase and order, the boundary rule, the `selfAura`
   resolve point, costs, memory, tests), "For the data track (M4-4b-3a)" (the exact JSON shapes, what is refused), "For the
   view (M4-V6d)" (`StatusKind.Frenzied` for a marker colour; `AbilityDef.Kind == SelfAura` means the card needs no target
   click); docs/01 change-log row "Producer decisions, owner may revisit" (wind-up unchanged; damage half deferred; Martyrdom
   magnitude 0.5; the boundary rule); docs/02 "Status effects" Frenzied row left as is (the data track pins it in D11).

**OUT of scope:** `spawn` / temporary units, `targetUnit`, `autocast` (slice b); stealth (M4-5); any `docs/factions/**` edit;
any `abilities.json` entry; the Frenzied marker on screen (view, next session); BUG-0401 (end-of-M4 hardening).

**Acceptance criteria.**
1. Loader tests: `frenzied` loads with its rules (magnitude rules at `applyStatus`; refused fields named at their paths);
   `passives` load for both triggers; every refused shape above fails with the path in the message; `selfAura` loads and a
   `range` on it (or a non-zero one) is an error; the "not supported yet" message is gone for `selfAura`.
2. `FrenziedTests`: a Frenzied +50 % unit with a 1.0 s (20-tick) cooldown swings every 13 ticks (`round(20 / 1.5)`); the
   wind-up is unchanged; +50 % and +20 % at once → 13 ticks; expiry restores 20 the next swing; the hash covers the entry.
3. `PassiveTests`: a Zealot at 31 / 63 hp is Frenzied and at 32 / 63 is not (and the same test with Dryjhna's Prophecy
   researched: 41 / 83 Frenzied, 42 / 83 not); a dying Zealot gives Frenzied 5 s (100 ticks) to own Zealots at 6.0 m and
   not at 6.01 m, not to own Raiders, not to enemy Zealots, not to itself; a Zealot already Frenzied by its own passive keeps
   it after Martyrdom's 100 ticks pass (still below 50 %); two Zealots dying the same tick each affect the other's
   neighbours by the documented order; a unit with no passive goes through phase 5 untouched (a unit-count-only scene
   measures the same tick time as the base within noise, and 0 B).
4. `SelfAuraTests`: a test-only `selfAura` ability with `applyStatus` on `own_units` within 4 m: the caster and own units at
   4.0 m take it, one at 4.01 m and every enemy do not; the point in the command is ignored (two casts with different points
   resolve identically); `createZone` from a `selfAura` makes the zone at the caster; the cooldown starts at the resolve.
5. `StateHashTests` rows for every new hashed field (`Passives` are data: not hashed; any new per-unit state that is state
   is hashed; derived values are classified as derived with a reason).
6. `Stress/PassiveFuzzStressTests`: 3 seeds x 2,000 ticks, 3 players, Zealots in every army, twins equal every tick, and a
   replay round trip (`ReplayRecorder` → `ReplayPlayer`) of one of them; `AbilityInvariantFuzzStressTests` gains a `selfAura`
   caster.
7. Perf alone (not under the other tracks' load): `EmptyTick(2500)` < 500 µs; a 500-unit brawl with 100 Zealots 0 B and
   within the existing brawl rows' budgets; `FieldBuildFairnessQaTests.World_1024Map_CacheStays32_MemoryBounded` passes at the
   new bound with the itemisation in its comment.
8. Golden: `cross_map_seed1.replay` `data-hash` changed and every `k` line equal (say so in the commit); `SameGameDataHashes`
   carries the new hash; `ReplayGoldenTests` and `Seed21PlayableReplay_*` green.
9. Build 0 warnings; `dotnet test` (full, once, alone) green; smoke PASS; the merged scene loop run on the branch (39 / 39
   expected: no scene uses a Zealot).
10. Docs as in Scope 6; the data track's `Content` tests stay green on the branch (`Frenzied` is in `PendingStatuses`, so the
    landed status reports and does not fail).

**Design references:** docs/02 "Ability system" (kinds table: Self / aura; `autocast` is slice b), "Status effects" (Frenzied
"Attack speed and/or damage up"; the stacking rule), "Zones" (the edge-counts rule reused for the aura); docs/factions/
whirlwind.md lines 55-57 (50 %, +50 % attack speed, 6 m, 5 s) and line 31 / 91 (Dryjhna's Prophecy +20 HP); docs/03
"Implementation (M4-4a / 4b-1 / 4b-2)" (phases 5 / 6 / 11, `StatusStore.Apply`'s never-shorten rule, `DataLimits.Planned*`),
"Vision, detection, fog" memory paragraph (the bound's history); CLAUDE.md rules 4-6 and 8.

**Tests required:** the loader tests, `FrenziedTests`, `PassiveTests`, `SelfAuraTests`, `StateHashTests` rows, the passive
fuzz + replay round trip, the Perf rows, the golden regen, `DataContentHashTests` rows for the new fields (every new field
in the content hash).

**Constraints most at risk:** every number in data (no constant for 0.5, 6 m, 5 s or 50 %); no per-tick work for units
without passives; no allocation in phase 5 / 11 paths (the aura's neighbour query through the spatial hash into existing
scratch); slot-order loops only; `Rts.Sim` warnings as errors; append-only on `statuses.json` and the enums; the sim stays
off `docs/factions/**`, `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`, `sim/Rts.Sim/ViewApi/**` and `game/**` except
`game/data/common/statuses.json` and `game/data/factions/whirlwind/units.json`.

**After:** slice b (`spawn` + temporary units: lifetime ticks, no cost / pop, freed on expiry, docs/factions/shadow.md
Wraith 20 s; `targetUnit`; `autocast` with its toggle command and `CommandDoorFuzz` kind; the AI hook noted for M5), then
M4-5 stealth / detection, the fog-on sandbox Playable, the end-of-M4 hardening (BUG-0401, 0302 b / c, 0275, 0271 / 0272
decision, 0144, 0142, 0113 item 2, 0094, 0391), sign-off.

## View track

### Current session plan: M4-V6c · zone discs + the Blinded marker · feature · QA standard · ~800 lines

**Goal.** Show the storm: a disc on the ground per live zone from `World.Zones`, drawn for the local player's own zones always
and for an enemy's only where the player may see it, fading in its last second; and give the Blinded marker its own colour
so a Sandstorm's victims read as Blinded + Slowed. BUG-0390 landed at the 0624 integration (verified on `main`: the per-unit
walk check is in `QaGhostViewTest.cs`), so nothing to do there.

**Scope.**
1. **`ViewApi.ZoneDiscs`** (pure, allocation-free, read-only): `Collect(world, player, fogOn, discs)` fills a caller-owned
   span of `ZoneDisc(slot, center, radius, alpha, own)` for every live zone the player may see, slots ascending. Visibility
   rule (Producer default, docs/02 "Zones": a hiding zone is seen into only by its owner and by units inside it; from
   outside the player sees the storm itself where its sight reaches): an **own** zone is always drawn; an **enemy** zone is
   drawn when any cell whose centre is within the zone's radius is visible (`Fog.Visibility == 2`: an own unit inside
   sees it), **or** any cell of the rim ring (centre farther than the radius but within radius + `RimWidth` 2 m) is visible
   (the player's sight reaches the storm's edge). Explored-only ground draws nothing (a storm the player only remembers the
   ground of is not shown). Without fog (`--no-fog`) every live zone is drawn. `alpha` is `DiscAlpha` (0.35) until the last
   second (`TicksRemaining <= 20`) then falls linearly to 0 at 1. A hash twin: collecting changes no state hash (2 seeds x
   1,200 ticks with storms cast).
2. **`AbilityViews`** draws one flat translucent disc per `ZoneDisc` on the terrain at the zone's centre height (`TerrainHeight`
   as the resolve flash does), radius from data, coloured by the zone's owner (own: a sand / dust tone; enemy: the same tone
   with the owner's team colour in its rim); one `MultiMesh` of `ZoneStore.Capacity` instances written densely each frame
   (a disc goes the frame its zone frees or the rule hides it). 0 B per frame.
3. **Blinded marker:** `AbilityViews.BlindColor` (a dark violet / purple: readable at zoom 30 beside the orange Burning and the
   deep blue Slowed, on sand and on both factions' bodies: take a windowed shot and look at it) wired in the `StatusKind`
   switch; `OtherStatusColor` stays the default arm (the sim adds `StatusKind.Frenzied` this session; the view gives it a
   colour next session, one line).
4. **Tests.** xUnit `ViewApi/ZoneDiscsTests`: a real Sandstorm (the Priest casts at a point): one disc at the resolve with the
   def's radius for 240 ticks (the owner), alpha 0.35 until the last 20 ticks then falling, none at tick 241; the enemy sees
   it only when one of its units stands inside or its sight reaches the rim (a unit 10 m from the rim: none; walked to the
   rim: one; stepped inside: one); two storms, slots ascending; a full output drops nothing it should show (capacity =
   `ZoneStore.Capacity`); 64 zones x 2 players 0 B; the hash twin. Headless `AbilityViewTest` rows per seed: a Priest's
   Sandstorm shows a disc at the resolve that lasts 12 s and fades in its last second; an enemy Priest's storm under the fog
   is collected never drawn until an own unit walks to its rim; the Blinded marker colour on a Sandstorm victim beside its
   Slowed one (both from the real cast: no reflection staging needed now). QA's own scene (`QaV6cTest`) in the loop.
5. Windowed shots: `ability-seedN-storm.png` (own disc, zoom 30), `-storm-enemy.png`, `-blinded.png`; looked at.
6. docs/03 "Implementation (M4-V6c)" (the visibility rule, its cost, the fade, the colours, tests) and a "Not yet" line for
   the Frenzied marker and stealth visuals.

**OUT of scope:** stealth visuals (M4-5), the Frenzied marker (next session), autocast toggles, a status tooltip, the minimap
(no zone dots this session), `game/data/**`, any sim file outside `sim/Rts.Sim/ViewApi/`.

**Acceptance criteria.**
1. `ZoneDiscsTests` as in Scope 4, green; the enemy-visibility rows prove: unexplored or explored-only ground → no disc;
   a visible rim cell → disc; an own unit inside → disc.
2. The hash twin: `Collect` every tick over 2 seeds x 1,200 ticks with storms cast on both sides changes no state hash.
3. `AbilityViewTest` headless rows (seeds 1 and 6) green, incl. the fade (alpha strictly falling over the last 20 ticks and
   the disc gone at 241) and the hidden enemy storm never drawn until the rim is seen.
4. `StatusColor(blinded) == BlindColor`, distinct from the other three colours; a windowed shot at zoom 30 with Burning,
   Slowed and Blinded side by side on a Raider, looked at and named in the report.
5. 0 B per frame in the `AbilityViewTest` steady match with 8 live zones (two blocking) and 500 units; the scene loop
   40 / 40 (the new QA scene included); smoke PASS; build 0 warnings; no new player-facing literal.
6. docs/03 as in Scope 6.

**Design references:** docs/02 "Zones", "Vision and fog of war" (what explored fog shows: last-seen terrain and buildings,
not live effects), "Fog of war" three states; docs/03 "For the view (M4-V6c)" (the `World.Zones` surface, `Fog.Visibility`
holds hidden cells as explored), "Implementation (M4-V6b)" (`StatusMarkers`, `ResolveFlashes` disc drawing, the colour
switch), "Implementation (M4-VH2)" (`SeenResources` as the model for a view-side fog-gated memory); CLAUDE.md rules 1, 5, 7.

**Tests required:** `ViewApi/ZoneDiscsTests` (incl. the hash twin and 0 B), the `AbilityViewTest` rows, `QaV6cTest`, the
windowed shots.

**Constraints most at risk:** dumb views (the disc rule lives in `ViewApi`, the node only draws); no per-frame allocation
(fixed spans, no LINQ); the disc must never show a storm the player cannot see (the rule is pure fog bytes: no peeking at
`ZoneStore` for enemy zones beyond `Alive` / `Owner` / `Center` / `Radius` / `TicksRemaining`); read-only on the sim (the
twin proves it); absolute paths in test I/O; `SceneExit` in the new scene script.

**After:** the Frenzied marker (one `StatusKind` line, after M4-4b-3a lands), the `selfAura` card (no target click: the
button casts; after M4-4b-3a), stealth visuals (M4-5), the sandbox Playable scene with fog on, the end-of-M4 view hardening
(BUG-0400, 0250 item 2, 0148 item 1, 0126 items 5 / 6, export hygiene).

## Data track

### Current session plan: D-H2 · the data track's second hardening · hardening · QA light · ~300 lines, tests only

**Why now.** D11 (pins for Frenzied + the Zealot passives, the `blinded` text, one golden regen) must wait: M4-4b-3a edits
`common/statuses.json` and the Whirlwind `units.json` this session, and no two tracks edit one `game/data/` file in a
session. The counter is 3 / 4, so the data hardening is due after one more feature task; taking it now (one early) costs
nothing and frees next session for D11. **No `game/data/**`, `docs/factions/**` or `docs/02` edit this session: no golden
move, no text change.**

**Scope** (files: `sim/Rts.Sim.Tests/Content/**` and `sim/Rts.Sim.Tests/QA/Content/**` only).
1. **BUG-0410 (S4):** `AbilityContentTests.DescriptionProblems` checks a description's numbers by **role**: a number next to a
   duration word ("second(s)", "s") must equal the entry's duration / lingering / cooldown as the words say ("every N s",
   "for N s", "N s cooldown"), a distance word ("m", "metre") the range / radius / sight / reach, a percent the magnitude
   x 100, a bare damage number the amount; a number in the wrong role fails naming the entry, the number and the role.
   Un-skip `SandstormBlindedPinQaTests.ADescriptionNumber_InTheWrongRole_Fails` (4 / 4 fail). Every shipped description
   stays green (if one is genuinely ambiguous, report it in the For-your-review note; do not change the text this session).
2. **D10b QA note:** `Compare` resolves `applyStatus` statuses through `GameData.Statuses` while `CompareStatuses` takes a list;
   make both use the list passed (an in-memory ability applying an in-memory status works), with a test.
3. **D-H1 QA note:** `QA/Content/AgesRuleQaTests` keeps its own `### Ages` heading lookup; use `PageTables.Heading`.
4. A sweep of the Content tests' summaries and messages for stale wording (e.g. "pending Sandstorm", "D9") with the fix in
   the same commit; `PendingStatuses` **keeps "Frenzied"** (the sim lands it this session; D11 pins it) and no new check may
   fail on a pending status of an unknown kind (it must report, never fail: `CompareStatuses`' `_ => ""` arm stays a report
   for pending names).
5. The D-H2 bug-file updates (BUG-0410 `fixed` with the test), `studio/qa/coverage.md` data rows, a docs/03 "For the data
   track" note only if a check's rule changed in a way content authors must know.

**OUT of scope:** any text or number change in `game/data/**` or `docs/factions/**`; the `blinded` wording (D11); pins for
Frenzied / passives (D11); the balance pass (waits for the fog-on sandbox); any C# outside the two Content folders.

**Acceptance criteria.**
1. BUG-0410: the un-skipped theory passes (4 / 4 wrong-role mutants fail with the entry, number and role named); every shipped
   description passes; a mutant moving Sandstorm's "12 s" to "18 s" fails; a mutant writing Telas Fire's "4 s" as "4 m" fails.
2. `CompareStatuses` / `Compare` with an in-memory ability applying an in-memory status: no throw, the right problems named
   (a test).
3. `AgesRuleQaTests` reads the heading through `PageTables.Heading`, green.
4. `git diff --stat` shows only `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, `studio/**`, and docs/03
   if touched; no `game/data/**`, no `docs/factions/**`; `data-hash` unchanged.
5. Content filter green with 0 skipped rows that D-H2 owns; the full non-Perf suite green; build 0 warnings.

**Design references:** docs/02 "Ability system", "Status effects" (the roles a description can state); docs/03 "For the data
track (M4-4a / 4b-1 / 4b-2)" (field names and units: seconds in data); docs/factions/*.md ability tables (Duration /
Cooldown / Range / Radius columns name the roles).

**Tests required:** the role theory un-skipped, the in-memory status test, the heading test, the mutants in criterion 1.

**Constraints most at risk:** staying off every `game/data/` and `docs/factions/` file; `PendingStatuses` keeps "Frenzied";
no check that fails on a landed pending status; QA light.

**After:** **D11** next session (the sim's M4-4b-3a on `main`): pin the Frenzied row (docs/02 Effect cell reworded to the
kind's exact effect) and the Zealot passives' page text to `UnitDef.Passives` (50 %, +50 %, 6 m, 5 s), the Frenzied
`description` and the `blinded` text ("can't attack", Producer proposal, owner may veto), one golden regen; then the owner's
tweaks, the full balance pass (QA standard) once the sandbox gives numbers, `ai.json` build orders (M5), M7-M9 faction data.

## Watch-outs (all tracks)

- **Integration order sim → view → data.** Merge sim; run the non-Perf suite + smoke; merge view, run the scene loop (40
  scenes with the view's new one); merge data (tests only: no golden conflict). Expected conflicts: `studio/bugs/README.md`
  (union by id), `studio/qa/coverage.md` (three sides), docs/03 (different sections), docs/01 (the sim's row only).
- **Golden:** only the sim moves `data-hash`. If the data branch's Content tests report "status 'Frenzied' has landed" on the
  merged tree, that is a report, not a failure (D10b's fix); D11 pins it next session.
- **Perf:** three suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- The view must not reference `StatusKind.Frenzied` (not on its base); the sim must not touch `AbilityViews` or the Content
  tests; the data track must not touch `GatherWedgeQaTests` (the sim adds the hash).
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name. Stale scratch worktrees are the
  owner's housekeeping (STATE "Waiting on you").
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
