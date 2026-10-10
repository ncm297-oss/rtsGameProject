# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-10-1314** (fourth full session of 2026-10-10, cap 8; base
`093329a` = `origin/main` after the 1015 integration). Three tracks GO. Checks at PLAN: inbox empty; build 0 warnings;
non-Perf suite run by the Producer alone on `main`: **4,794 / 8 skipped / 0 failed (13 m 38 s)**; claim verified (`StatusKind.Frenzied` = 3 asserted in
`StatusLoaderTests`, the Zealot's two passives in `whirlwind/units.json`, D22C849A175F9ED5 in `SameGameDataHashes` and the
golden); no open S1 / S2; counters sim 1 / 4, view 1 / 4, data 0 / 4 → feature sessions.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** This session closes criterion 6 (summons are the last owed piece; the Frenzied marker and the
  `selfAura` card on the view side) and ticks criterion 8 (BUG-0421). Then 7 stealth (M4-5 sim + visuals), 10 the fog-on
  sandbox Playable (view M4-V7, started this session), the end-of-M4 hardening per track, sign-off.
- **Producer decision (this PLAN, owner may revisit): `targetUnit` and `autocast` are deferred** until a faction's data
  needs them. Criterion 6 reads "target ground, self/aura, summon"; docs/02 lists target-unit as "reserved for later
  heroes" and no faction page (M7-M9 included) flags an ability `autocast`; the loader keeps refusing both ("not supported
  yet"), so content cannot drift onto them. The AI (M5) casts through `UseAbility` like the player and needs no autocast.
  Recorded at ACCEPT in the roadmap note, docs/01 and For your review.
- **Golden:** `data-hash` is **D22C849A175F9ED5**. **D11 moves it this session** (the Frenzied / Blinded text); the sim's
  M4-4b-3b must leave it where it is: no `game/data/**` edit, and `GameData.ContentHash` extended so a new field counts
  only where an entry uses it (shipped data has none). Rule 3 for the data track: the new hash into `SameGameDataHashes`
  in the same commit.
- **Bug ids this session:** sim from **BUG-0450**, view from **BUG-0460**, data from **BUG-0470**. Say so in every QA brief.
- Memory bound 231,920,000 (72 KB margin, itemised through M4-4b-3a). `EmptyTick(2500)` ~481 µs of 500: a summon store
  must add no per-tick work while no summoned unit is live (the `BelowHpUnits` counter pattern).
- Standing rules unchanged: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full
  suite incl. Perf once alone and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the
  `ui.json` `states.*` line itself; (3) whoever moves `data-hash` adds the new hash to `SameGameDataHashes` in the same
  commit when the seed-21 match is unaffected; (4) when the view needs a `ui.json` key the brief says so and the sim stays
  off the file; (5) when a sim brief changes a behaviour a view QA scene asserts on, the same session's view brief carries a
  named item for that scene (none this session: no view scene researches an hp tech or summons); (6) a sim task that adds a
  `Command` kind adds it to `CommandDoorFuzzStressTests` (none added this session).

## Sim track

### Current session plan: M4-4b-3b · feature · QA full · budget ~1,200 lines (QA included)

**Goal.** Make hp upgrades real (BUG-0421: Dryjhna's Prophecy's +20 HP never touched a unit's hit points, so the Zealot's
frenzy line sits at 65 % of its real health) and ship summons, the last owed piece of M4 criterion 6: a `summon` ability
kind with a `spawn` effect that puts temporary, free units beside the caster for a lifetime, then takes them away.

