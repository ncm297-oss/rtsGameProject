# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-10-1314** (fourth full session of 2026-10-10, cap 8). All three
tracks accepted: sim **M4-4b-3b** (hp techs raise hit points, the exact `whenBelowHp` line, summons; 0 fix rounds), view
**M4-V6d + M4-V7 slice 1** (the Frenzied marker, the `selfAura` card, `M4PlayableTest.tscn`; 2 fix rounds for the scene's own
timing flakes), data **D11** (the Zealot passives and Frenzied pinned, the Blinded / Frenzied wording; 0 fix rounds).
Integration order sim → view → data; **only the data branch moves the golden** (`data-hash` D22C849A175F9ED5 →
4383EBB9345FABF7, every `k` line equal, in `SameGameDataHashes`).

## Where we are

- M0-M3 Done. **M4: 9 / 10.** Criteria 6 (abilities incl. summons; the view's Frenzied marker + selfAura card), 8 (all four
  signature abilities with real numbers: BUG-0421 fixed) and 10 (the fog-on sandbox Playable, `M4PlayableTest`) ticked this
  session. **Left: criterion 7, stealth / detection** (sim M4-5, then view M4-V8 visuals), then the end-of-M4 hardening per
  track (sim counter 2 / 4, view 2 / 4, data 1 / 4: the milestone-end hardening comes regardless), then the sign-off.
- **Producer decisions this session (owner may revisit, docs/01 row 2026-10-10 "Hp upgrades and summons"):** max and current
  hit points move together when an hp tech lands; `targetUnit` and `autocast` deferred until a faction's data needs them;
  summons placed within 3 rings (6 m), cap 8 per spawn effect, an expiry counts no kill / loss. View: `FrenzyColor` magenta
  (taste default), the lead-caster rule for no-target casts, the sandbox's staging deviations (own Priest, Horse Raiders for
  "Lancers"). Data: Martyrdom's "+50% attack speed" written onto whirlwind.md (a Producer default); the Blinded "can't attack"
  proposal shipped (the owner did not veto).
- **No open S1 / S2.** New S3: BUG-0450 (view: hp bars use the type's hp as max; **first item of the next view task**),
  BUG-0451 (pre-existing: `EmptyTick(2500)` sits on its 500 µs gate; triage in the bug file: buy back cost, else re-baseline to
  550 µs with the trend itemised; until then a failure counts only alone and above base in a paired run), BUG-0461 (sim: a
  cast's walk-in hemmed in by own `Attacking` units is dropped after 1 s; **first item of the end-of-M4 sim hardening**,
  direction in the sim debt backlog). S4: BUG-0452 (summon loader nits), BUG-0462 (view nits incl. the overstated tick-budget
  margin), BUG-0470 (a second same-trigger page sentence is never compared).
- **Memory bound** 231,920,000: now 231,873,016 used (47 KB margin, +33,832 itemised in docs/03 M4-4b-3b). The next per-slot
  field re-baselines: say so in the brief, every byte itemised.
- **`EmptyTick(2500)`** paired 495 / 500 µs head / base: a summon-free tick does no new work (the counter pattern). M4-5 must
  keep it that way (a stealth pass gated on a `StealthedUnits` counter).
- **Bug ids next session:** sim from **BUG-0480**, view from **BUG-0490**, data from **BUG-0500**. Say so in every QA brief.
- Standing rules unchanged (see the previous handoffs): (1) a sim task that changes a placement, vision, combat or
  data-loading rule runs the full suite incl. Perf once alone and the merged scene loop before it reports; (2) a sim task that
  adds a `UnitState` ships the `ui.json` `states.*` line itself; (3) whoever moves `data-hash` adds the new hash to
  `SameGameDataHashes` in the same commit when the seed-21 match is unaffected; (4) when the view needs a `ui.json` key the
  brief says so and the sim stays off the file; (5) when a sim brief changes a behaviour a view QA scene asserts on, the same
  session's view brief carries a named item for that scene (**M4-5 changes `Fog.CanSeeUnit` for stealthed units: no shipped
  stealth unit exists, so no scene changes; say so**); (6) a sim task that adds a `Command` kind adds it to
  `CommandDoorFuzzStressTests`; (7) new: a sim task that adds a per-slot store field states the memory delta in its report.

## Sim track

### Next task candidate: M4-5 stealth / detection · feature · QA full · budget ~1,200 lines (QA included)

Criterion 7: "Stealth and detection system (tested now, even though Shadow arrives in M8)". Design: docs/02 "Status effects"
(Stealthed: invisible and untargetable to enemies without detection; Revealed: cannot be stealthed, overrides Stealthed;
sources: Shadow forest stealth, Shadow Archer stand-still; attacking from stealth reveals), docs/02 Buildings (towers
`detector` 16 m, schema on `main` since M4-3b), docs/factions/shadow.md (the Wraith can't be stealthed; the Hounds / Stalker
rows), docs/03 "Vision, detection, fog" and "Implementation (M4-4b-3a)" (the status / passive patterns). Shape (Producer
default; the builder may propose better, say so): `stealthed` / `revealed` status kinds in `statuses.json` (common; the data
track is off that file that session), a `detector` field on `UnitDef` (m) beside the buildings' one; a stealthed unit is a
unit with a live Stealthed entry and no Revealed one and not summoned (`Units.IsSummoned`); an enemy sees it only if one of
that enemy's detectors (unit or tower) has it within its detector range **and** within normal sight (the fog's `CanSeeUnit`
false otherwise, so the view, combat acquisition, retaliation and explicit Attacks all follow the one rule); a stealthed unit
that lands a hit gets Revealed for a data duration (docs/02); stealth as a passive trigger (`whenStanding` 2 s still, or
in-forest) only if cheap; test-only units and statuses (no Shadow data before M8); cost rule: a `StealthedUnits` counter, 0 B,
`EmptyTick` unchanged; hash: the statuses are already hashed, nothing new unless a detector memo is added (then classify it).
Docs: docs/03 "Implementation (M4-5)", "For the view (M4-V8)" (what to draw for own stealthed units and detected enemies), "For
the data track (M4-5)" (shapes), docs/01 row. Fold BUG-0452 if the loader is touched. **Out of scope:** BUG-0461 (hardening),
Shadow content, forest detection of trees (M8), any `game/data/factions/**` edit.

