# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-10-0215** (all three tracks accepted; integration sim 62dcaeb →
view 3dd6498 → data bf6a269). The PLAN of the next session replaces the "Current session plan" blocks below.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** Criterion 6 (abilities, statuses, zones) has M4-4a + M4-4b-1 + **M4-4b-2 (zones, Blinded,
  Sandstorm)** landed, and on screen M4-V6a + **M4-V6b (Burning / Slowed markers, the resolve flash)**; owed: summons /
  self-aura / autocast / passives (M4-4b-3) and zone discs + the Blinded marker (M4-V6c). Criterion 8: Telas Fire, the
  Cusser and **Sandstorm** work; the Zealot passives need M4-4b-3. Then 7 stealth (M4-5), 10 the fog-on sandbox Playable.
- `main` after this integration: build 0 warnings; QA's scratch merge of sim + data: non-Perf 4,444 / 15 skipped / 0 failed;
  sim branch Perf alone 146 / 3 / 0; view scene loop 39 / 39; smoke PASS on both branches. **No open S1 / S2** (BUG-0362 S2
  fixed in the data's fix round). Index: S3 and S4 open counts in STATE.
- **Hardening counters after this session: sim 4 / 4, view 4 / 4, data 2 / 4.** The next session is the **sim's and the
  view's hardening** (their debt backlogs in STATE are current and ordered); the data track runs **D10c** (feature).
- Golden `data-hash` is **5896D3E7C9FD36AD** (in `GatherWedgeQaTests.SameGameDataHashes`, with the three older ones).
- Bug ids next session: sim from **BUG-0390**, view from **BUG-0400**, data from **BUG-0410**.
- Standing rules: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full suite incl.
  Perf once (alone) and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the `ui.json`
  `states.*` line itself; (3) whoever moves `data-hash` adds the new hash to `GatherWedgeQaTests.SameGameDataHashes` in
  the same commit when the seed-21 match is unaffected, else re-records; (4) when the view needs a `ui.json` key, the
  brief says so and the sim stays off the file that session.
- **File ownership next session:** the sim hardening is code + docs only (no `game/data/**` edit, no `data-hash` move); the
  view hardening touches `game/**` and `sim/Rts.Sim/ViewApi/**` (+ its xUnit tests under `ViewApi/` and `QA/ViewApi/`);
  the data track may edit `game/data/common/statuses.json`, `game/data/factions/malazan/abilities.json` and
  `game/data/factions/whirlwind/abilities.json` **text fields only** (`description`), `docs/factions/whirlwind.md`, the
  docs/02 "Status effects" Blinded row, and, as a named exception for the golden regen, `sim/Rts.Sim.Tests/Replays/
  cross_map_seed1.replay` + the `SameGameDataHashes` line in `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs`.

## Sim track

### Current session plan: M4-H2 · sim hardening · QA full · batch to ~1,200 lines

Work the debt backlog (STATE "Debt backlog: sim track"), most valuable first; stop when the budget is used:
1. **BUG-0360 (S3):** the vision blocker hides by cell centre, the statuses by unit centre; option (a): `ZoneHides` and
   `Fog.CanSeeUnit` hide a *unit* by its own centre (cells keep the cell rule). Un-skip `ZoneQaTests.TheBlockerAndTheStatuses_
   AgreeOnWhoIsInside`, retire `TheRadiusEdge_CurrentBehaviour_*`, docs/02 "Zones" + docs/03 say the same thing.
2. **BUG-0361 (S3):** `ReplayRecorder` refuses a non-default `ZoneCapacity` (one `if` beside the two existing guards);
   un-skip `TheRecorder_RefusesANonDefaultZoneCapacity`.
3. **BUG-0363 (S4) items 1-2:** a cheap "any blocker near the scanner" test so far storms don't turn off the one-level
   scan shortcut (`ZoneScalePerfTests` ratio should drop); the 3-tick acquire window after a storm appears: fix or document
   in the `ZoneHides` summary + docs/03.
4. **BUG-0330 (S3):** size the death list units + buildings; un-skip the `CusserQaTests` row.
5. **BUG-0311 (S3):** end an Attack on a gone building's ghost on `fog.SeesFootprint` (the fog's rule); the view's
   `QaGhostViewTest` then runs `--strict`.
6. **BUG-0241 (S3):** the chaser that never gives up along a cliff; flip `ChasePrevQaTests.UnreachableTargetsTakenInTurn_*`.
7. **BUG-0157 (S3):** the kept-chase rule with the mean bound (the decision of 2026-10-08-2144).
8. **BUG-0270 (S3):** a tower's high-ground hit reveals the tower (per-(building, player) reveal, hashed; docs/01 row (d)).
9. **BUG-0302 b + c, BUG-0275 1-2, BUG-0271 / 0272 (decide or wontfix), BUG-0144, BUG-0142 1-2, BUG-0113 item 2, BUG-0094,
   BUG-0242, the docs/02 "allied and own units" wording** as the budget allows.
Acceptance: each fixed bug's skipped row un-skipped and green; a regression test per fix; full suite incl. Perf alone green;
`EmptyTick(2500)` < 500 µs and `TightBlob2500` < 4.6 ms (both thin: 473-480 µs and 4.48-4.50 ms now); golden unchanged
unless a hashed field is added (then regen once with the reason); docs/03 known limits updated; bug files set `fixed`.
**OUT:** M4-4b-3 features, stealth, any `game/data/**` edit.