**Scope.**
1. **BUG-0421 first (S3).** `UnitStore.TrySpawn` sets `Hp = def.Hp + Points(Techs.Bonus(owner, type, TechStat.Hp))` for
   every spawn path. When an hp tech lands (`ProductionSystem` line ~156, `Techs.Set(..., true)`), every live unit of each
   affected type owned by that player gains the delta to its current hp at once (Producer default, recorded in BUG-0421:
   max and current move together; a dead or dying unit is not touched). `PassiveSystem.MaxHp` stays. Any view-side max hp
   reader (`UnitDef.Hp` for hp bars) is a view concern: name it in docs/03 "For the view" (`Units.MaxHp(slot)` or the same
   `Points(Bonus)` formula; add the read-only accessor if cheap).
2. **Fold BUG-0420 (S3)** since `PassiveSystem` is touched: the below-the-line test becomes an exact integer compare (e.g.
   `hp * 1000 < fractionPerMille * maxHp` with the per-mille computed at load, or an equivalent that makes 0.15 x 100
   exactly 15 not below). Un-skip QA's BUG-0420 row.
3. **Summons.** `"kind": "summon"` (`AbilityKind.Summon` = 2, out of `PlannedAbilityKindIds`): no target and no `range`
   (as `selfAura`: `range` is a load error, the command's point must be on the map and is ignored, the caster never walks,
   casts at once, resolves at the caster's position; `radius` optional, 0 when absent); it must carry at least one `spawn`
   effect; `spawn` (`AbilityEffectKind.Spawn`, out of `PlannedAbilityEffectKindIds`) is accepted on `summon` only:
   `{ "kind": "spawn", "unit": <own-faction unit id>, "count": 1..DataLimits.MaxSummonCount (8), "lifetime": <s, at
   least a tick> }`; errors at `abilities[k].effects[j].<field>`. At the resolve, `count` units of the type spawn for the
   caster's owner at free cells round the resolve point (`FreeCellSearch`, the production spawn's ring search: same
   plateau, nearest first, slot / file order), at full hp (incl. the owner's hp bonus), with no cost and no population
   (never counted in `HalfPop`; a full unit store or no free cell drops the rest, documented). Each carries
   `UnitStore.ExpiresAtTick` (0 = permanent; hashed) and a `Summoned` flag (derived or hashed: pick, document; M4-5
   reads it: "can't be stealthed"). **Expiry** (choose the phase, document it; phase 5 beside the status countdown is the
   natural one): a summoned unit whose tick has come is freed through the death path with a `DeathEvent` whose new
   `Expired` flag is true (killer owner -1): no Loss, no Kill, no `onDeath` passive, its statuses and target gone like any
   death. A summoned unit killed before its time is an ordinary death. Otherwise summoned units are ordinary: they move,
   fight, take statuses, are targets, count for Martyrdom's radius if listed. Cost: a `SummonedUnits` counter; the expiry
   pass returns at once at 0; 0 B. Test-only abilities only (`SelfAuraTests.AddTo` pattern; summon an existing unit type,
   e.g. a Raider, since no Wraith exists in data); shadow.md's Summon Wraiths (3 units, 20 s, 1.0 s cast, 50 s cooldown)
   is the design reference, **not** shipped (Shadow is M8).
4. Docs: docs/03 "Implementation (M4-4b-3b)" (the hp rule, the summon kind, the spawn effect, placement, expiry, the
   hash, the cost, known limits), "For the data track (M4-4b-3b)" with the exact JSON shapes and what is refused, "For the
   view (M4-V7)" (`AbilityKind.Summon` = a no-target card like `SelfAura`; `DeathEvent.Expired`; `Units.ExpiresAtTick`;
   the max-hp read); docs/02 one sentence under techs or units: an hp upgrade raises max and current hit points of living
   units by the same amount; docs/01 change-log row (the hp rule; `targetUnit` / `autocast` deferred); the BUG-0421 and
   BUG-0420 files `fixed` with the proving tests.
**Out of scope:** `targetUnit`, `autocast` (deferred, see above); stealth (M4-5); any `game/data/**` edit (D11 owns
every data file this session: `common/statuses.json`, `factions/**`); `AbilityViews`; a new `Command` kind; replay format.

