# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-09-1155** (base `ef22043` = `origin/main` after the 0724
integration; third full session of 2026-10-09, cap 8). The ACCEPT replaces this file.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** Left: 6 abilities (slice 1 M4-4a landed; this session's M4-4b-1 is the second slice),
  7 stealth (M4-5), 8 the four signature abilities (Telas Fire works; **Cusser this session**; Sandstorm needs zones + Blinded;
  Zealot passives need passives), 10 the fog-on sandbox Playable.
- `main` at PLAN: build 0 warnings; non-Perf **4,268 / 11 skipped / 0 failed**; smoke PASS. No open S1 / S2 (S3 16, S4 15).
- Hardening counters before this session: sim 2 / 4, view 2 / 4, data 0 / 4 (feature sessions since the last hardening).
- **Weekly usage is at 92 % of the 95 % stop (reset 2026-10-10 02:00 local).** Every task below is sized to about half the
  usual, with its items ordered so that **the first commit is shippable on its own**: devs ship what is green when the
  conductor calls time, and say in the report which items did not land. After the reset, normal sizes.
- Bug ids: sim from **BUG-0330**, view from **BUG-0340**, data from **BUG-0350**.
- Standing rules: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full suite incl.
  Perf once (alone) and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the `ui.json`
  `states.*` line itself (none this session); (3) whoever moves `data-hash` adds the new hash to
  `GatherWedgeQaTests.SameGameDataHashes` in the same commit when the seed-21 match is unaffected, else re-records.

## Sim track

### Current session plan: M4-4b-1 · the Cusser's rules, the `abilityCooldown` tech effect, the whole-seconds DoT rule · feature · QA full

**Goal.** Make the second signature ability work from data (M4 criterion 8: the Sapper's Cusser) by extending the `damage`
effect to buildings and friendly fire, apply the `abilityCooldown` tech effect that `techs.json` already carries (Moranth
Supply, Dryjhna's Prophecy) but `AbilitySystem` ignores (`AbilitySystem.cs:100` uses the raw `CooldownTicks`), and refuse
at load a damage-over-time status shorter than whole seconds (Producer decision 0724). Zones, Blinded, summons, self / aura
and autocast are **M4-4b-2** next session (after the usage reset).

**Scope (in this order; each item is a commit that leaves `main` green):**
1. `abilityCooldown` applied: at the resolve, `ready = tick + max(1, CooldownTicks + TechBonus(owner, unitType,
   TechStat.AbilityCooldown))` (the loader already converts seconds; check `DataLoader.Techs.cs:246`). A tech researched
   while a cooldown runs does not shorten the running one (simplest; say so in docs/03). Applies to every ability of the unit.
2. The loader rule: an `applyStatus` effect whose status is `damageOverTime` must have a `duration` that is a whole number of
   seconds, at least 1 (a `DataError` naming file and field). Docs/03 "For the data track" + a docs/01 row ("Producer
   decision, owner may revisit").
3. The Cusser: two optional fields on the `damage` effect, additive and validated: `"buildings": true` (enemy buildings whose
   footprint rectangle is within `radius` of the point take the hit too, through `BuildingStore.Damage` as class
   `structure`; default false) and `"friendlyFire": 0.5` (the caster owner's units in the radius take the hit at this
   fraction, 0-1, rounded like splash; default 0; the caster itself included when inside; **never own buildings**,
   docs/02 "Splash and friendly fire"). No falloff: an ability's radius is exact, like Telas Fire (Producer default,
   docs/01 row). Ship the data: `factions/malazan/abilities.json` `cusser` (range 6, radius 3.5, cast 1.0 s, cooldown 45 s,
   `damage` siege 120, `buildings` true, `friendlyFire` 0.5, `affects` `enemy_units`) and the Sapper's `"abilities":
   ["cusser"]` in `malazan/units.json`. Golden regen (`data-hash` moves: the seed-21 match has no Sapper, so add the hash to
   `SameGameDataHashes`, rule 3). `DataLimits` bounds for the new fields.
4. If budget remains: fold in nothing else; write the report.

**OUT of scope:** zones / `createZone`, Blinded, `spawn`, `selfAura` / `targetUnit`, `autocast`, passives, the AI's casting,
BUG-0311 (M4-5's first item), any view file, `docs/factions/**` (the data track edits those pages this session), `ui.json`.

**Acceptance criteria:**
1. After Moranth Supply, a Sapper's Cusser is usable again 30 s after the resolve (600 ticks), not 45; a tech finishing
   mid-cooldown leaves the running one; a cooldown can't go below 1 tick (a test with a -60 s fixture). The same for a
   Priest fixture with `dryjhnas_prophecy` (any ability on the unit).
2. `AbilityLoaderTests`: `duration` 2.5 s, 0.5 s and 0 on a `damageOverTime` status are refused naming file + field; 4 s
   loads; a slow with 2.5 s still loads; `buildings` on an `applyStatus` effect and `friendlyFire` outside 0-1 are refused.
3. A Cusser cast at a Town Hall's footprint edge deals the docs number: `120 x 3.0 - 5 = 355` (`DamageCalc`, structure
   class); an enemy Light unit in the circle takes `round(120 x 0.5) - armor`; an own unit in the circle takes half of its
   hit (rounded like splash's 50 %); an own building in the circle is unhurt; a unit 3.51 m out is unhurt; the Sapper
   itself takes friendly fire when it casts within 3.5 m of itself. Death events credit the caster's owner (own-unit
   deaths follow the splash convention). A building taken to 0 goes through the normal destruction path.
4. Determinism: `Stress/AbilityFuzzStressTests` extended with Cusser casts (friendly fire, buildings) over 3 seeds x 2,000
   ticks with replay round trip, twins equal; `StateHashTests` unchanged or extended (no new state field is expected: the
   ready tick is hashed already).
5. Golden regenerated once with every `k` line equal; `SameGameDataHashes` has the new hash; `DataContentHashTests` green.
6. Full suite incl. Perf green alone (rule 1: a data-loading and combat rule changed); `AbilityPerfTests` still 0 B;
   smoke PASS; docs/03 "Implementation (M4-4b-1)" (the cooldown bonus timing, the two `damage` fields, the DoT rule) + the
   "Data format" row + "For the data track" updated; docs/01 rows (the DoT rule; no falloff on abilities); docs/02
   unchanged except, if needed, one sentence under "Ability system" that `damage` may reach buildings / friendly fire per
   effect.

**Design references:** docs/factions/malazan.md "Abilities" (the Cusser row: 6 / 3.5 m / 1.0 s / 45 s / 120 siege, full
damage to buildings ≈355 to a Town Hall, friendly fire 50 %) and "Faction upgrade" (Cusser cooldown 45 → 30 s); docs/02
"Damage type × armor class" (Siege x Structure 3.0), "Splash and friendly fire" (50 %, never buildings), "Ability system";
docs/03 "Abilities, statuses, zones" + "Implementation (M4-4a)" (resolve order, `affects`, the pulse clock), "Data format".

**Tests required:** `Data/AbilityLoaderTests` (criterion 2), `Abilities/AbilitySystemTests` (criteria 1, 3), the tech
cooldown rows beside `TechState` tests, `Stress/AbilityFuzzStressTests` (criterion 4), golden + `DataContentHashTests`
(criterion 5), `AbilityPerfTests` rerun.

**Constraints:** no Godot in `Rts.Sim`; no stat in C# (the Cusser's numbers only in `abilities.json`); no allocation in
phases 5 / 6 (`world.Neighbors` scratch for the building query, like `CanPlace`); `SpatialHash` order + exact distance
test as in M4-4a; the data track must not touch `game/data/**` this session and the sim must not touch `docs/factions/**`
(no content pin reads abilities today, so adding `cusser` breaks none). Files the sim edits under `game/data/`:
`factions/malazan/abilities.json`, `factions/malazan/units.json` only.

**QA focus (full).** Attack the friendly-fire and building paths: a Cusser on the caster's own position (self-kill, death
event owner, hash twins); a Cusser on a building footprint corner vs centre (the "within radius" rule for rectangles must
be the same one `UnitSeesFootprint` / splash use, say which); a site under construction hit by a Cusser (damage sticks,
BUG-0138 rule); two Cussers resolving the same tick on one building that dies (no double free); the cooldown bonus with
the tech researched the same tick as the resolve; a `-60 s` bonus (floor 1 tick); replays with Cusser casts round-trip;
hostile 3-player fuzz 3 seeds x 2,000 twins equal; `AbilityPerfTests` + `EmptyTick(2500)` / `TightBlob2500` alone (thin
margins: 0.49 of 0.5 ms, 4.52 of 4.6 ms). Loader fuzz on the two new fields.

### Watch-outs (sim)

- `World.Neighbors` / `FlowFields.BuildScratch` are shared scratch (phase 6 uses `Neighbors`; the fog lies over
  `BuildScratch` in phase 12).
- Golden `data-hash` is 7E04011FC88881F3 today; the Cusser moves it once.
- Next sim hardening (counter 3 / 4 after this): BUG-0311, BUG-0241, BUG-0157's kept-chase rule, BUG-0270, BUG-0302 b + c,
  BUG-0275 items 1-2, BUG-0271 / 0272 (decide or wontfix), BUG-0144, BUG-0142 items 1-2, BUG-0113 item 2, BUG-0094, BUG-0242.
- Then M4-4b-2: zones (`createZone`, `ZoneStore` hashed, phase 5 status application every tick, the per-player
  vision-blocker mask), Blinded (sight 2 m, acquire / attack ≤ 3 m), Sandstorm shipped by the sim with its `affects`
  (non-Whirlwind units); then summons / aura / autocast (M4-4b-3) and M4-5 stealth (BUG-0311 first).

## View track

### Current session plan: M4-V6a · the ability button, targeting and the cast on screen · feature · QA standard

**Goal.** Let the player cast Telas Fire from the window (M4 criterion 6's view half, Requests 26): the command card's
ability button, a targeting mode with the range ring and the radius circle, `Command.UseAbility` from the nearest selected
caster only, and the cast shown on the caster. Status markers and resolve flashes are **M4-V6b** (next session) unless
they fit after the rest is green.

**Scope (in this order; each item a green commit):**
1. The command card: for a selected unit whose type has `Data.Units[type].Abilities`, one button per ability (label
   `Data.Abilities[id].DisplayName`, tooltip `Description` + range / radius / cooldown in seconds, a grid hotkey from
   `ui.json` like the other buttons); while on cooldown the button is dimmed and shows the seconds left (from
   `Units.AbilityReadyTick[slot * DataLimits.MaxUnitAbilities + k] - TickNumber`); a mixed selection shows the buttons of
   the casters in it. New text keys in `ui.json` (view-owned; the sim does not touch it this session).
2. Targeting mode (like A + click): the button or hotkey arms it; the range ring (`Range`) is drawn around the caster that
   would cast, the radius circle (`Radius`) follows the cursor on the ground; left-click sends **one** `UseAbility` from the
   selected caster nearest the click whose ability is ready (Shift queues on that caster; the others stay); right-click /
   Esc cancels; a click beyond range still sends (the sim walks the caster in). Minimap click: not needed.
3. The caster on screen: the selection panel's state reads "Casting" (`states.casting` exists); a cast bar over the caster
   (`Units.CastTicks` left over the def's `CastTicks`), fog-gated like hp bars; a walking caster (`CastAbility >= 0` while
   `Moving`) keeps the radius circle at `CastPoint`. One F12 line (casts in flight / events last tick).
4. Test scene `AbilityViewTest.tscn`: a Cadre Mage and enemy infantry; the scripted button + click puts a `UseAbility` in the
   queue, the sim resolves it (an enemy's `Units.Statuses.Count > 0`), the circle's drawn radius equals the def's, the
   nearest-caster rule picks the right one of two mages, 0 B per frame at 500 units, hash twins equal with the view on / off.
5. If budget remains: Burning / Slowed markers from `Units.Statuses` and the resolve flash from `World.AbilityEvents`
   (M4-V6b otherwise). BUG-0310 only if a few lines (skip a ghost entry whose footprint has a visible cell).

**OUT of scope:** zone visuals (after M4-4b-2), stealth visuals, autocast toggles, the AI, any sim file outside
`sim/Rts.Sim/ViewApi/` (and only read-only accessors there, if any are needed; docs/03 says none are for slice 1).

**Acceptance criteria:**
1. Select a Cadre Mage: the card shows a "Telas Fire" button with its hotkey; press it, the ring and circle appear sized
   16 m / 3 m; click near enemy infantry: exactly one `UseAbility` is enqueued for the nearest ready mage; the mage walks
   if needed, stands "Casting" with a cast bar, and the enemies in the circle burn (the sim's statuses). Screenshot
   `ability-seed1-targeting.png` and `ability-seed1-casting.png` named and looked at.
2. With two mages selected and one on cooldown, the ready one casts; with both on cooldown nothing is sent and the button is
   dimmed with the seconds left.
3. Shift + click queues the cast on the chosen mage only; right-click / Esc leaves targeting with no command sent.
4. `AbilityViewTest` rows green headless; the scene loop (36 + this scene) green; 0 B at 500 units; twins equal.
5. Build 0 warnings; smoke PASS; no `ui.json` fallback literal in C#; docs/03 "Implementation (M4-V6a)".

**Design references:** docs/02 "Ability system" (hotkey or button, then click; only the nearest selected caster casts;
cooldown, no mana), "Controls and camera"; docs/03 "For the view (M4-V6)" and "Implementation (M4-4a)" (what
`AbilityReadyTick`, `CastTicks`, `CastPoint`, `AbilityEvents` mean), "Implementation (M3-V2)" (the card grid and `ui.json`).

**Tests required:** the test scene rows above, an xUnit row for the nearest-ready-caster pick if that logic lives in a plain
class (recommended: a static picker in `game/scripts` with no Godot types, testable from `game/tests` or a sim-free xUnit
project the view already uses for ViewApi rows), the scene loop.

**Constraints:** dumb views (no gameplay state in nodes; the circle reads the def each frame); text only from `ui.json` /
the data; one `UseAbility` per click; never mutate sim state (ViewApi read-only rule).

**QA focus (standard).** Click spam while targeting (500 clicks: one command per click, 0 B), a caster that dies while
targeting is armed, a mixed selection (mage + infantry + a second mage out of range), the circle under fog (an unexplored
cursor spot), Shift queues, the cooldown countdown against the sim's ready tick on every frame, twins with the view on / off.

### Watch-outs (view)

- `Fog.Ghosts(player)` changes only on update ticks (`tick % 4 == 1`); BUG-0310's cause.
- BUG-0251: `EconomyViewTest` can FATAL at shutdown after PASS under load (rerun once).
- Next view hardening (counter 3 / 4 after this): BUG-0310 (if still open), BUG-0251, BUG-0250 (3 items), BUG-0281 items
  1-3, BUG-0148 item 1, BUG-0126 items 3 / 5 / 6, export hygiene (M6).

## Data track

### Current session plan: D10a · the abilities pinned data → page, the statuses' text pinned · feature · QA light

**Goal.** Start the abilities content work against the M4-4a schema: pin every loaded ability's page row to
`abilities.json` and every status's name to docs/02's status table, so the Cusser (landing from the sim this session) and
Sandstorm (M4-4b-2) get checked the same way the units and buildings are. **D9 is dropped** (Producer decision, owner may
revisit): the towers' attack / detector numbers already sit in the pages' Provides text and D-H1 pinned them both ways to
the data; separate columns would add nothing the owner can't read now.

**Scope:**
1. `Content/AbilityContentTests` (+ a `PageTables` reader for the "Abilities" table): for **every ability the loader
   returns** (both factions), its page row matches on Unit (the unit whose `abilities` lists it), Kind (`Target ground` ↔
   `targetGround`), Range, Radius, Cast, Cooldown, Duration when the column exists, and the Effect text's numbers:
   `applyStatus` ↔ "<Status name>: <magnitude> <damage type> damage/s for <duration> s" (Telas Fire's "10 magic damage/s
   for 4 s"), `affects` ↔ "Enemy units" / "Own units" / "All units", "No effect on buildings" when the effect reaches none.
   Keep the parser tolerant of the Cusser's `damage` row ("120 siege damage in the area, full damage to buildings
   (≈355 to a Town Hall), friendly fire at 50%") so the row pins once the sim's entry is on `main`: pin amount + type +
   "friendly fire at 50%" ↔ `friendlyFire` 0.5 + "buildings" ↔ `buildings` **only if** the loaded def has those fields
   (read them by reflection-free `AbilityEffect` members if present on `main` at merge; otherwise leave a TODO row).
2. The other direction with an allowance: every page "Abilities" row has a loaded ability **or** is in a named allowance
   list (`Cusser`, `Sandstorm`); an allowance entry that *has* landed is tolerated (not a failure) and reported by name,
   so the sim's Cusser landing after this branch doesn't break the merged tree; the next data task shrinks the list.
3. `statuses.json`: each entry's `displayName` appears in docs/02's "Status effects" table with the matching Effect wording
   for its kind (Burning ↔ "Damage over time (magic)", Slowed ↔ "Movement speed × (1 - magnitude)"); pinned both ways for
   the kinds the sim loads (`damageOverTime`, `slow`); the not-yet-loaded rows (Stealthed, Revealed, Frenzied,
   Regenerating, Blinded) in a named allowance like item 2.
4. Page tidy: give the Malazan "Abilities" table the Duration column Whirlwind's has ("—" for both rows) so both pages share
   one header; no number or text change beyond that. Review Telas Fire's shipped `description` and `displayName` against
   the page; propose any wording change in the report's review table, do **not** edit `abilities.json` (the sim's file this
   session) — a text change becomes a request for the next data task.
5. The For your review table (what was pinned, the allowance lists, the Telas Fire text as shipped, quoted).

**OUT of scope:** any `game/data/**` edit (the sim edits `malazan/abilities.json` + `units.json` this session), any C#
outside `sim/Rts.Sim.Tests/Content/` and `QA/Content/`, number changes (the balance answer is still wanted), the
Whirlwind `abilities.json` file (M4-4b-2 ships Sandstorm), docs/02 edits (shared; propose instead).

**Acceptance criteria:**
1. `AbilityContentTests` pin Telas Fire's page row to the loaded def cell by cell; a scratch mutation of each cell (range,
   radius, cast, cooldown, magnitude, duration, affects) fails naming the ability and the field (QA checks a few).
2. The allowance rule: Cusser and Sandstorm rows without data pass; a landed Cusser passes with a report line.
3. The statuses pin both ways for `burning` / `slowed`; the allowance covers the five unloaded rows.
4. Both pages' "Abilities" headers identical; printed tables byte-identical for the Units / Buildings sections (D8 harness).
5. Content + QA.Content + DataValidation green; build 0 warnings; no `game/data/**` diff; no `data-hash` move.

**Design references:** docs/02 "Ability system", "Status effects" (the table the names must match); docs/factions/malazan.md
"Abilities" (Telas Fire 16 / 3 m / 0.8 s / 25 s; Cusser 6 / 3.5 m / 1.0 s / 45 s), docs/factions/whirlwind.md "Abilities"
(Sandstorm 18 / 6 m / 1.2 s / 45 s / 12 s); docs/03 "Implementation (M4-4a)" + "For the data track" (the fields the loader
accepts).

**Tests required:** `Content/AbilityContentTests` (items 1-3), the `PageTables` reader rows, the D8 byte-identical check.

**Constraints:** files: `docs/factions/malazan.md`, `docs/factions/whirlwind.md` (the "Abilities" sections only),
`sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, bug files, `studio/qa/**`. No `game/data/**`.

**QA focus (light).** Mutate the page cells and the loaded def in a scratch copy and confirm each is caught by name; confirm
the allowance tolerates a landed entry; run Content + QA.Content + DataValidation on the branch and on a scratch merge with
the sim branch (the Cusser row then has data).

### Watch-outs (data)

- The sim's branch moves `data-hash` (the Cusser); the data branch must not (no `game/data/**` edit).
- Next: D10b once M4-4b-1 is on `main`: the Cusser row pinned fully, Sandstorm after M4-4b-2, the statuses' descriptions
  review, then the balance pass when the fog-on sandbox gives numbers.

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the scene loop (36 + the view's new scene) and smoke on each **merged**
  result; push only green states. Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md`
  (append all sides); docs/03 one subsection per track; docs/01 rows appended. `docs/factions/malazan.md`: the data track
  edits its "Abilities" section; the sim must not touch the page.
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- **Usage:** the conductor may call time before a fix round; each brief's item order makes the first commit shippable.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