**QA focus.** The BUG-0360 fix at the rim (5.2 m in / 6.01 m out, both for `CanSeeUnit` and `ZoneHides`; a Blinded unit
inside by centre but in an "outside" cell); the oracle fuzz `ZoneFogOracleFuzzStressTests` re-derived with the new rule;
the Perf rows alone after BUG-0363; the recorder guard; the death-list overflow with 2,000 units + 50 buildings dying in
one tick; replay round trips after every hashed change.

After: M4-4b-3 (summons `spawn`, `selfAura` / `targetUnit`, `autocast`, the Zealot passives), M4-5 stealth / detection
(the Revealed status, detectors), the fog-on sandbox Playable.

## View track

### Current session plan: M4-VH2 · view hardening · QA standard · batch to ~1,000 lines

1. **BUG-0370 (S3, first):** `AbilityCaster.PickCaster(queued: true)` treats a caster as busy when its order queue already
   holds a `UseAbility` of that ability (or remembers the casters this armed session sent until the next tick); un-skip
   `ShiftQueuedCastQaTests.TwoShiftClicks_OnWalkingMages_*`; the "two clicks in one tick" gap in docs/03 closes with it.
2. **BUG-0371 (S4):** the Slowed marker's contrast (a darker / saturated blue or an outline; a taste default, owner may
   revisit), a fog-hidden resolve decided once on its first frame, the late-first-draw age jump.
3. **BUG-0251 (S3):** `EconomyViewTest` FATAL in .NET shutdown under load (dispose the Match references, GC, wait a frame).
4. **BUG-0281 items 1-3 (S4):** props / minimap resource layer relist in explored fog; minimap dots vs `CommandAt`.
5. **BUG-0250 (S4), BUG-0148 item 1, BUG-0126 items 3 / 5 / 6, export hygiene notes** as the budget allows.
Acceptance: each fixed bug's row green; scene loop all green (39 scenes); smoke PASS; 0 B rows kept (`QaV6bTest`,
`QaV6aTest`, `AbilityViewTest` steady); build 0 warnings; docs/03 updated; bug files set `fixed`.
**OUT:** zone discs / the Sandstorm look / the Blinded marker (M4-V6c, the next view feature session), stealth visuals.

**QA focus.** Shift-queued casts: two walking mages, two clicks in one tick, a caster dying mid-queue, a cast popped on
cooldown; marker colours windowed; the EconomyViewTest shutdown 30 runs under load.

After: M4-V6c zone visuals (a storm disc per live zone from `World.Zones`, drawn only where the player may see it, the
Blinded marker colour), stealth visuals (M4-5), the fog-on sandbox Playable.

## Data track

### Current session plan: D10c · Sandstorm's row + Blinded pinned, BUG-0380, the text tweaks · feature · QA light · ~400 lines

Inbox requests (owner review tweaks) come first if present. Then:
- Pin Sandstorm: drop it from `PendingAbilities`; `EffectProblems` learns a `createZone` effect (its statuses' text, "No
  effect on buildings" when nothing hits buildings, the Duration column = the zone's lifetime); reword the whirlwind.md
  Sandstorm row to the parsable form with **"Enemy units"** (`affects: enemy_units`, Producer decision (a) of M4-4b-2).
- Pin Blinded: drop it from `PendingStatuses`; `CompareStatuses` learns the `blind` kind against docs/02's Blinded row
  ("Sight radius 2 m; can't acquire or attack targets more than 3 m away") from `StatusDef.Sight` / `Reach`.
- **BUG-0380 (S3):** report every page damage claim no data effect consumed; un-skip `AnExtraPageDamageClaim_*`.
- **Text tweaks (Producer decision at the 2026-10-10-0215 ACCEPT, owner may revisit; see STATE For your review):** adopt
  the D10b proposals: Slowed → "Moves more slowly until it wears off. Only the strongest slow counts."; Cusser → "Throws a
  Moranth munition: 120 siege damage to enemy units and buildings in the area. Your own units caught in the blast, the
  Sapper included, take half; your buildings are safe."; Telas Fire → "Sets the ground ablaze: enemy units in the area are
  Burning, taking 10 magic damage a second for 4 seconds. Buildings are unharmed." Review `blinded`'s and `sandstorm`'s
  shipped text the same way (propose in the report, or adopt if clearly better). These move `data-hash`: regen the golden
  once (rule 3; named exception to edit the replay file and `SameGameDataHashes`), every `k` line equal.
- Files: `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`, `docs/factions/whirlwind.md`, the docs/02 Blinded row (text only),
  the three data files' `description` fields only, the golden + `SameGameDataHashes` line. **No other C#, no number change.**
- For your review entry: a table of every text changed (old → new, quoted) and the new pins.

**QA focus.** Sandstorm / Blinded mutants per field (both directions); the `createZone` effect text with re-ordered
phrases; BUG-0380's claim both before and after a matching claim; the golden regen (only `data-hash` + checksum differ).

After: the owner's text tweaks from the inbox, the full balance pass (QA standard) once the fog-on sandbox gives numbers,
`ai.json` build orders (M5), M7-M9 faction data.

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the scene loop and smoke on each merged result; push only green states.
  Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md` (append all sides); docs/03 one
  subsection per track; docs/01 rows appended.
- **Golden:** only the data track moves `data-hash` next session (text fields); the sim hardening must not add a hashed
  field without regenerating in its own commit, and if both move it the conductor re-records once on the merge.
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
  `EmptyTick(2500)` has ~15 µs of headroom now (473-486 µs of 500): the sim hardening should not add per-tick work.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name. Stale scratch worktrees
  (`merge362`, `merge-d10b`, `mut-merge`, `mut-branch`, `qa-sim-clone`, `qa-sim-base`, `gamedev-sim-base` and older ones in
  `git worktree list`) are listed for the owner under "Waiting on you" as non-blocking housekeeping.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
