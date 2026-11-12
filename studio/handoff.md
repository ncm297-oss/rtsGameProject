# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-11-2256** (session 3 / 8 of 2026-11-11). Sim **M7-5** ACCEPT, view
**M7-V3b** ACCEPT, data **D28-L** ACCEPT (0 fix rounds each, all three QA PASS). **The Teblor roster is on `main`.**
**M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 1 / 4 (criterion 2 ticked).** Counters: **sim 1 / 4, view 2 / 4,
data 2 / 4**. Bug ids next session: **sim 1280+, view 1290+, data 1300+**. Open S1 / S2: none. Inbox New: empty at ACCEPT.
The next session starts on 2026-11-12 (1 / 8 of that day).

**Next session in one line (proposal):** sim **M7-4** (Blood-oil Frenzy's aftermath and +20% damage, a tech effect on a
status's duration, a building `scale`; feature, QA full) · view **M7-V3c** (BUG-1260: giants drawn no wider than the room
they take, a scene with the real Teblor; feature, QA standard) · data **D29** (BUG-1241's AI half inside `ai.json`, only
if the PLAN's measurement shows it is fixable without M7-4's rules; else STOP until M7-4 is on `main`).

**PLAN 2026-11-12-0546 (session 1 / 8 of 2026-11-12): sim GO M7-4 · view GO M7-V3c · data GO D29 (a measurement; lands
nothing in the shipped files).** Each track's "Current session plan" is in its section below. Three things differ from the
proposal above, decided at this PLAN (docs/01 2026-11-12):
- **M7-4 puts Frenzy's damage and aftermath on the status row** (`statuses.json` `damageBonus` / `onEnd`), not on the
  ability's effect: no new stored state, no save format, and the Zealot's `frenzied` stays apart by id.
- **D29 measures and lands no shipped file.** The PLAN's scratch probe (QA's eight pairings, seed 1): shipped opening Age
  II in 1 of 8, a wave in 4 of 8 (BUG-1241); the eight Gatherers after Age II: 1 of 8 and 5 of 8; Armor I and the
  Gatherers after every other step: **3 of 8 and 6 of 8**. The order moves it but does not reach the target. Landing
  `ai.json` now would also move the golden header in the same session as M7-4's hash change (two tracks, one file): the
  chosen opening lands next session with M7-4's rows, one header move.
- **A giant's placeholder is about twice a soldier each way** (1.5 m across, 3.75 m tall against 0.8 m and 1.8 m): the
  height formula `(2 radius + 1) x scale` is unchanged; earlier notes said "one and a half times as tall", which was
  wrong for the capsule.

## Where things stand

- **`main` after this session = sim 1262b0e + view 4ff96b0 + data 60619c8.** The Producer's scratch clone of that tree:
  build 0 / 0; the default loop **8,906 / 0 / 21 of 8,927 in 20 m 33 s**; smoke **PASS**; the scene loop **97 / 97**; the Stress pin
  classes, the memory rows and the wide-crowd rows **103 / 0 / 1 of 104**; the data filters **2,143 / 2,143**.
- **Three factions ship:** `common/factions.json` order `malazan, whirlwind, teblor`. Ids: `teblor_blood_oil_anointing`
  tech 9, `blood_oil_frenzy` ability 3, units 14-20, buildings 20-29. The golden header is **67269D955D18D885**; every
  earlier save and recording refuses (`DataMismatch`); no `k` line moved.
- **The held branch `studio/2026-11-11-1317-data` was never merged** (D28-L brought its files over). It can be deleted.
- **Not in the Teblor data yet** (the page says so in each place): Frenzy's +20% damage and aftermath slow, the
  Anointing's +4 s (M7-4), the buildings' 1.25× (Request 47), the Gatherer's 15 a trip (Request 48), the AI's later-wave
  rules (Requests 46 / 51).
- **On screen the giants are wrong (BUG-1260, S3):** drawn 1.125 m wide on 0.75 m of room, so they stand inside each
  other. Pictures: `studio/review/2026-11-11-2256/`. M7 criterion 1 (scale) is not ticked for that reason.