**Acceptance criteria.**
1. `Abilities/HpTechTests` (new; the regression fails first): a Zealot at 63 / 63 whose owner researches Prophecy reads
   83 / 83 next tick; one at 41 / 63 reads 61 / 83 and is **not** Frenzied; a Zealot spawned after the tech reads 83;
   another player's Zealots and the owner's Raiders are untouched; a unit hit to 0 on the landing tick is dead, not healed;
   `PassiveQaTests.ProphecyResearchedMidFight_*` re-stated to the real rule (say so in the report; QA re-checks it).
2. BUG-0420: `PassiveTests` rows for 0.15 x 100 (15 is not below, 14 is) and 0.3 x 63 (18 is below 18.9, 19 is not); QA's
   skipped BUG-0420 row un-skipped and green.
3. `Data/AbilityLoaderTests`: `summon` loads; `range` on it fails; a `summon` without a `spawn` fails; `spawn` on a
   `targetGround` / `selfAura` fails; `unit` of another faction, unknown, `count` 0 / 9 / non-integer, `lifetime` 0 / missing /
   below a tick each fail at their path; "not supported yet" gone for `summon` and `spawn`.
4. `Abilities/SummonTests`: 3 units appear at the resolve (not at the cast start) round the caster on free cells, owner =
   caster's, hp full incl. an hp bonus, `HalfPop` unchanged, gold / wood unchanged; they expire exactly `lifetime` ticks after
   the resolve (one `DeathEvent` each, `Expired` true, no Loss / Kill, no Martyrdom from an expiring listed type); one killed
   early is a normal death (Loss counted) and does not expire again; a full store spawns what fits; no free cell within the
   search drops the rest; the caster dying mid-cast summons nothing; a queued summon pops like a `selfAura`.
5. `StateHashTests`: `ExpiresAtTick` changes the hash; a `Summoned` flag classified (hashed or derived) in a row;
   `DataContentHashTests`: every `spawn` field and the `summon` kind move `ContentHash` for an in-memory ability; **the
   shipped `data-hash` stays D22C849A175F9ED5** (`ReplayGoldenTests` green with no golden edit).
6. `Stress/SummonFuzzStressTests`: 3 seeds x 2,000 ticks, 3 players, summoners casting on cooldown among real combat,
   twins equal every tick + a replay round trip; the per-tick invariant: every live summoned unit has `ExpiresAtTick >
   TickNumber`, and `SummonedUnits` equals the count of live flagged slots.
7. Perf alone: `EmptyTick(2500)` within 500 µs and not above the base by more than noise; a 500-unit brawl with 60 summons
   live and expiring 0 B a tick (a `PassivePerfTests`-style row); `World_1024Map_CacheStays32_MemoryBounded` re-baselined
   only if needed, every byte itemised in its comment.
8. Full suite incl. Perf green once alone (rule 1: a data-loading and combat rule changed), `CommandDoorFuzz` untouched and
   green, smoke PASS, the merged scene loop green, 0 warnings.
9. Docs as in scope item 4; BUG-0420 / 0421 files `fixed`.