Then: the end-of-M4 sim hardening (BUG-0461 first, BUG-0451 decision, BUG-0401, 0452, 0302 b / c, 0275, 0271 / 0272 decision,
0144, 0142, 0113 item 2, 0094, 0391, 0422 items 2-3).

## View track

### Next task candidate: M4-V7 slice 2 · feature · QA standard · budget ~900 lines (QA included)

1. **BUG-0450 first (S3):** `UnitHpBars.Collect`, `CombatViews.SyncBars` and `UnitViews._maxHp` (the first-sight `HitFlash`)
   take the maximum from `World.MaxHp(slot)` (Requests 31), per slot; un-skip
   `QA/ViewApi/UnitHpBarsMaxHpQaTests.AProphecyZealotAt70Of83_ShowsABar`; a headless row with a Prophecy Zealot at 70 / 83
   showing a bar and one at 83 / 83 none; the `UnitHpBars` remark corrected; docs/03 "Implementation (M4-V1)" note.
2. The **`AbilityKind.Summon` card arm**: the same no-target card as `SelfAura` (`BeginAbility`, `SelfCaster`, the hover ring;
   `Radius` may be 0: no ring then); a test-only summon in `AbilityViewTest` (`SummonTests.AddTo` is the sim-test pattern): the
   three units appear at the resolve with views, an expiring unit's view goes with `DeathEvent.Expired` and leaves **no corpse
   disc** (`CombatViews` must check `Expired`), K / L unchanged by an expiry.
3. The sandbox's **enemy-storm step**: a scripted enemy Priest casts Sandstorm over the player's army (a staging `UseAbility` for
   player 1, like the duel's Attacks): Blinded markers on own units inside, the enemy disc drawn where seen, own units' sight
   shrinks on the fog texture; keep the tick budget and print the step's ticks.
4. BUG-0462 item 5: the measured range in `TickBudget`'s comment and docs/03 (state the 4,212 run; the margin as measured).
**Out of scope:** stealth visuals (M4-V8, after the sim's M4-5 is on `main`), BUG-0430 / 0462 item 1 (the ring across a cliff:
end-of-M4 view hardening), BUG-0431, any `game/data/**` edit. Then M4-V8 stealth visuals, then the end-of-M4 view hardening.

## Data track

### Next task candidate: inbox tweaks from the D11 review first; else D-H3 early · hardening · QA light · ~400 lines

If the owner answers the D11 table (the Frenzied / Blinded wording, Martyrdom's +50 %), plan those tweaks first (one golden
regen, rule 3). Else **D-H3** (the data hardening one session early, as D-H2 was): BUG-0470 item 1 (`PassiveContentTests.Compare`
counts page sentences per (unit, trigger) and fails a second one naming both; un-skip QA's
`ASecondSameTriggerPageSentence_WithOtherNumbers_Fails`), BUG-0440's four wordings (`DescriptionProblems`: "(cooldown: N s)",
"N-second", "dmg", the linger window), the known-gap notes in docs/03. Tests only, no data / page / golden change. **The full
balance pass** (QA standard) waits for the owner's answer to the D6 band (or a Producer default at the end of M4: the proposed
40-65 % band); the sandbox now exists for its numbers. The `summon` / `spawn` schema is on `main` for M8's Wraiths; nothing to
fill before then. If neither the inbox nor D-H3 applies, the data track STOPs cheaply.

## Watch-outs (all tracks)

- **Integration order sim → view → data.** Expected conflicts: `studio/bugs/README.md` (union by id: sim rows 0420 / 0421
  `fixed` + 0450 / 0451 / 0452; view rows 0460-0463; data row 0470), `studio/qa/coverage.md` (three sides), docs/03 (sim:
  "Implementation (M4-4b-3b)" + the M3-5 / M4-4b-3a edits + the Producer's Martyrdom-bullet fix; view: "Implementation
  (M4-V6d)" / "(M4-V7)" + the V6c "Not yet" line), docs/02 (sim: the hp sentence under techs; data: the Frenzied row), docs/01
  (sim row only). Only D11 moves the golden; the sim's `ContentHash` change hashes nothing for shipped data.
- **The merged scene loop** is the integration gate (41 scenes, both playables last; `M4PlayableTest` ~40 s). Perf under
  three suites fails on base and head alike; a failure counts only alone.
- The view must not reference stealth kinds before the sim's M4-5 is on `main`; the sim's M4-5 must not edit `ui.json`.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
- Scratch directories in the shared scratchpad carry the agents' names; never `rm -rf` a shared name.