## Standing rules (1-56)


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
**56 (new):** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
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
- **The third faction's order of work now:** sim M7-4 (the Frenzy aftermath and +20% damage, Requests 47 / 49 / 50) and
  view M7-V3c (BUG-1260) next session → the data's building `scale` and the balance pass (BUG-1241, Requests 46 / 48 / 51)
  → models (the owner's art call first).

---

## Sim track

### Where we are
M7-5 on `main` (BUG-1222 fixed). Counter **1 / 4**. Debt: BUG-1220 / 1221 S4, the three `SaveFormat` alias names
(BUG-1161 S4), BUG-1149 / 1151 / 1152 S3, BUG-1150 S4, BUG-1010.

### Current session plan (2026-11-12-0546): M7-4, the rules Blood-oil Frenzy needs (feature, QA full; bug ids 1280+)

**Goal.** Give the data the four things the Teblor page asks for and the files cannot say yet: a Frenzy that also hits
harder, a Slow when it wears off, an upgrade that lengthens it, and a drawn size for buildings. Generic rules with data
hooks; the Teblor rows are the data track's next session. This is the last sim piece of M7 criterion 3.

**Scope.**
- **`statuses.json` `damageBonus`** (optional, a `frenzied`-kind row only; above 0, at most 4; an error on any other
  kind). A unit's own attacks (melee swings and shots, against units and buildings) deal attack x (1 + the largest
  `damageBonus` among its frenzied-kind entries). Read where the hit's attack value and tech attack bonus are read today;
  rounding by `DamageCalc`'s existing rule, stated in docs/03 with a worked number (22 at +20%). Not ability damage, not
  damage-over-time pulses, not zones. Attack speed stays the entry's magnitude (largest across frenzied-kind entries).
- **`statuses.json` `onEnd`** (optional, any row): `{ "status", "magnitude", "duration" }`, validated like an
  `applyStatus` for the named status's kind. The named status may not be the row itself and may not carry an `onEnd`
  (no chains: load errors). A `whenBelowHp` passive naming a row with `onEnd` is a load error (a held entry has no end).
  When an entry of the row runs out of time in phase 5 it is removed, then the named status is applied to the same unit
  through `StatusSystem.Apply` (the normal stacking rule), source = the ended entry's source player. It does not fire
  when the unit died, when the entry was refreshed instead of ending, or when the entry was removed early
  (`RemoveEntry`). The applied status lasts exactly its duration: it must not be counted down in the pass that applied it.
- **A re-cast during the aftermath** (decided): the Frenzy entry comes back, the Slow runs out its own time, and when
  the second Frenzy ends the Slow is applied again by the stacking rule. Two frenzied-kind rows on one unit (a Zealot's
  `frenzied` and the new kind of row): two entries, speed and damage each take the largest, only the row with `onEnd` slows.
- **Tech stat `statusDuration`** (Request 49; model it on `abilityCooldown`): an effect names statuses and an amount in
  seconds. While a player has the tech, every application of a named status whose source is that player (ability
  effect, passive, `onEnd`) lasts that much longer, decided when it is applied; entries already running do not change;
  never under one tick. No new stored or hashed state: anything derived is rebuilt on load.
- **`buildings.json` `scale`** (Request 47): optional, default 1, the same range, validation and hash treatment as the
  unit's (M7-1); `BuildingDef.Scale`; the sim never reads it.
- **`GameData.ContentHash`** covers every new field (`DataContentHashTests` rows). If that moves the shipped data's hash,
  regenerate the golden header once, every `k` line identical, the reason in `GatherWedgeQaTests`' comment (rule 49).
- docs/03 "Implementation (M7-4)" with a "For the data track (M7-4)" block: the exact JSON for a Frenzy status row
  (`damageBonus` 0.2, `onEnd` slowed 0.2 for 4 s), the ability's effect, the Anointing's +4 s effect, a building `scale`.
- **Out of scope:** any shipped data row (`game/data/**` is not touched: tests use test-only data or copies);
  docs/02's tables and `docs/factions/**` (the data track's, with the rows); the view; Requests 46 / 48 / 51; AI
  casting; BUG-1220 / 1221 / 1161 / 1149-1152.

**Acceptance criteria.**
1. Loader: each new field loads; each bad value is one error at its path (a `damageBonus` on a non-frenzied row, 0, above
   the cap; an `onEnd` naming itself, an unknown id, a row with its own `onEnd`, a magnitude or duration its kind refuses;
   a `whenBelowHp` passive on an `onEnd` row; a `statusDuration` effect naming an unknown status; a building `scale` out of range).