**Design references.** docs/02 "Ability system" (the kinds table: Summon "Spawns temporary units"; the effect vocabulary
`spawn`), "Status effects" stacking; docs/factions/shadow.md lines 40, 54, 61 (the Wraith: temporary, 20 s, no cost or pop,
can't be stealthed; Summon Wraiths 3 beside the caster); docs/factions/whirlwind.md line 91 (Prophecy: Zealot +20 HP);
docs/03 "Implementation (M4-4b-3a)" (`selfAura` casting rules to mirror; `PassiveSystem` cost pattern; the hash rules),
"Implementation (M3-5)" (`TechBonus`, `Points`); BUG-0421 / BUG-0420 files.

**Constraints.** No Godot; no new NuGet; no `System.Random`; placement in slot / file order (never a dictionary); no
allocation per tick (the expiry pass and the spawn loop into existing scratch); every number in data or a named, documented
mechanism constant; `Rts.Sim` warnings as errors; **no `game/data/**` edit** (the golden collision); no new `Command` kind.

## View track

### Current session plan: M4-V6d + M4-V7 (first slice) · feature · QA standard · budget ~1,400 lines (QA included)

**Goal.** Finish the view side of criterion 6 (the Frenzied marker, the `selfAura` card) and start criterion 10, the
fog-on sandbox Playable: a scripted Malazan-vs-Whirlwind fight through the real HUD with fog on, the way `M3PlayableTest`
proved M3.

**Scope.**
1. **M4-V6d.** `AbilityViews`' `StatusKind` switch gets a `Frenzied` arm with its own colour (`FrenzyColor`, apart from
   orange Burning / deep-blue Slowed / violet Blinded on sand and on both factions' bodies at zoom 30; a windowed shot
   looked at). The **`selfAura` card**: `AbilityDef.Kind == AbilityKind.SelfAura` → the button (or hotkey) casts at once
   with no targeting state, sending the caster's position; while hovering the button the radius ring is drawn round the
   caster (nearest ready caster rule unchanged); Shift queues it. A headless `AbilityViewTest` row with a test-only
   `selfAura` ability (in-memory data, the `SelfAuraTests.AddTo` pattern; Requests 30). `AbilityKind.Summon` lands on
   `main` with the sim this session: **do not reference it** (not on your base); next session adds its arm (same card).
2. **M4-V7, slice 1: `game/tests/M4PlayableTest.tscn` + `.cs`** after `M3PlayableTest`'s pattern (real `Match`, shipped
   data, fog **on**, 8x, every action injected input, each step checked against the sim, the whole command stream replayed
   into a bare twin at the end). Stage with the dev spawn commands (`SpawnUnit` / `SpawnBuilding` ignore requirements):
   both sides a Town Hall + a few buildings on their start plateaus and an Age-II-style army (Malazan: Heavy Infantry,
   Crossbowmen, a Cadre Mage, a Sapper, a Catapult; Whirlwind: Raiders, Desert Archers, Lancers, a Priest, Zealots). Script
   at least: (a) the map starts black past own sight and the enemy army is **not** drawn until seen (`Check` through
   `Fog.CanSeeUnit` per visible view); (b) an attack-move with the Malazan army toward the enemy base through the real HUD
   (A + click), first contact seen, kills / losses move; (c) a Telas Fire cast on a Whirlwind blob (Q + click) and its
   Burning markers appear; (d) a Sandstorm cast by the Priest (the enemy side, so stage it as the player's own on seed 2,
   or make the scene play both seats in two runs: pick, say); (e) a Cusser on a building, rubble shown; (f) a Zealot under
   half hp shows the Frenzied marker (your V6d colour) and, with the sim's `Frenzied` on `main`, swings faster (count swings
   in a 60-tick window vs an unhurt Zealot); (g) ends with `M4 PLAYABLE TEST PASS` / `FAIL <step>: <what>` and exit code,
   `-- --break <n>` as M3's. Seeds 1 and 6 (or 1 and 2 if the two seats need it). Windowed `-- --shots <dir>` saves 4-6
   stills (first contact under fog, the storm, the Cusser rubble, the Frenzied Zealot) for the owner. Keep the tick budget
   fixed and print the ticks per step. If the budget runs out, land V6d + steps (a) (b) (c) (g) and list the rest for slice 2.
