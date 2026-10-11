# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-10-1954** (fifth full session of 2026-10-10, cap 8; usage 11 % /
18 %). `main` @ be96728 is green: build 0 warnings, smoke PASS, non-Perf suite run by the Producer (result in the session log).
No inbox notes. No open S1 / S2. Counters: sim 2 / 4, view 2 / 4, data 1 / 4 → all three tracks run **feature** sessions
except data, which takes its hardening (D-H3) one session early because its only other work (the balance pass) waits on the
owner's band answer or the end-of-M4 default.

## Where we are

- M0-M3 Done. **M4: 9 / 10.** Left: **criterion 7, stealth / detection** (sim M4-5 this session, then view M4-V8 visuals), then
  each track's end-of-M4 hardening (sim: BUG-0461 first), then the sign-off.
- **D11 is still held** on `origin/studio/2026-10-10-1314-data` @ 9978bd6 (`main` through 8bb7513 merged in; `main` since then
  has only the state-only be96728). It fails one test on the merged tree: `DataContentHashTests.cs:251` asserts the shipped
  `ContentHash` is the literal `0xD22C849A175F9ED5UL`; D11's shipped hash is 4383EBB9345FABF7. **Correction to the last handoff:**
  `GatherWedgeQaTests.RecordedDataHash` is the seed-21 replay's old hash (0xC22FBFEA0197CF3E), **not** the golden's; do not compare
  against it. The golden proof already lives in `ReplayPlayerTests` / `ReplayTestRun` (they compare `TestSim.Data.ContentHash()`
  with the replay header), so the right fix is to **delete the literal line** and fix the comment (exact text below).