2. Damage: a unit with a frenzied-kind entry of `damageBonus` 0.2 deals the documented rounded value on melee and on
   ranged hits, against a unit and a building; without the entry, and with a plain `frenzied` entry, the old value; the
   bonus stops with the entry (a test for a shot in flight when it ends, the answer stated).
3. On end: the named status is on the unit the tick the entry ends and lasts exactly its duration in ticks (pinned);
   not on death, not on a refresh, not on an early removal; with the unit's status store full it still lands; the
   re-cast case above; the two-rows case above.
4. Tech: with the tech an application lasts amount longer, without it not, a running entry is unchanged by the research
   finishing, another player's applications are unchanged; after a save and load mid-match the next application still
   gets it.
5. No shipped file under `game/data/**` changes; every pinned match digest and every golden `k` line is identical; if
   the header moved, it moved once, with the reason comment. No save or replay format version changes.
6. Determinism and fuzz: a Stress fuzz on test data that uses all three rules (3 seeds x 2,000 ticks, twins, a replay
   round trip, a save / load in the middle of an aftermath) with the unit invariants; `StateHashTests` rows showing the
   aftermath is ordinary hashed status state.
7. No allocation in the per-tick and per-hit paths (`AllocationTests`); the Perf rows' budgets unchanged, the brawl row
   with 100 frenzied units measured before and after and printed (a failure counts only under rule 42).
8. Build 0 / 0; the default loop green; the Stress pin classes green; smoke PASS; docs/03 written; every test process ended.

**Design references.** docs/factions/teblor.md "Abilities" and "Faction upgrade" (+20% damage, Slowed 20% for 4 s, +4 s);
docs/03 "Implementation (M4-4a)" (stacking, phase 5), "(M4-4b-3a)" (Frenzied, "Known limits" (1)), "(M7-1)" (unit
`scale`, the hash), "(M7-2)" (why the aftermath waited); docs/02 "Status effects"; docs/01 2026-11-12 (b).

**Tests required.** Loader tests per field; `Abilities/` tests for damage, on-end and the tech; `DataContentHashTests`
rows; `StateHashTests` rows; a save round trip; the Stress fuzz; an allocation row; the golden replay tests re-run.

**Constraints.** No stat in C# (the 0.2, 4 s and +4 s exist only in test data and docs). No allocation per tick or per
hit. Entry order and removal inside phase 5's loop decide determinism: say in docs/03 where the applied status sits and
when it first counts down. Rule 43 does not apply (no shipped data uses the fields, the Teblor are in no pinned match);
rule 50 does not apply if no format changes: if one must, stop and report instead. Size around 800 lines; if it runs
over, deliver the status fields and the tech first and report the building `scale` as left.

**QA focus.** Full tier. Attack the phase-5 loop: an entry ending while another entry of the same unit ends the same
tick; the applied status merging into an existing stronger or longer Slow; a unit dying from a pulse the tick its Frenzy
ends; 16 units ending on one tick in slot order; an `onEnd` naming a damage-over-time or a stealthed status. The damage
read: tech attack bonus plus `damageBonus` plus a class multiplier, rounding at .5; a shot launched frenzied landing
after. The tech: research finishing the same tick as an application; a negative amount down to one tick; load, then
apply. Twins in two processes and a replay round trip of the fuzz. Mutants: drop the on-end apply, count the applied
entry down in the same pass, read the bonus from a plain `frenzied` row. Run the scene loop once on the branch (the header may move).