3. **Watch BUG-0430** in the sandbox stills: if a storm across a cliff looks broken in the fight, say so (pulled into the
   end-of-M4 hardening or forward, Producer's call at ACCEPT).
**Out of scope:** stealth visuals (M4-5), the minimap zone dots, BUG-0430 / 0431 fixes, summons on screen, any
`game/data/**` edit, any `sim/` edit outside `ViewApi/` (read-only accessors only; if the scene needs a sim seam, write it
under Requests for the sim track and work round it).

**Acceptance criteria.**
1. `AbilityViews.StatusColor(frenzied) == FrenzyColor`, channel gap ≥ 0.4 to each other marker colour (a unit test row);
   a windowed shot at zoom 30 with all four markers side by side on a Raider and a Heavy, looked at by the dev and QA.
2. `AbilityViewTest` rows (headless, seeds 1 and 6): a `selfAura` button click sends exactly one `UseAbility` with the
   caster's position and no targeting state is entered; hovering shows the ring round the caster; Shift queues one; the
   resolve flash lands at the caster; 0 B per frame.
3. `M4PlayableTest.tscn` PASS on both seeds headless, every step printing its tick; the end-of-run twin replay hash equals
   the match's; the FAIL path proven with `--break`.
4. The fog rule holds in the scene: no enemy unit view is visible while `Fog.CanSeeUnit` is false for it, checked every
   frame of the approach (a counter printed).
5. Scene loop green (the new scene added to it), smoke PASS, 0 warnings, no new string literal in `AbilityViews` / the HUD
   (text through `ui.json` keys that already exist; a new key needed → say so, the sim is off `ui.json` this session so the
   view may add it).
6. docs/03 "Implementation (M4-V6d)" and "(M4-V7)" paragraphs; the sandbox's stills listed for the owner.

**Design references.** docs/05 M4 criteria 6 and 10; docs/02 "Ability system" (casting: nearest caster, Shift), "Status
effects"; docs/03 "For the view (M4-V6d)" and "Implementation (M4-V6a/b/c)"; `M3PlayableTest.cs` (the pattern and its
`TickBudget` note); Requests 30.

**Constraints.** Dumb views (rules in `ViewApi`, nodes only draw); read-only on the sim (hash twins prove it); C# only;
`.tscn` text; no `game/data/**` edit; input injected through the viewport, never a controller call; look at the stills.

## Data track

### Current session plan: D11 · feature · QA light · budget ~600 lines · inbox tweaks first if any (none at PLAN)

