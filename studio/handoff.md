# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-14-1246** (session 2 / 8 of 2026-11-14, started 12:46, about 8 hours).
Sim **M8-1** ACCEPT (feature, 0 fix rounds, QA full PASS_WITH_ISSUES), view **M6-V21** ACCEPT (0, QA standard
PASS_WITH_ISSUES), data **D32** ACCEPT (0, QA standard PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10,
M7 3 / 4, M8 started (0 / 3).** Counters: **sim 1 / 4, view 2 / 4, data 2 / 4**. Bug ids next session: **sim 1430+, view
1440+, data 1450+**. Open S1 / S2: none. The next session is 3 / 8 of 2026-11-14 (or 1 / 8 of the 15th).

**The inbox holds three owner requests from 2026-11-14, not yet processed (they landed after the PLAN): process them
first at the next PLAN; inbox requests come before roadmap work.** (1) Gatherers may overlap a bit while collecting
(sim; a data knob; movement). (2) Teblor models first: the five humanoids at the front of the art queue (view). (3)
Shift-click queues orders: audit what queues, close gaps, make the queue visible (sim + view).

**Next session in one line (proposal):** sim **the gatherer-overlap request** (feature, QA full; a `rules.json` knob,
one hash move, the golden and the AI pins re-measured: rules 41 / 43 / 45) · view **the Teblor humanoids** (BUG-1412 and
BUG-1410 first; feature, QA standard) · data **a light task with no hash move** (BUG-1400 first; BUG-1421 items 3-4,
BUG-1360; or the Shadow page made pin-ready), because the sim moves the economy and the hash that session.

## Current session plan (2026-11-14-2156, session 3 / 8 of 2026-11-14; written at the PLAN)

Inbox processed (both notes, three requests). All three tracks GO, all feature sessions (counters sim 1 / 4, view 2 / 4,
data 2 / 4 before this session). Bug ids: sim 1430+, view **1441+** (the Producer filed BUG-1440 at this PLAN), data
1450+. `main` at f07b849 on a scratch clone (rule 38): build `--no-incremental` 0 errors / 1 warning (BUG-1412, the
view's first item), the default loop 9,359 / 0 failed / 19 skipped of 9,378 in 21 m 24 s alone, smoke PASS (with the
real LFS files). Expected red: the one Stress row of BUG-1400 (the data's first item).

**What the Producer found at the PLAN (the Shift-queue audit, from the code):** every unit order already takes the
queue flag in the sim (`Command.QueuedFlag`: Move, AttackMove, Attack, Gather, Build, Repair, UseAbility, Stop,
HoldPosition; 8 entries a unit) and the view passes Shift on every one of them (right-click context orders, attack-move,
abilities, the minimap, Stop / Hold), with one gap: the **first** Shift-placed building replaces the queue
(`CommandCard.GhostClick`: `queued: shift && _placed > 0`). Nothing draws the queue. There is no patrol or garrison
order; a rally point is a building's single point, not a unit order. An order queued behind a Gather waits until the
loop ends (the node runs out), by design. So the request is a **view task** (draw the queue, close the first-build
gap, a scene per order kind); the sim owes nothing unless that task finds a kind that does not start. It is the view's
task **right after the Teblor humanoids** (ahead of merged surfaces: an inbox request comes before roadmap work).

**The gatherer overlap is two sessions on purpose:** this session builds the rule with the knob absent = off (nothing
shipped moves, every pin must hold untouched) and measures candidate values; the next session ships the value in
`rules.json` (one hash move, the golden regenerated, the AI pins re-measured by rules 41 / 43 / 45; rule 54's
rehearsal at that PLAN). Producer default to ship: **0.5** (two gathering workers may stand or pass at half their
touching distance), unless this session's table says otherwise. The data track moves no hash in either session.

### Sim track: M6-S9 (feature, QA full)

**Title:** Build the gather-loop overlap rule and its `rules.json` knob (off by default), and measure candidate values.

Goal: the owner asked (inbox 2026-11-14) that gatherers clip through each other "a bit" while collecting instead of
pushing each other apart. Build it as one generic movement rule with one data knob: two workers of the same player that
are both on a gather loop count a shorter touching distance between them. This slice builds the rule with the knob
absent = off (bit-identical to today), proves it on test data, and measures what candidate values do to income and to AI
matches. The shipped value lands next session as its own hash move.

Scope:
- Schema: `game/data/common/rules.json` field `gatherOverlap` (a number, 0 ≤ v ≤ a cap below 1 that you choose and
  justify; absent = 0 = today). A JSON null, a string, a negative or a value over the cap is a load error.
  `RulesDef.GatherOverlap`. It enters the data content hash only when non-zero (as M8-1 did for `forestStealth`), so
  the shipped hash 5353DF8A1473FC6D and the golden replay do not move. **The shipped `rules.json` is not edited.**
- The rule (Producer decision): a **gather pair** is two live units of the same owner that both have `GatherNode` set
  (the whole loop: walking to the node, harvesting, walking to the drop-off, delivering). For a gather pair,
  `MovementSystem` uses `(radius_i + radius_j) × (1 − gatherOverlap)` wherever it uses the pair's radii sum to decide
  touching, spacing, pushing or walls (the overlap push, `ArrivalSpacing` / crowded, `ShoveSpacing` and the pack
  limit, the standing-wall limit, the sidestep / detour / queue reach, shoves). Every other pair is untouched: a
  gatherer and a soldier, a gatherer and an Idle worker, a builder or repairer (`BuildTarget`), any enemy, any
  footprint. If a site provably does not matter you may leave it on the full sum; docs/03 lists which sites use the
  pair distance and which do not, with the reason.
- No new per-unit state is expected (the predicate reads `GatherNode`, already hashed and saved). If you find you need
  any, stop and report instead of changing a save or replay format.
- A measurement report (Stress category, no bound, printed into docs/03): on copies of the shipped data with
  `gatherOverlap` 0 / 0.25 / 0.5 / 0.75: (a) 5, 10, 16 and 24 workers on one gold mine beside a hall, and on a tree
  line, for 5 game minutes, for the Malazan Laborer (0.4 m) and the Teblor Gatherer (0.75 m, carry 15): resources
  banked, trips, stuck ticks, give-ups, the smallest pair distance seen; (b) Normal v Normal on the menu's default
  map and the three named maps, seed 1, at 0 and 0.5: the result tick, gold + wood gathered by 10:00, whether the
  match still ends. Keep (b) under about 15 minutes alone. These figures choose the shipped value at the next PLAN.
- OUT of scope: any byte under `game/data/**`; regenerating the golden; re-pinning or re-bounding any existing test;
  any `MovementConstants` value; Build / Repair walkers; armies and marching units; the view; the Shift-queue; `game/**`.
- Size: uncertain work, about 800 changed lines of production code and its own tests (QA's suites and the report
  don't count).

Acceptance criteria:
1. Loader: absent and 0 load as 0; a valid value loads; null, a string, a negative, a value over the cap and NaN are
   load errors naming the file and field. `DataValidationTests` green. The shipped content hash is unchanged (the two
   golden data-hash pins pass unedited).
2. Off is identical: with the knob absent, the default loop and the Stress pin classes pass with **no existing
   expected value, digest, bound or golden edited** (the diff shows none). Any pin that moves fails this criterion;
   no M5-5 row, tally or opening pin is expected to move (rules 41 / 43 / 45: nothing to re-measure this session).
3. On, at 0.5 on test data: (a) two gather-loop workers of one player meeting head-on between node and drop-off pass
   with their centres closer than the radii sum, and neither collects a stuck tick; (b) 12 workers on one mine reach a
   steady state where gather pairs stand closer than at 0, no two centres ever closer than the scaled pack limit less
   float tolerance (nobody coincides), and the bank after 5 minutes is not lower than at 0 (lower is a finding to
   explain, not a bound to loosen); (c) for 0.4 m, 0.75 m and mixed radii.
4. Scope holds: a scene with no gather pair in it (a marching army through standing soldiers; a soldier walking
   through a mining line; an Idle worker beside miners; a builder beside miners; two players' workers on one mine)
   gives the same position for every non-gather pair interaction as at 0: where the scene has no gather pair at all,
   the state hash is equal tick for tick at 0 and 0.5.
5. Leaving the loop: a worker overlapped with another gets a Move or Stop; the pair separates under today's rules
   within 2 s, and no unit's step in a tick exceeds its speed plus the overlap removed (no teleport).
6. Determinism with the knob on: two runs equal tick for tick over a 5-minute economy scene; the result does not
   depend on spawn / slot order (the existing permutation pattern); a save mid-scene loads and continues to the same
   hashes; a recorded replay plays back equal.
7. Invariant fuzz with the knob at 0.25 / 0.5 / 0.75 (economy fuzz): no NaN, no unit on a blocked cell, every worker
   keeps delivering (no worker without a trip for 60 s while its node lives), income above zero.
8. Cost: 0 bytes allocated per tick with the knob on; the gather Perf rows hold their existing thresholds with the
   knob on (report the figures; a Perf failure counts only alone, rule 42).
9. The report's table is in docs/03 "Implementation (M6-S9)", dated, with the rule, the predicate, the sites, the
   knob's range and "absent = off; hashed only when non-zero". docs/02 "Economy" gets one row: the knob, off until its
   value ships.
10. `dotnet build RtsGame.sln --no-incremental` after the last edit: 0 errors and no warning beyond BUG-1412's one
    (the view fixes it this session). The full default loop after the last edit (rule 62). Every test process you
    started is ended (rule 52).

Design references: inbox 2026-11-14 (the owner's words); docs/03 "Local movement" and the `MovementSystem` remarks
(two passes from start-of-tick state, fixed-point push sums, "Idle" = any standing state incl. Gathering / Returning);
`MovementConstants` (`ArrivalSpacing` 0.6, `ShoveSpacing` 0.5, the separation shares); `EconomyConstants.Reach` 1.25 and
`GoalInset` (BUG-0146: a queued worker must stay in reach of the footprint); docs/03 "Implementation (M8-1)" for the
"hashed only when present" pattern; docs/02 "Economy".

Tests required: loader tests; scene tests for criteria 3-5 (xUnit, test data via the existing gather maps); the
determinism, save and replay rows of criterion 6; the fuzz of criterion 7; an allocation row; the Stress report. The
replay golden tests run unedited. A helper that forges the rule must tolerate shipped data that later carries it
(rule 58).

Constraints: no stat in C# (the amount is data; only the cap is a named constant with a reason); no allocation in
per-tick code (the neighbor loop is the hot path; Debug builds don't inline, mind the written-out copies that must
stay in step); float sums stay in fixed point where they are today; no `Dictionary` / `HashSet` iteration; no Godot;
golden hashes untouched. Bug-file updates as a patch in your scratchpad (you cannot write `studio/**`). A scratch
clone, if you need one, is named `m6s9-...` (rule 63).

QA focus (full): attack "off is identical" first (the golden, the two data-hash pins, `GatherWedgeQaTests`, the M5-5
rows and the Stress pin classes, unedited). Then the rule on: a mine wrapped by 24 and 40 workers, a mine in a
1-cell pocket (`GatherPocketStressTests`' maps), a drop-off against a cliff, node exhaustion with 24 overlapped workers
re-targeting at once, a worker killed while overlapped, a hall destroyed mid-delivery, enemy workers sharing a mine,
an army ordered through the mining line, 0.75 m Gatherers at 0.75 overlap, the knob at the cap and just over it.
Look for workers that end out of `Reach` of the footprint (BUG-0146's shape), a wedge that only forms with the knob
on, any pair that separates with a jump, slot-order dependence (shuffle spawn order, 20 seeds), a save taken while
pairs overlap. `WideCrowdStressTests` at 0: both columns unchanged. Perf alone under rule 42. Rule 65: end with
`--no-incremental` on the commit that holds QA's files.

### View track: M7-V5 (feature, QA standard)

**Title:** Make the five Teblor humanoids figures (BUG-1412, BUG-1410 and the M6-V21 bench table first).

Goal: the owner asked (inbox 2026-11-14) for the Teblor on screen sooner: today all seven Teblor unit types are
capsules. Build the Gatherer, Warrior, Javelin Thrower, Shaman and Breaker as animated, team-coloured figures with the
import tooling, the way docs/04 plans, so a Teblor match shows giants instead of capsules. The Destrier Rider and the
War-dog stay capsules (no horse or dog pack on disk: the owner's download, already under Waiting on you).

Scope:
- First, small: **BUG-1412** (`game/tests/QaM6V21Test.cs` line 257: `string? why`; the build is 0 / 0 again);
  **BUG-1410** (`QaV5Test` line 203 reads `ModelMeshOf(slot) ?? ViewOf(slot)`; show the check red with the flash
  disabled, green with it; add the scene to docs/03's list); docs/03 "Implementation (M6-V21)"'s bench table replaced
  by the Producer's alone figures, dated 2026-11-14: 400 figures 10.11 ms while 90 %+ alive (worst 1 % 14.52), 400
  capsules 0.77 ms; 1,000 figures 26.87 ms (worst 1 % 32.79), 1,000 capsules 1.29 ms; 4,542 / 12,219 draw calls.
- Five manifest entries in `tools/asset-manifest.json`, built by `tools/import-assets.ps1` / `tools/art/build_unit.py`
  into `game/assets/models/teblor/<id>/<id>.glb` (the paths `units.json` already names): the Universal Base Character
  (male), the two outfits on disk (Peasant, Ranger) recoloured to the Teblor palette (docs/04 "Faction palettes":
  earth brown, bloodwood red-brown, bone white, furs), long hair if the pack parts allow, props as boxes (a wooden
  greatsword for the Warrior, javelins, the Shaman's staff, the Breaker's great club or maul, the Gatherer's tool),
  team colour on one `team` surface. Blender 5.2 and `asset-sources/` are in the owner's main checkout
  (`C:\Dev\rtsGameProject\asset-sources`, read only).
- **Size (Producer decision):** a figure is drawn at its base height × the data's `scale` (1.5: about 2.7-2.85 m),
  scaled uniformly, feet on the ground; it is not stretched to the 1.5 m room. The selection ring, target ring and
  corpse disc keep the data radius (0.75 m). The factor comes from the data, never a C# literal or a baked size.
- Clips: the Gatherer the worker clips; Warrior and Breaker `attack_melee`; the Javelin Thrower a throw clip and the
  Shaman a cast clip if UAL1 / UAL2 hold one (add it to the library), else the nearest clip with the reason in
  docs/04. Every state a type can be in maps to a clip (no T-pose).
- `tools/asset-manifest.json`, `tools/import-assets.ps1` and `tools/art/**` are yours; the rest of `tools/**` is not.
- If five do not fit, the Gatherer, Warrior and Javelin Thrower are the minimum; say so in the first lines of the
  report and the other two become the next task.
- OUT of scope: the Destrier Rider and the War-dog; Teblor buildings; merged surfaces or any change to how figures
  are drawn or animated beyond what a 1.5× figure needs; the Shift-queue display (the next view task); `game/data/**`;
  `sim/**` outside `ViewApi`; re-cutting existing Malazan / Whirlwind figures (BUG-1411 waits).

Acceptance criteria:
1. `dotnet build RtsGame.sln --no-incremental`: 0 errors / 0 warnings (BUG-1412).
2. BUG-1410: `QaV5Test` reads the drawn mesh; red with the flash disabled (shown once, on a scratch copy), green on
   the branch.
3. docs/03's M6-V21 bench table carries the Producer's figures above.
4. The five `.glb` files exist at the paths `units.json` names, each from a manifest entry; each at most 3,000
   triangles, 512² textures, the shared 65-bone skeleton, one `team` surface; a second run of the import on a fresh
   clone is byte-identical.
5. On the shipped data: `ModelViewTest` has a row per new type (11 model types; the Rider and the dog capsules, with
   no `ERROR` line); a Teblor figure's drawn height is the base figure's × the data `scale` within 2 %, read from the
   data; its ring radius is 0.75 m.
6. Each new type plays the mapped clip in each of its sim states, checked by `Animation` identity as M6-V21 did; no
   state without a clip.
7. Pictures (rule 56: the shipped data), written to a new scratch folder `m7v5-shots` and listed in the report: each
   type close up; the five in a row beside a Malazan Heavy Infantry and a Laborer at the default zoom, in two player
   colours; a Teblor base with Gatherers mining and chopping; a fight of Teblor against Malazan figures. The Producer
   judges: giants read as giants, the five can be told apart at the default zoom, the player's colour is visible.
8. The bench alone (rules 42 / 61): 400 and 1,000 mixed figures including the Teblor types, with the capsule figure
   from the same window, reading the "while 90 %+ alive" column. 400 figures at or under 16.7 ms (the Producer re-runs
   it alone); 1,000 is reported, not gated.
9. `tools/qa/smoke.ps1` PASS; the scene loop all green (105 + what you add); the full default loop after the last
   edit (rule 62).
10. docs/04 (the Teblor rows of the coverage plan, the clip table, what stands in for hair, furs and weapons; the
    licensing log only if a new source is used) and docs/03 "Implementation (M7-V5)" (the size rule, the scene list).
    Report the size of the art pass's binaries in LFS.

Design references: docs/04 "Scale and dimensions" (Teblor ~2.7 m, 1.5×), "Team color", "Faction palettes and
silhouettes" (1.5× scale, long hair, huge wooden swords), "Import process", "Animation setup"; docs/factions/teblor.md
(the roster, what each unit carries); docs/03 "Implementation (M6-V20)", "(M6-V21)", "(M7-V3c)" (a unit is drawn as wide
as the room it takes for capsules; `scale` is height); docs/01 change log 2026-11-14 (f).

Tests required: `ModelViewTest` rows; a scene row for the drawn height from the data `scale` (a forged scale of 1.25
gives 1.25×); clip-identity rows per type; `QaV5Test` fixed. A new scene that reads a slot's own mesh reads
`ModelMeshOf(slot) ?? ViewOf(slot)`.

Constraints: views hold no gameplay state; no stat or size in C#; player-facing text only from data; binaries through
Git LFS (types already in `.gitattributes`); no raw pack committed; never download; scratch folders named `m7v5-...`
and created new (rule 63); end every Godot and test host you start (rule 52); rule 64.

QA focus (standard): the default loop, the scene loop, smoke. Then: a fresh clone with `git lfs pull` + import (byte
identity, no `ERROR`); a clone without LFS (pointer files: the Producer saw the import print glTF `ERROR` lines and
leave `.import` files modified at this PLAN, BUG-1440 S4: add what you see to it, do not fix it here); a Teblor match
on the shipped data at game speeds 0.5 / 1 / 2 and paused; a forged `scale` of 1 and 2 on a Teblor type (the figure
follows, the ring does not); figures at the cliff edge and on ramps (feet on the ground at 1.5×); 100 Gatherers on a
mine (hands and props through each other is fine, floating is not); selection, health bars, status marks and the
Regenerating cue sit above a 2.8 m head, not inside it; fog: an enemy giant leaving sight; corpses. The bench alone
(rule 42), base against head. Pictures of all of it for the Producer. Rule 65.

### Data track: D-R38 (feature, QA light)

**Title:** Make the Shadow page pin-ready and list what the schemas lack (BUG-1400 first).

Goal: the sim changes the gather rules this session and ships their value next session, so no opening is measured
and no data hash moves on this track meanwhile (rule 41). Use the time to do for the Shadow what D-R36 did for the
Teblor: put `docs/factions/shadow.md` into the shape the content pins read, check its numbers against docs/02's
templates, and list every mechanic the schemas on `main` cannot express yet, so the roster can land the session its
schemas exist.

Scope:
- First: **BUG-1400** (rule 57). `Content/TeblorLandingOpeningReportTests.TheRows_ChangeTheMatch_FromTheFirstFrenzyOn_Report`
  compares "without the rows" against "with the rows", and the shipped data has carried the rows since D31. Re-build
  the "off" side from a copy with the rows removed, or retire the row with the reason in the file. Either way the
  Stress run of `Content` and `QA/Content` ends with 0 failed.
- `docs/factions/shadow.md` in the Malazan / Teblor shape: Units with the `Id` / `Targets` / `Spawn` / `Scale` columns
  and every column the pins read; the faction bonus as the exact `forestStealth` block docs/03 "For the data track
  (M8-1)" gives; a Techs table; Abilities with the fields the ability schema has; Buildings; Difficulty; "AI build
  order (Normal)" with one opening written as steps the `ai.json` schema can hold.
- `ContentFactions.PagesAhead` gains `shadow`; `PagePinReadinessTests` reports 0 problems for the page.
- A section "Schema gaps (D-R38)": each mechanic on the page that no loader field expresses today, one row each with
  the field you would want (the Shadow Archer's stand-still stealth and +50 % first shot; Pack Hunter's bonus per
  other Hound within 8 m, max +30 %; at most 7 Hounds alive; Shadow-step's group teleport; Summon Wraiths, a summoned
  unit with a lifetime, no cost, no pop and never stealthed; a ram that attacks buildings only, if `targets` does not
  cover it; anything else you find). Return them as proposed "Requests for the sim track" text in the report.
- A section "Owner review (D-R38, proposed)": `displayName` and `description` for every unit, building, tech, ability
  and the opening (at most 144 characters, no release name leaked, the `FactionWords` rules), and a counters proposal.
- A list of open questions for the Producer (a page cell that contradicts docs/02 or a built rule, e.g. the bonus
  text "or taken damage" against M8-1's rule; whether the Wraith is under `exceptSlots`), each with your recommendation.
- OUT of scope: any byte under `game/data/**` (no Shadow folder yet, no hash move); any C# outside
  `sim/Rts.Sim.Tests/Content/` and `sim/Rts.Sim.Tests/QA/Content/`; measuring any opening; BUG-1420 / D33 (after the
  gather value ships); BUG-1421 and BUG-1360 (the data's next hardening).

Acceptance criteria:
1. BUG-1400: the row is green or retired with its reason; `dotnet test sim/Rts.Sim.Tests --filter
   "FullyQualifiedName~Rts.Sim.Tests.Content|FullyQualifiedName~Rts.Sim.Tests.QA.Content"` (Stress included): 0 failed.
2. `PagePinReadinessTests`: 0 problems for `shadow.md` with `shadow` in `PagesAhead`; removing one section from a
   copy of the page fails naming the page, the section and the pin that reads it.
3. Every number on the page either equals docs/02's template for its slot or is listed as a deliberate difference
   with one line of why; the page's totals (cost, pop, train times) are consistent between its tables and its AI
   order.
4. "Schema gaps (D-R38)" is complete: every mechanic in the page's unit notes, abilities and bonus appears either as
   "expressible today: field X" (with the loader field named) or as a gap row.
5. "Owner review (D-R38, proposed)" holds a name and a description for every row the roster will ship; none over 144
   characters; no release name from the page's codename table appears.
6. The open questions are listed with a recommendation each.
7. `git diff` touches only `docs/factions/shadow.md`, `sim/Rts.Sim.Tests/Content/**` and
   `sim/Rts.Sim.Tests/QA/Content/**`. Build `--no-incremental` adds no warning; the default loop is green after the
   last edit (rule 62); test processes ended (rule 52).

Design references: docs/factions/shadow.md; docs/02 "Units" template table, "Stealth and detection", "Buildings",
"AI"; docs/factions/teblor.md and malazan.md as the shape; docs/03 "Implementation (M8-1)" "For the data track
(M8-1)"; the session log 2026-11-10-1236 (D-R36).

Tests required: the `PagesAhead` row and its mutation check; BUG-1400's row re-built (or its retirement recorded).
No new pin on values that are not shipped.

Constraints: no C# outside the two content test folders; no `game/data` byte; player-facing text proposals follow
the data rules (codenames only); scratch folders named `dr38-...` (rule 63); the bug patch for BUG-1400 goes in your
scratchpad.

QA focus (light: the task's filters + the default loop, never the full suite): run BUG-1400's row alone and with its
class; run the page through the pins' own parsers the way `TeblorPageParseQaTests` did (substituted ids) and look for
a stray number or a column the parser skips silently; check ten page cells against docs/02 by hand; check the gap
list against `DataJson.cs` field by field (a "gap" that already has a field, or a field claimed that does not exist);
count the characters of every proposed description.

## Where things stand

- **`main` after this session = sim 7b1a0f5 + view 35cceb9 + data c2fa20e** (plus this ACCEPT's studio files). The
  Producer's scratch clone of that tree (`acc1246` in the session scratchpad; conflicts only in
  `studio/bugs/README.md` and `studio/qa/coverage.md`, both sides kept): build `--no-incremental` **0 errors / 1
  warning (BUG-1412)**; the default loop **9,359 / 0 failed / 19 skipped of 9,378 in 20 m 28 s (beside the scene loop)**; smoke **PASS**; the scene loop **105 of 105, first pass**.
- **Forest stealth is a rule, on test data only (M8-1).** `faction.json` `forestStealth { status, delay, exceptSlots }`;
  `ForestStealthSystem` runs in phase 5 before `StealthSystem.Run`; per-unit state in `UnitStore.Forest`
  (`ForestStealthStore`: `LastAttackTick`, `Held`), allocated, hashed (state-word bit 22) and saved (`FRST` after
  `REGN`) only when the data has a faction with the block; one status entry is shared with a timed stealth
  (`StatusSystem.Hold`). No shipped file, pin, digest or format moved (`CurrentVersion` 10). docs/03 "Implementation
  (M8-1)" has **"For the data track (M8-1)"** (the exact block the Shadow `faction.json` will carry) and "For the view
  (M8-1)" (nothing new is needed). Cost alone: forest brawl 1.21 ms a tick, 0 bytes; 2,000 hidden units 0.73 ms.
- **Six unit types are figures (M6-V21)**, the clips follow the game speed, the Heavy Infantry and the Raider swing a
  sword clip, the Archer keeps the one-handed shot (no bow clip in UAL1 / UAL2). **The Producer's bench, alone (load
  3-4 %, no other test host): 400 figures 10.11 ms while 90 %+ alive (worst 1 % 14.52; limit 16.7), 400 capsules
  0.77 ms; 1,000 figures 26.87 ms (worst 1 % 32.79), 1,000 capsules 1.29 ms; 4,542 / 12,219 draw calls.** docs/03
  "Implementation (M6-V21)" still prints the developer's figures (11.26 / 28.17, two other test hosts running): the
  view's next task replaces them with these.
- **The Teblor AI's second pass shipped (D32, variant X7, `ai.json` alone).** Data hash and golden header
  **5353DF8A1473FC6D** (was 27036D3A2F2BA1CB); the golden's 15 `k` lines unchanged; save format 10. Every save and
  recording from before is refused `DataMismatch` (fixtures in `Content/Fixtures/pre_d32/`). 64 Normal matches: the
  target on 8 of 8 seeds, defeated 10 (was 14), a Shaman in 27 (was 4), a second Longhouse in 18 (was 0), Age II by
  10:00 in 55, a wave in 62. **The price (BUG-1420 S3): Breakers, Destrier Riders and War-dogs are fielded in 0 of 64
  on Normal (as before) and are now gone on Easy (Breakers 8 -> 0) and Hard (Breakers 22 -> 0, Riders 18 -> 5, dogs
  8 -> 0, the Anointing 43 -> 19)**; Hard is beaten less (18 -> 10). The "64 matches" are 34 distinct ones on Normal /
  Hard (the named maps barely vary by seed).
- **The Frenzy report on D32's opening** (`FRENZY_REPORT_OPENING=Content/Fixtures/teblor_standard_d32.json`, 32
  matches on the menu's map): a Shaman in 5, 8 casts sent / 8 landed, defeated 10. On the 64-match row 22 casts
  landed in 18 matches. So a fielded Shaman does cast with today's rule; Request 53's second half is a reading, not
  yet a rule. docs/03 "Implementation (M7-7)"'s table is D31's opening (a Producer note under it says so). The
  report's "army pop 15:00 (sum)" reads 0 for both openings with 19 undefeated matches: nobody has looked at why.
- **Why the expansion waited (D32):** gold, not list order: the bank held the Longhouse's cost in 36 of 22,926 thinks,
  and "base defended" was true in 14,088 thinks, which switches the reserve off. Two Requests for the sim track are
  in STATE (54: count only a real raid as "defended"; 55: a `train` step that may spend through an earlier step's
  reserve).
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's list).
- **After integration, at the PLAN:** set BUG-1391 to fixed (items 1, 2, 4 by D32; item 3 by M8-1: the Producer could
  not edit it in the sim worktree without a conflict with the data's edit).

## Standing rules (1-65)


Unchanged 1-51 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle **and with no
non-studio load on the machine** (QA says what else was running); 43: every sim task that can change Normal AI behaviour names which
M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table in the same commit, and a row that stops
ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on a scratch clone and integrated first, a new
task starts on a fresh branch; 45: a brief whose task changes Normal AI behaviour says what to do when a pinned criterion row stops
ending; 46: a branch held twice gets a terminal acceptance rule in its third brief, so no branch lives a fourth session; 47: QA's
re-check runs the default loop on the commit that includes QA's own suites, and a QA file that measures allocation declares
`[Collection(SerialCollection.Name)]`; 48: at PLAN the Producer checks every acceptance criterion against the brief's own out-of-scope
list; a figure the scope cannot move is "measure and report", never pass / fail; 49: no test pins the golden replay's header
`data-hash` as a literal (`GatherWedgeQaTests.SameGameDataHashes` is the model); 50: a sim task that changes a save or replay format
names the view scenes that pin those bytes and the view track re-checks them; 51: a data brief that re-pins sim-owned digests lists
the sim test folders and pin classes and says the `Stress` pin classes must be run; 52: a developer's report ends every test process
the developer started; a `testhost` or Godot left running in a worktree after the report is a finding; 53: the QA tier bounds the
suites QA runs: **light** = the task's filters + the default loop (`Category!=Perf&Category!=Stress&Category!=Soak`), never the full
suite; **standard** = the default loop + the Stress rows the task touches + the scene loop (view); **full** = the default loop with
Stress, then Perf alone under rule 42).
**54:** before a task adds a faction, a data type or a default that every test reads, the Producer applies it on a scratch clone
and runs the default loop and the scene loop at PLAN; "unblocked" is said only after that. **55:** that rehearsal uses a
copy that exercises every hook the new content will use (a passive, a scale, a pack, an aura, a new radius), or the content
branch itself when it exists; a Malazan copy alone missed 28 rows this session.
**56:** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
**57:** a QA regression scene or test that is red by an open S3 reaches `main` only by an explicit ruling at ACCEPT
(named in the session log and STATE's Tests row), with the fix as the first item of that track's next brief; until then
every loop's expected result names that one row. Two sessions red = the fix becomes the whole task.
**58:** a test helper that forges a data row must tolerate the shipped data already carrying it (look the id up,
then add or edit), and when docs/03 tells the data track which rows to write, the sim's QA applies them on a scratch
clone and runs the task's classes (BUG-1280).
**59:** a developer who writes a fix for "the data track's rows break our tests" runs its own new tests on a copy
carrying those rows before reporting (M7-6's fix broke on exactly the rows it was written for: BUG-1310 / 1311, one fix
round). A sim test about "every shipped row" reads the count from the shipped file, never a literal.
**60 (lifted 2026-11-13-2306):** BUG-1321 is closed for 400 figures (10.1 ms alone); model types may be imported again.
**61:** before more than ten unit types are models, a view task measures a 1,000-figure crowd of mixed types alone
(rule 42) and reports what 16.7 ms would take (merged surfaces, a shared skeleton or baked animation); every art brief
reports the 400- and 1,000-figure bench with the capsule figure from the same window and reads the "while 90%+ alive"
column. An LFS size criterion names "the art pass's binaries", never "total LFS".
**62:** a developer runs the full default loop after its last edit, or its report says in its first lines that
it did not (two sessions running a developer fixed one row and re-ran only the affected classes).
**63:** a scratch folder's name starts with its track and task (`m7v4-...`, `d31-...`), is created new, and no
agent writes into, cleans or resets a folder it did not create (2026-11-14-0616: the view developer reset another
agent's clone named `clone`; nothing committed was lost). A brief that asks for a scratch clone says so.
**64:** "build 0 / 0" in a report means `dotnet build RtsGame.sln --no-incremental` after the last edit (an
incremental build hid two warnings in a new test file: BUG-1380, one fix round).
**65 (new):** QA's re-check ends with `dotnet build RtsGame.sln --no-incremental` on the commit that holds QA's own
files, and its report says so (2026-11-14-1246: QA's new `QaM6V21Test` carried a CS8600 warning that the developer's
0 / 0 could not have seen: BUG-1412). A warning in a QA file with the developer's commit clean is S3, fixed first in
that track's next task; in the developer's own files it stays S2 (BUG-1380).
**61 (amended 2026-11-14-1246):** the measurement is done (M6-V21: 1,000 mixed figures 26.9 ms alone; merged surfaces
are worth about 4-5 ms, a shared skeleton or baked animation up to about 8 ms, both together about 14-15 ms). The owner
asked for the Teblor figures first (inbox 2026-11-14), so the checkpoint moves: **merged surfaces are the view task
right after the Teblor humanoids**, and every art brief still reports the 400- and 1,000-figure bench alone.
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.
(`SameGameDataHashes` is the name of the rule in that class's comments, not a test method: do not write it as a
criterion's test name again.)

## Watch-outs (all tracks)
- **Three factions ship now.** A fixture that means "the two original factions" uses `TestSim.TwoFactionData`,
  `TestDataDir.CopyOfShippedTwoFactions()` / `CutToFirstFactions(2)` (sim) or `ThirdFactionForge` (view, cut to two).
  A pin about "every shipped unit" must be true for the Teblor too (a bite, a pack, a scale, a self-cast aura, radius
  0.75, regeneration).
- **Radius 0.75 is a fourth radius class** (+131,072 bytes on a 1024 map; the shipped-data memory row allows it by
  class count). A single-type 0.75 m crowd arrives about 40% less often than a 0.4 m one on the crowd sweep's maps
  (`Stress/WideCrowdStressTests`, a report with no bound): 500 to 4 points 172.6 v 288.1 mean arrivals, 128 to 64 goals
  58.4 v 112.1. If a sim task changes movement, re-run it and print both columns.
- **The scene loop is all green: 105 scenes** (QA's `QaM6V21Test` is new; `QaM6V21Shots` and `ModelShots` are windowed tools, not in the loop). `QaM6V20Test`
  samples after a forced collection (objects flat at 3,252); BUG-1350 is fixed by cause, so an ERROR line there is a
  finding. Rows that flake under load are listed below; any other red row is a finding.
- **Model animation is manual:** every model's `AnimationPlayer` is in manual mode and `UnitViews._Process` advances it
  through `CrowdAnimator` (48 nearest every frame, the rest 1 frame in 3). A scene that calls `Sync` directly and wants
  motion calls `AdvanceAnimations(delta, eye)` too. A model shows no worker tint (`ShownTint` still records the state).
  Since M6-V21 the figures follow the game speed (`UnitViews.AnimationRate`: the runner's speed, 0 while the tree is
  paused); a replay that has reached its end is not paused, so its figures keep moving in place (a small view item).
  Each model type has its own `AnimationLibrary` (state names -> the shared clips, from the clip map in the `.glb`'s
  glTF extras); a slot's type and model are set before the fog check.
- **Six unit types are models** (Laborer, Crossbowman, Heavy Infantry; Camp Follower, Raider, Desert Archer); 15 of the
  two first factions' types and all seven Teblor types are capsules. A scene that reads a slot's own mesh, overlay or
  material for a model type reads nothing that is drawn (BUG-1410: `QaV5Test` line 203): read `ModelMeshOf(slot) ??
  ViewOf(slot)`.
- **Unit models are on by default** (`UnitViews.UseModels`; `--placeholder-art` forces capsules). Five scenes pin capsules
  and set it false: `EconomyViewTest`, `QaM6V3Test`, `QaM7V3cTest`, `ScaleViewTest`, `ShellTest`. A new scene that reads a
  slot node's own mesh or material must do the same or read `ModelMeshOf`. The model files are Git LFS: a clone needs
  `git lfs pull` then an import (a pointer file gives the capsule and one warning, never an `ERROR`).
- **`tools/import-assets.ps1` needs Blender 5.2 and `asset-sources/` (the main checkout's)**; its outputs are committed and
  a re-run is byte-identical (QA, fresh clone, 180 s). `tools/asset-manifest.json`, `tools/import-assets.ps1` and
  `tools/art/**` are the view track's from now on (the rest of `tools/**` stays the sim's): say so in each brief.
- **The Teblor rows are on `main`.** A Frenzy status id sorts after `stealthed`, and `teblor_frenzied` is the last row
  of `common/statuses.json`: a brief that adds a status says where it sorts (`StatusLoaderTests.Shipped_Regenerating_*`
  pins the sorted order on purpose). The two golden data-hash pins
  (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`)
  are sim-owned: a data brief that moves the hash is handed them by name (rule 51).
- **Two tracks must not both move the data hash in one session** (each regenerates the golden's header and checksum:
  a certain conflict). A sim task that ships a value in `game/data/common/**` and a data task that ships a faction
  file are planned in different sessions, or one of them lands test data only.
- **A red Stress row on `main`, by ruling (rule 57): `Content/TeblorLandingOpeningReportTests.TheRows_ChangeTheMatch_FromTheFirstFrenzyOn_Report`**
  (BUG-1400, red since D31; the default loop does not run it). First item of the data's next brief; until then a
  "default loop with Stress" run expects exactly this one failure.
- **`main` builds 0 errors / 1 warning** until BUG-1412 is fixed (`game/tests/QaM6V21Test.cs` line 257, `string?`):
  first item of the view's next brief.
- **Every data change moves the golden header now** (the Teblor use `damageBonus`, `onEnd`, `statusDuration`, a
  building `scale`, `carry` and a per-opening share): batch data changes into one hash move a session, and say in the
  report that older saves and recordings stop loading.
- **`QaM7V3aTest` fails on purpose at six shipped factions** ("no room for three clones under 8"). `ThreeFactionTest` and
  `RegenCueTest` never show the real Teblor (the forge cuts them away): M7-V3c adds a scene that does.
- **`tools/qa/third-faction-copy.ps1`'s display name "<id> (Malazan copy)" trips BUG-1230** at 1152 x 648 with five
  factions (5 setup scenes red from name width): use short ids or expect them.
- **A QA scratch scene left a data copy** at `%APPDATA%\Godot\app_userdata\RtsGame\qa-teblor-shot\` (could not be
  deleted: permissions). Harmless. Scratch scenes should write under the scratchpad, not `user://`.
- **A dead session loses the developers' reports and bug patches** (they live in the conversation / scratchpad). When a session
  is resumed, QA writes the proof lines and the Producer applies them at ACCEPT.
- **Three test hosts at once** flake the wall-clock Perf and scene rows (rules 42 / 53). The default loop is ~25-30 min alone,
  ~45-90 min beside other suites. **BUG-1170 is closed as machine state:** a Perf run is evidence only when `ASweepRetarget`
  reads near 16 us (85 us = the PC's slow state; do not chase it).
- Scene rows that flake under load and pass alone: `DebugOverlayTest` and `QaM25Test` (1 ms rows), `MinimapTest` (0.5 ms),
  `BenchTest` (7 s), `QaH1Test` (600 s timeout).
- Merge conflicts to expect: `studio/bugs/README.md`, `studio/qa/coverage.md` (both sides kept), docs/03 when two tracks
  append an "Implementation" section (keep both, the sim's first).
- A developer must never kill another track's Godot or `testhost`; a developer ends its own (rule 52). **The developer cannot
  write `studio/**`:** bug-file updates come as a patch in the scratchpad; the Producer applies it.
- `tools/qa/third-faction-copy.ps1 -Repo <scratch clone> [-Id <id>]` makes a Malazan copy; a Whirlwind-shaped copy is made
  by hand (`Stress/M7H1KillCounterQaStressTests.AddWhirlwindCopy` is the recipe); a Teblor-shaped one is the `teblor`
  folder under another id (QA's `barghast`).

- **Pinned matches on `main` after M7-3b (outcomes unchanged, digests re-measured):** the Hard v Normal tally **32 / 8 / 2** (Raraku
  10 / 4 / 0, Pale Hills 11 / 1 / 2, Vathar 11 / 3 / 0); `StandardPresetPinQaTests` (8 save scenes + 7 AI rows), `NonHardMatchPinQaTests`
  8, the 8 `CounterCompositionTests.TheNonHardPins_*`, `ReserveTwinTests` (2 rich rows), `SharedVisionFfaPinTests` (2): all re-pinned
  with the old value in a comment; **QA's `OutcomePinM73bQaTests` (Stress, 20 id-free fingerprints equal to de591f0's)** is the
  outcome pin that must hold through any later renumbering; `HardKnobsPinTests` / `HardKnobsMatchTests`, `ScoutManagerTests` (311),
  `GatherBonusTests` (169 / 191), the premap AI replay (81 commands; its Research ids moved 4 → 3 and 7 → 5 by key);
  `MatchCriterionTests` (Vathar 23,322 / Raraku 28,231 / Pale Hills 50,140 / generated 17,053 / FFA 56,896 / 2v2 26,070);
  `MapSlice2StressTests` (49,565 / 55,485); `D21OpeningsQaTests` / `DH6ThresholdQaTests` (outcomes only); `HuntStressTests`
  (75,382 / 53,892 / 64,226); the Teblor-copy FFA hash 6C448DFB90134A32 (`FactionOrderQaTests`), the four `FactionOrderTests` copy
  pins (Stress).
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), `FarExpandProbeQaStressTests` Pale Hills FFA
  seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156), BUG-1161's
  three rows, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's
  `QaM7V1Test` zoom-40 1.5 head row; `DebugOverlayTest`'s 1 ms frame row flakes under three-track load (passes on a rerun).
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.
- **The third faction's order of work now:** the sim's gatherer-overlap change (the owner's request; it moves the
  economy, so every opening measurement is redone after it) -> data **D33** (BUG-1420: Breakers, Riders and War-dogs
  back in the army without losing the Shamans and the expansion; BUG-1421) as the start of the balance pass -> the
  sim reads the Frenzy report again (Request 53's second half) -> the Teblor models (the view's next task, the
  owner's request). M7 criterion 4 is ticked after the balance pass and the Teblor models; the end-of-M7 hardening
  follows it.

- **Session length:** 2026-11-13-1118 ran 9.5 hours, 2026-11-13-2306 about 6, 2026-11-14-0616 about 6, **2026-11-14-1246
  about 8** (no fix round; QA full on the sim beside two standard passes, every default loop 1 h 26 m to 2 h 12 m
  beside the others against about 21 m alone). A full-tier sim task beside two measuring tasks is too much for this
  PC: when the sim is on QA full, give one of the other tracks a light task.

---

## Sim track

### Where we are
M8-1 on `main`: forest stealth on test data. Counter **1 / 4**. Fixed: BUG-1370, BUG-1391 item 3. No new sim bug
(BUG-1400 was found by the sim's QA but is the data's file). Debt: BUG-1161's three alias names S4, BUG-1149 / 1151 /
1152 S3, BUG-1150 S4, BUG-1010; notes: `[0]` lookups left in `FrenzyRulesLoaderTests`, `StatusDurationTechTests`,
`DataContentHashTests`, QA's `M7H1QaTests`; a JSON `null` block loads as absent in `forestStealth` / `regen` but is an
error in `reserveArmyShare` (pick one policy at the next hardening); QA's first combined run had one failure that did
not recur and could not be named (watch for a load-sensitive row).

### Next task candidates
1. **The owner's request: gatherers may overlap a bit while collecting** (inbox 2026-11-14; feature, QA **full**:
   movement). At the PLAN:
   - Read `MovementSystem`'s separation and what a Gathering / Returning / drop-off-queuing worker counts as there
     (the note says idle units are shoved, holding and attacking ones are walls). Generic: any worker of any faction,
     only while gathering at a node or waiting / delivering at a drop-off; armies and marching units keep today's
     separation.
   - The amount is a data knob in `game/data/common/rules.json` (the sim's file), not a C# literal. Producer default
     to record in docs/01 and STATE: the share of the separation push kept between two such workers (propose 0.5;
     1 = today). Decide at the PLAN whether the shipped value changes this session (then: one hash move, the golden
     regenerated with the reason, older saves refused once more, and **the data track gets no hash move that
     session**) or the schema lands on test data first.
   - **It moves the economy, so it moves every AI match.** Rules 41 / 43 / 45: name the M5-5 rows, the Hard v Normal
     tally, the opening pins and the content report rows expected to move; re-measure the bounds and docs/03's tables
     in the same commit; a row that stops ending is a finding. Size it as uncertain work (about 800 lines; QA's
     suites don't count). The golden replay tests re-run; say why if the `k` lines change.
2. **The owner's request: Shift-click queues orders, the sim half** (inbox 2026-11-14). Audit which unit orders append
   with the queue flag today (move, attack-move, attack, gather, repair, garrison, patrol if any, rally, abilities,
   build); close the gaps with the same flag, no new mechanic; expose the queued orders to the view read-only if
   `ViewApi` lacks them (that part may be the view's). The view then draws the queue. Tests per order kind; golden re-run.
3. **M8-2: the Shadow Archer's stand-still stealth and its +50 % first shot**, schema first, test data only (QA full).
4. **Requests 54 / 55** (from D32): "base defended" counts only a real raid; a `train` step may spend through an
   earlier step's reserve. Each changes Normal AI behaviour (rules 43 / 45): plan with a data task that re-measures.
5. **Request 53, part two:** read the Frenzy report on `main` (D32's opening is the shipped column now), re-print
   docs/03's table, look at why "army pop 15:00 (sum)" is 0; a cast rule only if a fielded Shaman standing with the
   army does not cast (today's figures say it does).
6. A hardening with no opening measurement beside it: BUG-1149 / 1151 / 1152, BUG-1161's names.

---

## View track

### Where we are
M6-V21 on `main`. Counter **2 / 4**. 105 scenes, all green. New: **BUG-1412 S3** (the build warning in `QaM6V21Test`,
filed by the Producer), **BUG-1410 S3** (`QaV5Test`'s flash check reads a node a model never draws on), **BUG-1411 S4**
(look nits: the sword clip's wind-up reads as a T-pose up close; Ochre / Bone players tint the Whirlwind figures one
tone; soldier types carry unused worker clips). Older: BUG-1381 S4, BUG-1177 item 2 (the owner's taste), BUG-1115
item 4. What reads badly on the pictures (the developer, QA and the Producer agree): the Camp Follower in a player's
colour is nearly the Laborer; the Heavy Infantry's box helmet is a pale block and the shield is edge-on from the
side; the Archer's bow hangs level at the hip; the Raider's blade is a thin line at the default zoom; player 1's
slate is close to the grey Malazan outfits.

### Next task candidates
1. **The owner's request: the Teblor humanoids first** (inbox 2026-11-14; feature, QA standard). **First, small:
   BUG-1412** (`string? why` at `QaM6V21Test.cs` line 257; the build is 0 / 0 again), **BUG-1410** (`QaV5Test` reads
   `ModelMeshOf(slot) ?? view`; add it to docs/03's list), and docs/03 "Implementation (M6-V21)"'s bench table replaced
   by the Producer's alone figures (above). Then the Gatherer, Warrior, Javelin Thrower, Shaman and Breaker: the
   Universal Base Character at the data's `scale` 1.5 on a 0.75 m radius, the two outfits recoloured to the Teblor
   palette (docs/04 "Style guide"), props as boxes (a javelin, a great club or blade, the Shaman's staff), team colour
   on one `team` surface, clip maps (the Gatherer the worker clips, the Javelin Thrower a throw if the libraries hold
   one). If five is too many for one slice, the Gatherer, Warrior and Javelin Thrower first. The Destrier Rider and the
   War-dog stay capsules (no horse or dog pack on disk: the owner's download, already under Waiting on you).
   `ModelViewTest` per type; pictures on the shipped data beside a Malazan soldier (rule 56: the real 0.75 m giants);
   the 400- and 1,000-figure bench alone. `tools/asset-manifest.json`, `tools/import-assets.ps1` and `tools/art/**`
   are the view's. Rules 62 / 64 / 65.
2. **Merged surfaces** (rule 61 as amended: the task right after the Teblor humanoids): one material and one atlas a
   model in the import tooling, every model re-generated; target 1,000 figures measured alone, reported against 16.7 ms.
3. **The owner's request: the Shift-queue made visible** (a line or markers per queued order for the selection),
   after the sim's audit says what queues; check first what the view already draws.
4. Small: figures at a finished replay keep moving in place (`AnimationRate` is 0 only under a paused tree); BUG-1411
   item 1 (try UAL2 `Sword_Regular_A/B/C`).
5. The view's part of M8 criterion 1 (stealth visuals for a forest-hidden unit) once a roster ships the block.
6. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Where we are
D32 on `main`: the Teblor AI's second pass. Counter **2 / 4**. Fixed: BUG-1390, BUG-1391 items 1, 2, 4. New:
**BUG-1420 S3** (Breakers, Riders and War-dogs off the field), **BUG-1421 S4** (five small items), and **BUG-1400 S3**
(a Stress report row red since D31, found by the sim's QA: the data's file). Open: BUG-1360 S4.

### Next task candidates
1. **BUG-1400 first, whatever the task** (rule 57): `Content/TeblorLandingOpeningReportTests.TheRows_ChangeTheMatch_FromTheFirstFrenzyOn_Report`
   compares "without the rows" against "with the rows", and the shipped data carries the rows since D31: re-think it
   against a copy with the rows removed, or retire it with the reason.
2. **If the sim ships the gatherer-overlap value next session (a hash move and a changed economy): a light task with
   no hash move.** BUG-1421 items 3-4 (pin or date-label the page's measured figures; the two unwrapped lines; the
   192 KB fixture's size and name), BUG-1360; or **the Shadow page made pin-ready** (as D-R36 did for the Teblor:
   `docs/factions/shadow.md` cells checked against docs/02 and the schemas that exist, the open questions listed for
   the Producer). No opening is measured beside a sim task that moves the economy (rule 41).
3. **D33: Breakers, Destrier Riders and War-dogs back in the Teblor army** (BUG-1420; BUG-1421 items 1, 2, 5), the
   start of the balance pass against Malazan and Whirlwind, **on the economy as it is after the sim's gather change**.
   Measure Easy, Normal and Hard this time (the D32 brief measured Normal only: the Producer's miss), the mirror too,
   and count distinct matches (the named maps give 1-3 outcomes over 8 seeds). Keep the Shamans (27) and the second
   Longhouse (18); the tooltip follows what most matches do. One hash move. The For-your-review table as always.
4. The owner's answers if any land ("teblor ai: early raid", the Teblor table, "teblor buildings: 1.5", "opening
   text: ...", "Frenzy text: ...", the counters, `goldShare`, BUG-0780, `counterStrength`).