### Next task candidates
1. **M7-4: Blood-oil Frenzy, whole** (feature, QA full; around 800 lines: a new hook on the status store). Request 50:
   Frenzy's +20% damage beside its +40% attack speed, and the aftermath slow when it ends (Producer default:
   `applyStatus.onEnd {status, magnitude, duration}`; read docs/03 M4-4b-3a "Known limits" (1) on the one-entry-per-id
   merge first and say in the brief what a re-cast during the aftermath does). Request 49: a tech effect on a status's
   duration (the Anointing's +4 s). Request 47: an optional `scale` on a building row (visual only, in the data hash, the
   sim never reads it; like the unit's).
   - Schema + validation + the minimum test entries only (the data rule): the Teblor rows are the data track's, after.
     If a Teblor file must change for a test, use a copy.
   - It changes a save format if the aftermath needs stored state: rule 50 (name the view scenes that pin those bytes).
   - Rule 54 / 55 rehearsal at PLAN is not needed (no new faction); rule 43 only if a Normal AI casts Frenzy in a
     pinned match (the Teblor are in no pinned match: say so in the brief after checking).
   - If the size is over budget, split: M7-4a (damage share + `onEnd`), M7-4b (the tech effect + building `scale`).
2. Requests 46, 48, 51 with the balance pass (measure which the pass needs before building any).
3. Hardening at 4 / 4: BUG-1220 / 1221, BUG-1161's names, BUG-1149 / 1151 / 1152.

---

## View track

### Where we are
M7-V3b on `main` (BUG-1231 fixed). Counter **2 / 4**. 97 scenes. Debt: **BUG-1260 S3 (new: the next task, not debt)**,
BUG-1230 S3, BUG-1177 item 2 (the owner's taste), BUG-1115 item 4.

### Current session plan (2026-11-12-0546): M7-V3c, the real Teblor on screen (BUG-1260; feature, QA standard; bug ids 1290+)

**Goal.** Giants are drawn 1.125 m wide on 0.75 m of room and stand inside each other. Draw every unit as wide as the
room it takes, keep `scale` for the height, and prove it on the shipped Teblor with a scene and pictures. This is what
M7 criterion 1 waits on.

**Scope.**
- `UnitViews.DrawnRadius` (both overloads) returns the collision radius alone. Everything that reads it follows with no
  second edit: the capsule's width, the selection ring (1.35 x), the target ring (1.7 x), the corpse disc and rim, the
  refused-cast cue disc. `DrawnHeight` (`(2 radius + 1) x scale`), the hp bar's length and height, the status and cast
  markers over the top and the shot heights stay as they are.
- A new scene on the shipped data, no forge, with the real Teblor (criteria 3).
- The scenes that pin `radius x scale` for the width or a ring are updated to the new rule; list every changed
  assertion in the report. Nothing else in them is weakened.
- Four screenshots of the same views as `studio/review/2026-11-11-2256/` (setup row, the base at the start, a Warrior
  beside a Heavy Infantry, giants in a group), written under the scratchpad, looked at and described (rule 56).
- BUG-1260 item 2 (the white first frame): reproduce it in a normal Teblor match start, or show it was the scratch scene's.
- docs/03: the formula paragraph of "Implementation (M7-V1)" and a short "Implementation (M7-V3c)".
- **Out of scope:** models; the buildings' 1.25 x (the data's row comes after the sim's M7-4); BUG-1230; any file under
  `sim/` (no ViewApi change is expected: if one is needed, stop and report); `game/data/**`.

**Acceptance criteria.**
1. For every shipped unit type the drawn width equals twice its collision radius and the rings and corpse disc their
   multiples of it (asserted over the whole roster in a scene); `scale` appears in the height only.
2. Scale-1 units are unchanged: every Malazan and Whirlwind type's mesh radius and height equal the old formula's, and one
   existing fixed-camera screenshot of a two-faction scene is pixel-identical before and after (both files named).
3. The new scene, on shipped data: (a) the setup's third row can be set to Teblor and Start works; (b) a Teblor base
   around tick 30: no two of the five Gatherers' drawn bodies overlap (centre distance at least the sum of drawn radii
   wherever the sim keeps them that far apart); (c) a Warrior beside a Heavy Infantry: width ratio 0.75 / 0.4, heights
   3.75 m and 1.8 m, printed; (d) "Regenerating" on a real hurt Warrior; (e) the War-dog's "x3" square in the Stables &
   Kennels queue.
4. The four screenshots exist, were looked at, and the report says in plain words what each shows (do giants in a group
   stand apart; does a giant read as taller than wide).
5. BUG-1260 item 2 answered with evidence.
6. Build 0 / 0; smoke PASS; the scene loop green with the new scene (98); the default loop green; docs/03 updated; the
   bug's proof lines as a patch in the scratchpad; every Godot and test process the developer started is ended.

**Design references.** BUG-1260; docs/01 2026-11-12 (a); docs/03 "Implementation (M7-V1)" / "(M7-V2)" / "(M7-V3a)";
docs/factions/teblor.md "Faction bonus" (scale 1.5, radius 0.75) and the unit table.

**Tests required.** The new scene; the updated M7-V1 / M7-V2 scenes; the roster-wide width assertion.

**Constraints.** Views hold no gameplay state and no number per faction in C#: width from `UnitDef.Radius`, height from
`UnitDef.Scale`, nothing typed. Player-facing text from data only. The hit test keeps the sim's radius. Never kill
another track's Godot or `testhost`.

**QA focus.** Standard tier. A forged unit with scale 3 and radius 0.3, and one with scale 0.5 and radius 0.9: rings,
bar, markers, corpse disc and shots still sit right. Thirty giants ordered to one point: do any drawn bodies overlap
more than the sim's own overlap. Selection by click and by box on a giant at its drawn edge. The War-dog (scale 1) beside
a giant. Mutants: put `x scale` back in one overload only; scale the ring but not the body. Compare the four pictures
with last session's. The `DebugOverlayTest` and `QaM25Test` 1 ms rows flake under load: rerun alone before counting them.

### Next task candidates
1. **M7-V3c: the real Teblor on screen (BUG-1260).** Feature, QA standard.
   - Producer default (record it in docs/01's change log and docs/03 at the PLAN, "owner may revisit"): the body's
     width, the selection and target rings and the corpse disc follow the collision radius alone; `scale` sets the
     height, the bar's length and height and the shot heights as today. One place: `UnitViews.DrawnRadius`. Shipped
     Malazan and Whirlwind units (scale 1) must render pixel-identical.
   - A scene on the shipped data (no forge) with the real Teblor: the setup's third row, a Teblor base at tick ~30 (five
     Gatherers not overlapping: assert drawn centres at least two drawn radii apart, or the sim's spacing), a Warrior
     beside a Heavy Infantry (width ratio 0.75 / 0.4, height ratio 1.5), "Regenerating" on a real hurt Warrior, the
     War-dog's "×3" square at the Stables & Kennels.
   - Screenshots of the same four views as `studio/review/2026-11-11-2256/` after the change, looked at and described
     (rule 56); the Producer copies them for the owner.
   - BUG-1260 item 2 (a white first frame): reproduce in a normal match or close it as the scratch scene's.
   - Out of scope: models, the building placeholder at 1.25× (after Request 47), BUG-1230.
   - M7 criterion 1 is ticked at that ACCEPT if the pictures hold.
2. BUG-1230 (long faction names) at the view hardening (counter 4 / 4 in two sessions).
3. An owner item (the art call, ".NET 10 installed", templates, the sound notes) whenever one lands.

---

## Data track

### Where we are
**D28 is on `main`** (D28-L). Counter **2 / 4**. BUG-1210 fixed. Debt: BUG-1241 S3 (the Normal Teblor AI is
gold-starved: the balance pass).

### Current session plan (2026-11-12-0546): D29, measure the Teblor AI's opening (BUG-1241; feature, QA standard; bug ids 1300+)

**Goal.** A Normal Teblor AI is still in Age I at ten minutes in 7 of 8 pairings and never attacks in 4. Find out how
far `ai.json` alone can fix that, hand over the best opening ready to land, and say exactly what the sim must add if
the file cannot do it. **Nothing in the shipped files changes this session** (the sim's M7-4 may move the data hash this
session; the chosen opening lands next session with M7-4's Teblor rows, one hash move).

**Scope.**
- Report tests in `sim/Rts.Sim.Tests/QA/Content/` or `Content/` (category Stress) that play QA's eight pairings
  (`TeblorAiMatchQaStressTests`: four maps, Teblor on seat 0 against Malazan and on seat 1 against Whirlwind, Normal
  against Normal, 12,000 ticks) on seeds 1, 2 and 3 with the shipped opening and with each variant, on a data copy
  whose Teblor `standard` opening is replaced. Per row: the tick Age II completes (or "not by 12,000"), waves started,
  Gatherers alive, gold and wood in hand at 7:00 and 10:00.
- Variants, at least: **V0** shipped; **V1** the eight Gatherers (step 19) after the Age II step in time; **V2** Armor I
  and the eight Gatherers after every other step (the PLAN's probe: seed 1, Age II 3 of 8, a wave 6 of 8); **V3** V2
  with the Age II step due earlier; **V4** the developer's best idea inside the schema (step order, triggers, times,
  counts, the composition weights, the wave thresholds). Read `BuildOrderExecutor.Run` and `AiPlayer.MayReserve` first:
  a step's cost is held back only while the army is at least one wave and the base is not defended, and only from the
  train steps after it in file order.
- The best variant with the seats swapped (Teblor on seat 1 against Malazan, seat 0 against Whirlwind), beside V0.
- The best variant's opening as a JSON fixture in the test folder, with one default-loop test that a copy of the
  shipped data carrying it passes the loader's validation.
- A written conclusion in the report: does any variant give Age II by 10:00 and a wave in at least 6 of 8 on each seed?
  If not, which rule is missing, with a per-minute gold table from one failing row (the bug file's format).
- **Out of scope:** `game/data/**`, `docs/factions/**`, the golden replay, any unit or building number, any C# outside
  the two test folders, the sim's Stress classes (call their helpers, do not edit them).

**Acceptance criteria.**
1. The report rows run and print the table for V0-V4 on seeds 1-3 (120 matches) and the swapped-seat rows; V0 on seed 1
   reproduces BUG-1241's figures (Age II in 1 of 8, a wave in 4 of 8) or the difference is explained.
2. Measure and report, not pass / fail (rule 48): the count of pairings with Age II by 10:00 and with a wave, per
   variant and seed, against the target of 6 of 8.
3. The proposed opening's fixture loads and validates on a shipped-data copy (default loop).
4. The conclusion names the opening to land, what the player would read differently (the opening's description says
   "four Warriors raid at 8 army pop": does it still hold), and either "the file is enough" or the missing sim rule
   stated as a request (what, where in `ai.json` or `rules.json`, the measured reason).
5. `git diff` against the base touches only the two test folders; build 0 / 0; the default loop green; every test
   process ended.

**Design references.** docs/factions/teblor.md "AI build order (Normal)", "Opening 1" and "Spending on the menu's map";
BUG-1241; docs/03 "Implementation (M5-4g)" (the reserve) and "(M5-8a)" (openings); docs/02 "AI opponent".

**Tests required.** The report class (Stress); the fixture's validation test (default loop).

**Constraints.** The AI plays through its fog-filtered view and the command API: the tests change data, never the AI's
state. No shipped file moves, so no pin moves: if one does, something else was touched. Rule 41: the sim's task this
session changes no shipped behaviour, so the figures hold on the merged tree; QA re-runs one variant there.

**QA focus.** Standard tier. Re-run V0 and the proposed variant on seed 1 and on one seed the developer did not use
(4 or 5): does the improvement hold. Check the variant copy really differs only in the opening (diff the two loaded
openings). Is the proposed opening worse anywhere (a pairing that reached Age II or sent a wave on V0 and does not now)?
Does a proposed opening still expand, build every hall and research the Anointing by 15:00? Check the conclusion's
missing-rule claim against `MayReserve` and the executor's code, not against the developer's words.

### Next task candidates
1. **D29: the Teblor AI reaches Age II (BUG-1241's opening half).** Standard tier (it needs scenario numbers). Only
   `factions/teblor/ai.json` and the page's opening table: the bug file's first candidate (the Age II research before
   the eight extra Gatherers in time; the Armor upgrade after it), no unit cost, no stat. Measure on QA's eight pairings
   (`TeblorAiMatchQaStressTests`) on three seeds, both seat orders: Age II by 10:00 and a wave sent in at least 6 of 8,
   or report why the order alone cannot do it (then the fix is Request 51's gold reserve, the sim's, and the task ends
   as a measurement).
   - `ai.json` is in the content hash: the golden header moves again (rule 51: the data brief lists the sim pin classes
     and runs their Stress rows; the reason goes in `GatherWedgeQaTests`' comment). The Teblor are in no pinned match:
     check `OutcomePinM73bQaTests` stays equal.
   - At PLAN: confirm on a scratch clone that the reorder moves the result at all (one pairing, one seed) before saying GO.
     If it does not, STOP the data track until M7-4 and Request 51.
2. After M7-4 is on `main`: the Teblor rows for Frenzy's damage / aftermath / the Anointing's +4 s and the buildings'
   `scale` 1.25, the page and the pins both ways.
3. The full balance pass (several seeds, both seat orders, the D21-style pairing rows; the 0.75 m crowd figures as an input).
4. The owner's answers if any land (the Teblor table, the counters, `goldShare`, BUG-0780, `counterStrength`).
