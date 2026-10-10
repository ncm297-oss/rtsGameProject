# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-10-0624** (sim M4-H2, view M4-VH2, data D10c: all three
ACCEPT; integration sim → view → data with one conductor-dispatched view round for BUG-0390 before the merged scene loop).
The next PLAN rewrites this file; the candidates below are what the Producer expects to plan.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** Criterion 6 (abilities, statuses, zones) owes summons / self-aura / autocast / passives
  (M4-4b-3) and zone discs + the Blinded marker (M4-V6c); criterion 8 owes the Zealot passives (M4-4b-3). Then 7 stealth
  (M4-5), 10 the fog-on sandbox Playable, and the end-of-milestone hardening before sign-off.
- `main` after this integration: build 0 warnings; **no open S1 / S2**; S3 11 / S4 20 open by the merged index (partly-fixed
  multi-item nits counted). The only S3 filed this session, BUG-0390, is a view-file test rewrite the conductor dispatches at
  integration; if it is still open at PLAN it is the view's first item.
- **Hardening counters after this session: sim 0 / 4, view 0 / 4, data 3 / 4.** Both hardenings are done; the next four
  sessions are feature sessions for sim and view.
- Golden `data-hash` is **EA5CB5A0AFCEEDBE** (D10c, text only; in `GatherWedgeQaTests.SameGameDataHashes` with the four
  older ones). The sim did not touch the golden; its new hashed state (zone fog records, tower reveals) is live-only, and
  `ChaseChainBest` rides in the combat word (no combat in the golden's one-player match).
- Bug ids next session: sim from **BUG-0420**, view from **BUG-0430**, data from **BUG-0440**.
- Standing rules: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full suite incl.
  Perf once (alone) and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the `ui.json`
  `states.*` line itself; (3) whoever moves `data-hash` adds the new hash to `SameGameDataHashes` in the same commit when
  the seed-21 match is unaffected, else re-records (BUG-0275 item 1, `BuildingGhost.Site`, waits for the next re-record);
  (4) when the view needs a `ui.json` key, the brief says so and the sim stays off the file that session; **(5) new: when a
  sim brief changes a behaviour a view QA scene asserts on, the same session's view brief carries a named item to adjust
  that scene** (BUG-0390's lesson: the sim dev was rightly barred from the file and the view had no item for it).
- **Memory bound margin is 18.9 KB** (231,843,112 of 231,862,000, BUG-0302 b): M4-4b-3's per-unit passive state will cross
  it; the task re-baselines with every byte itemised (M4-H2's +35,016 are; M4-4b-2's +18,536 are not yet).
- `EmptyTick(2500)` has ~15 µs of headroom (483-485 µs of 500 paired medians): M4-4b-3 must add no per-tick work for units
  without passives (a per-type "has passives" flag, a count of units with any).

## Sim track

### Next task candidate: M4-4b-3 · the rest of criterion 6 · feature · QA full · first slice ~800 lines (new system)

**Goal.** Finish the ability system's vocabulary so criterion 6 can be ticked and the Zealot passives (criterion 8) work:
the Frenzied status, unit passives, self / aura casts, summons, autocast, target-unit casts. A new system with real
uncertainty (passives have no schema yet), so plan the first slice only and name the second.

**Suggested slice a (plan first):** (1) `statuses.json` kind `frenzied` (attack-speed and / or damage multiplier; docs/02
"Status effects": "Attack speed and/or damage up"; the strongest wins like `slow`); shipped `frenzied` +50 % attack speed;
`CombatSystem` reads it for the cooldown (and `DamageCalc` for a damage magnitude if the schema has one). (2) **Unit passives**
(`UnitDef.Passives`, a small fixed vocabulary, data hooks not code per passive, CLAUDE.md rule 6): `whenBelowHp` (fraction →
applyStatus on self while below; the Zealot's Frenzy of the Apocalypse: below 50 % HP, Frenzied) and `onDeath` (an aura of
applyStatus on own units of listed types within a radius; Martyrdom: Zealots within 6 m gain Frenzied 5 s). The Whirlwind
Zealot's `units.json` entry gets both (the sim ships the minimum entries; the data track pins the pages after). (3) The
`selfAura` ability kind (no target; effects resolve around the caster at `radius`; docs/02 "Self / aura": Blood-oil Frenzy
is Teblor, so ship the kind with a test-only ability and no shipped unit). **Slice b (next):** `spawn` effect + temporary
units (lifetime ticks, no cost / pop, freed on expiry; docs/factions/shadow.md Wraith: 20 s), `targetUnit` kind,
`autocast` (the sim casts when a target is in range; the toggle is a command), the AI hook noted for M5.

**Design references:** docs/02 "Ability system" (kinds table, effects vocabulary, autocast), "Status effects" (Frenzied,
stacking rule); docs/factions/whirlwind.md Zealot passives (50 % HP, +50 % attack speed, 6 m, 5 s); docs/03 "Implementation
(M4-4a / 4b-1 / 4b-2)", "For the data track", `DataLimits.PlannedAbilityEffectKindIds`; docs/01 rows 2026-10-09 / 10.

**Watch-outs:** every number in data (no constant for 50 % or 6 m); a passive must cost nothing per tick for units without
one; the memory re-baseline (above) in the same task, itemised; golden: `statuses.json` + the Zealot entry move `data-hash`
(regen once, every `k` line equal; rule 3); the data track is off `common/statuses.json` and the Whirlwind `units.json` /
`abilities.json` that session; a new `UnitState` (none expected) means the `ui.json` line (rule 2); `CommandDoorFuzz` kinds
if a new command kind (autocast toggle) arrives; `Rts.Sim` warnings as errors.

**Tests required:** loader tests per new field and kind (errors at their paths); `FrenziedTests` (cooldown shortening,
strongest wins, expiry); `PassiveTests` (the Zealot at 49 % / 50 % / 51 % HP; Martyrdom reaches 6.0 m not 6.01, own
Zealots only, 5 s); `selfAura` resolve rows; `StateHashTests` for every new field; an invariant fuzz with passives and
twins; a replay round trip; Perf alone (`EmptyTick(2500)` < 500 µs; a 500-unit brawl with 100 Zealots 0 B).

**After:** slice b, then M4-5 stealth / detection (the Stealthed / Revealed statuses, `detector`, the Priest's 10 m), the
fog-on sandbox Playable, the end-of-M4 hardening (BUG-0302 b / c, 0275, 0271 / 0272 decision, 0144, 0142, 0113 item 2,
0094, 0391), sign-off.

## View track

### Next task candidate: M4-V6c · zone visuals + the Blinded marker · feature · QA standard · ~800 lines

**First, if BUG-0390 is still open at PLAN** (the conductor's round did not land): rewrite `QaGhostViewTest`'s seed-6 walk
check per the bug file (the unit that ended the order came within its sight of the footprint and walked ≥ 10 m toward it,
per-unit start distances; drop the `--strict` gate so `|ordersEnded - ghostGone| > UpdateInterval` always fails). No
`Check` weakened; the loop 39 / 39.

**Goal.** Show the storm: a disc per live zone from `World.Zones` (docs/03 "For the view (M4-V6c)": `Alive`, `Owner`,
`Center`, `Radius(slot)`, `BlocksVision(slot)`, `TicksRemaining`, `AbilityId` for the name), drawn for the local player's
own zones always and for an enemy's only where the player may see it (`Fog.Visibility`: a hidden cell reads explored, and
`Fog.CanSeeUnit` already hides the units in it), fading in its last second; the Blinded marker gets its own colour
(`StatusKind.Blind`, today `OtherStatusColor` pale) readable at zoom 30 beside Burning / Slowed (a windowed shot). Keep the
0 B rows. A `ViewApi.ZoneDiscs` (pure: which zones to draw, radius, alpha) with a hash twin; `AbilityViewTest` rows (a storm
cast: disc appears at the resolve, lasts 12 s, the enemy's disc only where seen; `--no-fog`); QA's own scene.

**OUT:** stealth visuals (M4-5), the Frenzied marker (after M4-4b-3 lands: one `StatusKind` colour line), `game/data/**`.

**Design references:** docs/02 "Fog of war" (what explored fog shows), "Zones"; docs/03 "For the view (M4-V6c)",
"Implementation (M4-V6a / b / VH2)". **Watch-outs:** dumb views; no per-frame allocation; the disc must not reveal a storm
the player cannot see (a storm in unexplored ground: nothing drawn); absolute paths in test I/O.

**After:** the Frenzied / passive markers (one line each), stealth visuals (M4-5: a stealthed own unit drawn translucent,
enemies not drawn), the sandbox Playable scene with fog on, the end-of-M4 view hardening (BUG-0400, 0250 item 2, 0148 item
1, 0126 items 5 / 6, export hygiene).

## Data track

### Next session: STOP (planned), unless an inbox note asks for a text tweak

Why: M4-4b-3 (sim) edits `common/statuses.json` (Frenzied) and the Whirlwind `units.json` (the Zealot's passives), so the
pending `blinded` text change (Producer proposal: "can't target" → "can't attack anything more than 3 m away") cannot go in
the same session (never two tracks in one `game/data/` file); no new schema it needs is on `main` yet; the balance pass
waits for the fog-on sandbox. If the owner's inbox answers the wording or the balance proposal, that comes first (QA light,
one golden regen, off the files the sim edits that session).

**D11 candidate (the session after M4-4b-3 lands):** pin the new content (the Frenzied row, the Zealot passives' text on
whirlwind.md, any `selfAura` test ability kept out of the pages), the `blinded` text, BUG-0410 (the description-number check
by role: duration vs cooldown vs magnitude), one golden regen. Then the owner's tweaks, the full balance pass (QA standard)
once the sandbox gives numbers, `ai.json` build orders (M5), M7-M9 faction data.

## Watch-outs (all tracks)

- **Integration gate (this session):** merge sim; merge view and run the scene loop: `QaGhostViewTest` seed 6 fails
  (BUG-0390) until the view's walk check is rewritten (conductor-dispatched round on the view branch, then loop 39 / 39);
  then data; full non-Perf suite + smoke on each merged result. Conflicts: `studio/bugs/README.md` (union by id: the sim's
  rows for 0157 / 0241 / 0242 / 0270 / 0311 / 0330 / 0360 / 0361 / 0363, the view's for 0126 / 0250 / 0251 / 0281 / 0370 /
  0371, the data's for 0380; new 0390 / 0391 / 0400 / 0410), `studio/qa/coverage.md` (all three sides), docs/01 (the sim's
  M4-H2 row and the data's D10c row both appended after the same line: keep both, sim first), docs/03 (different sections).
- **Golden:** only the data branch moves `data-hash` this session; the sim's hash additions are live-only. The merged tree
  runs the golden and the seed-21 rows in the non-Perf suite.
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name. Stale scratch worktrees (15 in
  `git worktree list`, incl. this session's `gamedev-base`) are the owner's housekeeping.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