**Goal.** Pin the content the sim landed in M4-4b-3a (the Frenzied status, the Zealot's two passives) to its design pages
both ways, so a drifting number fails a test by name, and ship the one wording the Producer proposed for Blinded.

**Files (yours alone this session):** `game/data/common/statuses.json` (`frenzied`, `blinded` `description` only),
`docs/factions/whirlwind.md` (lines 55-57, the Zealot passives; a Frenzied row if the page's status table needs one),
`docs/02-game-design.md` "Status effects" (the Frenzied Effect cell only), `sim/Rts.Sim.Tests/Content/**`,
`sim/Rts.Sim.Tests/QA/Content/**`, `GatherWedgeQaTests.SameGameDataHashes` (the named exception), the golden replay
file(s) regenerated once. The sim and view tracks touch none of these.

**Scope.**
1. Pin `UnitDef.Passives` both ways to whirlwind.md: Frenzy of the Apocalypse (`whenBelowHp` 0.5 → "below 50% HP",
   magnitude 0.5 → "+50% attack speed") and Martyrdom (`onDeath` 6 m / 5 s / +50 % / `["whirlwind_zealot"]`): the page
   must state the +50 % and "Zealots" for Martyrdom (add it: today lines 56-57 give 6 m and 5 s only; the strength is a
   Producer default the owner may revisit, say so in the review table). A page number drifting from the data fails
   naming the passive and the field.
2. The docs/02 "Status effects" Frenzied Effect cell reworded to the kind's exact effect ("Attacks faster: the attack
   cooldown is divided by 1 + the strength; the wind-up is unchanged; only the strongest counts"), pinned as the other
   rows are; `Frenzied` out of `PendingStatuses`. Its `description` may state a number only if every shipped source applies
   that magnitude and the pin covers it (today both Zealot passives are 0.5, so "+50 %" is pinnable; numberless is also
   fine: choose, say why).
3. `blinded.description`: "can't target anything more than 3 m away" → "can't attack anything more than 3 m away" (the
   Producer proposal under For your review since D10c; the owner has not vetoed; the number pins unchanged).
4. One golden regeneration: every `k` line equal, the new `data-hash` into `SameGameDataHashes` in the same commit, said in
   the commit message. Content suite green with no new skip; `DescriptionProblems` 0 for shipped text.
**Out of scope:** any number change; `abilities.json`; `units.json` beyond text (none needed); BUG-0440 (debt, the next
data hardening); summons content (no Wraith before M8).

**Acceptance criteria.**
1. `Content/PassiveContentTests` (new): both Zealot passives pinned field by field to whirlwind.md both ways; a mutated page
   number and a mutated data number each fail naming the passive and field (QA mutants).
2. `Frenzied` absent from `PendingStatuses`; the Frenzied row pinned by its kind's effect wording; `StatusContent` tests
   green; `DescriptionProblems` 0 problems on shipped text.
3. `blinded.description` reads "can't attack anything more than 3 m away"; its 2 m / 3 m pins green.
4. `git diff --stat` only the files above; `data-hash` moved once; `ReplayGoldenTests` + `Seed21PlayableReplay_*` green;
   `SameGameDataHashes` carries the new hash; non-Perf suite green; 0 warnings.
5. The review table for the owner (what text changed, old → new, why) in the dev report.

**Design references.** docs/factions/whirlwind.md lines 55-57, 91; docs/02 "Status effects" (Frenzied, Blinded rows),
"Stacking rule"; docs/03 "Implementation (M4-4b-3a)" (the exact cooldown rule), "For the data track (M4-4b-3a)" (shapes);
the D10c entry under For your review (the Blinded proposal); the Frenzied row in the M4-4b-3a review table.

**Constraints.** No C# outside `Content/` and `QA/Content/` (+ the `SameGameDataHashes` line); player-facing text only in
data; numbers must match the page; one golden regen, hash said in the commit; never touch `units.json` numbers.

## QA focus (per track; ids sim 0450+, view 0460+, data 0470+)

- **Sim (full):** hp-delta edge cases (a unit at 0 on the landing tick, two hp techs / overlapping `appliesTo`, a unit mid-DoT,
  hp techs for a type with no live unit then a spawn), the Frenzied line after the fix (41 / 83 real), BUG-0420's integer line
  across fractions 0.001-0.999 x hp 1-400 against a decimal oracle; summons: expiry on the same tick as a killing blow (one
  death, one event, one free), a summoned unit's slot re-used the next tick, a summon landing on the fog cadence, placement
  beside cliffs / map edges / a full ring, store full mid-count, 3-player fuzz twins + replay round trip, the `SummonedUnits`
  counter vs live flags every tick, no per-tick work at 0 summons (paired `EmptyTick`), 0 B with 200 live and expiring, Perf
  alone, memory itemised, the shipped `data-hash` unchanged.
- **View (standard):** read-only twins (the card and the scene never move the hash), the `selfAura` click never enters
  targeting, the Frenzied colour against the other three in stills, the sandbox scene's fog check every frame of the
  approach, a FAIL step named with `--break`, 0 B rows, the scene loop; look at every still.
- **Data (light):** page / data mutants both ways for both passives and the Frenzied cell, `DescriptionProblems` by role on
  the new wording, the golden `k` lines unchanged, diff confined to the named files, no number drift.

## Watch-outs (all tracks)

- **Integration order sim → view → data.** Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md`
  (three sides), docs/03 (different sections), docs/01 (sim + data rows: keep both). Only D11 moves the golden.
- **Perf:** three suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- The view's `selfAura` row needs no shipped ability: in-memory data. The view must not reference `AbilityKind.Summon` yet.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