- **How D11 lands this session (Producer decision):** the **sim and data branches both start from `main` (be96728) with
  `origin/studio/2026-10-10-1314-data` merged in** (expected clean: D11's later commits touch no file be96728 changed). Each
  makes the **identical** first commit below to `DataContentHashTests.cs`, so each branch's suite is green on its own and git
  merges the identical hunk without conflict (if a builder deviates and it conflicts, take either side). Integration order
  **sim → data → view**; whichever of sim / data merges first carries D11 to `main`. If one is rejected, the other still lands
  D11. The view branch starts from be96728 as usual and touches no `game/data/**` / replay / `DataContentHashTests.cs`.
- **Only the sim moves the golden this session** (M4-5 adds hashed data: `stealthed` / `revealed` statuses, `rules.json` stealth
  fields, a unit `detector` field), from 4383EBB9345FABF7 once; rule 3 (`SameGameDataHashes`). The data track's D-H3 is tests
  only: no `game/data/**`, page, or golden change.
- **Memory bound** 231,920,000: 231,873,016 used (47 KB margin). Any new per-slot field is itemised in the report (rule 7).
- **`EmptyTick(2500)`** paired 495 / 500 µs: a stealth-free tick must do no new work (counter-gated, like summons). BUG-0451's
  counting rule stands: a Perf failure counts only alone and above base.
- **Bug ids this session:** sim from **BUG-0480**, view from **BUG-0490**, data from **BUG-0500**.
- Standing rules unchanged (1-8, see the 1314 handoff in `studio/sessions/2026-10-10-1314.md` and the 1314 handoff text:
  full suite incl. Perf alone + merged scene loop for vision / combat / loader changes; a new `UnitState` ships its `ui.json`
  line; whoever moves `data-hash` adds it to `SameGameDataHashes`; `ui.json` keys named in the brief; a sim behaviour change a
  view scene asserts on gets a named item in the view brief (**M4-5 changes `Fog.CanSeeUnit` only for stealthed units; no
  shipped unit can be stealthed, so no view scene changes: stated in the view brief**); a new `Command` kind goes into
  `CommandDoorFuzzStressTests`; per-slot memory itemised; the shipped `data-hash` is pinned in the golden only, never a literal).

### The identical first commit (sim and data branches; the sim-owned file is a named exception for the data track)

In `sim/Rts.Sim.Tests/DataContentHashTests.cs`, **delete** the line `        Assert.Equal(0xD22C849A175F9ED5UL, baseline);` (line 251
on 9978bd6) and replace the summary's three middle lines so the whole comment reads exactly:

```
    /// <summary>
    /// M4-4b-3b criterion 5: the summon kind and every field of a spawn effect move the hash, for an in-memory ability (no
    /// shipped ability has either, so the spawn fields are hashed only on that kind and the shipped data's hash is untouched;
    /// the shipped hash itself is pinned by the golden replay alone, never here: rule 8). The kind's "no target" ripples
    /// nowhere else: the summon's Range and Radius are the hashed fields they were.
    /// </summary>
```

Nothing else in that file changes in this commit. Commit subject: `M4: pin the shipped data-hash in the golden only (D11 landing)`.

## Sim track

### Current session plan: M4-5 stealth / detection · feature · QA full · budget ~1,200 lines incl. QA

**Base:** be96728 + `origin/studio/2026-10-10-1314-data` merged; first commit as above.

**Goal.** M4 criterion 7: "Stealth and detection system (tested now, even though Shadow arrives in M8)". Build the one rule that
makes a stealthed unit unseen and untargetable by an enemy unless detected, with every number in data and test-only sources
(no faction can stealth before M8), so the view (M4-V8), combat and the M5 AI all follow `Fog.CanSeeUnit` / `VisionSystem.UnitSeesUnit`.

**Scope (Producer default shape; the builder may propose better and must say so in the report).**
1. **Data (schemas, sim-owned files):** `StatusKind.Stealthed = 4`, `Revealed = 5` (+ `DataLimits.StatusKindIds`), entries
   `stealthed` / `revealed` in `game/data/common/statuses.json` (`displayName` "Stealthed" / "Revealed"; descriptions per
   docs/02 "Status effects", numberless or matching the rules); `rules.json` `stealth: { "proximity": 3, "revealSeconds": 3 }`
   (or flat fields; validated > 0, seconds → ticks at load); `UnitDef.Detector` (m, optional, 0 = none, ≤ `DataLimits.MaxSight`,
   `units.json` `detector`), hashed in `ContentHash` beside the buildings' one. **No `game/data/factions/**` edit:** the shipped
   casters' `detector 10` (docs/02 "Caster" class row, "Stealth and detection") is the data track's next task (D12); say so in
   docs/03 "For the data track (M4-5)".
2. **The stealth predicate:** `World.IsStealthed(slot)` = a live Stealthed entry **and** no live Revealed entry **and** not
   summoned (`Units.IsSummoned`; shadow.md: the Wraith can't be stealthed). Own player always sees its own units.
3. **Detection:** enemy player p detects stealthed unit j when j's cell is visible to p **and** j is within the `detector`
   radius of one of p's live units or buildings with `detector > 0`, **or** within `proximity` (3 m) of any live unit of p
   (docs/02: proximity is a detector of 3 m on every unit; the same "cell visible" clause). Store the result as a per-slot
   per-player bit mask (`UnitStore.DetectedBy`, 1 byte per slot for up to 8 players, **derived**, not hashed: recomputed
   from hashed state), recomputed by a `StealthSystem.Run` each tick that **returns at once when `StealthedUnits == 0`**
   (a derived counter kept by the status store's add / remove and by `Free`). Run it after statuses and before vision /
   combat so the same tick's checks read it (document the phase).
4. **The one hook:** `FogStore.CanSeeUnit(p, j)` and `VisionSystem.UnitSeesUnit(i, j)` return false for an undetected
   stealthed enemy. Through that: no acquisition, no retaliation, an explicit `Attack` on it is dropped on apply (as on an
   unseen target), a kept target that goes stealthed and undetected is lost like a target lost from sight (the BUG-0150
   rule), towers never pick it, the ghost / last-known logic is unaffected (units only). Zones: a stealthed unit inside an
   enemy's storm stays hidden unless detected.
5. **Reveal:** when a stealthed unit's attack is delivered (the same moment the high-ground reveal is granted) or its cast
   resolves, it gets Revealed for `revealSeconds` (the status store's refresh rule: reapplying refreshes). Revealed overrides
   Stealthed for its duration; a Stealthed entry applied while Revealed stays stored but has no effect until Revealed ends.
   Starting a stealth while already in combat is allowed (the status decides, not the state).
6. **Test sources only:** a test-only `selfAura` ability with an `applyStatus stealthed` effect on the caster (the
   `SelfAuraTests.AddTo` / `SummonTests.AddTo` pattern) and test-only units with `detector` (a "spotter"); the `whenStanding`
   passive trigger, forest stealth and the Shadow Archer's first-shot bonus are **M8**, not here.
7. **Hash / replay:** statuses are already hashed; `DetectedBy` and `StealthedUnits` derived (classified in `StateHashTests`);
   `ContentHash` grows by `Detector` + the two rules fields; **one golden regen** from 4383EBB9345FABF7, the new hash appended
   to `SameGameDataHashes` with a comment (no stealth in that match). No new `Command` kind.
8. **Cost:** `EmptyTick(2500)` unchanged (a `StealthedUnits` read is the only new work on a stealth-free tick; the hot-path
   branch in `UnitSeesUnit` / `CanSeeUnit` tests the counter first); the memory delta (1 B x unit capacity + counters)
   itemised in the report; re-baseline the bound only if the margin is gone, itemised.
9. **Fold BUG-0452 items 1-2** (the loader is touched: `affects` optional on a spawn-only summon, `radius` refused); item 3
   only if a few lines.
10. **Docs:** docs/03 "Implementation (M4-5)" (the predicate, detection, phase order, the derived mask, costs), "For the view
    (M4-V8)" (own stealthed units: translucent, read `World.IsStealthed`; detected enemies: drawn as normal, plus a flag the
    view can read `World.IsDetectedBy(p, slot)`; a detector ring from `UnitDef.Detector` / `BuildingDef.Detector`), "For the
    data track (M4-5)" (the casters' `detector 10` in `units.json`, the page rows, the status texts to pin), the "Not yet"
    paragraph under "Vision, detection, fog" rewritten; docs/02 line "Until M4-5's Revealed status only attacks from high
    ground reveal" replaced by the shipped rule; docs/01 change-log row (Producer decisions: proximity as a 3 m detector under
    the same visible-cell clause; reveal at delivery / resolve; summoned never stealthed; Revealed stored-but-dormant stealth).

**Out of scope:** BUG-0461 (end-of-M4 hardening), BUG-0401, Shadow content, forest / tree detection (M8), the `whenStanding`
trigger, any `game/data/factions/**` edit, any `ui.json` edit (no new `UnitState`), `DataContentHashTests.cs` beyond the first
commit (put new hash rows in a new file, e.g. `Vision/StealthContentHashTests.cs`, so the data branch's identical hunk merges).

**Acceptance criteria.**
1. `Vision/StealthTests` (fail-first): an enemy without detectors neither sees (`CanSeeUnit` false, `UnitSeesUnit` false) nor
   acquires nor retaliates against a stealthed unit standing in its sight; an explicit `Attack` on it is dropped; the owner
   sees it; a unit of a third player likewise does not.
2. Detection: a detector unit (test-only `detector 10`) and a watch tower (`detector 16`, shipped) each detect a stealthed unit
   inside their radius **and** in a visible cell, and not one outside the radius or in a cell they cannot see (high ground from
   below: not detected); the detected unit is acquired / shot like any visible unit; proximity: any enemy unit within 3 m
   (edge counts, 3.05 m not) detects, 2.9 m from a Blinded (2 m sight) enemy does not when the cell is unseen.
3. Reveal: a stealthed unit that lands a hit (melee and a projectile) or resolves a cast is Revealed for exactly 60 ticks
   (`revealSeconds` 3) and seen by every enemy whose sight covers its cell; re-revealing refreshes; at tick 61 it is stealthed
   again if its Stealthed entry still lives; `Revealed` overrides a Stealthed applied during it.
4. Summoned units ignore stealth (`IsStealthed` false with a live Stealthed entry); a target that goes stealthed and
   undetected is dropped by chasers within the lost-from-sight rule; the status-count hash rows: `DetectedBy` /
   `StealthedUnits` classified derived in `StateHashTests`, the two status kinds and the rules fields change `ContentHash`.
5. Loader: `detector` refuses 0 / negative / > MaxSight at `units[k].detector`; `rules.stealth.*` refuses ≤ 0; the two kinds
   load from `statuses.json`; BUG-0452 items 1-2 refuse / accept as the bug file says; `DataValidationTests` green on shipped data.
6. Determinism: a 3-seed x 2,000-tick fuzz with stealth casts, detectors, towers and storms (twins + replay round trip, the
   per-tick invariants) in `Stress/StealthFuzzStressTests`; `ReplayGoldenTests` green on the regenerated golden; the new hash in
   `SameGameDataHashes`; `CommandDoorFuzz` untouched and green.
7. Perf alone: `EmptyTick(2500)` not above base in a paired run; a 500-unit brawl with 50 stealthed units and 10 detectors
   within budget, 0 B per tick after warm-up; the memory delta itemised.
8. Full suite incl. Perf alone green; smoke PASS; the merged scene loop 41 / 41 (rule 1); 0 warnings; the first commit as
   specified and nothing else in `DataContentHashTests.cs`.
9. Docs per scope item 10; docs/01 row; BUG-0452 items marked in its file.

**Design references:** docs/02 "Status effects" (Stealthed, Revealed rows), "Stealth and detection" (all six bullets),
"Buildings" (Watch Tower detector 16 m), "Caster" class row (detector); docs/03 "Vision, detection, fog" + its "Not yet"
paragraph, "Implementation (M4-3a / M4-3b / M4-4a / M4-4b-3a / M4-4b-3b)" (fog, towers, statuses, passives, summons);
docs/factions/shadow.md lines 15-23, 45-54 (what M8 will need from this system).

**Constraints most at risk:** no stat in C# (3 m / 3 s / 10 m in data); no allocation on the tick path; slot order
everywhere (detector scan in slot order); no Godot; the derived / hashed split documented; `Rts.Sim` warnings as errors.

Then: the end-of-M4 sim hardening (BUG-0461 first, BUG-0451 decision, BUG-0401, 0452 rest, 0302 b / c, 0275, 0271 / 0272
decision, 0144, 0142, 0113 item 2, 0094, 0391, 0422 items 2-3), then the sign-off once every track's hardening is done.

## View track

### Current session plan: M4-V7 slice 2 · feature · QA standard · budget ~900 lines incl. QA

**Base:** be96728 (`main`). No `game/data/**`, replay or `DataContentHashTests.cs` edit. The sim's M4-5 changes `Fog.CanSeeUnit`
only for stealthed units; no shipped unit can be stealthed, so **no existing view scene changes for it**; the view must not
reference `StatusKind.Stealthed` / `Revealed` or `UnitDef.Detector` this session (not on the base).

1. **BUG-0450 first (S3):** `ViewApi.UnitHpBars.Collect`, `CombatViews.SyncBars` and `UnitViews._maxHp` (the first-sight
   `HitFlash`) take the maximum from `World.MaxHp(slot)` per slot (Requests 31 / docs/03 "For the view (M4-V7)"); un-skip
   `QA/ViewApi/UnitHpBarsMaxHpQaTests.AProphecyZealotAt70Of83_ShowsABar`; a headless row (`CombatViewTest` or
   `AbilityViewTest`) with a Prophecy Zealot at 70 / 83 showing a bar at 70 / 83 and one at 83 / 83 showing none; the
   `UnitHpBars` remark corrected; docs/03 "Implementation (M4-V1)" note.
2. The **`AbilityKind.Summon` card arm:** the same no-target card as `SelfAura` (`BeginAbility`, `SelfCaster`, the hover ring;
   `Radius` may be 0: no ring); a test-only summon in `AbilityViewTest` (`SummonTests.AddTo` is the sim-test pattern): the
   three units appear at the resolve with views, an expiring unit's view goes with `DeathEvent.Expired` and leaves **no corpse
   disc** (`CombatViews` checks `Expired`), K / L unchanged by an expiry.
3. The sandbox's **enemy-storm step** in `M4PlayableTest`: a scripted enemy Priest casts Sandstorm over the player's army (a
   staging `UseAbility` for player 1, like the duel's Attacks): Blinded markers on own units inside, the enemy disc drawn where
   seen, own units' sight shrinks on the fog texture; the step prints its ticks; the tick budget kept (measure both seeds).
4. **BUG-0462 item 5:** the measured range in `TickBudget`'s comment and docs/03 (state the 4,212 run; the margin as measured).

**Out of scope:** stealth visuals (M4-V8, after the sim's M4-5 is on `main`), BUG-0430 / 0462 item 1 (ring across a cliff:
end-of-M4 view hardening), BUG-0431, BUG-0462 item 3.

**Acceptance criteria.**
1. BUG-0450 fixed: the QA row un-skipped and green; a headless row shows a bar for 70 / 83 and none for 83 / 83; the first-sight
   flash reads `World.MaxHp`; the sandbox's Prophecy step (if it researches it) still passes.
2. The Summon card: one click (or key) sends one `UseAbility` at the lead caster, no targeting state; Shift queues; the summoned
   views appear at the resolve tick, vanish at expiry with no corpse disc; K / L unchanged; `AbilityViewTest` row per claim.
3. The enemy-storm step: at least one own unit Blinded (marker drawn) while inside, the enemy disc drawn only while one own
   unit sees a cell within the storm's radius + 2 m (the M4-V6c rule), the fog texture shows the shrunken sight; `M4PlayableTest`
   PASS on seeds 1 and 6 with the step's ticks printed, the twin equal, 0 fog violations.
4. Tick budget: both seeds within the budget; the comment and docs/03 give the measured range incl. the 4,212 outlier.
5. Scene loop 41 / 41, smoke PASS, 0 warnings, no new player-facing literal (existing `hud` / `states` keys).
6. docs/03 "Implementation (M4-V7)" extended; BUG-0450 and BUG-0462 item 5 marked in their files.

**Design references:** docs/03 "For the view (M4-V7)", "Implementation (M4-V6c / M4-V6d / M4-V7)", docs/02 "Zones" and the
Sandstorm row; the dumb-views rule (CLAUDE.md 7).

Then M4-V8 stealth visuals (once M4-5 is on `main`), then the end-of-M4 view hardening (BUG-0430 + 0462 item 1, 0431, 0462
item 3, 0400, 0250 item 2, 0148 item 1, 0126 items 5 / 6, export hygiene).

## Data track

### Current session plan: D-H3 (the data hardening, one session early) + the D11 landing commit · hardening · QA light · ~400 lines

**Base:** be96728 + `origin/studio/2026-10-10-1314-data` merged (so D11's `PassiveContentTests` / `ZealotPassivePinQaTests` are
present); **first commit: the identical `DataContentHashTests.cs` edit above** (sim-owned file: named exception, nothing else
in it). Then, tests only (no `game/data/**`, no page, no golden, no C# outside `sim/Rts.Sim.Tests/Content/` + `QA/Content/`):

1. **BUG-0470 item 1:** `PassiveContentTests.Compare` counts page sentences per (unit, trigger) and fails a second one naming
   both sentences; un-skip `QA/Content/ZealotPassivePinQaTests.ASecondSameTriggerPageSentence_WithOtherNumbers_Fails`.
2. **BUG-0440's four wordings** in `AbilityContentTests.DescriptionProblems`: "(cooldown: N s)" with a colon reads as the
   cooldown; "N-second" binds to the duration role; "dmg" is a damage word; the linger phrase is detected per sentence, not per
   clause (the fix sketch is in the bug file); each with a mutant row that failed first.
3. The known-gap notes in docs/03 (D-H2 paragraph) updated for what items 1-2 close; BUG-0470 / 0440 marked fixed.

**Out of scope:** any content change (the balance pass waits on the owner's D6 band answer or the end-of-M4 Producer default);
D12 (the casters' `detector 10`, after the sim's M4-5 is on `main`); the `UnitHpBars` remark (the view's, BUG-0450).

**Acceptance criteria.**
1. The QA row un-skipped and green; a second same-trigger sentence with other numbers fails naming both.
2. The four BUG-0440 wordings each caught by a new mutant row (fail-first noted in the report); `DescriptionProblems` still 0
   on shipped text.
3. `git diff --stat` against the base: only `DataContentHashTests.cs` (the specified hunk), files under
   `sim/Rts.Sim.Tests/Content/` and `QA/Content/`, docs/03, `studio/bugs/**`, `studio/qa/**`.
4. Non-Perf suite green on the branch (expect about 4,945 / 8 / 0: D11's merged count plus the un-skipped row); 0 warnings.

**Design references:** docs/02 "Abilities" / "Status effects" wording rules, docs/factions/whirlwind.md Unit notes,
docs/03 "Implementation (D-H2)" and "(D11)".

Then: D12 (the casters' `detector 10` + page pins + the two stealth status texts pinned; one golden regen) once M4-5 is on
`main`; owner tweaks from the D11 table if any arrive; the balance pass (QA standard) on the owner's band or the end-of-M4
default (40-65 %).

## Watch-outs (all tracks)

- **Integration order sim → data → view.** Both sim and data carry D11 (merged base) and the identical test hunk; the first to
  merge lands D11. Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md`, docs/03 (different
  sections). If the identical hunk conflicts anyway, take either side.
- **The merged scene loop** is the integration gate (41 scenes, both playables last). Perf under three suites fails on base and
  head alike; a failure counts only alone and above base (BUG-0451).
- The view must not reference stealth kinds; the sim must not edit `ui.json` or `game/data/factions/**`.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
- Scratch directories in the shared scratchpad carry the agents' names; never `rm -rf` a shared name.
