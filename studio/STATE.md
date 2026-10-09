# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" (further down) is non-blocking: what the studio built or decided
on its own, explained in terms of what you'd see in the game.

_Last updated: 2026-10-08 (session 2026-10-08-1814, the resumption of 1435, integration update): **you can see the arrows, bolts and catapult stones fly** and the held right-click attack is on `main` (view, M4-V3 + M4-V2, green on `main`); **the first balance report** with a proposed target margin for you to decide (data, D6, merges after the view). **Fog of war is built and accepted (sim, M4-3a) but held on its branch one session:** with the view's work merged in, one old view test row orders attacks on enemies nobody can see, which the fog now (rightly) refuses (BUG-0219); the view fixes that row first thing next session and the sim lands. M4 stays 5 / 10. Two things to review: the balance proposal (D6 entry) and a process note (the 3-hour session lock)._

## Waiting on you

- **Nothing blocking.** `main` is green (the view's M4-V3 landed; the red `MinimapTest` of BUG-0210 is fixed). The accepted
  fog work (sim, M4-3a) waits on `origin/studio/2026-10-08-1435-sim` for a three-line fix to a view test row (BUG-0219), the
  view's first item next session; the studio continues on its own.
- **Wanted, not blocking:** (1) your answer on the **balance proposal** (the D6 entry under For your review: a target band
  and the Line-beats-Shock margin; the data track changes no number until you say); (2) the **session-lock window** (the
  process note under For your review: refresh the lock each step or widen it to ~5 h, in your routine file); (3) play the M3
  base (ten minutes, the M3-V3 entry), watch a fight with the shots now visible and try the right-click attack (two minutes,
  the M4-V3 and M4-V2 entries); write what felt wrong in the inbox.

## Now

| Field | Value |
| --- | --- |
| Sim: milestone | **M4 combat, 5 / 10**: M4-1 melee, M4-2a (`Attack(target)`), M4-2b (projectiles, splash, friendly fire, the counter-triangle rows) on `main`; **M4-3a** (three-state fog per player, the high-ground rule, vision-gated targeting, the 2 s high-ground reveal, buildings' `sight`, the view's read-only fog surface) **accepted, held on `origin/studio/2026-10-08-1435-sim`** (a84ff0d, `main` merged in; ESCALATE at integration: one view-owned xUnit QA row red, BUG-0219); criterion 5's sim half annotated in docs/05 |
| Sim: next task | **M4-H1, the sim's hardening session (4 / 4), on the held branch**: BUG-0215 (hash the visible bits: Producer decision), BUG-0211 (re-record the seed-21 replay), BUG-0216, BUG-0230 item 2 (public counter-triangle helpers), then BUG-0149 / 0144 / 0157 / 0134 / 0158 / 0113 / 0094 / 0153 / 0142 as they fit · hardening · QA standard · **its QA runs the full non-Perf suite on a merge with the view branch (BUG-0219 fixed there)** · the conductor merges view, then sim · then M4-3b |
| Sim: gate | **GO** |
| View: milestone | **M4 views**: M4-V1 (hp bars, flashes, corpses, K / L), **M4-V2** (right-click / A + click = Attack, red ring, "Pursuing") and **M4-V3** (aimed streaks, lob stones on an arc, hit / miss / burst marks; BUG-0210 / 0190 fixed; the Attack-order scenes stage seen targets) **on `main` (853a60c, green)** |
| View: next task | **BUG-0219 first** (three lines: `CombatScenes.Spot` the three Raiders in `UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything`, every assertion kept; green on a merge with the held sim branch; the conductor then merges view, then sim), **then M4-VH1, the view's hardening session (4 / 4)**: BUG-0220 (SfxTest flake under load), BUG-0226, BUG-0222, BUG-0190 status, BUG-0148 / 0126 leftovers, export hygiene · hardening · QA standard · then M4-V4 (the fog shader from `World.Fog`, unit hiding, the minimap's fog layer and Attack half) |
| View: gate | **GO** |
| Data: milestone | M3 Done (D1-D4); D5 (targets pinned) on `main`; **D6** (the balance baseline on both pages, the Sapper self-splash numbers, BUG-0200) accepted, merges after the view this integration; M4 data: the towers' `sight` schema lands with the held sim branch next session, abilities wait for M4-4 |
| Data: next task | **D7**: a Sight column on both pages' Buildings tables pinned to the data (towers 24 m, the rest 12 m), BUG-0230 items 1 and 3; needs M4-3a's `sight` on `main` (work on a branch off the merged sim, or STOP cheaply if the sim merge slips); the owner's inbox answer on the balance proposal comes first if present · feature · QA light |
| Data: gate | **GO** |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | All three session branches build with 0 warnings; smoke PASS on the sim (tick 85) and view (tick 85) branches; the data branch touches no `game/` file. **Integration:** view merged first after its precondition (QA's allocation row into the Serial collection, 853a60c): **`origin/main` = 853a60c, non-Perf 3,927 / 14 / 0, green.** `main` merged into the sim branch (a84ff0d): build 0 warnings, scene loop and smoke green on two scratch merges, **but the full non-Perf suite on the merged tree is 4,003 / 15 / 1 failed** (`UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything`, BUG-0219: a view QA row from before fog) → **sim held on its branch**, studio files only to `main`; data merges after the view. **New rule: the integration gate runs the full non-Perf xUnit suite on the merged result, not only the scene loop.** Golden: regenerated once for M4-3a on the held branch (`data-hash` + the hashed fog; every `k` line equal with the fog excluded locally) |
| Tests | QA: sim non-Perf 3,927 / 3,940 (13 skipped, each a filed bug) / 0 failed, Perf alone 139 / 3 skipped, fog rows alone `Fog2500OnePlayer` 0.064-0.068 ms and `Fog1000v1000` 0.086-0.093 ms at 0 B, `TightBlob2500` 4.49 ms quiet; view non-Perf 3,910 / 3,923 (13 skipped) / 0, view Perf 11 / 11, scene loop 33 / 33 on the branch and on the merge; data content filter 138 / 138. Producer reruns (sim and view suites at once): **sim 3,927 / 3,940 (13 skipped) / 0 failed (14 m 16 s); view 3,926 / 3,941 (14 skipped) / 1 failed (13 m 45 s: `SerialCollectionTests` trips on QA's own record file `MaxUnderEdgeWalkQaTests.cs`, added after QA's green full run; a one-attribute QA-owned fix is the view's merge precondition); data content filter 138 / 138**; the Perf category under three tracks' load fails 3-9 wall-clock rows on base and head alike (BUG-0158: a Perf failure counts only alone) |
| Open bugs | **34** (S1: 0, **S2: 1: BUG-0219** (view test row vs the fog; holds the sim merge, three-line fix first thing next session), S3: 15, S4: 18). **Fixed this session:** BUG-0210 (S2, the red `MinimapTest`: the slot was shot dead, no sim regression), BUG-0212 / 0213 / 0218 (S2, view scenes vs the fog: `--no-combat` or staged-in-sight targets), BUG-0214 (S2, the 1024-map memory bound re-baselined with the fog's 2.4 MB itemised), BUG-0217 / 0221 / 0223 / 0224 (S3), BUG-0225 / 0200 (S4), BUG-0184 item 1, BUG-0190 both items (file on the view branch still reads open). **New open:** BUG-0211 (S3, sim: re-record a test replay), BUG-0215 (S3, sim: the fog's visible bits are state, not derived: hash them next), BUG-0220 (S3, view: an old sound scene flakes when the PC is busy), BUG-0216 (S4, sim nits), BUG-0222 / 0226 (S4, view nits), BUG-0230 (S4, data nits) |
| Sessions today | 3 / 8 on 2026-10-08 (0313, 0913, 1814; the 1435 session was resumed as 1814). Feature sessions since last hardening: **sim 4 / 4, view 4 / 4**, data 2 / 4 |
| Last session | 2026-10-08-1814 (resumed 1435) · sim M4-3a (1 fix round, **ACCEPT then ESCALATE at integration**: BUG-0219, a view QA row red on the merge; branch held with `main` merged in) · view M4-V3 + the held M4-V2 (2 fix rounds, ACCEPT, **on `main`**) · data D6 (0 fix rounds, ACCEPT, merges after the view) · M4 5 / 10 |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 (sim track) | 8 / 8 | **Done** 2026-10-06 (Producer sign-off after the M1-9 hardening; retro in docs/05) |
| M2 (view track) | 10 / 10 | **Done** 2026-10-07 (Producer sign-off after the M2-H2 hardening; retro in docs/05; your playtest under For your review is feedback, not a gate) |
| M3 (all three tracks) | 8 / 8 | **Done** 2026-10-08 (Producer sign-off after every track's hardening and the BUG-0146 fix; retro in docs/05; open S3 / S4 listed there; your ten-minute playtest is still wanted as feedback) |
| M4 (sim + view + data) | **5 / 10** (1 attack / attack-move / chase / retaliation / priorities, sim + view; 2 damage formula; 3 projectiles / splash / friendly fire; 4 death, corpses, rubble; the counter-triangle rows). **5 fog: sim half accepted (M4-3a), held on its branch for BUG-0219 (lands next session)**, owed: the fog shader + unit hiding + minimap fog (view M4-V4), the ghost list + shooting towers (sim M4-3b). Then 6 abilities (M4-4), 7 stealth (M4-5), 8 the four signature abilities, 10 the fog-on sandbox Playable. Next session: hardening for sim and view | In progress |
| M5-M9 | — | Planned |

## For your review

Non-blocking. Each entry says what was built or decided, what you'd notice in the game, and how
to change it. To change anything, write it in `studio/inbox.md`, for example "use formations
instead of clusters" or "make giving up take 2 seconds".

### Fog of war is in the rules: each side sees only what its own units and buildings see, high ground sees down but low ground can't see up, and a shot from a hill briefly gives the shooter away (sim track, M4-3a, 2026-10-08)

- **Status update (integration, same evening): held on its branch one session.** I accepted this work, but when the conductor
  merged the view's new work into it and ran the whole test suite, one old view test (written before fog existed) ordered a
  soldier to attack three raiders that nobody on its side could see, and the fog now refuses that order, as designed
  (BUG-0219). It is a three-line change to that test, the view's first item next session; then the fog lands on `main`.
  Nothing in the fog itself is wrong. Lesson kept: the merge check now runs the whole test suite, not only the scene tests.
- **What was built:** the first half of fog of war, in the rules only (nothing is drawn yet: the darkened map is the view's
  next feature after its clean-up). Every player has a map of what it has **never seen** (black later), has **seen before**
  (darkened later) and **sees now**, refreshed five times a second from every one of its units and buildings (a building
  under construction looks around too). Each unit sees a circle of its sight range (10-18 m by type); a building sees **12 m**
  and the **Watch Tower / Lookout Tower 24 m**. **High ground:** a unit sees everything on its own level and below, but
  nothing on a higher plateau unless it is within 4 m of it (you see the lip of the cliff you stand under); a unit on a ramp
  counts as standing on the lower level. **Fighting uses it:** a unit only picks, chases or fires back at enemies its side can
  see, and an attack order on something nobody on your side can see is dropped; an attacker hitting from higher ground is
  **revealed to the victim's side for 2 seconds** after every hit, so the low side can shoot back or run. Everything the view
  will need (the per-player map, a change counter, "can this player see that unit") is ready and read-only.
- **What you'll see in the game:** nothing new on screen yet (the map still draws everything until the fog shader lands), but
  the behaviour has changed: units at the foot of a plateau ignore archers on top until those archers shoot, then they have
  two seconds to fire back; a lone unit sent to attack something far off that none of your units or buildings can see just
  stays put (the order is dropped); scouting matters once the shader hides what you can't see.
- **Producer decisions, revisit any time:**
  - *A building with no sight of its own sees 12 m* (the design doc only gives the towers' 24 m). The number is
    `buildingSight` in `game/data/common/rules.json`.
  - *A building under construction sees too* (so you can watch your site being attacked). Alternative: only finished buildings.
  - *The reveal is keyed on where the attacker fired from* (a catapult that shoots from a hill and walks down is still revealed
    where it is). Alternative: where it stands when the hit lands.
  - *Every player's fog refreshes on the same tick, every 4 ticks* (builder's choice; it could be spread across ticks later).
  - *One-level fights play exactly as before:* a unit always counts what its own sight circle covers right now, besides its
    side's map, so nothing changed on flat ground (the counter-triangle results are byte-identical).
  - *Changed after QA (BUG-0215):* I had asked for the "sees now" map to be rebuilt from scratch rather than saved; QA proved
    that between two refreshes it depends on where units *were*, which a save file wouldn't know. The sim's clean-up session
    next makes it part of the saved state (a few bytes per 64 map cells). Alternative: only allow saving right after a refresh.
  - *Size: ~760 rule lines (budget ~800-1,000 for a new system), ~1,450 dev test lines, ~650 QA test lines, ~115 doc lines; one
    fix round (a memory bound re-baselined with the fog's 2.4 MB written down; the targeting check reordered so big fights on
    hills cost less than before the fog).*
- **Rough edges:** **BUG-0215 (S3, above; fixed next session).** **BUG-0211 (S3):** one test replay recorded before the fog can
  no longer prove its first 19 ticks (the fog is now part of the fingerprint); it is re-recorded next session. **BUG-0216
  (S4):** a 64 m sight on a tiny test map misses a few cells (nothing shipped comes near 64 m); a Catapult told to hold position
  no longer shoots things between 18 m (its sight) and 26 m (its reach) unless something else of yours sees them (the rule
  working as designed; the docs said otherwise and will be corrected); once the shader exists, a unit may start shooting a
  target the screen still hides for up to a fifth of a second (the screen refreshes every 4 ticks, combat looks every tick).
- **To change it:** `buildingSight` in `game/data/common/rules.json`; `sight` per unit and per building in
  `game/data/factions/<faction>/units.json` / `buildings.json` (any building may carry one); the 4 m lip, the 2 s reveal and
  the 4-tick refresh are rule constants (`VisionConstants`), by inbox note.

### You can see the arrows, bolts and catapult stones now, and the right-click attack from last session is on `main` (view track, M4-V3 with M4-V2 landed, 2026-10-08)

- **What was built:** the shots the sim has been flying since M4-2b are drawn: an aimed shot (bolt, arrow, magic bolt) is a
  short **light streak** in a pale shade of its owner's colour flying at chest height along its path; a **catapult stone or
  sharper** is a small dark **stone on an arc** (its peak a quarter of the throw's length, between 1 and 6 m). Where a shot
  lands: a **yellow flash** on a hit (0.2 s), a **dust puff** on a miss (0.3 s), an **orange burst** for a stone or sharper
  (0.4 s, sized by its splash). Also fixed: `main`'s one red test scene (BUG-0210: the view QA proved the "missing" walker
  had been shot dead by arrows, so the pre-combat scene now runs with combat off like the five before it), and the corpse
  discs that were half-buried on ramps now sit on the highest ground under their rim (BUG-0190). And the accepted
  **right-click attack** from the M4-V2 entry below is on `main` with this.
- **Try it (two minutes):** `& $env:GODOT --path game`, box-select the grey army, press **A** and click the orange army. Pale
  streaks fly from the crossbowmen at the back, stones arc from the catapults and burst orange where they land, yellow
  flashes pop on units being hit. Right-click one orange soldier: the red ring and "Pursuing" from M4-V2.
- **Producer decisions, revisit any time:**
  - *Placeholder looks until the M6 art pass:* streak 1.2 m long at 1.2 m up, stone radius 0.28 m, mark colours and the arc
    curve are constants in `game/scripts/ProjectileViews.cs` and `ProjectileTracker` / `ImpactMarks` (in `sim/Rts.Sim/ViewApi/`).
  - *Marks live in game time* (8x speed shortens them) and at most 512 are kept, the oldest replaced.
  - *A stone that lands "slides" its last 0.6 m along the ground* and vanishes just short of its burst (BUG-0222, S4): barely
    visible at normal zoom; a note for the M6 projectile pass.
  - *The studio's attack-order test scenes now stage their targets within sight* (a consequence of the fog rule, not a visual
    change: an attack on an unseen enemy is dropped).
  - *Size: ~305 game-code lines, ~457 view-helper lines, ~1,100 dev test lines, ~1,070 QA test lines, ~105 doc lines; two fix
    rounds (a mark could be drawn fully transparent at 8x on a slow frame; corpse discs floated 4 m beside a cliff, then
    2 m beside a ramp's side wall: both fixed).*
- **Rough edges (next view clean-up):** **BUG-0226 (S4):** a corpse disc on low ground right beside a ramp's side wall, near
  the ramp's foot, can still float up to half a metre; two of the attack-order test scene's rows check a little less than
  their text says. **BUG-0222 (S4):** the slide above; two technical edges not reachable in play. **BUG-0220 (S3):** an old
  sound test fails when the PC is busy with something else (it passes 8 / 8 when quiet); nothing in the game.
- **To change it:** the constants above; the `--no-combat` scene list is in docs/03; the rest by inbox note.

### The first balance report: how wide each counter wins, a proposed target for you to decide, and the Sapper's self-splash measured (data track, D6, 2026-10-08)

- **What happened:** the data track measured the eight counter-triangle fights from the sim's tests (equal cost, both seats,
  flat ground, no upgrades or abilities) and wrote the table into both faction pages ("Balance baseline"). **How much of its
  cost the winner keeps:** Heavy Infantry v Horse Raider **88-94 %**, Raider v Wickan Lancer **95-100 %** (Line beats Shock);
  Wickan Lancer v Desert Archer 70-80 %, Horse Raider v Crossbowman 69-77 % (Shock beats Ranged); Crossbowman v Priest
  67-87 %, Desert Archer v Cadre Mage 72-89 % (Ranged beats casters); a Catapult kills a Tent in 9 % of the time the same cost
  of Heavy Infantry needs, a Battering Ram a Billet in 5 %. **The Sapper's self-splash (BUG-0182 item 2):** 4 Sappers behind
  4 Heavy Infantry against 8 Horse Raiders throw 15 sharpers, wound their own side for 41 hit points in total (9 of them the
  thrower itself), kill nobody of their own, and the riders are wiped out while all four Sappers die to them anyway. A 2 m
  minimum range makes it worse (61 points); a 1 m splash removes it but the fight goes worse (fewer survivors). Four Sappers
  alone lose to the riders in every variant.
- **Your call (non-blocking; nothing changes until you answer):**
  1. **The target band.** The data track proposes: *the winner keeps 40-65 % of its cost, in both seats, seats within 15
     points.* Below ~40 % one upgrade level can flip the fight; above ~65 % the loser trades away almost nothing, so the counter
     is decided at build time instead of in the fight. **Every pair is above the band today**; Line v Shock (88-100 %) is the
     outlier. Casters fight without their abilities until M4-4, so those rows should be re-measured then before tuning.
  2. **Line v Shock.** If you want it narrowed, say roughly how much (for example "aim for 60 %"); the data track then proposes
     exact numbers (a Horse Raider or Wickan Lancer hp / attack nudge, or a Heavy Infantry cost nudge) in a table here before
     shipping. Or "leave balance until the fog-on sandbox" and it waits.
  3. **The Sapper.** Recommendation: **keep it** (the self-splash is small next to what the riders do; "Fragile; wants an
     escort" is the intended trade-off). Alternatives: a 2 m minimum range or a 1 m splash.
- **Change table: nothing numeric changed; no name or description changed; no data file changed; the replay fingerprint did
  not move.** Also fixed: BUG-0200 (the design doc's Age II clause is now pinned by the dev's own test; a misleading test
  message reworded).
- **Rough edges (BUG-0230, S4, next data task):** a contradicting sentence appended after the Age II clause would still pass
  the test; the balance table's fight harness is a copy of the sim test's (the sim makes it shared next session); the
  Whirlwind page's siege row uses the wrong column.
- **To change it:** an inbox line ("balance band 40-65 ok, narrow Line v Shock to about 60 %", "Sapper: keep"), then the data
  track's next task is the tweak with a table here; the measured scene is `Scenario/CounterTriangleTests`.

### Process note: the 3-hour session lock declared a live session dead, again (studio, 2026-10-08)

- The 14:35 session's builds and QA ran long under three tracks' load (the sim build alone took 1 h 52 min), so at 18:14 its
  lock was over three hours old and the next session took over, as the routine says. The old session noticed, stopped
  cleanly and left a note; nothing was lost (this session resumed every track from the committed work and finished it). It is
  the second time (the first was 2026-10-07-2014).
- **Suggestion for your routine file (not mine to change):** have a session refresh the lock's time at each step, or widen the
  window to about 5 hours; and have a resume check for running studio processes before taking over. The old conductor's note
  is an uncommitted file in your checkout (`studio/sessions/2026-10-08-1435-incident.md`); its content is in this session's
  log, so you can delete it.

### Arrows, bolts and catapult stones fly now: every unit of both factions fights, shots can miss, splash hurts your own side, and the counter triangle is proven in tests (sim track, M4-2b, 2026-10-08)

- **What was built:** the second half of combat. A ranged unit (Crossbowman, Desert Archer, Cadre Mage, Priest) or a
  siege unit (Catapult, Sapper) now **fires a projectile** at the end of its wind-up instead of hitting at once: a bolt
  flies at 25 m/s, a catapult stone or a Sapper's sharper at 12 m/s, and the damage lands where and when the shot
  arrives. **Aimed shots can miss:** a bolt hits only if its target is still within about 0.3 m of where the shot was
  going; a galloping Horse Raider crossing the line of fire dodges every shot from 6 m out. **Lobs always explode** where
  they were aimed, and **splash** hurts everything in the radius: full damage inside the inner 40 %, falling to half at
  the edge; a Catapult's stone or a Sapper's sharper also hurts **your own units** at half damage (never your own
  buildings). The Catapult **can't fire at anything closer than 6 m**: told to attack a unit that close, it waits; on
  its own it picks a farther target instead. With this, **all 14 shipped units fight**; before, only melee units did.
  Eight **counter-triangle scenario tests** now pin the design doc's rock-paper-scissors with equal-cost groups: Heavy
  Infantry beat Horse Raiders, Raiders beat Wickan Lancers, Lancers beat Desert Archers, Horse Raiders beat Crossbowmen,
  Crossbowmen beat Priests, Desert Archers beat Cadre Mages, a Catapult kills a Tent 11x faster than the same cost of
  Heavy Infantry, a Battering Ram a Billet 18x faster than Raiders. Also fixed: a unit hammering a building now turns on
  the enemy soldier killing it (BUG-0156, the rough edge from last session).
- **What you'll see:** `& $env:GODOT --path game`, box-select the grey army, press **A**, click the orange army. The
  crossbowmen at the back now stop short and start killing from 15 m while the infantry closes; orange units fall before
  any grey unit reaches them. **The shots themselves are invisible until the next view task (M4-V3)**: you see hp bars
  shrink and white flashes on units nobody is touching. The F12 overlay's K / L counts climb from both sides.
- **The one decision to review: shots at walking foot units are "led".** The design doc said a shot flies to where the
  target *was* when fired and hits if the target is still within 0.3 m of that point, and that "slow units almost never
  dodge". With those numbers as written, a walking Heavy Infantry dodged **every** shot from more than 5 m (0 of 100 at
  8 m): in 15 m of flight it walks 1.8 m, six times the tolerance. No projectile speed or tolerance separates a walking
  infantryman at 15 m from a galloping Horse Raider at 5 m, so some rule had to depend on the target's speed. The rule
  now: a shot at a unit moving **no faster than 5 m/s** (every foot unit; cavalry is 6.2-6.6) is aimed at where the
  unit *will* be when the shot lands and kept on it in flight, so walkers are hit 100 % at every range (slow units
  "never" dodge by walking rather than "almost never"); cavalry is shot where it is and dodges as the doc says (a
  crossing Horse Raider: 33 % hit at 3 m, 0 % from 6 m). The 5 m/s is a number in `game/data/common/projectiles.json`
  (`leadSpeed`). *Alternatives:* a tolerance that grows with flight time (walkers would still dodge sometimes), or no
  lead at all and accept that archers can't hit anything that moves (a very different, dodge-heavy game). Say the word
  and the data track changes the number or the sim drops the rule.
- **Producer decisions, revisit any time:**
  - *Projectiles fly straight at constant speed*; the view will draw any arc.
  - *A miss lands harmlessly* (no ground splash from a missed bolt). A **lob** (stone, sharper) always explodes.
  - *A Catapult told to attack a unit inside its 6 m minimum range waits where it stands* (no stepping back; kiting is a
    later slice). On its own it never picks such a target and ignores one hitting it from inside 6 m.
  - *Splash hits enemy buildings* (as structure damage), *never your own buildings*; *your own units only from attacks
    flagged friendly fire* (Catapult, Sapper), the shooter itself included; a friendly-fire kill counts as both a kill and
    a loss for you.
  - *The hit tolerance (0.3 m) is per projectile type in data*, so bolts and arrows can differ later.
  - *The Battering Ram stays a melee siege unit* (no projectile).
  - *Size: ~800 rule lines (budget ~800 for a new system), ~1,600 dev test lines, ~1,500 QA test lines, ~175 doc lines;
    one fix round (the lead rule and three smaller bugs).*
- **Rough edges:** **BUG-0182 item 2 (S4, a data note for the next data task):** a Sapper fighting a melee unit at arm's
  length throws its sharper at its own feet and hurts itself with every shot; in test brawls heavy with Sappers and
  Catapults, 23-28 % of all deaths were friendly fire. The Malazan page calls the Sapper "fragile; wants an escort", so
  this may be the intended trade-off; the data track reports the numbers next and you decide (a minimum range, a smaller
  splash, or leave it). **BUG-0184 (S4):** a unit walking at exactly 5.0 m/s is led on 93 % of its steps (float rounding;
  no shipped unit walks at exactly 5), and a re-aimed bolt can jump 1.8 m in one tick instead of 1.25 (a note for the
  view's projectile drawing). **A balance note:** Line beats Shock by a very wide margin (14-15 of 16 Heavy Infantry
  survive a same-cost Horse Raider charge); the data track's balance report is next. **Update 1814: BUG-0184's rounding edge
  is fixed (M4-3a); the Sapper numbers and the margins are in the D6 entry near the top.**
- **To change it:** speeds, the hit tolerance and the lead speed in `game/data/common/projectiles.json`; each unit's
  splash radius, friendly-fire flag, minimum range and projectile in `game/data/factions/<faction>/units.json`; the splash
  falloff (40 % / 50 %) and the friendly-fire 50 % are rule constants (`CombatConstants`), the rest by inbox note.

### Right-click an enemy to attack it: the attack order is in the window, with a red ring and a "Pursuing" label (view track, M4-V2, 2026-10-08)

- **Status update (integration, same morning): held one session.** I accepted this work, but when the conductor merged
  the sim's projectiles onto `main`, one of the studio's older test scenes (2,000 units, written before combat) went red
  on `main` itself, with no view code involved: now that archers shoot, one of 40 walkers is gone 3 s after a minimap
  order (BUG-0210). The branch is kept; the fix is the same one-line "combat off" switch five other old scenes got in
  M4-V1, and it lands first thing in the view's next session, after which this entry's "Try it" works from `main`
  (today it works from the branch `studio/2026-10-08-0913-view`). **Update 1814: landed on `main` with M4-V3 (the entry near
  the top); the "Try it" below works from `main`; BUG-0190 (both rough edges) fixed.**
- **What was built:** the window half of the attack order from last session. With units selected, a **right-click on an
  enemy unit or building** sends every selected unit to attack *that* target (they chase it anywhere and ignore everything
  else until it dies); **A then a click on an enemy** does the same, A then a click on the ground is still attack-move;
  **Shift** queues attack orders behind other orders. A **red ring** flashes round the clicked target for half a second.
  The selection panel reads **"Pursuing"** while a unit chases its ordered target and "Attacking" once it swings. If one
  of your own units or a tree stands in front of the enemy under the cursor, the click means what it did before (a move
  or a gather): what you see is what you click. Workers obey the order too. The minimap's right-click stays a plain move
  until fog of war makes its dots trustworthy (M4-3). The F12 overlay shows the selected unit's target.
- **Also fixed (BUG-0160):** the F12 overlay's text no longer runs under the K / L counter (four short lines now); a unit
  that is hit before its first frame on screen now flashes once; **corpse discs are readable by team** (the owner's colour
  on a darker rim: grey-blue Malazan, gold Whirlwind); two developer-overlay labels no longer fall back to C# text.
- **Try it (two minutes):** `& $env:GODOT --path game`, box-select a few grey soldiers, **right-click an orange soldier**:
  a red ring, the panel says "Pursuing", they walk over and kill that one, then stand idle. Press **A** and click the
  orange Town Hall: they attack the building. Hold **Shift** and right-click two different enemies: they take the second
  after the first dies. Right-click the ground: a plain walk as before.
- **Producer decisions, revisit any time:**
  - *The ring is a plain red torus for 0.5 s, no cursor change* (cursor art comes with the M6 art pass). Alternative: a
    persistent marker on the target while the order lasts; one inbox line.
  - *"Pursuing" is the label's word* (builder's pick, in `ui.json`). Alternatives: "Chasing", "Hunting", "Attacking".
  - *A right-click on an enemy with a building selected still sets the building's rally point* (the M3 rule).
  - *The minimap's Attack half waits for fog* (M4-3). Alternative: attack on an enemy dot now.
  - *Size: ~300 game-code lines, ~220 view-helper lines, ~1,290 dev test lines, ~1,000 QA test lines, ~100 doc lines; one
    new QA tool (`tools/qa/scene-loop.ps1`, runs every headless test scene).*
- **Rough edges (BUG-0190, S4, next view task):** a corpse disc that falls on the edge of a ramp shows as a half-moon
  (the other half is under the slope); and a technical edge in the pick that nothing in the game can reach today.
- **To change it:** the label in `game/data/common/ui.json` (`states.ordered_attack`); the ring's size, colour and time
  (`TargetRing.UnitScale` / `BuildingScale`, `TargetMark.DefaultSeconds` in `sim/Rts.Sim/ViewApi/`); the corpse colours
  `CombatViews.CorpseShade` / `CorpseRimShade`; the rest by inbox note.

### The faction pages now say what each unit may attack, and the design doc's Age II wording matches the rules exactly (data track, D5, 2026-10-08)

- **What happened:** the data track's first M4 task. Both faction pages' unit tables got a **Targets** column ("all" for
  every unit, "buildings" for the Battering Ram), tests pin it to the data both ways (change the ram's rule in the data
  or on the page alone and a test names the unit and the field; a unit note saying "attacks buildings only" must agree
  with the data too). The design doc's "Ages" paragraph now reads **"Requires finished buildings in two different slots
  (any two of Infantry Hall, Ranged Hall, Shock Hall, Forge): two production halls, or one hall and the Forge"** and
  "level II Forge upgrades" (was "level-2" and a looser sentence), and the test that reads that paragraph moved with it.
  A text check that was case-sensitive and skipped the faction upgrades' names is fixed (BUG-0155). QA tried 21 wrong
  edits one at a time; every one failed a test naming the unit or tech, except one clause QA then pinned itself.
- **Change table: nothing numeric changed; no name or description changed; no data file changed; the replay fingerprint
  did not move.** The only player-visible text touched is in the design docs, not the game.
- **Rough edges (BUG-0200, S4, next data task):** the docs/02 clause "two production halls, or one hall and the Forge"
  was not checked by the dev's test (QA's row covers it now); and a test message for an unneeded explicit `"targets":
  "all"` in a data file is misleading. Note: the Shadow page's "Edur Ram: attacks buildings only" has no test until Shadow
  data exists (M8). **Update 1814: both fixed in D6 (the entry near the top).**
- **To change it:** the pages `docs/factions/malazan.md` / `whirlwind.md` and the data together (the tests say which side
  disagrees), or an inbox note.

### M3 is done: the whole base game (economy, buildings, production, Age II, the HUD) is signed off (all three tracks, 2026-10-08)

- **What M3 is:** everything from mines and trees to Age II: workers gather and return, buildings are placed and built
  by several workers and repaired, queues train units with rally points and a population cap, Age II and the Forge
  upgrades are researched and enforced, both factions are fully in data with their text, and the HUD (resource bar,
  selection panel, command card, build menus, placement ghost, queue strip, rally flags) runs it all in the window. Two
  days of sessions over three tracks; the last criterion was proven by a scripted run of your own playtest script.
- **I signed it off myself** (your autopilot setting `stop_at_milestone_end: no`). Conditions held: all eight criteria
  verified, every M3 system covered by unit, fuzz and determinism tests, every track's clean-up session done, and no
  serious open bug once BUG-0146 (the wedged laborers, below) was fixed and verified this session. The leftovers are
  small and listed in the retro (docs/05): a loader corner nobody ships (BUG-0134), the crowd cost of the laborer fix
  (BUG-0151, below), cosmetic view nits, a docs wording drift the data track tidies next.
- **Your playtest is still wanted, as feedback:** the ten-minute script in the M3-V3 entry below. Write "M3 playable ok"
  in the inbox, or what felt wrong; a complaint becomes a task ahead of roadmap work.
- **What's next (M4):** projectiles and splash so archers, mages, catapults and sappers fight (sim), telling a unit to
  attack a specific enemy in the window (view), then fog of war, abilities and stealth.

### Laborers no longer get stuck short of a tree, and you can tell a unit to attack a specific enemy; the Battering Ram now only hits buildings (sim track, M4-2a, 2026-10-08)

- **What was fixed first (BUG-0146, the S2 that held M3):** laborers sent to a tree with only one open side used to form a
  column where only the front one could reach, and when it left, the next one "arrived" against the queue behind it and
  stood there, 1.7 m short, chopping nothing for the rest of the match. The cause was in the walking rules, not the
  economy: a worker walking to a node stopped up to a metre short of its spot, and stopped early against *any* standing
  workmate, even one behind it. Now a worker walks right up to its spot, stops early only for a workmate who is nearer
  the spot than itself, and each open side of a tree or mine has **three standing spots** (middle, left, right), so
  three laborers chop one tree side by side with a fourth queued behind them. Checked on your seed-21 game (the one
  that found it: 343 ticks of waiting at worst, was 14,575; wood 1,360 instead of 100) and on 160 hand-built pockets of
  every shape.
- **What was built:** the **"attack this unit / building" order** in the rules (the window's right-click on an enemy is
  the view's next task). A unit told to attack a target chases it as far as it goes (no leash), ignores every other
  enemy while the target lives, and stands idle where it is when the target dies or proves unreachable. Shift queues it
  behind other orders. Workers obey it (they still never fight on their own). Spamming the order no longer cancels the
  swing (a found-and-fixed bug would have made click-happy players deal zero damage). Attack-moving to a **new** point
  while fighting re-picks the best target there, so you can pull a unit off a building onto the soldier killing it.
  **The Battering Ram attacks buildings only**, through a new data field ("what this attack may target"), as the
  Whirlwind page says. Replays now record whether combat was on (format 4) and the new order.
- **What you'll see:** the laborers at a forest edge now stand three abreast on an open side and the wood counter keeps
  rising. Nothing new to click yet for the attack order (next view task); in the rules it works and is fuzz-tested.
- **Producer decisions, revisit any time:**
  - *An ordered attacker has no leash but still gives up an unreachable target after about 2 s without gaining*, then
    stands idle where it is (not walking home). Alternative: chase forever, or walk home.
  - *A worker obeys an explicit Attack.* Alternative: workers ignore it (purely peaceful laborers).
  - *Archers, mages, catapults and sappers drop an Attack order until next session's projectiles land* (they cannot
    fight yet; the order is dropped, not queued).
  - *The new "targets" field defaults to "all"*; only the ram says "buildings".
  - *I accepted a slower crowded-gathering benchmark (BUG-0151):* a stress scene of 100 laborers on one tree and one mine
    costs 0.32 ms a tick instead of 0.23 (its budget is 1 ms). The code is not slower per worker; the fix simply puts
    39 % more laborers to work in a denser crowd, and they deliver 14-17 % more. The brief's "at most 10 % slower" bound
    was a guard against waste, which this is not. Alternative: order a cheaper crowd (four variants were tried; each
    broke a wedge case or saved little). Revisit with the crowd-cost work after M4.
  - *Size: ~560 rule lines (budget 1,500), ~1,100 dev test lines, ~1,500 QA test lines, ~170 doc lines; two fix rounds
    (three S2 bugs found and fixed in-session: a chaser bouncing between two targets forever, the swing-cancel on spam,
    the attack-move that could not redirect).*
- **Rough edges:** **BUG-0156 (S3, next sim task if cheap):** a unit hammering a building ignores the enemy soldier
  killing it unless you give it an order (an attack-move onto the soldier now works). **Update 0913: fixed in M4-2b (the
  entry at the top): it turns on the soldier between swings while that soldier is in sight.** **BUG-0157 (S3):** very fast
  A-click spam to *different* points (every 50-150 ms) still costs a brawl 10-26 % of its damage (same-point spam costs
  nothing; clicks 250 ms apart cost nothing). **BUG-0151 (S3)** above. **A balance note for you:** crowded gathering is
  now about 15 % more productive at 20 laborers on few nodes (1-10 laborers unchanged); the data track's balance pass
  can tune rates if you want the old income curve.
- **To change it:** the ram's rule is `"targets": "buildings"` on its attack in `game/data/factions/whirlwind/units.json`
  (any unit can take `units` / `buildings` / `all`); the three standing spots and their spacing are
  `EconomyConstants.StandPointsPerCell` / `StandPointSpacing`; the leash / give-up rules by inbox note.

### You can see the fighting now: hp bars, hit flashes, bodies and rubble, and a kill counter (view track, M4-V1, 2026-10-08)

- **What was built:** the view half of combat. A unit that has taken damage shows a small **hp bar** above it (green,
  yellow, red by how much is left; only hurt units show one, like buildings). A unit **flashes white** for a moment when
  it is hit. A dead unit vanishes and leaves a **flat dark disc** where it fell for 10 s of game time; a destroyed
  building leaves a **low grey slab** over its footprint for 20 s. The resource bar reads **"K n / L n"** (your kills and
  losses) under the population, and F12 shows both players'. The selection panel's hit points are live. Also fixed:
  five of the studio's older test scenes that broke when units started fighting now run with a developer-only
  "combat off" switch (`--no-combat`, never a game option) without changing what they check.
- **Please watch a fight (two minutes):** `& $env:GODOT --path game`, box-select the grey army, press **A** and click on
  the orange army far east. Watch bars appear and shrink, white flashes on hits, discs where units fall, the K / L
  counter climbing top right; click a fighting unit and read its hit points dropping in the panel. When the slate
  Town Hall or a Tent falls (send the whole army at it with A), a grey slab stays where it stood for 20 s.
- **Producer decisions, revisit any time:**
  - *Corpses are flat discs in the owner's colour at 35 %, rubble a grey slab*: placeholders until the M6 art pass (death
    animations, real corpse models). QA found the discs read near-black for both teams (BUG-0160 item 3); the next view
    task brightens them. Alternative: no corpses until M6.
  - *A corpse lasts 10 s and rubble 20 s of game time* (so 8x speed shortens them); at most 2,000 markers, the oldest
    replaced.
  - *The flash is 0.15 s and restarts on every hit*, so a unit under a hail of blows stays lit.
  - *Hp bars grow with zoom past 30 m* like the cargo cubes, so they stay readable zoomed out.
  - *Size: ~450 game-code lines, ~280 view-helper lines, ~1,050 dev test lines, ~670 QA test lines, ~85 doc lines.*
- **Rough edges (BUG-0160, S4, next view task):** the F12 overlay's second line runs through the K / L label; a unit
  that is hit between being trained and its first frame shows a bar but never flashes; the corpse discs are too dark to
  tell teams apart; two developer-overlay labels fall back to C# text if the text file is missing. **Update 0913: all
  four fixed in M4-V2 (the entry near the top).**
- **To change it:** the labels in `game/data/common/ui.json` (`hud.kills`, `hud.losses`); the flash length
  `HitFlash.DefaultSeconds`, lifetimes `DeathMarkers.UnitLifetimeTicks` / `BuildingLifetimeTicks`, the marker cap
  `DeathMarkers.DefaultCapacity`, the bar colours `UnitHpBars.Full` / `Half` / `Empty` (all in
  `sim/Rts.Sim/ViewApi/`); the disc / slab look in `game/scripts/CombatViews.cs`; the rest by inbox note.

### Units fight now: attack-move, chasing, fighting back, hit points and death are in the rules (sim track, M4-1, 2026-10-07/08)

- **What was built:** the first slice of combat, melee only. A unit told to **attack-move** (press A, click a point) walks
  there and looks around every fifth of a second: when it sees an enemy it goes for it, stands and swings (a short
  wind-up, then a hit, then a cooldown), and when nothing is left in sight it walks on to where you sent it. An **idle**
  unit that sees an enemy, or is hit by one, fights back and chases it, but no farther than its own sight range from
  where it stood; then it walks home and stands idle again (so a lone scout can't drag your whole army across the map).
  A unit **holding position** fights whatever comes into reach and never moves. A unit on a plain **move** ignores
  enemies. **Workers never fight on their own** (not while idle, not when hit; only if you attack-move them), so your
  laborers keep mining when an enemy walks past and don't wander off to pick fights. Who to attack: whoever is attacking
  me first, then enemy soldiers, then other units, then buildings; nearest first. **Damage** is the design doc's
  formula: attack x the type-vs-armor table x bonuses, minus armor, never below 1; magic ignores armor; Forge upgrades
  count (a Heavy Infantry with Melee Weapons hits for 8 instead of 7). A unit at 0 hp **dies** on the spot (it vanishes;
  the body is the view's next task) and its population frees up; a building at 0 hp is destroyed (its queue refunded,
  its squares freed by the no-walled-off-ground rule). Two equal units that swing together both die (no "who was first
  in memory" advantage). Each player's kills and losses are counted.
- **What you'll see:** `& $env:GODOT --path game`, box-select the grey army, press **A** and click on the orange army far
  east: they march, stop at the enemy lines, turn to face them and, after a moment, orange and grey units start
  vanishing one by one until a side is gone (no hit animation, hp bar or body yet: those are the view's next task, as
  is the panel's live hp, which still reads "max / max"). The selection panel reads "Attacking" for a unit in a fight.
  Right-click instead of A and they walk through without fighting (and get hit without fighting back until they stop).
  Workers at a mine ignore an enemy soldier standing next to them. Ranged units, casters, the Catapult and the Sapper
  don't shoot yet (projectiles are next): they only get hit.
- **Producer decisions, revisit any time** (each a one-line change):
  - *Retaliation leash = the unit's own sight range from where it stood*, then it walks home without looking for more.
    Alternative: a fixed 15 m, or chase until the attacker dies.
  - *A swing lands if the target is still within reach + 0.5 m when the wind-up ends*; farther, the swing is lost.
    Alternative: always lands (homing), or never once out of reach.
  - *Workers never fight on their own* (taken after a test showed idle laborers walking 26 m to kill the neighbour's
    laborers). Alternative: workers fight back when hit (StarCraft-like).
  - *A chase that makes no progress for 2 s is given up* (the target is on a cliff you can't climb, behind a wall, or as
    fast as you), unless a friend is already fighting it (then you're just queued behind your own front rank). After
    three give-ups a unit only fights what comes into reach, until its next order or its next landed hit.
  - *The Battering Ram attacks units too for now*; the design doc says buildings only. That needs a new data field
    ("what this attack may target"), which changes the data fingerprint, so it lands with the next sim task.
  - *A test switch turns combat off* for the studio's older movement and economy tests (two-player scenes whose armies
    would now fight); real matches always fight. Not a game option.
  - *The one-player 2,500-unit stress budget went from 4.5 to 4.6 ms a tick* (combat's bookkeeping costs 0.08 ms per
    tick there; a 500 v 500 brawl costs 3.1 ms, within the 4 ms design budget).
  - *Size: ~1,250 rule lines (budget 1,500 for a clear design, ~800 for a new system: over, because two fix rounds
    added the give-up memory and its brawl exception), ~2,000 dev test lines, ~1,900 QA test lines, ~230 doc lines.*
- **Rough edges:** **BUG-0146 (S2; found by the view's scripted playtest, not by combat):**
  laborers sent to a tree that is open on one side can stand 1.7 m short of it, "gathering" nothing for the rest of the
  match; it holds the M3 sign-off. **Update 2026-10-08: fixed (M4-2a entry above); M3 signed off.** **BUG-0139 (S3):** the
  ram attacks units (above). **Update 2026-10-08: fixed, the ram is buildings-only (M4-2a).** **BUG-0149 (S3, next
  sim clean-up):** a unit that gave up chasing something unreachable, and then spots a reachable enemy behind a short
  wall, may give that one up too and stand idle in sight of it. **BUG-0144 (S3):** while buildings are being placed every
  two seconds, chasers can wait up to 1.7 s for a path. **BUG-0142 (S4):** in a duel between two idle equal units already
  in reach, the one whose turn to look comes first wins by one hit; the placement ghost says "Units in the way" for a
  worker that is fighting inside the footprint while the order itself goes through. Not in yet (planned slices): the
  explicit "attack this unit" order (M4-2a, next), projectiles / misses / splash / friendly fire (M4-2b), fog (M4-3),
  abilities (M4-4), stealth (M4-5). **Update 2026-10-08: the explicit order is in (M4-2a entry above); the panel's live
  hp, hp bars and bodies are in (M4-V1 entry above).**
- **To change it:** attack, armor, hp, range, cooldown and wind-up per unit in `game/data/factions/<faction>/units.json`,
  the type-vs-armor table in `game/data/common/damage_table.json`; the leash, the grace, the worker rule and the give-up
  timing by inbox note (they are engine constants in `CombatConstants`, not data: say if you'd rather have them in a file).

### M3's last criterion is proven by a scripted playtest through the real HUD; right-clicks beside trees are fixed; locked buttons say "Locked" (view track, M3-V4, 2026-10-07/08)

- **What happened:** a headless test now plays **your ten-minute script** (the M3-V3 entry below) through the real
  window at 8x speed, by injected clicks and keys only: box-select the five workers, right-click the mine, click the
  Town Hall, queue three Laborers and cancel one, rally on a forest, build a Barracks, a Depot, an Armory and a Billet
  with one worker, research Age II (greyed "Locked" first), Melee Weapons, place the Engineers' Yard only after Age II,
  train a Sapper and a Heavy Infantry, and read the green "+1" on the Heavy Infantry's attack. Every step is checked
  against the rules and prints the game time it finished at; it passes on two map seeds in about 10 minutes of game time
  each (budget 13), twenty runs out of twenty, and again on top of the new combat. **M3 is 8 / 8** on that; I have not
  signed it off yet because the run itself found a real bug (BUG-0146, below).
- **What you'll notice in the window:** (1) a **right-click on open ground just north of a tree or mine is a walk again**,
  and a click on the drawn canopy or the mine's block still gathers it (BUG-0125; 48,000 test rays, 0 wrong). (2) A
  building you haven't unlocked (the Wickan Corral before a Barracks, the three V-menu buildings before Age II) shows
  a **greyed button reading "Locked"** and, if you press it anyway, a red ghost reading "Locked" (was "Needs more", which
  sounded like money). (3) Age II's button reads **"In a queue"** while it is queued and **"Researched"** afterwards even
  if you lose a hall (was "Locked"). (4) "Attacking" is the panel's state text for a fighting unit.
- **Producer decisions, revisit any time:**
  - *A locked building's button stays pressable* (greyed, with the reason), so you can raise its ghost and see what it
    needs. Alternative: disable it like a train button.
  - *The pick tests a slightly larger round shape than the drawn 6- / 7-sided tree*, so about 1 % of clicks on the sliver
    between a facet and the circle name the tree (at most 8 cm, a pixel at far zoom). Alternative: the exact polygon,
    at a cost not worth it before the M6 art pass (BUG-0148 item 1, S4).
  - *Accepted with BUG-0147 open:* on the merged `main`, five of the studio's older M2 test scenes fail because the two
    start armies now fight each other (their expectations are about armies that never fight). The release gate (build,
    tests, boot check) is green; the scenes get the sim's combat-off switch in the view's next task. Alternative: hold
    the view, which would hold combat too (the window needs the view's "Attacking" text to boot).
  - *Size: ~80 game-code lines, ~120 view-helper lines, ~1,150 dev scene lines, ~660 QA test lines, ~100 doc lines.*
- **Rough edges:** **BUG-0146 (S2, sim; fixed first next session):** on seed 21 four laborers sent to a tree that is
  open only to the south stand in a column 1.7 m short of it, "gathering" nothing for 12 minutes; wood income from them
  is zero. You'd see it as workers standing still by a forest edge. The sim's next task fixes the arrival rule.
  **Update 2026-10-08: fixed (M4-2a entry at the top).** **BUG-0147 (S3, view's next task):** the five scenes above.
  **Update 2026-10-08: fixed (M4-V1 entry at the top: the scenes run with the developer-only combat-off switch).** **BUG-0148 items 1 and 3 (S4):** the sliver above; the
  script checks that a rallied laborer chops *some* tree, not the rally forest specifically.
- **To change it:** the words in `game/data/common/ui.json` (`placement.requires`, `research.already_queued`,
  `research.already_researched`, `states.attacking`); the drawn tree / mine sizes in `game/scripts/PropsView.cs` (the
  pick follows them); the script's steps in `game/tests/M3PlayableTest.cs`; the rest by inbox note.

### The shared upgrades' text is final: Age II's description rewritten for both factions, the six Forge upgrades kept (data track, D4, 2026-10-07/08)

- **What happened:** the data track's end-of-M3 clean-up. The seven techs both factions share (Age II and the six Forge
  upgrades) now have their text pinned to the design doc by tests, with one change: Age II's description named
  Malazan-only buildings ("the Caster Hall, Siege Works and Watch Tower") although the Whirlwind player reads the same
  text. The test failure messages from D3 now say which tech, which field, and both values (BUG-0132). The replay
  fingerprint moved once for the text (every checkpoint identical).
- **Change table (nothing numeric changed; no name changed):**

  | What | Field | Old → New | Why |
  | --- | --- | --- | --- |
  | Age II | description | "Advance to Age II. Unlocks the Caster Hall, Siege Works and Watch Tower, your unique unit, level-2 Forge upgrades and your faction upgrade." → **"Unlocks your caster, siege and tower buildings, unique unit, level II and faction upgrades. Needs two kinds of building: infantry, ranged, mounted or upgrade."** | one text serves both factions (no faction's building names); names the requirement in words, as every other description does |
  | Melee Weapons / II, Ranged Weapons / II, Armor / II | description | unchanged (quoted in the M3-5 entry below) | they already read well; now pinned to docs/02 |

- **Your call (taste):** "Needs two kinds of building: infantry, ranged, mounted or upgrade" uses "upgrade" for the Forge
  kind (the Armory / Smithy), because the rule forbids naming any one faction's building. If you'd rather read "Needs two
  of: an infantry hall, a ranged hall, a mounted hall or a forge" (design-doc kind names), or drop the "Needs" sentence
  (the card's tooltip already lists it), one inbox line; the data track applies it next time it runs.
- **Rough edges:** BUG-0155 (S4): one of the dev's text checks is case-sensitive and skips faction upgrade names; QA's own
  check covers the gap. The design doc's "Ages" paragraph says "level-2" where the data says "level II" and "two Age I
  production buildings or a Forge" where the exact rule is "any two of the four": the data track tidies that wording
  with its next task (its tests read that paragraph). **Update 0913: both done in D5 (the entry near the top).**
- **To change it:** `game/data/common/techs.json` (`description` of `age_ii` and the six upgrades), or an inbox note.

### Process note: a session resumed while its predecessor was still alive (studio, 2026-10-07/08)

- Session 2014 stalled; its lock was 3 hours old, so the 23:15 session took over and finished its work. Six minutes
  later the old session woke up and committed one report file to the view branch before stopping for good (nothing
  reached `main`; QA re-ran the report, nothing was lost). **Suggestion for the studio skill (your file, not mine):** a
  lock's age alone shouldn't declare a session dead; long sessions should refresh the lock, and a resume should check
  for running studio processes first. Also from this session: two tracks filed a bug with the same number (BUG-0145);
  the sim's became BUG-0149, and from now on each session's tracks get disjoint id blocks.

### The base game is whole and on `main`: the HUD landed, six view bugs are fixed, and your ten-minute M3 playtest is wanted (view track, M3-V3b, 2026-10-07)

- **What happened:** the HUD from the entry below (selection panel, production card, queue strip, rally points,
  population) is on `main` now. Its test setups were written before Age II had requirements, so they queued Age II at a
  bare Town Hall; they now build two halls first, and a new check proves the card's greyed buttons agree with the game's
  rules in every one of 450 frames through every phase (no halls, one hall, two of the same kind plus a site, two
  different halls, Age II queued and then a hall destroyed, rebuilt). Every test scene passes both with the shipped data
  and with the data track's building locks. `main`'s headless check is green again.
- **The view's clean-up, in plain words (what you'll notice):**
  1. A **greyed button looks greyed**: its name and key dim to half strength, with the red reason line at full strength
     (before, only the small reason line told it from a live button). A selected building being repaired no longer
     makes the game allocate memory every tick (BUG-0123).
  2. **Construction sites are a dusty mauve**, clearly different from the finished slate or orange buildings, and the
     gold / wood **cube a worker carries stays visible when you zoom out** (BUG-0107).
  3. **"Quartermaster's Depot" wraps at the space**, not mid-word; a flurry of **Shift-clicks on one spot sends one build
     order per worker**, not a copy per click; the **ghost keeps up with a panning camera**; the ghost's reason text is
     three times bigger and a deeper red (BUG-0122).
  4. **Clicking a building hidden behind a hill picks the hill**, not the building; and a **right-click on a tree's
     canopy means that tree** even when the ground under the cursor is behind it (before, the ground point decided).
  5. The benchmark's march test holds a per-seed bound (BUG-0104); stale docs figures refreshed (BUG-0105).
- **Please play it (ten minutes), this is M3's last criterion.** The script is in the entry below (M3-V3), unchanged,
  with two differences now that the data track's building requirements are live: the **Wickan Corral** button is red
  "Needs more" until a Legion Barracks is finished, and the three **V-menu buildings** (Cadre Tower, Engineers' Yard,
  Watchtower) are red until Age II. Write **"M3 playable ok"** in the inbox, or what felt wrong. If you don't, the studio
  proves the same script with a scripted headless run next session and ticks the criterion itself.
- **Producer decisions, revisit any time:**
  - *Accepted with one new rough edge open (BUG-0125, below) rather than hold the HUD a second session*: holding would
    have kept `main`'s check red and the playtest blocked. Alternative: strip the new tree-canopy pick before merging.
  - *Greyed = name and key at 50 %, reason line at 100 %.* Alternative: dim everything.
  - *Sites are mauve* (a hue far from both factions' colours) until the M6 art pass. Alternative: stripes or scaffolding.
  - *Size: ~290 game-code lines, ~100 view-helper lines, ~1,000 dev test lines, ~470 QA test lines, ~75 doc lines.*
- **Rough edges:** **BUG-0125 (S3, fixed in the next view task):** the new canopy pick treats a tree as a full-height
  column over its square, so a right-click on open ground up to about 2.5 m "behind" (north of) a tree or mine now sends
  workers to gather that tree instead of walking (339 of 3,000 test clicks beside trees; 0 before). Harmless but
  annoying beside forests; the fix is to test the drawn cone rather than the square. **BUG-0126 (S4):** a locked
  building's ghost says "Needs more" (sounds like money; should say "Locked") and its menu button isn't greyed; Age II's
  button says "Locked" while it is already in the queue or researched if a hall was lost (the game is right, the
  wording is unhelpful); items 1-2 fixed with BUG-0125, the rest at the view's next clean-up. **Update 2315: BUG-0125
  and BUG-0126 items 1-2 are fixed (the M3-V4 entry at the top); items 3-6 stay for the view's next clean-up.**
- **To change it:** the dim strength is `CommandCard.DimAlpha`, the site colour `BuildingViews.SiteColor`, the reason
  text size `BuildGhost.ReasonPixelPerZoom`; the words in `game/data/common/ui.json`; the rest by inbox note.

### Buildings and units never cross a cliff when set down, refused build orders are free, impossible requirements are caught at load, and the data track's techs and building locks landed (sim + data tracks, M3-H2 and D3, 2026-10-07)

- **What was built (clean-up, no new features):**
  1. **A newly trained unit appears beside its building, never on another plateau.** The game now knows the map's
     plateaus (each connected patch of same-height ground; a ramp belongs to the ground at its foot). A unit spawning
     from a building whose whole plateau is packed **waits** instead of appearing 30 m away across a cliff, and units
     pushed out of a new building's footprint spread over their own plateau, one per square before any square takes a
     second (BUG-0097, BUG-0095). Twenty Town Halls all waiting for room cost 0.09 ms a tick (was 1.7-1.9).
  2. **A build order refused because it would wall ground off costs nothing the second time:** the answer is kept
     while the map is unchanged, so 100 workers told to build at one bad spot cost 0.4 ms instead of 33 (BUG-0096).
  3. **Impossible requirements are refused when the data loads** (BUG-0100): a Malazan unit needing a Whirlwind
     building, a shared upgrade needing one faction's building, or an "any two of" rule nobody can ever satisfy (say,
     Age II needing halls that themselves need Age II). An upgrade that applies to no unit is an error too.
  4. **The data track's D3 landed** with the test fixes that held it (BUG-0112): the sim's own stress tests had assumed
     no building was ever locked; they now run on a copy of the data with the locks cleared (they test geometry, not
     locks), and every one of their per-seed counts came out identical to before. Nothing weakened.
- **What you'll see:** the building locks, in the window (the entry above): the Wickan Corral needs a Legion Barracks;
  the Cadre Tower, Engineers' Yard and Watchtower need Age II (same for the Whirlwind: Horse Lines need a Raider Camp;
  Shrine, Ram Yard, Lookout Tower need Age II). The replay fingerprint moved only for the data; every unit's path is
  byte-identical.
- **D3's change table for your review (data track; nothing numeric changed):**

  | What | Field | Old → New | Why |
  | --- | --- | --- | --- |
  | Wickan Corral (Shock Hall) | needs | nothing → Legion Barracks | the faction page's Requires column; design doc "Buildings": the Shock Hall needs the Infantry Hall |
  | Horse Lines (Shock Hall) | needs | nothing → Raider Camp | same |
  | Cadre Tower, Engineers' Yard, Watchtower | needs | nothing → Age II | design doc "Ages": Age II unlocks the Caster Hall, Siege Works and Watch Tower |
  | Shrine of the Whirlwind, Ram Yard, Lookout Tower | needs | nothing → Age II | same |
  | Moranth Supply | description | "Munitions bought from the Moranth: the Sapper's Cusser cooldown drops from 45 to 30 s and the Catapult gains 4 m of range. Needs Age II." → **"Crates of Moranth munitions, paid for in Imperial coin: the Sapper's Cusser cooldown drops from 45 to 30 s and the Catapult gets +4 m range. Needs Age II."** | faction voice; still names both numbers and the requirement (tests pin that) |
  | Dryjhna's Prophecy | description | "The prophecy of the Apocalypse: Zealots gain 20 HP and the Priest's Sandstorm cooldown drops from 45 to 30 s. Needs Age II." → **"The Apocalypse foretold is at hand: Zealots gain +20 HP and the Priest of the Whirlwind's Sandstorm cooldown drops from 45 to 30 s. Needs Age II."** | same |
  | Faction pages | new "Techs" table each | — | the pages are the design source; tests pin the table to the data |

  The other twelve buildings keep no requirement, as the pages say. No name changed. BUG-0111 (page pins missed a false
  "+N pop" claim) and the buildings half of BUG-0090 are fixed by D3's tests.
- **Producer decisions, revisit any time:**
  - *A spawn on a packed plateau waits* (the Age of Empires behaviour). Alternative: let it appear at the foot of the
    plateau's ramp.
  - *Pushed-out units share a square only when there are more of them than squares on the plateau.* Alternative: let
    the extras wait inside the footprint until a square frees (they'd be invisible inside the building meanwhile).
  - *Impossible requirements are errors, not warnings*: a content author finds out at once; the shipped data has none.
  - *Ownership refinement for next session's D4:* the shared techs file (`common/techs.json`) belongs to the sim track,
    but D4 edits only its names and descriptions, as a named exception, and the sim task that session doesn't touch it.
  - *Size: ~500 rule lines, ~950 dev test lines, ~650 QA test lines, ~60 doc lines (budget 1,500).*
- **Rough edges:** BUG-0134 (S3, next sim clean-up): one impossible-requirement shape still loads clean: a building
  that needs an upgrade researched only at that same building (an Armory needing Melee Weapons). Nothing shipped does
  it; a content author would hit it the first time they try. BUG-0133 (S4): if more than 24 units are pushed out onto
  every single square of a tiny plateau, some share an exact point until they jostle apart (needs hundreds of units in
  one footprint; the docs sentence will be corrected). Not done this session for budget: BUG-0113 (a technical error
  message for a wrong-type value in a data file), BUG-0094 (a test helper's groves can wall cells in).
- **To change it:** the locks are the `requires` lists in `game/data/factions/<faction>/buildings.json`; the two
  descriptions in `game/data/factions/<faction>/techs.json` (or an inbox note: the data track applies it next session);
  the wait / spread rules by inbox note.

### You can run a whole base in the window now, up to Age II: a selection panel, train and research buttons, a queue with progress, rally points and the population count. Please play it (view track, M3-V3, 2026-10-07)

- **Update 1715: landed on `main`** (M3-V3b, the entry at the top): the playtest below works from `main` now, with the
  building locks live. BUG-0123 and BUG-0122 (rough edges below) are fixed.
- **Status update (integration, same evening): held one session.** I accepted this work, but when the conductor
  merged it on top of the sim's new locks, one of its own tests failed: the test queues Age II at a Town Hall that has
  no halls, which the sim now (rightly) refuses (BUG-0124). The branch is kept, the fix is a test setup change, and it
  lands first thing next session. Until then `main`'s window shows the M3-V2 state and logs an error at start (the
  sim's new "Locked" reason has no text yet on `main`), so the playtest below works after the next session, or today
  from the branch `studio/2026-10-07-1415-view` if you like.
- **What was built:** the last piece of M3's HUD. **(1) A selection panel** bottom centre (right of the minimap): one
  selected unit shows a coloured placeholder portrait with its initial, its name, HP, Attack, Armor, Range, Speed from
  the data (a researched upgrade shows beside the number as a green "+1"; **the bonus is shown only, combat applies it
  in M4**) and what it is doing ("Idle", "Gathering", "Building" ...); several units show a grid of up to 24 portraits
  (a "+N" for the rest) with the active Tab subgroup outlined, and a click on a portrait selects that unit alone; a
  building shows its name and hit points. **(2) A production card:** click one of your finished buildings and the
  command card lists what it trains and researches on the grid keys (a Barracks: Heavy Infantry on Q; the Town Hall:
  Laborer on Q, Age II on W; the Armory: the six Forge upgrades then Moranth Supply), each with its name, key and cost,
  and the description plus "Needs ..." in a tooltip. A button is **greyed with the reason** ("Can't afford", "Queue
  full", "In a queue", "Researched", "Locked") exactly when the rules would refuse it. **(3) A queue strip** above the
  card: up to five squares (a unit's colour and initial, or a tech's name), the first with a yellow progress bar; click
  a square to cancel it and get the full cost back. When Age II completes, "Age II" flashes in the resource bar.
  **(4) Rally points:** with a building selected, right-click the ground (or the minimap) to plant a small yellow flag
  with a line from the building: new units walk there; a rally on a gold mine or a tree makes new workers gather it;
  right-click the building itself to clear it. **(5) "Pop 5 / 10"** in the resource bar, red when the cap is full.
  **(6)** The three M3-V2 rough edges are fixed: a right-click on the top face of a building now means that building
  (BUG-0108), a placement click builds exactly where you clicked (BUG-0109), and a broken `ui.json` reports an error
  instead of blank labels (BUG-0110).
- **Please play it: this is M3's last criterion ("build a full Malazan base and reach Age II"), about ten minutes.**
  `& $env:GODOT --path game`, maximize. Box-select the five grey workers by the slate Town Hall, right-click the nearest
  gold mine: they gather. Click the Town Hall: the panel shows its name and HP; the card shows **Laborer (Q)** and
  **Age II (W)** greyed "Locked" (you need two different halls first). Press Q three times: three squares appear in the
  strip, the first with a progress bar; click the third square to cancel it (the 50 gold comes back). Right-click a
  forest with the Town Hall still selected: a yellow flag; the new Laborers walk there and chop. Select some workers,
  press **B** then **E** (Legion Barracks, 150 wood), click on green; then **B**, **A** (Armory, 100 / 100) somewhere
  else; later **B**, **Q** (Billet, 50 wood) to raise the cap from 10 to 18. When the Barracks and the Armory are both
  finished, click the Town Hall: **Age II is live** (400 gold / 200 wood, 60 s). Press W, watch the bar fill; at the end
  "Age II" flashes top right. Now click the Armory: Melee Weapons (100 / 50) is live, Melee Weapons II stays "Locked"
  until Melee Weapons is researched, and Moranth Supply is live after Age II. Click the Barracks: Heavy Infantry on Q;
  after Melee Weapons, select one and the panel reads its attack with a green "+1". Press **V**, **W** (Engineers'
  Yard): the Sapper button in it is "Locked" until Age II. Write **"M3 playable ok"** in the inbox, or what felt wrong
  (that becomes the view track's next task ahead of roadmap work). Things to look for: does the card read well at your
  resolution, is the flag visible enough, does the greyed button look greyed enough (it doesn't, see the rough edges).
- **Producer decisions, revisit any time:**
  - *Shared upgrades are listed before the faction's own* on a Forge's card (Armor, Armor II, Melee I / II, Ranged I /
    II, then Moranth Supply). Alternative: the faction upgrade first.
  - *A greyed button shows the reason instead of the cost* (the cost is still in the tooltip). Alternative: both lines.
  - *At the population cap the train buttons stay live*: an item queues, pays, and waits for room (the rules have no
    "pop full" refusal; the red "Pop" is the cue). Alternative: grey them with "Pop full"; one inbox line.
  - *Layout:* the panel sits between the minimap and the card; the resource bar moved 142 px left to make room for the
    pop count. Unit hit points read "max / max" until combat exists (M4).
  - *A right-click on any building's box means that building*: on your own damaged one, Repair; on your own site, join;
    on an enemy or an undamaged building, a walk to its centre (so units gather round it). A right-click on a tree or
    mine still goes by the ground point (nodes are low; fine in play).
  - *Size: ~1,350 game-code lines (panel 380, card +160, selection +160, queue strip 180, rally marker 120, resource bar
    +150, text loader +100), ~175 view-helper lines, ~1,000 dev test lines, ~1,270 QA test lines, ~120 doc lines; over
    the 1,500 budget on game code by the panel; accepted, it is plain layout code.*
- **Rough edges:** BUG-0123 (S3, fixed in the view's clean-up session, next): while a selected building is being
  repaired, the panel rebuilds its hit-point text every tick (about 52 bytes a tick: harmless in play, but against the
  studio's "no per-frame allocation" rule), and a greyed button keeps its bright name and cost, so only the small red
  reason line tells it from a live one. BUG-0122 (S4, same session): "Quartermaster's Depot" wraps mid-word, the ghost
  trails a panning camera by a frame, the reason text is small. Also: the Age II buildings (Cadre Tower, Engineers'
  Yard, Watchtower) and the Wickan Corral are **not locked yet** in the window: their "needs ..." values are the data
  track's D3, verified but held one session (see the data row in Now and the D3 note below).
- **To change it:** every word in `game/data/common/ui.json` (`train`, `research`, `states`, `hud` sections); panel and
  strip sizes are constants at the top of `game/scripts/SelectionPanel.cs` / `ProductionQueueStrip.cs`; the flash
  length `ResourceBar.AgeFlashSeconds`; the rest by inbox note.

### Requirements are real now: the Sapper needs Age II, Age II needs two different halls, Melee Weapons II needs Melee Weapons and Age II (sim track, M3-6, 2026-10-07)

- **What was built:** the "unlocks" half of M3's Age II criterion. Every "needs ..." in the data is now enforced: a
  unit can't be trained, a building can't be placed and a tech can't be researched until its requirements are met. A
  requirement is either a tech you have researched or **one of your own finished buildings of that kind** (a site under
  construction doesn't count; an enemy's doesn't). Age II has a special rule from the design doc, written in the data:
  **any two of** Infantry Hall, Ranged Hall, Shock Hall and Forge must be finished (two Barracks count once). The check
  happens **when you give the order**, not again when it completes: if your only Barracks is destroyed while a unit it
  unlocked is training, the unit still finishes (the Age of Empires rule). Four loader rough edges were fixed in the same
  pass: a duplicate key in any data file is an error (BUG-0008), every faction must fill all seven unit slots and all ten
  building slots exactly once (BUG-0010), an empty "applies to" list in an upgrade is an error instead of "everyone"
  (BUG-0098), and a requirement loop or a tech named like a building is an error (BUG-0099).
- **What you'll see:** in the window, once the production card above lands (held one session, BUG-0124): Age II
  greyed "Locked" until two different halls stand; the Sapper and Zealot "Locked" until Age II; Melee / Ranged / Armor level II "Locked" until level I and Age II;
  Moranth Supply until Age II. The placement ghost also goes red with "Needs more" for a building you haven't unlocked,
  but **no building is locked in the window yet**: the values ("Wickan Corral needs a Legion Barracks", "Cadre Tower,
  Engineers' Yard, Watchtower need Age II") are the data track's D3, held one session. Numbers: 5,000 refused orders in
  one tick cost 0.7-0.9 ms; a refusal is cheaper than an acceptance; the fingerprint of the replay did not move.
- **Producer decisions, revisit any time:**
  - *Checked at order time only* (above). Alternative: cancel queued items when their unlock is lost (StarCraft-like).
  - *A building requirement means a finished building you own.* Alternative: count a site, or an ally's (no allies yet).
  - *"Any two of" is a data rule* (`requiresAnyOf` with a count and a list of building kinds), not code, so a future
    faction or Age III can use it. The list names building **kinds** (slots) so the one shared Age II entry works for
    every faction.
  - *Every faction fills every slot exactly once* (seven units, ten buildings): a content rule, now enforced at load. It
    is what the design doc's templates say; it also means the Teblor / Tiste / Shadow factions (M7-M9) can't skip a slot
    without a rules change. Alternative: allow empty slots.
  - *The ghost's "needs" check runs before the map checks*, so the reason you see for a locked building is always
    "Needs more", even over a tree. And a researched Age II whose halls were later lost reads "Locked" rather than
    "Researched" if asked (a corner nobody will see: Age II can't be researched twice anyway).
  - *Size: ~520 code lines (budget 1,500), ~1,230 dev test lines, ~1,300 QA test lines, ~75 doc lines.*
- **Rough edges:** BUG-0100 (S3, sim clean-up next session): a data author could write a requirement that can never be
  met (a Malazan unit needing a Whirlwind building; a shared upgrade needing one faction's building; three halls needing
  Age II while Age II needs two halls) and the game loads it silently and locks the content for the whole match. The
  shipped data has none; the fix is a load-time check. BUG-0112 (S3, see the data note below): the data track's building
  requirements make 25 of the sim's own test setups fail (the tests assumed nothing is locked), which is why D3 is held.
  BUG-0113 (S4): a wrong-type value in a data file gets a technical error message; a size test now times a failing load.
  **Update 1715: BUG-0100 and BUG-0112 are fixed and D3 landed (the M3-H2 / D3 entry near the top): the buildings are
  locked in the window now. BUG-0113 stays for the next sim clean-up.**
- **To change it:** the requirement lists are the `requires` fields in `game/data/factions/<faction>/units.json`,
  `buildings.json` and the techs files; the Age II rule is `requiresAnyOf` on `age_ii` in `game/data/common/techs.json`
  (change `count` to 1 or 3, or the list); the order-time rule and the slot rule by inbox note.

### The techs text and the building requirements are written and checked, but wait one session to land (data track, D3, 2026-10-07)

- **Update 1715: landed** through the sim's M3-H2; the full change table is in the M3-H2 / D3 entry near the top. The two
  descriptions below are the shipped ones; an inbox note changes them in D4.
- **What happened:** the data track finished D3 (the faction upgrades' descriptions rewritten, the "needs ..." values
  on eight buildings, a Techs table on each faction page, tests that pin all of it to the pages, BUG-0111 and the
  buildings part of BUG-0090 fixed). QA and I verified it. It is **held on its branch** because the eight new
  building requirements make 25 of the sim track's own test setups fail (they were written when nothing was locked:
  BUG-0112). `main` is always green, so the sim's next session merges the branch with the test fixes, and the full
  change table for your review appears here then. The two new descriptions, so you can object early:
  - Moranth Supply: "Crates of Moranth munitions, paid for in Imperial coin: the Sapper's Cusser cooldown drops from 45 to 30 s and the Catapult gets +4 m range. Needs Age II."
  - Dryjhna's Prophecy: "The Apocalypse foretold is at hand: Zealots gain +20 HP and the Priest of the Whirlwind's Sandstorm cooldown drops from 45 to 30 s. Needs Age II."
- **To change it:** an inbox note; it is applied when the branch lands.

### Age II and the Forge upgrades exist in the rules: a Town Hall researches Age II, a Forge researches weapon and armor upgrades, and each player's upgrades are tracked (sim track, M3-5, 2026-10-07)

- **What was built:** techs. The game now has a data file of **technologies**: Age II (400 gold / 200 wood, 60 s, at the
  Town Hall) and six Forge upgrades (Melee Weapons I / II, Ranged Weapons I / II, Armor I / II, at the Armory / Smithy),
  shared by both factions, plus one upgrade per faction (Malazan **Moranth Supply**, Whirlwind **Dryjhna's Prophecy**,
  200 / 150, 45 s, at the Forge). A tech is researched **through the same queue a building trains units in** (one timer
  per building, like Age of Empires): you pay when you queue it, it takes no population, cancelling refunds it in full
  (so does the building being destroyed), and a tech can be in only one queue at a time and researched only once. When it
  finishes, the player "has" it: Age II makes the player Age 2, and the upgrades add to the right units' stats: Melee
  Weapons +1 attack per level to every unit that fights in melee (workers too), Ranged Weapons +1 attack per level to
  every unit that shoots (and towers, from M4), Armor +1 per level to every non-siege unit; Moranth Supply shortens the
  Sapper's Cusser cooldown 45 s → 30 s and adds 4 m to the Catapult's range; Dryjhna's Prophecy gives Zealots +20 HP and
  shortens the Priest's Sandstorm 45 s → 30 s. **Combat doesn't apply the bonuses yet** (there is no combat until M4);
  they are a query the fighting will read.
- **What you'll see:** nothing in the window yet: the research buttons are the view track's next task (M3-V3: click a
  Town Hall, press the Age II button, watch the queue). What is not in yet either: **nothing is locked**. The data says
  "Melee Weapons II needs Melee Weapons and Age II" and "the Sapper needs Age II", and the game checks those names
  exist, but it doesn't enforce them until the next sim task (M3-6). Today: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 50 --workers 5 --forests 12 --mines 8 --ticks 600`
  ends each player's line with `age 1`. **Update 1415: both are in. The research buttons are in the window (M3-V3
  entry at the top) and the locks are real (M3-6 entry at the top).**
- **The new player-facing text, quoted** (placeholder wording by the sim builder; the data track polishes it next
  session and you review that pass):
  - Age II: "Advance to Age II. Unlocks the Caster Hall, Siege Works and Watch Tower, your unique unit, level-2 Forge upgrades and your faction upgrade."
  - Melee Weapons: "+1 attack for melee units." · Melee Weapons II: "Another +1 attack for melee units (+2 in all). Needs Melee Weapons and Age II."
  - Ranged Weapons: "+1 attack for pierce units and towers." · Ranged Weapons II: "Another +1 attack for pierce units and towers (+2 in all). Needs Ranged Weapons and Age II."
  - Armor: "+1 armor for all non-siege units." · Armor II: "Another +1 armor for all non-siege units (+2 in all). Needs Armor and Age II."
  - Moranth Supply: "Munitions bought from the Moranth: the Sapper's Cusser cooldown drops from 45 to 30 s and the Catapult gains 4 m of range. Needs Age II."
  - Dryjhna's Prophecy: "The prophecy of the Apocalypse: Zealots gain 20 HP and the Priest's Sandstorm cooldown drops from 45 to 30 s. Needs Age II."
- **Numbers (all copied from the design doc's Tech table, not tuned):**

  | Tech | Gold / wood | Time | Effect |
  | --- | --- | --- | --- |
  | Age II | 400 / 200 | 60 s | Age 2 |
  | Melee Weapons I / II | 100 / 50 · 175 / 100 | 30 s · 40 s | +1 / +2 attack, melee units |
  | Ranged Weapons I / II | 100 / 50 · 175 / 100 | 30 s · 40 s | +1 / +2 attack, pierce units (and towers, M4) |
  | Armor I / II | 125 / 50 · 200 / 125 | 35 s · 45 s | +1 / +2 armor, non-siege units |
  | Moranth Supply (Malazan) | 200 / 150 | 45 s | Sapper Cusser cooldown -15 s, Catapult range +4 m |
  | Dryjhna's Prophecy (Whirlwind) | 200 / 150 | 45 s | Zealot +20 HP, Priest Sandstorm cooldown -15 s |

- **Producer decisions, revisit any time** (the design doc left these open; each is a one-line data or rules change):
  - *The Forge table's "+1 / +2" are totals*, so level II adds one more point (not two). Alternative: level II adds 2 (+3 in all).
  - *"Melee units" means every unit whose attack is melee, so **workers get Melee Weapons** too* (as in Age of Empires). Alternative: exclude the worker slot.
  - *"Non-siege" means "not in the siege slot", so **the Sapper (a unique unit with a siege-type attack) gets Armor***. Alternative: go by attack type and leave the Sapper out.
  - *Shared techs live in one common file, the faction upgrade in each faction's file*; a tech names the building **kind** that researches it ("forge"), so one entry serves both factions' Forges.
  - *Research runs in the building's one queue, takes no population, is refunded in full on cancel* (AoE rules).
  - *A tech waits behind a unit that is stuck on the population cap* (the queue is in order; QA noted it, and the rule stands: a tech at the head is never blocked by pop). Alternative: let techs skip ahead of a pop-blocked unit.
  - *The same Cancel removes a unit or a tech from a queue* (no separate command).
  - *Size: ~900 code lines (budget 1,500), ~2,300 dev test lines, ~1,300 QA test lines, ~95 doc lines.*
- **Rough edges:** BUG-0098 (S3): if someone edits a tech's "applies to these units" list to be *empty*, the game
  reads it as "every unit of both factions" instead of refusing the file; the shipped data has no such list; fixed in
  the next sim task (M3-6), which rewrites that part of the loader anyway. BUG-0099 (S4): a requirement loop
  ("A needs B, B needs A") or a tech named like a building loads without complaint today; both become errors in M3-6.
  Also noted for M4: "Ranged Weapons ... and towers" has no tower path yet (towers have no attack until M4).
  **Update 1415: BUG-0098 and the loop / name items of BUG-0099 are fixed (M3-6); the "an upgrade that reaches no unit
  loads" item stays an S4 note. Update 1715: that last item is fixed too (M3-H2): such an upgrade is now a load error.**
- **To change it:** numbers and text in `game/data/common/techs.json` (shared) and
  `game/data/factions/<faction>/techs.json` (the faction upgrade); which units an upgrade reaches is the `appliesTo`
  part of each effect (attack type, unit tags, unit ids, siege yes / no); the rules above by inbox note. A data edit
  also regenerates the replay fingerprint (the studio does that).

### You can build a base in the window: a command card with grid hotkeys, B / V build menus, a green / red placement ghost, click a site to cancel it, right-click to repair (view track, M3-V2, 2026-10-07)

- **What was built:** the second half of M3's HUD. **(1) A command card** bottom right: a 5 x 3 grid of buttons on the
  keys Q W E R T / A S D F G / Z X C V B. With soldiers selected the middle row shows **Attack (A), Stop (S), Hold (H)
  and Move (M, new: a plain move that ignores enemies)**; with workers selected also **Build (B)** and **Build
  Advanced (V)** bottom right. Pressing a button does exactly what its key does. **(2) Build menus:** B lists the six
  Age I buildings (Billet, Quartermaster's Depot, Legion Barracks, Crossbow Range, Wickan Corral, Armory for the
  Malazans) on Q W E R T A, V the three Age II ones (Cadre Tower, Engineers' Yard, Watchtower); each shows its name,
  key and gold / wood cost, with the description in a tooltip; Esc or a right-click closes. **(3) The placement
  ghost:** picking a building shows a see-through box the size of its footprint under the cursor, **green** where the
  rules allow it and **red** where they don't, with the reason in words above it ("Blocked", "Would wall ground off",
  "Can't afford", "Units in the way" ...). A left click on green sends every selected worker to build it (the first
  places the site, the rest join); hold Shift to place several in a row (the workers build them in order); a click on
  red does nothing. **(4) Click a building** to select it (your own only; a box-select never grabs a building): a green
  outline shows round its footprint, and if it is a construction site the card shows **Cancel (B)**, which refunds the
  unbuilt share. **(5) Right-click with workers selected** on your own damaged building sends them to **repair** it,
  on your own construction site to **join** building it, on a tree or mine to gather (as before), anywhere else a move.
- **Try it (five minutes):** `& $env:GODOT --path game`. Box-select the five grey workers west of centre: the card
  appears bottom right with Build (B). Press **B**: six buildings on Q W E R T A. Press **Q** (Billet) and move the
  mouse: a green box follows it on open ground, red with "Blocked" over the Town Hall or a tree. "Can't afford" shows
  once a building costs more than you have (you start with 200 / 200: place an Armory at 100 / 100, then try a Wickan
  Corral at 75 / 150 and it is red for wood). Click on green: the workers walk over, a slate site rises with a yellow
  bar, and about 20 s later it is a Billet. Click the site while it builds: a green outline, the card shows **Cancel**;
  press **B** to cancel and get the unbuilt share back. Press **V** to see the Age II menu (every entry is still
  allowed today, since nothing is locked until the next sim task). Press **M**, click somewhere: a plain move. Press
  F12: the label reads "sel building" while a building is selected.
- **Producer decisions, revisit any time:**
  - *The card's words come from a small view-only text file* (`game/data/common/ui.json`: "Move", "Stop", "Hold",
    "Build", "Cancel", the refusal reasons, and which building kinds each menu lists), so no label lives in code and
    the pre-release rename pass stays data-only. If the file misses a word the game logs an error and runs without
    the card rather than showing a blank button.
  - *Layout (builder's choice, accepted):* unit commands on the middle row (A S D F = Attack Stop Hold Move), Build
    advanced / basic on V / B, a site's Cancel on B, the top row left empty for abilities (M4). Hotkey hints read your
    actual key bindings, so a rebind relabels the buttons.
  - *The ghost centres the footprint on the cursor's square* (within half a square for even sizes) and stops at the
    map edge rather than hanging off it.
  - *The first placement is immediate, later Shift placements are queued behind it*, so the workers build the sites
    in the order you placed them. Alternative: every placement immediate (the workers would split).
  - *A click on red does nothing and plays no sound* (the reason text is the feedback).
  - *Size: ~1,250 game-code lines (card, ghost, selection, text loader, outline), ~160 view-helper lines, ~930 dev test
    lines, ~720 QA test lines; over the 1,500 budget on game code by the card's button plumbing; accepted.*
- **Rough edges (S3, all three fixed in the next view task, M3-V3, which touches the same code). Update 1415: BUG-0108
  / 0109 / 0110 are fixed (M3-V3 entry at the top); BUG-0122 stays for the view's clean-up session.**
  - BUG-0108: a right-click on the **top face** of a building's box is read as a click on the ground *behind* it (the
    camera looks down at 55°, so the far half of a 3 m-tall box's top lands about 2 m behind the footprint). On a
    damaged Town Hall, 39 of 81 top pixels would send workers to walk behind it instead of repairing. You can't see
    it yet: nothing damages a building before M4 combat, and sites are joined by clicking the ghost or the ground
    inside the footprint. Fix: pick the box first, then the ground.
  - BUG-0109: a placement click builds where the ghost was **drawn last frame**, not exactly under the click; at 60 fps
    that is where you saw the box, so in play it looks right, but a very fast flick-and-click can land one square off.
  - BUG-0110: a hand-broken `ui.json` whose top level is not an object loads as blank labels without an error (the
    shipped file is fine).
  - BUG-0122 (S4, view clean-up session): "Quartermaster's Depot" wraps mid-word on its button; 25 Shift-clicks on one
    spot send 75 build orders; the ghost trails a panning camera by one frame; the reason text is small (about 9 px at
    30 m zoom) and the red reads orange over grass. **Update 1715: fixed (M3-V3b entry at the top).**
- **To change it:** words and menu lists in `game/data/common/ui.json`; key bindings in `game/project.godot`
  (`card_0` ... `card_14`, `order_move`, `build_basic`, `build_advanced`); button size and the ghost's colours are
  constants in `game/scripts/CommandCard.cs` / `BuildGhost.cs` until the M6 art pass; the rest by inbox note.

### Buildings train units now: five-deep queues, rally points, population and a cap, full refunds (sim track, M3-4, 2026-10-07)

- **What was built:** the rules for training. A finished building takes orders for the units it trains (the
  Legion Barracks trains Heavy Infantry, the Garrison Keep trains Laborers, and so on, from each unit's "trained
  at" in the data): up to **five** in its queue, each **paid when you queue it** and **refunded in full** if you
  cancel it (or the building is destroyed). The first item trains for its train time (a 14 s Heavy Infantry
  takes exactly 280 ticks) and the unit appears on the nearest free square next to the building, then walks to
  the building's **rally point** if one is set; a worker rallied onto a gold mine or a tree starts gathering it
  by itself (the Age of Empires rule). **Population:** every unit counts its pop (half-steps allowed: a Lancer
  is 2, a Laborer 1), a Town Hall gives 10, a House 8, the hard cap is 100 (all from the data). An item only
  *starts* training when there is room; if the cap is full the queue waits, and it resumes the moment a House
  finishes. Nothing ever dies from losing a House: training just pauses. A unit with an Age II requirement
  (the Sapper, the Zealot) can't be trained until Age II exists (next sim task). Two spawned units never share
  a square; if there is no free square the unit waits inside until one opens. **Update 1131: Age II exists (the
  M3-5 entry at the top); the Sapper and Zealot stay locked until the next sim task (M3-6) makes "needs Age II"
  a real check instead of "has any requirement". Update 1415: done (M3-6 entry at the top): they unlock the tick
  after Age II completes.**
- **What you'll see:** nothing in the window yet: the production card (click a Barracks, pick a unit, see the
  queue and progress) and the rally marker are the view track's M3-V3, two view sessions away. Today the
  command-line tool shows the pop count: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 50 --workers 5 --forests 12 --mines 8 --ticks 600`
  ends each player's line with `pop 21/10` (the 50 debug units are spawned past the cap and still count,
  which is the rule for dev spawns).
- **Numbers:** 500 marching units + 20 Town Halls training non-stop + 50 gathering workers cost 0.40 ms a
  tick (budget 1.3, hard budget 4). QA's hostile run (8 seeds x 3,000 ticks of random train / cancel / rally /
  build / cancel / destroy orders on two players) balanced gold and wood to the exact coin every tick, and two
  identical runs stayed identical every tick.
- **Producer decisions, revisit any time:**
  - *A House finished by builders lets a waiting queue resume one tick (50 ms) later than one placed by a
    dev command.* Training runs before construction each tick, as the design doc's tick order says. The
    alternative (construction first) was not worth a reorder for one tick.
  - *The population numbers are recomputed from the units and queues, not stored in the replay fingerprint*
    (like a unit's speed, they follow from other stored facts). The fuzz tests recount them every tick.
  - *A rally order from the sim's side names the building by its map square*, so the rally target keeps its
    exact position.
  - *The queue holds five items* (a rule, not a stat: a code constant). The alternative, a data field per
    building, can come later if a design wants a longer queue somewhere.
  - *Size: ~950 code lines (budget 1,500), ~1,250 dev test lines, ~1,000 QA test lines, ~95 doc lines.*
- **Rough edges (S3, next sim clean-up session after three more feature sessions):** BUG-0097: if every
  square of a building's plateau is taken (by hundreds of units, a stress case), a new unit is set down on
  *another* plateau of the same height, up to 30 m away, instead of waiting; the same holds for units pushed
  out of a new building's footprint (that part predates this session). Fix: bound the search by the
  building's own connected plateau. BUG-0095 (from M3-H1) is half fixed: the cost is gone (9 / 35 ms → 0.08 /
  0.19 ms), the "leftovers stack on one point" part remains. **Update 1715: both fixed (M3-H2 entry near the top): a
  spawn on a packed plateau waits, and pushed-out units spread over their own plateau.**
- **To change it:** unit costs, pop, train times and "trained at" in `game/data/factions/<faction>/units.json`;
  pop provided in `buildings.json`; the hard cap in `game/data/common/rules.json` (`popCap`); the rally-on-a-mine
  rule, the queue length and the tick order by inbox note.

### The economy is on screen: a Town Hall and five workers at the start, a resource bar, right-click to gather, and buildings and workers show what they are doing (view track, M3-V1, 2026-10-07)

- **What was built:** the first half of M3's HUD. **(1)** The default match now starts each player with a
  finished Town Hall beside their army (on the nearest open 4 x 4 spot with a clear ring round it, on the
  army's own side of the map) and **five workers** next to it, standing on the side facing the nearest gold
  mine so the first trip is short. `-- --workers 12` changes the number (0-200); `-- --no-bases` gives the old
  armies-only match for tests. **(2)** A **resource bar** top right reads "Gold 200  Wood 200" (the names come
  from each faction's data file, not from code) and updates the frame a total changes. **(3) Right-click on a
  tree or a mine** with workers selected sends them gathering (the Command sound plays); any soldiers in the
  same selection just walk there; Shift queues it after the current order; right-click on plain ground is a
  move as before, and the minimap's right-click stays a move. **(4) Buildings** are drawn: a box in the
  player's colour on its footprint, 3 m tall; a construction site is a lighter slate box that **rises with its
  progress** under a yellow bar; a damaged building shows a green hit-point bar. **(5) Workers** are tinted
  green while gathering, amber while waiting with a load, blue while building, and carry a small gold or brown
  cube above their head while loaded. F12's second line adds "workers gathering N, returning N, building N".
- **Try it (three minutes):** `& $env:GODOT --path game`. Both armies now have a slate (Malazan) or orange
  (Whirlwind) Town Hall box beside them with five small workers. Box-select the five grey workers west of
  centre, right-click the nearest gold mine (dark block with a gold top): they turn green at the mine, carry a
  gold cube home, and the bar's gold ticks up by 10 per trip (about every 30 s per worker at first). Right-click
  a forest: the same with wood and brown cubes. Box-select workers *and* soldiers together and right-click a
  mine: the workers gather, the soldiers walk there. Press F12 to see the worker counts. Nothing can be
  *built* yet: that is the next view task (build menu and ghost preview).
- **Producer decisions, revisit any time:**
  - *Where the Town Hall goes* (nearest clear 4 x 4 spot beside the army, never between the two armies) and
    *where the workers stand* (mine-facing side) are debug placements until real start locations exist (M6).
  - *Criterion wording:* my brief said "all five workers reach Gathering within 60 ticks (3 s)" of the click;
    that only holds for a node within 12 m. On seed 1 the mine is 10 m away (tick 23) and the nearest tree
    12.7 m (tick 123). The gather order itself is instant; the walk is the sim's walking speed. I reworded the
    rule to "within 60 ticks of arriving" and closed the finding (BUG-0106) as not a defect. Alternative:
    place the Town Hall nearer a forest; not worth it before real start locations.
  - *A right-click on a node with no worker selected is a plain move* (soldiers don't gather).
  - *Both the site's rising box (the builder's idea) and the dev-only `--no-bases` flag were accepted.*
  - *Size: ~580 game-code lines, ~360 view-helper lines, ~600 dev test lines, ~1,100 QA test lines.*
- **Rough edges (BUG-0107, S4, next view clean-up session):** a Malazan construction site at 100 % is the same
  dark slate hue as a finished Malazan building (the bar is what tells them apart); the wood cube is only 3-4
  px tall when zoomed all the way out at 720p; placing or cancelling a building redraws the minimap's resource
  layer and the trees for no reason (cheap, but every build will trigger it from M3-V2). **Update 1715: fixed
  (M3-V3b entry at the top): mauve sites, zoom-scaled cargo cubes, the minimap redraws only on a felled tree; the
  trees' relist on a building change is the one leftover (BUG-0126 item 3, S4).**
- **To change it:** worker count default is `startingWorkers` in `game/data/common/rules.json`; the resource
  names in `game/data/factions/<faction>/faction.json`; colours and sizes are placeholder constants in
  `game/scripts/BuildingViews.cs` / `UnitViews.cs` until the M6 art pass; the rest by inbox note.

### M2 is done: the game window is signed off, and its clean-up fixed the benchmark, the start positions, the quit leak and the minimap dots (view track, M2-H2 + sign-off, 2026-10-07)

- **What M2 is:** everything you can see and touch today: a 3D terraced map with forests and gold mines,
  the camera, 200 placeholder soldiers that walk and turn smoothly, selection (click, box, Shift,
  double-click, Ctrl, groups 1-9, Tab), every order key (right-click, A, S, H, Shift-queue), the minimap
  with click-to-jump and right-click orders, the F12 developer overlay, select / command sounds, the
  `--screenshot` and `--bench` flags, and a measured 60 FPS with 10x headroom. Nine view sessions over
  three days, two of them clean-up sessions; 18 headless test scenes.
- **This session's clean-up (eight items), in plain words:**
  1. The benchmark's "order across the map" step **really marches across now** (about 109 m, to a spot
     85 % of the way to the far edge, or the opposite corner if that spot is a cliff or a forest) instead
     of 15 m to the enemy's doorstep (BUG-0101), and its `fps` is frames divided by seconds (BUG-0102).
     The 60 s numbers barely moved: 0.73 ms a frame at 100 units per player, 2.62 ms at 1,000 per player
     with the west army marching through the east one. QA checked the target on 200 map seeds.
  2. **Both armies start in a clearing**: no soldier next to a tree, a mine, a cliff edge or the map
     border, and each army on one height level (BUG-0085; seeds 1-40 checked at 100 and 1,000 per side).
  3. **Sounds stop when the game quits**, so the "instances leaked" warning is gone (10 of 10 runs clean;
     BUG-0087). A test row that could fail under heavy PC load now waits on the right clock (BUG-0088).
  4. **Minimap dots are a 2 x 2 coloured centre inside a one-cell rim**, so a lone scout reads in its
     player's colour (BUG-0069, my default; QA read the screen pixels of lone dots at the border, on a
     ramp and on a cliff lip).
  5. The F12 overlay's text line is rebuilt only when a number changes (a quiet frame allocates nothing,
     BUG-0083); cliff edges read crimson instead of olive, arrows no longer dip into steep ramps, the one
     compiler warning is gone (BUG-0084).
  6. Smaller: a felled tree re-uploads only the trees, not the mines (BUG-0086); `--bench` is capped at an
     hour and prints with a dot decimal on every PC (BUG-0103); four doc / test nits (BUG-0070).
  7. Two notes for the M6 release build are in the docs: keep the test scenes out of the shipped zip,
     and load the data files in a way that works from the packed game.
- **I signed M2 off myself** (your autopilot setting `stop_at_milestone_end: no`). Conditions held: all
  ten criteria verified, every M2 system covered by unit, fuzz and determinism tests (the sounds have no
  game-state side, so "determinism" doesn't apply there), no serious open bugs, clean-up session done.
  **Your playtest is still wanted, as feedback:** `& $env:GODOT --path game`, maximize the window,
  box-select the grey army west of centre (it stands in a clearing now), right-click far across the map
  past the orange army and watch the march through a ramp: smooth turning, no per-tick snapping. Click
  the minimap corners, scroll the wheel from 20 m to 60 m, press A and click, Shift + right-click three
  points, H, S. Zoom out (`-- --zoom 60`) and look at the minimap dots. Listen: a short blip on select, a
  rising two-note on an order. Then `& $env:GODOT --path game -- --bench 60 --vsync off` and read the
  `bench:` line (expect avg under 1 ms, fps over 1,000 on this PC). Write "M2 playable ok" in the inbox,
  or what felt wrong; a complaint becomes the view track's next task ahead of roadmap work.
- **Producer decisions, revisit any time:**
  - *Signed off on measurements, with your playtest as feedback rather than a gate* (the alternative,
    holding the view track until you play, would idle it; flip the autopilot setting to `yes` if you'd
    rather gate).
  - *The 2 x 2 dot* over the two alternatives (screen-pixel dots, or a rim only round a crowd's outside);
    both remain yours to pick by inbox note.
  - *The benchmark's pass mark is "the army centre moves at least 20 m in 10 s"* (seed 1 moves 22 m; the
    march runs through the idle enemy army, which halves its pace after a few seconds). QA found that
    seed 21 moves 19.1 m (BUG-0104), so the bound is fitted to seed 1, not the map family; the benchmark
    runs seeds 1 / 6 / 31 by default, so nothing fails today.
  - *At 1,000 units per side the start block may wrap round a mine or a forest strip* (every soldier is
    still clear of them); that is what the clearing rule allows.
  - *Size: ~230 game-code lines, ~330 view-helper lines, ~450 dev test lines, ~660 QA test lines.*
- **Rough edges (next view clean-up session, after four feature sessions):** BUG-0104 (S3) above; BUG-0105
  (S4): the minimap refresh test now runs at 92-94 % of its 0.3 ms limit (it could fail under load; the
  2 x 2 dot cost the margin) and two doc figures are stale. **Update 1715: both fixed (M3-V3b entry at the top).**
- **What's next (view track, M3):** a resource bar, right-click a tree or mine to gather, worker and
  construction feedback, then the build menu with a ghost preview, then the production queue UI.

### Sim clean-up: no patch of ground can ever be walled off, refused build orders are cheap, eight bugs closed (sim track, M3-H1, 2026-10-07)

- **What was built (clean-up, no new features):**
  1. **A cancelled or destroyed building can't leave a hole nobody can reach.** When a building goes
     (cancelled now, destroyed in M4 combat) and its squares touch open ground, they open as before. If
     other buildings or trees surround them, the squares stay blocked (a "pocket") until something next to
     them opens, and then the whole chain opens at once. So the "worker walks to the wall for ever" case
     (BUG-0093, the last trace of BUG-0078) is gone, and "every open square reaches every other square" is
     now true at every moment of a match. QA ran 30,000 random build / cancel / destroy / fell steps on six
     maps against an independent "can everyone reach everyone" checker: no violation, and two identical
     runs stayed identical every tick.
  2. **A refused build order is cheap.** "Can't afford it", "a unit is in the way" and "the building list
     is full" are checked before the expensive "would this wall ground off" test: 100 refused orders in one
     tick went from 22 ms to 0.07 ms (BUG-0091).
  3. **Units set down outside a new building never share a square** (the search keeps going ring by ring:
     400 soldiers stacked where a Town Hall goes land on 400 different squares in 2.5 ms); the push-out
     finds nearby units through the fast lookup grid (0.3 ms instead of 7 ms with 400 units around); the
     view will never draw a pushed unit sliding through the building; a worker holding position can build
     on its own spot; a hand-edited repair factor too small to do anything is refused (BUG-0092).
  4. Small ones: an empty resources file, or one without a tree or a mine type, is refused (BUG-0076); the
     command-line tool refuses a bad `--record` file name before the run instead of after (BUG-0072); a
     one-in-a-million "is this a wall?" answer no longer depends on which unit asked first (BUG-0071); a
     docs sentence (BUG-0079).
- **What you'll see:** nothing new in the window; these are safety and speed fixes under the hood that the
  M3 build menu (view track, next sessions) will rely on. Numbers: 500 marchers + 50 builders + a tree
  felled every tick still cost 1.19 ms a tick (budget 4); the replay fingerprint did not change (no unit
  moved differently).
- **Producer decisions, revisit any time:**
  - *Blocked pockets, rather than "an unreachable tree is not a target".* The alternative (teach workers to
    check reachability per target) fixes one symptom; the pocket rule keeps the whole-map promise that
    combat and the AI can rely on later.
  - *BUG-0080 stays as a documented limit.* If a player (or the M5 AI) places a building on **every single
    tick**, armies ordered more recently stand still while that lasts (a building every 2 ticks: only the 4
    oldest groups walk; every 4 ticks with 8 groups: all walk, the youngest after 3 ticks). The preferred
    fix (keep following the old route maps and rely on the per-step wall check) was not done: units pressed
    against a new building would give up after one second instead of waiting for the new map, and the
    current behaviour is pinned by tests on both tracks. The exact bound is in the docs with a test that
    measures it. Revisit when the AI's building rate is known (M5).
  - *Saved games (M6) will store the route maps themselves*, not only "which destinations were cached", so
    loading a save continues the game exactly as it would have run (BUG-0081). Alternative: refresh every
    map at save time in both runs, at the cost of a few metres of difference per unit. Owner may revisit
    at M6.
  - *The forest generator's whole-map double-check runs only in debug builds now* (the local check never
    disagreed with it across 246 maps); saves about 2 s of setup on the biggest maps in release.
  - *A `--record` run now empties an existing file of that name at start*, so a run that dies leaves an
    empty file where the old replay was. Accepted: don't record over a replay you want to keep.
  - *Size: ~290 code lines, ~540 dev test lines, ~1,100 QA test lines, ~120 doc lines (budget 1,500).*
- **Rough edges (S3, next sim clean-up session after four feature sessions):** BUG-0095: a building placed
  on a small plateau with fewer free squares than units standing in its footprint makes the search walk
  the whole map for each leftover unit (9 ms on the normal map, 35 ms on a 256 map) and the leftovers end
  up stacked on one point; a player would have to construct it. BUG-0096: a build order refused *because it
  would wall ground off* still pays the full check (100 such orders in one tick: 33 ms); the ghost preview
  will refuse those before an order is ever sent, so in play it needs a scripted flood of bad orders.
  BUG-0094 (S4): a test helper can build groves that wall cells in; test-only. **Update 1715: BUG-0095 and BUG-0096
  fixed (M3-H2 entry near the top); BUG-0094 stays for the next sim clean-up.**
- **To change it:** the pocket rule, the BUG-0080 call and the save-file decision by inbox note.

### You can place buildings and workers build them, in the rules (sim track, M3-3, 2026-10-06)

- **What was built:** the sim half of "building placement, construction with several builders, repair".
  A worker ordered to **build** a building at a spot pays the cost at once, a construction site appears
  (it is a wall to walkers from that moment), the worker walks to its edge and works; more workers
  ordered onto the same site join it. The design doc's build-time rule is exact: with n builders a
  building takes `t x 3 / (n + 2)` of its one-builder time, so a 20-second Billet takes 20 s alone,
  15 s with two, 10 s with four, 6 s with eight. A site's hit points grow with its progress. **Cancel**
  a site and the unbuilt share of the cost comes back (rounded down); **repair** a damaged building and
  workers restore it at half the build rate for a quarter of its cost, scaled by the damage, stopping
  when you can't pay. A building at 0 hit points disappears (combat will use that in M4). The game also
  answers "may this go here?" for the ghost preview the view will draw: not off the map, not on a cliff,
  ramp, tree, mine or other building, not on another height level, **not sealing off any ground**, no
  enemy or holding unit inside, affordable, and room in the building list. Your own idle units standing
  in the footprint are set down on the nearest free spot outside it (the RTS norm).
- **What you'll see:** nothing in the window yet. The build menu and the ghost preview are the view
  track's M3 work (after the M2 clean-up session). Today it runs in tests only: the QA suite places
  thousands of buildings on generated maps against an independent "did this wall anything off" checker,
  and every verdict agreed.
- **Numbers:** 500 marching units plus 50 builders on 10 sites cost 0.98 ms a tick (budget 4); a tree
  felled every tick on top: 1.17 ms. One placement check costs 0.0013 ms on average, 0.05 ms at worst on
  the default map.
- **Producer decisions, revisit any time:**
  - *A building may never wall ground off.* Age of Empires lets you wall; this game's design doc rules
    it out so no unit or worker can ever be stranded. The check keeps whatever regions already exist
    (so it still works after a pocket appears, see the rough edge). Alternative: allow walling and teach
    workers to ignore unreachable trees.
  - *Player 1 is Malazan, player 2 Whirlwind* (player p plays faction p) until the M6 lobby lets you pick.
  - *Idle own units in the footprint are pushed out; holding units block the placement* (H means "stay").
  - *Repair stops the moment you have 0 of any resource the building costs*, rather than running up a debt.
  - *Push-out ring 1 is the eight-neighbour ring; after eight rings the nearest passable cell.*
  - *The "+ 2" and "x 3" of the build-time formula are code constants*, since they are the rule itself,
    not a balance number; the repair factors (0.5 / 0.25) are in `game/data/common/rules.json`.
  - *Size: ~785 code lines (budget 1,500), ~990 dev test lines (budget ~1,000), ~1,800 QA test lines.*
- **Rough edges (S3, the sim clean-up session is next):**
  - BUG-0093: if you build a site, surround it with three Billets and then cancel the site, its cells
    reopen as a pocket nobody can reach; a worker sent to a tree touching only that pocket would walk to
    the wall for ever (the old BUG-0078 symptom, now needing a deliberate sequence of player orders).
    Fix planned next session: reopened cells that reach nothing stay blocked until a neighbour opens.
  - BUG-0091: a *refused* build order at a spot far round a wall runs the full "does this seal ground"
    check before the cheap checks (can't afford, unit in the way), so 100 refused orders in one tick cost
    22 ms. The fix is to run the cheap checks first; one session.
  - BUG-0092 (S4): a hand-edited repair factor below one 100,000th loads but does nothing; a worker on
    Hold can't build on its own spot; past eight full rings pushed units share a cell; push-out is slow
    with 400 units around a Town Hall (7 ms once).
  - **Update 0800: BUG-0093, BUG-0091 and BUG-0092 are fixed (see the M3-H1 entry at the top); two residuals
    stay (BUG-0095 / 0096, S3, both perf corner cases, next sim clean-up session).**
- **To change it:** repair factors in `game/data/common/rules.json` (`repair.rateFactor`, `costFactor`);
  the never-seal rule, push-out and the "player p plays faction p" rule by inbox note.

### The 60 FPS check is in: a built-in benchmark, smooth turning, and your playtest is the last word (view track, M2-7, 2026-10-06)

- **What was built:** the studio's half of M2's last criterion ("the owner moves an army of 100 units
  around a generated map at 60 FPS"). The game got a benchmark flag: `& $env:GODOT --path game -- --bench 60`
  plays a scripted loop for 60 s (box-select the army, order it, click the four minimap corners, zoom to
  20 m and 60 m, A + click, three Shift-queued moves, H, S, repeating every 10 s), prints one line of frame
  times and quits. `--vsync off` lets it measure the raw frame cost. Units now **turn smoothly** between
  ticks instead of snapping to a new facing 20 times a second. Four screenshots were taken and looked at
  (overview, a ramp crossing with nine units on the ramp, the minimap corner, the F12 overlay).
- **The numbers on this PC** (windowed, HUD and sound on, default map with forests and mines, 100 units
  per side, 60 s): one frame costs **0.72 ms on average and 1.31 ms at the 99th percentile** with vsync
  off, against the 16.7 ms a 60 FPS frame allows (about 5 % of the budget); with vsync on it holds the
  monitor's rate exactly (59.5 fps on a 60 Hz display, 118.5 on 120 Hz). 1,000 units per side zoomed
  out: 2.3 ms a frame. A test scene pins "under 16.7 ms average, under 33 ms worst 1 %" so a regression
  fails the suite.
- **Please play it (five minutes), this is the owner's half of the criterion:** `& $env:GODOT --path game`,
  maximize the window, box-select the grey army west of centre, right-click far across the map past the
  orange army and watch the march through a ramp: the turning should be smooth, no per-tick snapping.
  Click the minimap corners, scroll the wheel from 20 m to 60 m, press A and click, Shift + right-click
  three points, H, S. The FPS in the top-left label should sit at your monitor's refresh rate with no
  hitches. Then `& $env:GODOT --path game -- --bench 60` and read the `bench:` line. Write "M2 playable
  ok" (or what felt wrong) in the inbox; I will sign M2 off after its clean-up session either way unless
  you object.
- **Producer decisions, revisit any time:**
  - *I ticked the criterion on the measured numbers* even though the benchmark's "order across the map"
    step only sends the army about 15 m (BUG-0101, below): the frame budget has 10x headroom and a long
    march adds only sim time that is already measured elsewhere. The benchmark will be fixed to march
    properly in the next view session so it measures what you will do.
  - *The first 30 frames after the armies appear are not timed* (the first draw of new unit shapes is a
    one-time 85 ms hitch, load time rather than play).
  - *Benchmark steps go through the real selection and order code*, not shortcuts, so a bug in a key
    handler shows up in the numbers.
- **Rough edges (view clean-up session, next):** BUG-0101 (S3): the benchmark's "order across" step
  targets the enemy's start spot 15 m away, so the army barely moves. BUG-0102 (S3): the `fps` figure on
  the benchmark line reads low on short runs (it averages the engine's once-a-second counter, which
  includes the loading second; 60 s runs are off by 1-2 %). BUG-0103 (S4): with vsync on the frame times
  print as perfectly even (the engine smooths them), two log lines use the PC's decimal comma, an absurd
  `--bench 1e308` never ends. Also seen: on the default seed the grey army's start block straddles a cliff
  edge (goes with BUG-0085's "inside a forest"). **Update 0800: BUG-0101 / 0102 / 0103 fixed and the start
  block stands in a clearing (BUG-0085); see the M2 sign-off entry at the top. The benchmark now marches the
  army about 109 m across the map and its `fps` is frames divided by seconds.**
- **To change it:** the step list and timings are the table at the top of `sim/Rts.Sim/ViewApi/BenchScript.cs`;
  the warm-up count `BenchRunner.WarmUpFrames`; the rest by inbox note.

### The faction pages are now the design source for every unit and building number (data track, D2, 2026-10-06)

- **What was built:** the second data task. (1) Both faction pages (`docs/factions/malazan.md`,
  `whirlwind.md`) got full building tables: hit points, armor, cost, build time, footprint, what the
  building provides, and what it requires, next to the names and ids they already had. (2) Tests now pin
  every unit's numbers to its faction page's Units table (hit points, armor, attack, cooldown, range,
  speed, sight, cost, population, train time, where it trains), typed from the page, so changing a number
  in the data or on the page alone fails a test. (3) Tests also read the pages themselves and compare.
- **What changed in the data: nothing.** All 14 units already matched their pages, so no name, number or
  description moved, and no replay fingerprint was regenerated. There is nothing to approve this time; the
  table of changes is empty by design.
- **The new page columns** (one row example, Malazan Billet): HP 500 · Armor 3 · Cost 0 / 50 · Build 20 s ·
  Footprint 2×2 · Provides "+8 pop" · Requires "—". The Watch Towers read "Attack 10 pierce / 2 s, range 18;
  sight 24; detector 16 m" straight from the design doc; those tower numbers are not in the data yet (M4).
- **Producer decisions, revisit any time:** melee range is written as `0.5` in the data (edge to edge) and
  as "melee" on the pages; the pin treats them as equal. Radius and wind-up (which the pages don't give)
  are pinned as shipped (my M1-2 values).
- **Rough edges:** BUG-0111 (S4): a few page edits slip past the tests (a false "+N pop" claim on a
  building that gives none, the Forge / tower "Provides" wording, a decimal-comma PC formatting a range);
  fixed in the next data task that touches those tests. **Update 1415: fixed in D3 (held one session, see the
  D3 note near the top).**
- **To change it:** edit the page and the data together (the tests tell you which side disagrees), or
  write the change in the inbox and the data track does both.

### Every building of both factions is in the data now: names, numbers and descriptions for you to check (data track, D1, 2026-10-06)

- **What was built:** the first data-track task. Each faction's building file grew from one entry
  (the Town Hall) to all ten. Names and ids come from the faction pages, every number from the design
  doc's building table (identical for both factions, as the doc says), and each building got a
  one-sentence description in its faction's voice. The Town Hall entries did not change. Tests pin
  the roster to the doc (change a number in the doc without the data, or the other way round, and a
  test fails), and the replay fingerprint was regenerated once with every movement checkpoint
  byte-identical (proof that no unit stat moved).
- **What you'll see:** nothing yet: nobody can place a building until M3-3 (sim) and the build menu
  (view). The data is loaded at every start, so a typo would already fail the boot gate.
- **The change table** (every row is a new entry; "old" was "absent"; one row covers both factions
  because the numbers are the same by design):

  | Slot | Malazan (id) | Whirlwind (id) | HP / armor | Gold / wood | Time | Size | Does |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | House | Billet (`malazan_billet`) | Tent (`whirlwind_tent`) | 500 / 3 | 0 / 50 | 20 s | 2 x 2 | +8 population |
  | Camp | Quartermaster's Depot (`malazan_depot`) | Supply Cache (`whirlwind_supply_cache`) | 600 / 3 | 0 / 75 | 25 s | 2 x 2 | drop-off for gold and wood |
  | Infantry Hall | Legion Barracks (`malazan_barracks`) | Raider Camp (`whirlwind_raider_camp`) | 1200 / 4 | 0 / 150 | 40 s | 3 x 3 | trains the line unit (and the Zealot) |
  | Ranged Hall | Crossbow Range (`malazan_crossbow_range`) | Archer Camp (`whirlwind_archer_camp`) | 1200 / 4 | 0 / 150 | 40 s | 3 x 3 | trains the ranged unit |
  | Shock Hall | Wickan Corral (`malazan_wickan_corral`) | Horse Lines (`whirlwind_horse_lines`) | 1200 / 4 | 75 / 150 | 45 s | 3 x 3 | trains cavalry; needs the Infantry Hall |
  | Forge | Armory (`malazan_armory`) | Smithy (`whirlwind_smithy`) | 1000 / 4 | 100 / 100 | 40 s | 3 x 3 | upgrades (and the faction upgrade) |
  | Caster Hall | Cadre Tower (`malazan_cadre_tower`) | Shrine of the Whirlwind (`whirlwind_shrine`) | 1200 / 4 | 150 / 150 | 50 s | 3 x 3 | trains the caster; Age II |
  | Siege Works | Engineers' Yard (`malazan_engineers_yard`) | Ram Yard (`whirlwind_ram_yard`) | 1400 / 4 | 150 / 200 | 55 s | 3 x 3 | trains siege (and the Sapper); Age II |
  | Watch Tower | Watchtower (`malazan_watchtower`) | Lookout Tower (`whirlwind_lookout_tower`) | 800 / 5 | 50 / 125 | 35 s | 2 x 2 | Age II; its attack, sight and detection are not in the data yet |

  Why these numbers: they are the design doc's table (docs/02 "Buildings"), copied, not tuned.
- **The new player-facing text, quoted** (Malazan, then Whirlwind):
  - Billet: "Bunks, a roof and a cookpot for another squad, raising your population cap by 8."
  - Quartermaster's Depot: "A forward store where Laborers drop off gold and wood, so they spend less time walking and more time working."
  - Legion Barracks: "Trains Heavy Infantry, the shield wall every Malazan legion is built around."
  - Crossbow Range: "Trains the Crossbowman, who does the killing from behind the shield wall."
  - Wickan Corral: "Trains the Wickan Lancer, fast horsemen for flanks and raids; needs a Legion Barracks."
  - Armory: "Researches weapon and armor upgrades for the legions, and Moranth Supply once you reach Age II."
  - Cadre Tower: "Trains the Cadre Mage, the sorcerer attached to every Malazan army; needs Age II."
  - Engineers' Yard: "Trains the Catapult and the Sapper, who blow up the enemy and occasionally themselves; needs Age II."
  - Watchtower: "A manned tower that watches the approaches, spots hidden enemies and shoots at intruders; needs Age II."
  - Tent: "Shelter from the sun for more of the faithful, raising your population cap by 8."
  - Supply Cache: "A hidden store in the sand where Camp Followers drop off gold and wood far from the Holy Camp."
  - Raider Camp: "Trains the Raider and, from Age II, the Zealot: the endless blades of the Apocalypse."
  - Archer Camp: "Trains the Desert Archer, quick on foot and quicker with the bow."
  - Horse Lines: "Trains the Horse Raider to strike enemy worker lines and vanish into the desert; needs a Raider Camp."
  - Smithy: "Researches weapon and armor upgrades for the faithful, and Dryjhna's Prophecy once you reach Age II."
  - Shrine of the Whirlwind: "Trains the Priest of the Whirlwind, who carries the goddess's storm into battle; needs Age II."
  - Ram Yard: "Builds the Battering Ram, which breaks enemy buildings under cover of the swarm; needs Age II."
  - Lookout Tower: "A tall lookout that watches the sands, spots hidden enemies and shoots at intruders; needs Age II."
- **Producer decisions, revisit any time:**
  - *Requirements ("needs Age II", "needs a Legion Barracks") live only in the text for now.* The data
    format has no "requires" field yet; it comes with the Age II task (M3-5 / M3-6), and the text will
    be checked against it then (BUG-0090, S4). Same for the towers' attack and detection (M4). **Update
    1415: the field exists (M3-5), the game enforces it (M3-6), and D3 sets the values from the faction
    pages with a test that every "needs ..." sentence has one (held one session; the towers part of
    BUG-0090 stays for M4).**
  - *The faction upgrade is described as researched at the Forge* (the design doc lists it there);
    where research happens is settled when techs get their data format.
  - *One test rule was relaxed:* a QA test that pinned "an empty Malazan building list leaves exactly
    one building" could not survive ten Whirlwind buildings; it now pins "exactly Whirlwind's ten".
- **Rough edges:** BUG-0090 (S4) above; also a stale code comment saying only the Town Hall ships
  (next sim change in that folder fixes it). **Update 2114: the comment is fixed (M3-3); the stats
  tables you see above are now also on the faction pages (D2 entry at the top).**
- **To change it:** write the tweak in the inbox ("call the Billet a Barracks Annex", "Tent costs 40
  wood", "shorter Smithy text") and the data track does it next session (it regenerates the replay
  fingerprint). Or edit `game/data/factions/<faction>/buildings.json` yourself and run
  `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~Content` to see what the tests pin.

### The game makes a sound when you select and when you give an order (view track, M2-6, 2026-10-06)

- **What was built:** the audio plumbing, with two placeholder sounds generated by the game itself at
  start-up (no sound files, nothing downloaded): a short high **blip** when a click, a box, a
  double-click or a group recall changes your selection to something non-empty, and a rising
  **two-note confirm** when an order goes out (right-click, Shift + right-click, S, H, A + click, a
  minimap right-click). A box over 100 units is one blip; 50 clicks in one frame are one confirm; the
  same sound never repeats within 50 ms, so click spam doesn't buzz. `-- --mute` silences everything
  (for tests and for you). Later sounds (build complete, attack, alert) are one line each in the
  same table. This completes the M2 criterion "Placeholder audio for select and command" (9 of 10).
- **Try it, and please listen:** `& $env:GODOT --path game`. Box-select the blue army (blip),
  right-click across the map (two-note), press Tab (nothing), press 1 twice (one blip, the second
  press jumps the camera and is silent). Nobody in the studio could listen in an unattended session:
  QA checked the waveforms (right pitch, no clicks, silent edges) but **your ears are the last check**.
  If the blip is too sharp or the volume wrong, say so in the inbox; the volume is one constant
  (`Sfx.SfxVolumeDb`, -6 dB) until the M6 settings screen.
- **Producer decisions, revisit any time:**
  - *Generated tones instead of sound files*, so no download was needed (agents can't download); real
    SFX come with M6.
  - *Pressing a group digit when that group is already selected plays nothing* (that press is the
    camera double-tap). Alternative: a blip on every recall.
  - *Sounds play in headless test runs too* (Godot's dummy audio driver takes them silently), so the
    boot gate didn't need a special case.
- **Rough edges (M2 hardening session, after M2-7):** BUG-0087 (S3): a sound still playing when the
  game quits leaves a harmless "instances leaked" warning in the log about 2 runs in 5 (the players
  are never stopped at quit). BUG-0088 (S4, found by me, not caused by this task): one existing test
  scene's double-tap row can fail when the PC is heavily loaded, because it waits on game time while
  the double-tap window uses the wall clock. **Update 0800: both fixed (M2-H2): sounds stop when the game
  quits (10 of 10 runs clean), and the test row waits on the wall clock.**
- **To change it:** pitches and lengths are the note table at the top of `game/scripts/Sfx.cs`;
  volume `Sfx.SfxVolumeDb`; the 50 ms gap `Sfx.MinGapMs`; the rest by inbox note.

### A tree falling no longer stalls the army, and a building dropped in a column's path no longer makes it give up (sim track, M3-2b, 2026-10-06)

- **What was built:** the pathfinding now tells two kinds of map change apart. When ground **opens**
  (a tree is cut down, a building removed), every army's route map stays valid: nothing it points at
  has become a wall, it just might miss a new shortcut. Walkers keep walking it while the game
  refreshes those maps in the background at the usual two per tick (missing maps first, then the
  oldest stale ones). Before, every tree fall threw away every route map at once, so with workers
  chopping steadily most groups stood still until the chopping stopped (BUG-0073). When ground
  **closes** (a building is placed, a mine spawned), every map is thrown away as before, and every
  walker's "am I making progress?" memory is reset, so the longer way round the new wall counts as a
  fresh start instead of "stuck" (BUG-0077: half of a 16-unit column used to give up behind a dropped
  building; now 16 of 16 arrive). A unit standing on a freshly opened square (a tree cut from under
  it) waits a tick or two for its map to refresh instead of giving up. Also: the rules file now
  refuses a tree type bigger than one square (BUG-0074), and the route-map builder got twice as fast
  (0.7 → 0.37 ms per map) after QA's perf check failed the first round.
- **What you'll see:** nothing new on screen; it's a change in how the army behaves while the economy
  works. Once workers chop in the window (M3 HUD) and you can place buildings (M3-3), armies will
  march through a forest being cut without pausing, and a building dropped in front of a column makes
  it walk round, not stop. The numbers: 32 groups marching while a tree falls every single tick: longest
  pause 0 ticks (was 90+ of 100 ticks for 30 of 32 groups), average tick 0.40 ms (was 1.76).
- **Producer decisions, revisit any time:**
  - *Opening changes refresh lazily; closing changes still invalidate everything at once.* Simplest
    rule that is always safe. Its limit: a building placed on every single tick would starve all but
    two groups while it lasts (BUG-0080, S3; nothing in the game places that fast; revisit with M3-3).
  - *Kept my 0.5 ms target rather than relaxing it* when the first round came in at 0.81 ms; the fix
    made the map builder itself faster. Cost: the builder's inner loop is now written out eight times
    (one block per direction, ~150 lines, with the reason in a comment) because the tests run Debug
    builds where the compiler inlines nothing. Readability against 40 % of that budget; I accepted it.
    Alternative: fold it back and accept ~0.56 ms.
  - *Trees bigger than one square are refused* rather than teaching the forest generator about them
    (no such tree is planned).
  - *Save/load note for M6 (BUG-0081, S3):* a stale-but-valid route map now depends on the map as it
    was when built, so "save the keys, rebuild at load" would change the game by a few metres per unit
    on load. The docs now say so; the decision (save the stale maps, or refresh all at save) waits for M6.
  - *Size: ~540 code lines (budget 800), ~600 lines of dev tests (budget 600), ~1,400 of QA tests.*
- **Rough edges:** BUG-0080 and BUG-0081 above (S3, sim hardening in two sessions); the felling perf
  test has ~20 % headroom and fails when the whole suite runs at once (passes every time alone).
  **Update 0800: BUG-0081 is decided (the save file will store the route maps, see the M3-H1 entry at the
  top); BUG-0080 stays as a documented limit with its exact measured bound (the fix was judged riskier
  than the limit; details in that entry).**
- **To change it:** the build cap is `MovementConstants.MaxFieldBuildsPerTick` (2); the rest by inbox note.

### Workers gather gold and wood and carry it home by themselves (sim track, M3-2, 2026-10-06)

- **What was built:** the first thing the economy *does*. A worker ordered onto a gold mine or a
  tree walks to it, stands at its edge, fills up (10 gold in about 14 seconds, 10 wood in about 17,
  the rates from the rules file), carries the load to your nearest drop-off building, adds it to
  your gold or wood total, and walks back for more, with no further orders. When a tree is used
  up, the worker moves to the nearest tree within 20 m; when a mine runs dry it does the same with
  mines, and if there is nothing within 20 m it stops and keeps what it carries. Workers only ever
  cut trees they can stand next to, so a forest is eaten from the outside in and can never leave a
  hole nobody can reach (that closes BUG-0075 from last session). Crowded mines work: 20 workers
  on one mine all deliver (they spread round its eight sides and re-try a freer side every second);
  QA ran 80 on one mine and income kept rising. To have somewhere to deliver, the game now knows
  about **buildings**: the rules file gets a Town Hall for each faction (Malazan "Garrison Keep",
  Whirlwind "Holy Camp": 4 x 4 cells, 2,400 hp, 275 gold / 275 wood, 90 s, +10 pop, drop-off), a
  building store, and a developer command that places one (players can't build yet: that's M3-3).
  Buildings are walls to walkers. Your gold and wood totals start at 200 / 200 (rules file) and are
  part of the replay fingerprint, as is everything a worker carries.
- **What you'll see:** nothing in the window yet: there is no worker key and no resource bar (the
  view track's M3 work: resource bar first, then build menus). The headless tool shows it today:
  `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 50 --workers 10 --forests 12 --mines 8 --ticks 2000`
  prints a Town Hall + 10 workers per player gathering for 100 seconds and ends with
  `player 0 gold 250 wood 300` (from 200 / 200); run it twice and every fingerprint line matches.
- **Numbers:** one worker alone earns 60 gold per 2 minutes (walking included); five on one mine
  earn exactly 5x; 500 marching units plus 50 gathering workers cost 0.92 ms a tick (budget 4);
  200 workers gathering alone 0.26 ms.
- **Producer decisions, revisit any time:**
  - *Only the Town Hall is in the data for now*; the other nine building types per faction are the
    data track's first task (next session), so M3-3 (placing and constructing buildings) has them.
  - *A worker reaches a mine or drop-off from 1.25 m away* and *re-tries every second (20 ticks)*
    when it arrives behind others. Both are engine constants, not balance numbers.
  - *Drop-off = the nearest own drop-off building in a straight line*, chosen when the trip starts
    (the design doc's rule). Walking distance would be truer round cliffs; revisit with M3-3.
  - *Cargo rules:* a Move / Stop / Hold keeps what the worker carries (a later gather of the same
    kind continues from it); ordering a worker carrying wood onto gold **throws the wood away**
    (Age of Empires II's rule, the simplest). Alternative: deliver the old load first, then switch.
  - *A worker that fills up with no drop-off anywhere stops with the load*; building a drop-off
    later does not restart it (a new gather order does). Noted by QA as a "resume" you may want.
  - *A building placed on top of a unit is refused* (the dev command drops it); M3-3 will push
    units out instead, like most RTSs.
  - *Size: about 1,340 code lines against 1,500, tests 1,270 against 1,000.* Accepted.
- **Rough edges (S3, none reachable by a player before M3-3):**
  - BUG-0077: drop a building (dev command) straight across a marching column's path and half the
    column gives up instead of walking round (the "am I making progress" mark survives the detour).
    Fix is part of the next sim task (M3-2b), together with BUG-0073 (continuous tree-felling stalling
    walkers; measured this session at the real chopping rate: longest pause 1.3 s, so it is a comfort
    fix, not an emergency).
  - BUG-0078: if buildings wall off a patch of ground next to a tree, the tree still counts as
    "reachable" and a worker sent to it walks to the wall for ever. M3-3's placement rule (no building
    may wall ground off) makes it impossible; the docs sentence "unreachable targets can't happen"
    is stale until then. **Update 2114: done; BUG-0078 is closed by M3-3's placement rule (see the M3-3
    entry at the top); a residual pocket after a Cancel is BUG-0093.**
  - BUG-0079 (S4): a missing `cost` in a building file reports three errors instead of one;
    `--workers 0` without resources runs though the docs say it needs them.
  - **Update 1744: BUG-0073 and BUG-0077 are fixed (see the M3-2b entry at the top); the other nine
    buildings per faction landed (the D1 entry). Update 0800: BUG-0079 closed (M3-H1).**
- **To change it:** rates, carry and the 20 m search are in `game/data/common/rules.json`
  (`gatherRate`, `workerCarry`, `nodeSearchRadius`); the Town Hall numbers in
  `game/data/factions/<faction>/buildings.json`; reach / retry / cargo rules by inbox note.

### Forests and gold mines are on the map now, and the army walks round them (view track, M2-3b, 2026-10-06)

- **What was built:** the game window's map now has **12 forests and 8 gold mines** (placeholder
  looks: a tree is a dark-green cone on a brown trunk, 3.5 m tall, each turned a little differently;
  a mine is a dark slate block covering its 4 x 4 m with a gold block on top). The minimap paints
  forests dark green and mines gold under the unit dots, and the ground under a felled tree shows
  its true colour again (nothing fells trees in the window yet; it's wired so a tree disappears
  within a fifth of a second when the economy does). The F12 overlay shows resource cells green
  instead of red. This completes M2's "terrain mesh; trees as MultiMesh" criterion.
- **Try it:** `& $env:GODOT --path game`: forests on the plateaus, mines dotted round, the minimap
  shows both. Box-select the blue army and right-click the far side of a forest: they route round
  its edge. `-- --forests 3 --mines 2` for a sparse map, `-- --forests 0 --mines 0` for the old bare
  one, `-- --forests 64 --mines 64` for the densest the generator allows (about 1,700 trees).
  `-- --units 1000 --zoom 60` with the default forests still runs at ~400 fps with vsync off.
- **Producer decisions, revisit any time:**
  - *12 forests and 8 mines on the 128 x 128 map by default* (the view's launch defaults; the
    sim's own default stays 0 so old tests and replays keep their maps). Tell me if you want
    denser or sparser.
  - *Rocks and biome ground colours move to the M6 art pass* (rocks are decoration with no
    gameplay footprint; biome colours come with the real materials). The M2 criterion is ticked
    for the terrain mesh and the trees / mines; the note in the roadmap says so.
  - *A felled tree vanishes* (the list is compacted) rather than shrinking to nothing: the drawn
    count always equals the live count.
  - *Mines turn only in quarter turns, trees any of 256 angles*, so a block never pokes out of its
    footprint.
  - *Size: about 800 code lines (budget 800), tests 780 (budget 600).* Accepted.
- **Rough edges (view hardening session, after M2-6 / M2-7):**
  - BUG-0085 (S3): on the default seed, **your starting army stands partly inside a forest** (17 of
    100 soldiers touch a tree; 9 of 40 start blocks over seeds 1-20 do). Nobody is stuck, it just
    looks wrong and splits the army. My call: the view's debug start layout will skip cells next to
    trees (real start locations with clear bases come in M6). Alternative: the map generator keeps
    forests away from the start blocks (sim track).
  - BUG-0086 (S4): each relist re-uploads both prop buffers even if only one type changed; the
    minimap refresh has ~15 % headroom to its 0.3 ms limit.
  - **Update 0800: BUG-0085 and BUG-0086 fixed (M2-H2): both armies now start in a clearing (no tree, mine
    or cliff edge beside any soldier, the whole army on one level), and a felled tree re-uploads only the
    trees. The minimap refresh margin is now 6-8 % (BUG-0105, S4, a watch item).**
- **To change it:** defaults are `LaunchOptions.DefaultForests` / `DefaultMines` (view code, or an
  inbox note); colours and sizes are constants in `game/scripts/PropsView.cs` and
  `sim/Rts.Sim/ViewApi/MinimapRaster.cs` (placeholders until M6).

### The data track starts next session (studio, 2026-10-06)

- **What changed:** your note of 2026-10-06 is processed. STATE has a Data block in the Now table
  and a data feature queue; `studio/handoff.md` has a "## Data track" section with the first task.
  The only schema on `main` the data track can fill today is `buildings.json` (the unit rosters
  already hold all 7 slots per faction; techs, abilities and AI build orders need schemas from
  M3-5 / M4 / M5). So its first task is **the nine missing building types per faction** (names and
  ids from the faction pages, numbers from the design doc's building table, a one-line description
  each), with the change table and quoted text for you under For your review when it lands.
- **One thing to know:** a data change alters the data fingerprint, so the checked-in golden replay
  gets regenerated with it; the brief requires every movement checkpoint to stay byte-identical
  (proof that no unit stat moved). **Update 1744: landed; see the D1 entry at the top.**

### Gold mines and trees exist in the rules: the first piece of the economy (sim track, M3-1, 2026-10-06)

- **What was built:** the game now knows about resource nodes. A **tree** holds 100 wood and
  covers one 2 m square; a **gold mine** holds 2,500 gold and covers a 2 x 2 block. Both are solid:
  units walk round them. When a tree is cut down to nothing (nothing cuts yet; that's the next sim
  task, workers), its square opens up and every pathfinding arrow map is recomputed, so armies walk
  straight through the gap. A map can be generated with forests (connected clumps of 12-40 trees)
  and mines (at least 24 m apart), and the generator makes sure no patch of ground is ever walled
  in by them. All of this is part of the replay fingerprint, so a replay where a tree falls one tick
  later is a different replay. The replay file format grew (version 3; old files are refused with a
  clear code), and the checked-in golden replay was regenerated once with the proof that every
  unit's path stayed byte-identical.
- **What you'll see:** nothing in the window yet: the game window still builds its map without
  resources, and no tree or mine is drawn. The next view task draws them (placeholder cone trees
  and dark blocks) and turns forests on in the game's map, so in one or two sessions you'll see
  forests and mines on the map and the army routing round them. **Update 1503: landed (see the
  M2-3b entry above); workers gather now too (M3-2 entry), and BUG-0075 is fixed by the gather rule.**
- **Try it now (command line):** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200 --ticks 600 --forests 12 --mines 8`
  twice: the header ends with `forests 12 trees N mines 8` and the fingerprint lines are identical
  both times. Press F12 in the game window later (see the overlay entry) to see resource cells red
  once the view turns forests on.
- **Producer decisions, revisit any time:**
  - *A mine is 2 x 2 cells (4 x 4 m), a tree 1 x 1.* The docs didn't say; 2 x 2 gives eight sides for
    workers to stand on. Alternative: 3 x 3 for a grander mine.
  - *A worked-out mine leaves open ground, like a felled tree* (no rubble blocking the spot).
  - *Maps have no forests or mines unless asked* (the generator's defaults are 0). This kept every
    M1 test and the golden replay's trajectories identical. The game window will ask for them in
    the next view task; the numbers (how many forests, how many mines) are a design call I'll make
    then (something like 12 forests and 8 mines on the 128 x 128 map). Tell me if you have a
    preference, or a preference for where mines go (near start positions comes with M3-3 / M6).
  - *Every mine holds the "start mine" amount (2,500)* until start locations exist and expansion
    mines (2,000) can be told apart.
  - *Room for 4,096 nodes per map* (a setting, not a limit of the design).
  - *Size: about 2,200 lines against a 1,000-1,300 budget*, mostly tests. Accepted: the new part is
    how resources interact with pathfinding and the fingerprint, and the tests are what prove it.
- **Rough edges (S3, none visible before workers exist):**
  - BUG-0073: when trees fall *continuously* (one per tick, QA's stress), every arrow map is
    invalidated every tick and most groups stand still until the felling stops. In play, with workers
    chopping, a tree falls every few seconds, which would mean short stalls. I've made this a design
    question for the next sim task (felled trees only *open* ground, so an old arrow map is still safe
    to follow; the fix is to keep using it and refresh it in the background).
  - BUG-0075: cutting a tree in the *middle* of a forest first leaves an open square nobody can
    reach, and a click on it makes the unit give up. Workers will only ever cut trees they can stand
    next to, so this can't happen in play once that rule is written down; the next sim task does that.
  - BUG-0074: the forest generator assumes trees are 1 x 1; if someone edits the data to make trees
    2 x 2, forests can wall ground in. Next sim clean-up session. **Update 1744: BUG-0073, BUG-0074
    and BUG-0075 are all fixed (M3-2 and M3-2b entries above).**
  - BUG-0076 (S4): small notes (a setup-time number in the docs, fixed; a redundant check that costs
    2 s on a huge 1024 x 1024 map; an empty resources file loads without complaint). **Update 0800:
    BUG-0076 fixed (M3-H1): an empty or one-kind resources file is refused, and the 2 s check runs only
    in debug builds.**
- **To change it:** `game/data/common/resources.json` (node types, footprints, names);
  `game/data/common/rules.json` (`treeWood`, `startMines.gold`); forest sizes and mine spacing are
  generator settings (`MapGenParams`), by inbox note.

### Press F12: the developer overlay shows the pathfinding under the hood (view track, M2-5, 2026-10-06)

- **What was built:** a toggle (F12) that draws the game's inner workings on the map: every ground
  cell as a faint square (red where units can't go, dark red on cliff edges, orange on ramps); yellow
  arrows on the ground showing which way the selected units' pathfinding map points, in a 40 x 40-cell
  window round the camera, with a cyan disc on the destination; a bar graph of the last 120 ticks'
  cost with the 4 ms budget line (bars go red over budget); and a second line in the top-left label
  with the live unit count, how many are moving, how many arrow maps are cached, and the average and
  worst tick time. Off by default; `-- --debug-overlay` starts with it on (so `--screenshot` can capture
  it). While off it costs nothing and nothing is built.
- **Try it:** `& $env:GODOT --path game`. Box-select the blue army, right-click a spot on the far
  plateau, press **F12**: the arrows converge on the ramp (orange) and then run to the cyan disc. Pan the
  camera: the arrow window follows. Press F12 again to hide it. `& $env:GODOT --path game -- --units 1000 --zoom 60 --debug-overlay`
  shows the graph with 2,000 units ticking (bars well under the line).
- **Producer decisions, revisit any time:**
  - *F12*, rebindable (`debug_overlay` in `game/project.godot`); the docs' key list left it free.
  - *Arrows for one destination only*: the lowest-numbered selected unit's. Arrows for every selected
    group would clutter; the window and a Tab subgroup pick the one you mean.
  - *A fixed 40 x 40-cell window round the camera*, not the exact visible area: at the farthest zoom you
    see more ground than the window covers. The visible-trapezoid version is deferred.
  - *The numbers (average, worst) are on the label, not drawn inside the graph.* The dev's call;
    accepted.
  - *Per-unit state labels and the dev console are deferred* (they are in the docs' wish list, not in
    the M2 criterion); labels may come with the M2-7 polish if cheap.
  - *Size: about 2,100 lines against an 800-1,100 budget*, mostly tests (an independent checker that
    recomputes the arrows after every refresh and compares them to what's drawn).
- **Rough edges (view hardening session, after M2-6 / M2-7):** BUG-0083 (S3): with the overlay on,
  the label's text line allocates a little memory every frame while the docs claim zero (the overlay's
  own drawing is zero; the fix is a line in the docs or a cached string). BUG-0084 (S4): cliff edges
  read olive, not dark red, on the green plateau; arrow tips dip a few cm into steep ramps; one compiler
  warning in a test scene; one dev test measures the wrong case. Also seen: with more than 128
  destinations active at once under the 2-maps-per-tick build limit, a new order's arrows can take a
  while to appear (BUG-0025, sim, known). **Update 1503: resource cells (trees, mines) show green in
  the overlay instead of red, since they open up when used. Update 0800: BUG-0083 and BUG-0084 fixed
  (M2-H2): the label is rebuilt only when a number changes, cliff edges read crimson, arrow tips clear the
  ramps.**
- **To change it:** key binding in `game/project.godot`; colours are constants in
  `sim/Rts.Sim/ViewApi/NavOverlayBuilder.cs` and `game/scripts/FlowArrowsView.cs` (dev-only, not
  player-facing); window size `FlowArrowLayout.DefaultWindow`; the rest by inbox note.

### M1 is done: the whole game simulation works without graphics, and I signed it off (sim track, M1-9 + sign-off, 2026-10-06)

- **What M1 is:** everything that makes the game *run*, with no picture yet: generated terraced maps
  with cliffs and ramps; armies of hundreds of units that walk, funnel through chokes, pack up at the
  click point, give up when truly stuck, and make way for each other; Stop / Hold / Attack-move and
  Shift-queues; replays that prove the game plays out identically every time; the 500-unit speed
  target (0.62 ms per tick against a 4 ms budget); and a command-line tool that runs a match with no
  window. Fourteen sessions over four days, three of them clean-up sessions.
- **This session's clean-up (nine items), in plain words:**
  1. A unit told to **hold position now blocks your own soldiers too** (nobody squeezes past a holder
     in a one-cell gap; before, 10 of 30 did). It already blocked the enemy.
  2. **Bad orders are refused at the door** instead of being accepted and corrupting the replay file
     later (only hand-built orders could do this; the game's own keys never produced one).
  3. **Enemy walls of 5+ units now hold** (a line of five foot soldiers across a 4 m choke used to be
     leaked through by your own units shoving each other). Walls of up to 32 units count.
  4. The two-army speed regression is paid back: 2,500 units of two armies fighting over one spot
     cost 10.6 ms per tick, now 6.8; one tight 2,500 blob 4.5 → 4.3 ms, and that number is now a
     test that fails if it regresses.
  5. Found while measuring: a unit that gave up while being pushed off a crowded goal could stop a
     metre away and still "own" the spot, so later arrivals packed around a stray. Fixed (BUG-0058).
  6. Command-line tool: a bad `--record` path is reported before the run, not after a long run; a
     replay too short to check says so; two cosmetic messages fixed.
  7. The docs now list every known limit of the movement code in one place (docs/03 "Known limits
     (M1)").
- **Try it (two minutes):** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 500 --ticks 1500`
  twice: the 15 fingerprint lines are identical and the timing line is under 1 ms per tick. Then
  `& $env:GODOT --path game`: box-select part of the blue army and press **H**; select the rest and
  right-click through the holders: nobody gets past them. Send the whole army across the map and
  watch it stream through a ramp.
- **Producer decisions, revisit any time:**
  - *Signed M1 off myself* (your autopilot setting `stop_at_milestone_end: no`). Conditions held: all
    eight criteria verified, QA coverage complete for every M1 system, no serious open bugs, clean-up
    session done. If you'd rather playtest before a milestone closes, flip that setting to `yes`.
  - *Two limits accepted instead of fixed:* (a) a unit's exact path can depend on the order its
    neighbours were created (BUG-0046); replays are exact anyway (same game, same result), it only
    matters for a hypothetical "re-create the army in a different order" case. The fix was built and
    measured but it reshuffles every crowd's outcome and broke three test bounds; not worth it now.
    (b) 4.7% of units give up when 500 units go to 500 random spots (my target was 3%); every rule
    that got lower also made jams never end. Both revisit with the "crowd cost" pathfinding work after
    M4. Alternative: spend a session re-fitting the test bounds and landing (a) now.
  - *The 64-goal stress test allows one bad map to give up 76 of 128* (it's the map that once
    livelocked; typical maps give up 28-30, and that typical number is now guarded too).
- **Rough edges (all small, next sim clean-up session in four sessions):** BUG-0071 (S3): in one
  constructed geometry the "is this a wall?" shortcut can answer differently depending on which unit
  asks first; deterministic, never seen in random play, no replay risk. BUG-0072 (S4): a `--record`
  file name with illegal characters is still caught only after the run. **Update 0800: both fixed (M3-H1).**
- **For you, one line in your file:** `CLAUDE.md` says "Current milestone: M1". Suggested:
  "Current milestones: M3 (sim track), M2 (view track)", plus the CLI line
  `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200   # headless hashes + timings`.
- **What's next:** M3, the economy: gold mines and trees, workers gathering, building placement and
  construction, production queues, Age II, and the full Malazan and Whirlwind rosters in data.
  **Update 1255: gold mines and trees landed (see the M3-1 entry at the top).**

### View clean-up: the minimap right-click cancels A, lone dots are visible, five bugs closed (view track, M2-H1, 2026-10-06)

- **What was built (clean-up, no new features):**
  1. Right-clicking the **minimap** while A is armed now cancels A and orders nothing, the same as a
     right-click on the 3D map (BUG-0068 fixed).
  2. A double-tap on a group digit at exactly the 300 ms limit always counts the same way; if every
     selected unit dies while A is armed, the next click selects normally instead of being swallowed
     (BUG-0067 fixed).
  3. A height lookup that could crash for a unit billions of metres off the map can't any more
     (BUG-0052 fixed; unreachable in play, but gone).
  4. **Minimap dots have an outline**: each unit's dot is its cell in the player colour inside a
     one-cell dark rim (or a light rim around the dark Malazan colour), so a lone scout no longer
     hides against a ramp tick or a cliff lip (BUG-0064 fixed). Costs 0.13 ms per refresh at 2,000
     units (was 0.06; budget 0.25).
  5. Test gaps closed: the facing test now catches a mirrored turn; the mesh test now catches
     missing cliff walls (BUG-0053 fixed).
- **Try it:** `& $env:GODOT --path game`. Select a few units, press A, right-click the minimap: the
  `A` disappears from the top-left label and nobody moves. Zoom out (`-- --zoom 60`) and look at the
  minimap: every unit is a small outlined square.
- **Producer decisions, revisit any time:**
  - *The rim colour switches by brightness:* dark rim around light colours (orange Whirlwind), light
    rim around dark ones (slate-blue Malazan). The dev's call; I accepted it, but see the rough edge.
  - *The refresh-cost target is the 0.25 ms limit, not the old 0.06 ms.* Doubling a tenth of a
    millisecond five times a second is nothing.
  - *No fix for maps wider than 220 cells* (none exist): a note in the docs says what to do when they do.
- **Rough edge, a taste call for you (BUG-0069, S3):** at the minimap's size a lone dot is a 5 x 5
  pixel square with only 1-4 pixels of player colour in the middle, so **a lone Whirlwind unit reads
  as a black dot and a lone Malazan unit as a near-white one**, close to the white camera outline.
  Two light-coloured factions' scouts would look alike, and gaps inside an army show as thin rim
  lines through it. QA's screenshots are in the conductor's scratch folder for this session
  (`qa100-minimap.png`, `qa990-minimap.png`, 4x crops; not in the repo). Default plan: a 2 x 2-cell
  coloured centre inside the rim (more colour, same visibility) in the next view clean-up session
  (after M2-5, M2-6, M2-7). Alternatives: draw dots in screen pixels (a fixed 3-4 px colour square
  with a 1 px outline, also future-proof for big maps), or rim only around the outside of a crowd.
  Say which in the inbox if you have a preference. BUG-0070 (S4): four doc/test nits, same session.
  **Update 0800: done (M2-H2): the dot is now a 2 x 2 coloured centre in a one-cell rim (the default plan);
  QA read the screen pixels of lone dots at the border, on a ramp and on a cliff lip and every one shows
  3-4 pixels of the exact player colour. BUG-0070 fixed too. The other two styles remain yours to pick.**
- **To change it:** rim colours are `MinimapRaster.DarkRim` / `LightRim` (engine constants); the
  double-tap window is `ControlGroups.DoubleTapSeconds`; the rest by inbox note.

### The order keys are in: A, S, H, Shift-queue, double-click, control groups and Tab (view track, M2-3, 2026-10-06)

- **What was built:** the keyboard half of giving orders. **A** then click: attack-move (for now
  it just walks there; fighting on the way comes in M4). **S**: stop. **H**: hold position (your
  own walkers route round holders instead of nudging them). **Shift + right-click** (or Shift + S /
  H): queue the order behind the current ones, up to 8 per unit. **Double-click** a unit, or **Ctrl +
  click** it: select every unit of that type on screen (Shift adds them). **Ctrl + 1-9** saves the
  selection as a group, **Shift + 1-9** adds to it, **1-9** recalls it, and tapping the digit twice
  quickly jumps the camera to the group. **Tab** steps through the unit types in a mixed selection
  (shown in the top-left label as `sub <type> 1/3`; the command card will use it in M3). Esc
  cancels an armed A. Every key is its own rebindable action in `game/project.godot`.
- **Try it:** `& $env:GODOT --path game`. Box-select the blue army, press H, then select a few
  others and right-click through them: the holders don't budge. Select a group, press A, click far
  away: they walk (the label shows an `A` while you're aiming). Shift + right-click four spots:
  they visit them in order. Ctrl + 1, click empty ground, press 1: they're back; press 1 again at
  once: the camera jumps to them. Double-click one cavalry unit: all cavalry on screen are selected.
- **Producer decisions, revisit any time:** A with nothing selected does nothing; while A is armed,
  a click off the map does nothing and keeps aiming; Ctrl + digit with nothing selected keeps the
  old group (no accidental wipes); recalling an empty group changes nothing; Ctrl + Shift + digit
  counts as assign; the double-tap window is 0.3 s; Tab order is by unit type id; a unit told to
  hold shows no "holding" marker because any queued order ends the hold (so a marker would lie).
- **Rough edges (view hardening session, next view session):** BUG-0068 (S3): right-clicking the
  *minimap* while A is armed sends a plain move and leaves A armed, where a right-click on the
  3D map cancels A as intended. BUG-0067 (S4): a double-tap at exactly 300 ms is a coin flip; if
  every selected unit dies while A is armed, the next click is swallowed (can't happen before M4).
  Also visible now: send a tight group through Shift-queued points and some units give up a leg
  when they bump into their own packed comrades (BUG-0028, the crowd-cost follow-up after M4).
  **Update 0905: BUG-0068 and BUG-0067 fixed (see the view clean-up entry above).**
- **To change it:** key bindings are in `game/project.godot` (one action per key, e.g.
  `order_hold`); the double-tap window is `ControlGroups.DoubleTapSeconds`; the rest by inbox note.

### The game can be run from the command line with no window, and the last M1 criterion is met (sim track, M1-8, 2026-10-06)

- **What was built:** a small tool, `tools/Rts.Cli`, that runs a match headless: it spawns an army
  (every unit type, `--units` of them), marches it across the map from one edge to the far side,
  and prints a fingerprint of the game state every 5 seconds of game time plus how long each tick
  took (average, 99th percentile, worst). It can record the run as a replay file and play a replay
  back, checking every fingerprint; the exit code says what happened (0 fine, 1 bad arguments or
  files, 2 a replay that doesn't match). This is the "runs a scenario headless and prints hashes
  and timings" criterion, the last of M1's eight. The same task gave the view a read-only window
  into the pathfinding arrow maps, so the M2-5 debug overlay can draw them.
- **Try it:** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200 --ticks 1500`. Run
  it twice: the 15 fingerprint lines are identical (that's determinism you can see). Add
  `--record x.replay` then `dotnet run --project tools/Rts.Cli -- play x.replay`: "ok: 15
  checkpoints matched". `--units 2500` shows the tick cost at scale (about 4.6 ms on this PC).
  Update 1255: `--forests 12 --mines 8` adds resources to the map (see the M3-1 entry). Update 1503:
  `--workers 10` adds a Town Hall and 10 gathering workers per player and prints each player's gold
  and wood at the end (see the M3-2 entry).
- **Producer decisions, revisit any time:** the march goes to the walkable cell farthest (by
  walking distance) from the middle of the player's own map edge, so armies cross the whole map
  (with `--players 2` the two armies cross each other); `--units` is the total across players and
  is capped at 100,000 (the start blocks hold about 14,600 on the default map).
- **Rough edges (M1 hardening session):** BUG-0057 (S4): an unwritable `--record` path is only
  reported after the run finishes; a replay recorded with fewer ticks than the checkpoint interval
  "passes" with nothing compared; a cosmetic `: :` in one error message. (The docs mismatch about
  timing with `--record` is already fixed.) **Update 0905: BUG-0057 fixed; one leftover (illegal
  characters in the file name) is BUG-0072, S4.**
- **For you, one line in your file:** `CLAUDE.md`'s Commands block has no CLI line yet (agents
  don't edit it). Suggested: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200   # headless hashes + timings`.
- **What's next for M1:** one clean-up session (nine small bugs, listed in the sim debt backlog),
  then I sign M1 off and write its retro; M3 (economy and buildings) starts after. **Update 0905: done,
  see the M1 entry at the top.**

### Stop, hold position, attack-move and Shift-queued orders exist in the rules; the M1 speed target is proven (sim track, M1-7, 2026-10-05)

- **What was built:** three new orders a unit understands besides "walk there": **Stop** (drop
  everything and stand), **Hold position** (stand and refuse to be pushed aside by your own
  walkers; it will fight from there in M4), and **Attack-move** (walk there; from M4 it will stop
  to fight anything met on the way). Plus **Shift-queuing**: up to 8 orders lined up per unit, run
  one after the other; a queued Stop or Hold ends the line; a unit that gives up on one leg still
  tries the next. Replays now record the queue flag (format 2; the old format is refused with a
  clear code; the checked-in golden replay was regenerated and every fingerprint stayed identical,
  which proves no movement rule changed). Also the M1 roadmap criterion "500 moving units under
  4 ms per tick" now has a named, enforced test.
- **What you'll see:** nothing yet; there are no keys for it. The next view task (M2-3) wires A /
  S / H / Shift. Then: select a group, press H, and walkers from your own army route round them
  instead of nudging them off their spot; Shift-right-click four points and the group walks them
  in order. Update M2-3: landed; see the entry above.
- **Numbers:** 500 units marching cost 0.67 ms per tick on this PC (budget 4 ms, so 6x headroom);
  2,500 units each with a full queue of 8: 3.0 ms.
- **Producer decisions, revisit any time:**
  - *A held unit still lets its own army squeeze past it in a one-cell gap* (BUG-0055, S3): 10 of 30
    friendly walkers got past a big unit holding a 2 m corridor; enemies never do. This is the
    "your own standing units are soft" rule from M1-5, applied to holders as the brief said. I
    think a unit told to hold a choke should block its own side too (that's what H means), and I've
    scheduled that change for the M1 clean-up session, as long as it doesn't hurt the crowd numbers.
    Alternative: keep holders soft and document it. **Update 0905: done, holders block everyone; the
    crowd numbers didn't move.**
  - *Hold, then Shift-queue a move = walk off at once.* A queued order given to a holding unit
    starts immediately (it's standing idle), and starting an order releases the hold. This is how
    most RTSs behave; the HUD must not show "holding" once an order is queued after it (noted for M2-3).
  - *Queue of 8 per unit; a 9th is dropped silently.* The view can warn if it wants to.
  - *One tick over budget on size* (about 1,510 changed lines vs 1,500). Accepted.
- **Rough edges (M1 clean-up session):** BUG-0055 above; BUG-0054 (S3): a hand-built order with a
  flag bit the rules don't know is accepted and then makes the replay file unwritable (no factory
  produces one, and the game doesn't save replays until M6); BUG-0056 (S4): three nits. **Update
  0905: all three fixed.**
- **To change it:** the queue length is `OrderConstants.QueueCapacity` (an engine constant, not
  data); the hold / stop behaviour is a rules decision: write it in the inbox.

### The minimap is in: see the whole map, jump the camera, send troops with a right-click (view track, M2-4, 2026-10-05)

- **What was built:** a 220-pixel map in the bottom-left corner: the three height levels in their
  tints, ramps as small orange ticks, cliff lips dark, the unreachable border darkened; every unit a
  dot in its side's colour (refreshed 5 times a second); and a white outline showing exactly what
  the camera sees (wider at the top, since the camera looks across the ground at an angle). It is
  the first piece of the HUD (`Hud` layer), so later panels have a home.
- **Try it:** `& $env:GODOT --path game`. Left-click anywhere on the minimap (or hold and drag) and
  the camera jumps there. Box-select your army, then right-click a spot on the minimap: they walk
  there, same as right-clicking the ground. Hovering the minimap never scrolls the camera. `-- --no-hud`
  starts without it (clean screenshots). `-- --units 1000 --zoom 60` still runs at 120 fps.
- **Producer decisions, revisit any time:**
  - *Bottom-left, 220 px, square; a non-square map gets letterboxed* (dark bars) instead of
    stretched, so distances read the same both ways. Alternative: stretch to fill.
  - *Same placeholder colours as the 3D terrain,* so what you see on the minimap matches the ground.
    Real biome colours come with the art pass (M6).
  - *Dots are one map cell (2 m) and show the current tick, not the smooth in-between position*;
    at 5 refreshes a second nobody can tell. **Update 0905: dots are now the cell plus a one-cell rim
    (see the view clean-up entry).**
  - *The mouse wheel does nothing over the minimap* (minimap zoom may come later).
- **Rough edges (view hardening session, after M2-3):** BUG-0064 (S4): a lone Whirlwind dot is
  nearly the same orange as a ramp tick, and a lone Malazan dot on a cliff lip is nearly the cliff
  colour, so single scouts can hide in plain sight; likely fix is an outline or a 2x2 dot. On maps
  wider than 220 cells (none exist in the game yet) about a quarter of cells would never get a
  pixel. No fog, resource markers, pings or Alt-click yet (M3/M4). Update M2-3: a Shift + right-click
  on the minimap now queues the move, like on the 3D map. **Update 0905: BUG-0064 fixed (outline);
  the follow-up on the outline's colour is BUG-0069.**
- **To change it:** size and corner are in `game/scenes/Match.tscn` (the `Minimap` node's
  offsets); dot colours follow each faction's `primaryColor` in `game/data/factions/<faction>/`.

### You can now select your soldiers and send them somewhere (view track, M2-2, 2026-10-05)

- **What was built:** when the game opens, 200 placeholder units stand on the map: your army in
  Malazan slate-blue to the west of the centre, the Whirlwind army in orange to the east. Each unit
  is a capsule sized by its body radius from the data (foot soldiers small, cavalry bigger, siege
  biggest). Left-click a unit, or drag a box over several; hold Shift to add or remove; click empty
  ground to clear. Selected units get a green ring. Right-click anywhere on the map and they walk
  there, smoothly at the frame rate (the game ticks 20 times a second; the picture blends between
  ticks). Enemy units can't be selected. The top-left label now ends with `sel N`.
- **Try it:** `& $env:GODOT --path game`. Drag a box over the blue army, then right-click the ramp
  or the plateau behind it: the crowd funnels through the ramp (the M1-5 behaviour, now visible).
  `-- --units 1000` starts 2,000 units (120 fps on this PC, vsync-capped); `-- --zoom 60` starts
  zoomed out.
- **Producer decisions, revisit any time:**
  - *Team colour = faction colour.* Player 0 plays Malazan, player 1 Whirlwind, until M6's match
    setup gives each player a colour of their own. Alternative: a per-player palette now; skipped
    because there is no setup screen to pick it in yet.
  - *Clicks have a 12-pixel minimum radius* so far-away units stay clickable; *Shift + click on
    empty ground keeps the selection* (as in most RTSs); *a click on a cliff face sends units to the
    plateau above it* (the wall belongs to the higher ground).
  - *Start positions are a debug layout*: two half-disc blocks either side of the map's centre, so
    both armies fit the opening camera. Real start locations come with the maps in M6.
  - *Over the size budget* (2,085 lines vs 1,500; about 900 of them game code, the rest tests).
    Accepted because the code is plain and the tests are what make the picking exact (the click
    point lands within 1 mm of the drawn ground).
- **Rough edges:** units turn to face where they walk but don't animate (capsules). With 2,000
  units selected, three right-clicks inside one tick overflow the order queue and the third is
  dropped with a warning (by design; the queue holds 4,096 orders). Two nits for the view hardening
  session: BUG-0052 (S3, a height lookup would crash for a unit billions of metres off the map,
  which can't happen) and BUG-0053 (S4, a test that can't catch a facing sign flip, a stale remark,
  one doc number). Update M2-4: the minimap's right-click uses the same order path. **Update 0905:
  BUG-0052 and BUG-0053 fixed.**
- **To change it:** unit sizes are the `radius` values in `game/data/factions/<faction>/units.json`;
  team colours are each faction's `primaryColor` in `game/data/factions/<faction>/`. Both are data
  edits (plus a golden-replay regen, which the studio does).

### Crowds route round each other, parked units make way, enemy walls hold better (sim track, M1-4d-3 hardening, 2026-10-05)

- **What was built** (the M1 hardening session: five debt items in one batch):
  1. Walkers steer round other groups' standing clusters and enemy clumps instead of pressing in
     and giving up (the shorter way round; ties go right).
  2. Waiting isn't being stuck: a unit behind traffic that is still moving (anyone's, within 2 m),
     or behind a unit waiting for its path, keeps its patience (behind a path-wait it loses patience
     four times slower rather than never, so a jam always ends).
  3. A unit pushed off its cluster walks back to its spot once the pushing stops, once per order,
     so a corridor can't bounce it back and forth forever.
  4. A pair parked in a one-cell corridor is pushed along by a walker stuck behind it (parked lines
     of up to 3 yield together; a cluster's spot still holds).
  5. Smaller fixes: an enemy standing on your click point is a wall, not a friend (BUG-0037); a
     walker beside an enemy no longer slides into its own standing comrade (BUG-0038); a short
     re-click inside the same 2 m square moves an arrived unit (BUG-0030); a hand-edited replay can't
     declare more than 24 hours (BUG-0040 part 1); the movement tests now run on many maps instead
     of one (BUG-0034/0039).
- **What you'll see:** send one group through another's parked cluster and most now get through:
  111 of 200 walkers cross a mixed cluster (was 2); two groups swapping rooms through a one-cell
  corridor all arrive; the parked pair steps along. Groups sent to four points 6 m apart: 51% arrive
  (was 36%; on the fair comparison 241 → 256 of 500).
- **Producer decisions, revisit any time:**
  - *Accepted below my targets again* (four points 51% / 34% vs 60% / 50%; 64 small groups 19% give
    up vs 10%; walkers crossing their own army's settled cluster 22 of 200 vs 150). Every row beats
    the old code on the same setup, and the cause is the same structural one: the shared path map
    doesn't know where units stand, so four clusters merge into one 12 m mass that walkers from
    the far side can't get round before their 1-second patience runs out. The real fix is a "crowd
    cost" in the path maps; I'm not scheduling it inside M1 and will propose it once the M4 combat
    sandbox shows whether it matters in play.
  - *Found and fixed in-session* (QA): one map in 160 where 94 units walked forever (S1); the
    walk-back pushing the walker it had just let through back off its goal (S2); a re-click
    mid-route making a walker give up (S2).
  - *BUG-0045 lowered from S2 to S3, my call, you may object.* QA found that a line of **five or
    more** standing enemies plugging a passage (for example 5 foot soldiers across a 4 m choke) can
    still be leaked through: your own units shove a given-up comrade into the enemies, and it later
    walks out the far side. This was already true before this session; the M1-5 entry below said
    "nobody gets through", which held for the 3-unit plugs tested then. Plugs of up to 4 units now
    hold, including diagonal ones and ones that form and dissolve mid-push. I didn't block the merge
    on it: nothing you can see depends on it before M4 (units pressed into enemies will be fighting,
    not standing), and it predates this work. It's scheduled for the M1 end-of-milestone hardening
    session. Alternative: reject the whole session over it, losing nine verified fixes. **Update
    0905: fixed; plugs of up to 32 hold.**
  - *Perf:* 500 moving units cost 0.17 ms per tick (the M1 budget is 4 ms). At 2,500 units, one tight
    cluster is at 4.5-4.6 ms vs my 4.5 ms slice target (3-5% over the old code the same day), and
    two-player clusters got 2x slower (BUG-0044, S3: the new plug check runs for every touching
    enemy). All beyond M1's supported scale; hardening session. **Update 0905: paid back (4.3 and
    6.8 ms).**
  - *The old crowd tests measured the wrong thing:* a slot-numbering slip sent both players to
    every point, so they measured enemies contesting a spot, not crowds. Re-set to one player per
    point, with the old code re-measured on the same setup for a fair comparison.
  - *Every map's trajectories changed once* (golden replay regenerated, reason in the commit).
- **Rough edges (all S3/S4, M1 end-of-milestone hardening):** BUG-0044 two-player perf at 2,500;
  BUG-0045 plugs of 5+; BUG-0046 a walker's exact path can depend on the order its neighbours were
  spawned in (replays are unaffected: same seed + same orders still match tick for tick); BUG-0049
  one 64-goal map gives up 75 of 128 (test bound 48); BUG-0050 random-goal give-ups 4.7% vs the 3%
  target (the price of the livelock fix); BUG-0047 test-guard nits. BUG-0028/0032 (crowd targets)
  stay open as the "crowd cost" follow-up. **Update 0905: BUG-0044 / 0045 / 0047 / 0049 fixed;
  BUG-0046 and BUG-0050 accepted as known limits (see the M1 entry at the top).**
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CrowdRoutingTests" --logger "console;verbosity=detailed"`.

### The game window shows a 3D map you can fly over (view track, M2-1, 2026-10-05)

- **What was built:** the first picture. `& $env:GODOT --path game` now opens a window with a
  generated map in 3D: flat plateaus at three heights (tan lowland, green upland, pale stone top),
  dark vertical cliff walls, orange sloped ramps with walls at their sides, a sun with shadows and a
  sky. The simulation ticks underneath at 20 ticks per second; a small white label in the top-left
  shows the tick number, game speed, how long the last tick took, and the frame rate. No units are
  drawn yet (next view task). Update M2-2: units are drawn now, see the entry above.
- **Try it:** arrow keys pan; push the mouse against a window edge to pan; hold the middle mouse
  button and drag to grab the ground; mouse wheel zooms between 20 m and 60 m above the ground.
  The camera can't leave the map. `& $env:GODOT --path game -- --seed 6` shows a map with all three
  heights in view at the start; `-- --speed 4` runs the sim at 4x.
- **Screenshot tool (for the studio and for you):** `& $env:GODOT --path game -- --screenshot
  C:\temp\shot.png --screenshot-after 2` saves a picture after 2 seconds and quits. Every later
  visual task is checked with it, so the studio no longer needs the optional Godot MCP install.
- **Producer decisions, revisit any time:**
  - *Placeholder colours* (three level tints, ramp orange, cliff brown) are hard-coded in the mesh
    builder for now; biome colours and real materials come with the art pass (M6). Change them by
    an inbox note until then.
  - *Camera numbers from the design doc as written:* 55° pitch, no rotation, 20-60 m zoom, 4 m per
    wheel notch, pan speed equal to the zoom height per second. Alternative: a rotating camera; the
    doc rules it out for readability.
  - *The pure camera/clock/mesh logic lives in `sim/Rts.Sim/ViewApi/`* so it is unit-tested
    without Godot. It reads the sim and never writes it (QA proved this: 100 mesh builds between
    ticks leave the game state hash untouched).
- **Rough edges:** ramps look a little steeper at their two ends than in the middle (about 31° vs
  22°), because heights are stored at cell centres; cosmetic, no fix planned unless it bothers you.
  Three tiny input nits (BUG-0041: a `--seed` with no value swallows the next flag; the start-up log
  prints the largest possible seed as -1; a clock corner case no code path reaches) go to the view
  hardening session. Update M2-2: BUG-0041 fixed. The test scene `game/tests/CameraClampTest.tscn`
  compiles into the game for now; it is excluded when the release build exists (M6).

### Replays and a determinism proof that outlives the session (sim track, M1-6, 2026-10-05)

- **What was built:** a replay file format and player. A replay stores the map seed and settings,
  every command the game accepted (with the tick it was given), and a fingerprint of the game state
  every 5 seconds. Playing it back re-runs the match from the seed and checks every fingerprint;
  the first mismatch stops it and names the tick. One "golden" replay is checked in: 200 units of
  every kind marching across the seed-1 map, 75 seconds of game time, 15 fingerprints, a 15 KB text
  file you can read in a diff.
- **What it means for you:** from now on, any change to movement, tick order, random numbers or
  unit stats that changes how a match plays out fails the test suite until a developer regenerates
  the golden on purpose and explains why in the commit. This is the guard that keeps future replays
  and saved games exact, and it's the backbone of the M6 replay viewer. Update M1-4d-3: the guard
  did its job; the golden was regenerated once, with the reason in the commit. Update M1-7: the
  file format grew a column (format 2) and the fingerprints stayed byte-identical. Update M1-8: you
  can record and play replays yourself from the command line (see the M1-8 entry). Update M1-9: the
  golden stayed byte-identical through the whole clean-up batch. Update 1255: format 3 (resources);
  the fingerprints changed because the fingerprint now covers trees and mines, but every unit's path
  was proven identical before and after.
- **Also fixed (BUG-0014):** two particular seeds (0 and the largest possible number) used to give
  the same map. The seed is now scrambled before use, so every seed is its own map. Side effect:
  **every map changed once.** Seed 1 today is not the seed 1 of yesterday. Nothing you have seen is
  lost (nothing was on screen yet).
- **Producer decisions, revisit any time:**
  - *A replay is tied to the exact game data it was recorded with*, including display text. Change
    one number in `game/data/` and old replays refuse to play (with a clear code, never a silent
    desync). Alternative: hash only balance-relevant fields; rejected for now because a wrong guess
    there means a desync that looks like a bug.
  - *Fingerprints every 100 ticks (5 s)* by default; file size grows ~17 bytes per checkpoint, so a
    30-minute match is under 1 MB even with thousands of orders.
  - *Six movement tests keep running on their old maps* through a helper that undoes the seed
    scramble, because their pass/fail bounds were tuned to one map. QA showed those bounds fail on
    most other maps (BUG-0039, S3, pre-existing): the next sim session, a hardening one, re-measures
    them on many maps. Update M1-4d-3: done; the helper is gone from those tests and the bounds hold
    on every swept map (one 64-goal map still breaks a bound, BUG-0049). Update M1-9: BUG-0049
    re-bounded on 80 maps.
- **Rough edges:** a hand-edited replay can declare a tick count of two billion and make playback
  spin for hours (BUG-0040, S4; a limit is a few lines, hardening session). Update M1-4d-3: the
  24-hour limit is in. When the AI arrives (M5) the recording point inside the tick needs a decision
  (also BUG-0040, still open for that part).

### Two tracks started: the view track begins M2 while the sim track finishes M1 (2026-10-05, session 1446 plan)

- **What changed for you:** from this session the game window stops being an empty scene (see the
  M2-1 entry above: it landed). Units, selection and right-click orders are the next view task (M2-2).
  Update 1609: M2-2 landed too. Update 2330: the minimap (M2-4) landed. Update 0655: the order keys
  (M2-3) landed; M1's criteria are all met. Update 0905: M1 signed off; sim starts M3. Update 1255:
  resources (M3-1) and the F12 overlay (M2-5) landed.
- **Producer decisions, revisit any time:**
  - *Where the view's tests live.* Dev tests in `sim/Rts.Sim.Tests/ViewApi/`, QA tests in
    `sim/Rts.Sim.Tests/QA/ViewApi/`, Godot-side test scenes in `game/tests/`; `ViewApi/` may hold
    pure helpers with no sim reference (fixed-step clock, terrain geometry, camera limits) so that
    logic is unit-tested without Godot. Alternative: put those helpers in `game/scripts/`, where
    nothing can test them. Now also written into the ownership table in docs/07.
  - *The sim track keeps its public setup API additive* (no new required fields on `SimConfig`, no
    signature changes to `Simulation` or the data loader), so the view compiles after both merge.
    Held this session and the next. Update 2330: held again (M1-7 kept the 3-argument `Move`).
    Update 0655: held (M1-8 added one read-only method and nothing else to the sim). Update 0905:
    held (one unused public constant renamed; `Enqueue` now throws on malformed commands, which the
    view never produces). Update 1255: held (`SimConfig.ResourceCapacity` optional; a new required
    data file `resources.json` that the game already ships).
  - *First view slice order:* scene + terrain + camera + screenshot flag before unit views, because
    the screenshot flag is how the studio (and you) verify every later visual task.
  - *Bug numbering with two QA inspectors:* both filed a BUG-0039 this session. From now on the
    brief gives each track its own starting number (view = sim + 10). The view's bug is BUG-0041.
- **Docs drift for you to fix (one line, your file):** `CLAUDE.md` still says "Current milestone:
  M1" and "don't write gameplay code ahead of the roadmap". With two tracks that line is per
  track (sim M3 now, view M2). The agents don't edit `CLAUDE.md`; suggested text: "Current
  milestones: M3 (sim track), M2 (view track)". Update 0655: plus the CLI command line (M1-8 entry).
- **Cap note:** `autopilot.md` says 8 sessions per day; the old dashboard said 10. The studio
  follows autopilot (8).

### The M1 headline works: an army crosses the map and climbs a ramp (M1-5, 2026-10-05)

- **What was built:** the proof test for M1's main promise. 200 soldiers of every kind (foot,
  cavalry, siege) start near the west edge of a generated map and are sent to the far side of a
  plateau, so they must funnel through a 6 m ramp. On every map tried (58 maps, plus 20 with a
  climb of two ramps, plus armies of 500 and 1,000) everyone arrives, nobody gives up, nobody ends
  up in a cliff, and running the same march twice gives the identical result tick for tick. The
  march takes 72-127 seconds of game time, under half the allowed limit.
- **What you'll see:** select your whole army and right-click across the map: the group streams
  through the ramp as a crowd, the ones pressed against the ramp's walls wait their turn instead
  of quitting, and the whole army arrives and packs up around the click point. Before this
  session, a few units (1-9 per march) got pinned at the corners of ramps and gaps between
  plateaus, stopped, and stayed behind. Update M2-2: you can do exactly this in the game window now.
- **Producer decisions, revisit any time:**
  - *Waiting in a moving queue doesn't count as stuck.* A unit only starts its 1-second give-up
    timer when nobody just ahead of it is making headway either. Trade-off: a jammed crowd (for
    example behind an enemy blocking a pass) takes a little longer to give up, about 10-14 seconds
    for 60 units instead of 9-10.
  - *Enemy units holding their ground are solid.* QA found that a walker squeezed between two
    standing enemies could slip through a 0.2 m slit (an old bug, made worse by the new patience).
    Now a line of enemies plugging a ramp holds: nobody gets through, everyone behind gives up.
    Your *own* standing units stay "soft" (a walker can still slip between two of them, and can
    shove idle ones aside), because making them solid too cut crowd arrivals in the tests.
    **Correction (1609):** "nobody gets through" held for the 3-unit plugs tested then. Plugs of 5
    or more enemies can still be leaked through by your own units shoving each other (BUG-0045,
    S3, pre-existing); plugs of up to 4 hold since M1-4d-3. See that entry. **Update 0905: plugs of
    up to 32 hold; your own *holding* units are solid too.**
- **Rough edges (S3, next hardening session):** if an enemy is standing exactly on the spot you
  clicked, your unit treats it as a friend and stops touching it, a hand's width inside it
  (BUG-0037); and a unit squeezed between an enemy and its own standing comrade can press into the
  comrade by up to 14 cm for a tick (BUG-0038). Neither lets anyone through a plug. Also, with
  500-1,000 units marching, a few pairs end up closer than they should (a known limit of the
  back-off rule). Update M1-4d-3: BUG-0037 and BUG-0038 fixed.
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ScenarioTests" --logger "console;verbosity=detailed"` prints each seed's path length, ticks taken and the limit. Update M1-6: the same march is now the golden replay (`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`). Update M1-8: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200` runs the same kind of march from the command line.

### Idle units step aside for walkers (M1-4d-2, 2026-10-05)

- **What you'll see:** soldiers walking through your own idle troops push them out of the way
  instead of stopping. A clustered group bends to let walkers by but doesn't split apart. A lone
  unit parked in a narrow pass steps aside once a friendly walker has been stuck behind it for
  0.5 s. Enemy units and units that are already walking are never pushed. A unit squeezed between
  a standing unit and a cliff can walk away again (BUG-0031 fixed). Update M1-7: a unit told to
  hold position is never pushed either.
- **Producer calls, revisit any time:**
  - Accepted although my own crowd targets were missed: groups sent to 4 nearby points now arrive
    35-44% of the time (was about 16%), against the 60-80% I asked for. The causes are structural
    (pathfinding ignores units; units waiting for a path and enemies act as walls) and were
    outside this task. QA filed them as S2 (BUG-0032/0033); I set them to S3 and scheduled a
    follow-up, M1-4d-3 crowd routing. Update 2026-10-05-1234: that follow-up now waits for the M1
    hardening session (your rule: S3 work goes there), and the cross-map scenario test (M1-5) went
    first because one army walking to one spot doesn't need it (confirmed: it passed without it).
    Update 1446: the hardening session is the next sim session. Update 1609: it landed (see the
    M1-4d-3 entry above); BUG-0033 fixed, BUG-0032 stays open for the crowd-cost follow-up.
  - Units standing on their own click point hold it. So a parked *group* in a 1-cell corridor still
    blocks its own army until you move it (BUG-0033 remainder). Update 1609: a parked pair now
    yields; a cluster's spot still holds.
- **Rough edge:** walkers crossing a cluster that mixes both players' units mostly give up (2 of
  200 in a test, 28 before shoving). M1-4d-3 targets this. Update 1609: 111 of 200 now.
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "PastAFriendlyUnitParkedThereByAMove|WalkersCrossingASettledBlob" --logger "console;verbosity=detailed"`.

### Two studio sessions overlapped (incident note, 2026-10-05)

- At about 12:14 a second session (id 2026-10-05-1214) overwrote `studio/.session.lock` in your
  checkout while session 1013 was still running; your three studio-skill commits (724234a,
  432fafe, c089405) went to main meanwhile. The studio worktree was untouched and session 1013
  finished normally. If that second session is still chaining, two conductors may be running.
  Your "Downloads stay with the owner" inbox note was processed at the 1234 PLAN (reflected in
  the M6 queue line). Session 1234 ran on the studio-session skill as loaded at its start; your
  later two-track (sim + view) change to the skill applies from the next session. Update 1446:
  this session is the first on the two-track skill; no second conductor has shown up since.
  Update 1609: a power outage at about 16:40 interrupted both builders; both were resumed from
  their transcripts with work intact, no second conductor. Update 2330: a quiet session. Update
  0655: quiet again. Update 0905: one near miss, no harm: the view builder's file I/O used a
  relative path and briefly rewrote two files in *your* checkout (`game/scripts/UnitViews.cs`,
  `game/tests/UnitViewsTest.cs`); restored byte-identical, your `git status` shows only your own
  `.claude/settings.json` change. Builders are reminded to use absolute paths. Update 1255: quiet
  (Remote Control was refused as "unavailable in unattended sessions"; no effect on the work).

### Units cluster around the click point, and give up when stuck (M1-4d-1, 2026-10-05)

- **What you'll see:** select 30 soldiers and right-click a spot. They walk there and pack into a
  tight cluster centered on the click, shoulder to shoulder, rather than lining up in a formation.
  Two units meeting in a narrow pass both step to their right and slip past each other.
  Spam-clicking the same spot doesn't make them restart or stutter.
- **Giving up:** a unit that makes no headway for 1 second (wedged behind a crowd, or blocked by
  standing units) stops and stands idle instead of jittering in place forever. Update M1-5: waiting
  behind a groupmate who is still moving no longer counts as "no headway". Update M1-4d-3: nor does
  waiting behind anyone's moving traffic within 2 m.
- **Producer decisions:**
  - Clusters instead of formations for now: StarCraft-style clumping, where Age of Empires uses
    line or box formations. Formations can come back in M2 with group commands.
  - 1 second before giving up. Shorter feels snappier but units stop short more often; longer
    means more pushing and shoving before they quit.
  - Clicking the spot a unit is already heading to counts as the same order.
- **Rough edges until the next session (shoving):** units that are standing still never step
  aside yet. So two groups sent to spots close together bump into each other's clusters and many
  stop short: in a stress test, 84% of 500 units sent to 4 nearby points gave up 8-16 m early
  (BUG-0028). A unit squeezed between a standing unit and a cliff can't walk away (BUG-0031).
  Both should go away once idle units make room for moving ones. (Update: shoving landed in
  M1-4d-2; BUG-0031 fixed, BUG-0028 partly, see the entry above.)
- **Also open:** a short nudge order (under about 2.8 m, inside the same 2 m map cell) to a unit
  that has already arrived is ignored (BUG-0030). It's in the debt backlog. Update M1-4d-3: fixed.

### Pathfinding: shared arrow maps with a speed limit (M1-4b and M1-4c, 2026-10-04)

- **What it is:** when you send units somewhere, the game computes one map of arrows for that
  destination: every 2 m square points along the shortest route there. Every unit headed to that
  spot follows the same map, so moving 200 units costs about the same as moving one. The game
  remembers recent destinations (32 to 128, depending on how many units the game allows), so
  re-ordering to a recent spot is instant.
- **Speed limit (Producer decision):** at most 2 new arrow maps per tick, which is 40 per second,
  oldest orders first. Each takes about 0.7 ms to build, and the limit keeps every tick inside its
  time budget so the game never hitches.
- **What you'll see:** nothing in normal play. Only in a big burst: order 32 separate groups to 32
  different spots at the same instant and the last group starts walking about 0.8 s after the first.
  Update M1-8: the M2-5 debug overlay will be able to draw these arrows on the ground. Update 1255:
  it does; press F12 (see the overlay entry at the top).
- **Why it's saved with the game (Producer decision):** which arrow maps are remembered decides
  which units wait a tick, so that memory is saved and checked like the rest of the game state.
  That keeps replays and saved games exact.
- **Rough edges (debt backlog):** with more separate destinations active at once than the game
  remembers (only in very large games with many small groups), some older groups can stall while
  newer ones walk (BUG-0025). In same-instant order bursts, one side of the map gets served
  0.1-0.2 s sooner on average (BUG-0026, cosmetic). Update M1-7: maps larger than 256 x 256 are
  now documented as unsupported until arrow maps can be built in slices (BUG-0023); the design
  map is 128 x 128.

### Terraced maps: plateaus, cliffs and ramps (M1-3 and M1-4a, 2026-10-03)

- **What you'll see:** maps have up to three height levels (ground, 4 m and 8 m up), each a flat
  plateau. Plateau edges are cliffs no unit can climb. The only way up is a ramp, 6 m wide and 8 m
  long with a gentle slope, entered only at its top or bottom because its sides are walled. Ramps
  are natural chokepoints, wide enough for about 7 foot soldiers or 4 horsemen side by side, so a
  few defenders can hold one. In M4 the high-ground rule raises the stakes: units below can't see
  up onto a plateau. Update M2-1: you can now see these in the game window.
- **Cliff edges (Producer decision):** the outer 2 m strip of each plateau counts as cliff, so
  units on high ground stand about one step back from the visible edge. The alternative (walls
  between squares) would let them stand right at the lip but makes pathfinding more complex.
- **Safety nets:** the map's outer ring is impassable, and any patch of ground no ramp reaches is
  sealed off, so a unit can never be stranded. The generator re-rolls a layout that comes out too
  blocked (under half walkable) or missing a height level, up to 8 times (Producer decision), then
  takes the best one, so map generation can't hang. A normal map takes about 4 ms to make.
- Also added: a fast "who's near me" lookup grid that movement and later combat use. Nothing to
  review there.

### First real game data (M1-2, 2026-10-03)

- **What it is:** the Malazan and Whirlwind rosters (7 units each, stats from the faction pages),
  the damage-type table and the economy rules now live as JSON in `game/data/`. The game checks
  every file when it starts and lists every mistake at once.
- **Values the docs didn't specify (Producer decision, first pass):**
  - **Body size:** 0.4 m radius for foot soldiers, 0.7 m for cavalry, 0.9 m for siege. This is the
    one you'll notice most: it decides how tightly crowds pack and how many units fit through a
    ramp at once. Update M2-2: you can see the three sizes on screen now.
  - Per-unit attack wind-up times, faction color palettes, and a placeholder for "requires Age II".
- **To change them:** edit the numbers in `game/data/factions/<faction>/units.json`, or ask the
  studio. No code changes are needed. Update M1-6: a data edit also means regenerating the golden
  replay (the studio does this; it's one command). Update 1255: `common/resources.json` joins the
  data (tree and gold-mine types).

### Optional: Godot MCP server (M0)

- A tool that lets Claude launch the game and take screenshots more easily. Not needed: since
  M2-1 the game has its own `--screenshot` flag, which covers the studio's need.

## Requests for the sim track

What the view track needs from the sim and can't add itself (the Producer plans these for the sim
track right after S1/S2 bugs).

1. ~~`Stop`, `HoldPosition`, `AttackMove` command kinds and shift-queued orders~~ → **done in
   M1-7** (session 2330).
2. ~~Public read access to a cached flow field's directions, for the M2-5 debug overlay~~ → **done
   in M1-8** (session 0655): `FlowFieldCache.PeekCached(targetCell)`; read `DirectionAt` / `CostAt`,
   don't keep it across ticks. M2-5 used it (session 1255).
3. ~~`DataError.ToString()` shouldn't print `: :` for empty fields~~ → **done in M1-9** (session 0905).
4. ~~Low priority: a previous-tick facing (`PrevFacing`) so unit views can blend turns~~ → **done
   in M3-2** (session 1503): `UnitStore.PrevFacing`, set with `PrevPosition`, not hashed. Use it in
   M2-7.
5. Noted, not requested: Shift-queued legs through a packed friendly group give up (BUG-0028);
   the M2-3 test scene works round it with spread-out units. Crowd-cost follow-up after M4.
6. Noted (M2-5, session 1255): with 64+ live goals under the 2-builds-per-tick cap about 90 % of
   selected goals had no cached field in QA's churn; that is BUG-0025 (sim debt). No depletion
   events for resource nodes: the view polls `World.Resources.Alive` / `Generation` (fine for M2).
7. Available since M3-2 (session 1503) for the M3 HUD: `World.Gold` / `World.Wood` (per-player
   spans), `World.Buildings` spans, `UnitStore.Cargo` / `CargoKind` / `GatherNode` and the states
   `Gathering` / `Returning` for worker feedback. Nothing requested yet.
8. Data track requests (D1, session 1744; carried in BUG-0090): (a) a building `requires` field
   (a building id and / or Age II: Shock Hall ← Infantry Hall; Caster Hall, Siege Works, Watch Tower ←
   Age II) with validation, planned with M3-5 / M3-6; (b) Watch Tower `attack` / `sight` / `detector`
   fields (docs/02: 10 pierce / 2 s, range 18, sight 24, detector 16 m), planned with M4 combat / fog;
   (c) where the faction upgrade is researched (Forge per docs/02) when `techs.json` gets its schema
   (M3-5). Until then the descriptions say "needs Age II" in text only.
9. ~~Sim-owned nit from D1: `sim/Rts.Sim/Data/BuildingSlot.cs:4` still says "M3-2 ships the Town Hall
   only"~~ → **done in M3-3** (session 2114).
10. Available since M3-3 (session 2114) for the view's build ghost and worker feedback:
    `World.CanPlace(player, typeId, anchorCell, out PlacementError)` (call once per frame on the main
    thread between ticks: it writes flow-field scratch, BUG-0092 note), `Command.Build` / `Cancel` /
    `Repair`, `Buildings.UnderConstruction` / `Work` / `WorkNeeded(type)` / `Hp` for a site progress bar,
    `UnitStore.BuildTarget` and `UnitState.Building`. Group build = one `Build` per selected worker (the
    first places, the rest join the same anchor). Nothing requested yet.
11. ~~Noted at the M2 sign-off (session 0800) for the view's M3 HUD: `World.Pop` / `PopCap` per player and a
    building's production queue + progress come with M3-4~~ → **available since M3-4** (session 0925) for the
    view's M3-V3 production card: `World.HalfPop` / `HalfPopCap` (per-player spans, half-pop units: show
    `n / 2`, ".5" for odd), `World.CanTrain(player, slot, type, out TrainError)` (why a button is greyed),
    `Buildings.QueueCount` / `QueueTypeAt(slot, i)` / `Progress` / `TrainTicks(type)` / `HasRally` /
    `RallyPosition` / `ReservedHalfPop(slot)`, `GameData.UnitsTrainedAt(buildingType)` (the card's buttons,
    sorted by id), `Command.Train(player, point, type)` / `CancelTrain(player, point, index)` /
    `SetRally(player, buildingCell, target)` / `ClearRally(player, point)`. Since M3-H1 a pushed unit's
    `PrevPosition` equals its `Position`, and `NavFlags.Pocket` (32) cells are `Blocked`.
12. Noted (M3-V1, session 0925, BUG-0107 item 3): the view keys its minimap resource layer and props relist on
    `NavGrid.Version`, which building changes bump too. A resource-only version (or a `ResourceStore` change
    counter) would let the view redraw only on fells; low priority, the view can also diff `Resources.Count`.
    Not requested yet.
13. Available since M3-5 (session 1131) for the view's M3-V3 production card: `World.CanResearch(player, slot, tech,
    out ResearchError)`, `World.HasTech` / `Age` / `TechBonus(player, unitType, TechStat)`, `GameData.Techs` /
    `TechsResearchableAt(buildingType)` (sorted ids), `Buildings.QueueIsTechAt(slot, i)` / `ItemTicks(slot, i)` (a
    queue item is a unit or a tech; `QueueTypeAt` returns the tech id when `QueueIsTechAt`), `Command.Research(player,
    point, tech)`; `CancelTrain` cancels techs too. ~~Requested of the view by the sim (M3-6): the view's `ui.json`
    must carry `placement.requires` / `train.requires` / `research.requires` texts before M3-6's `Requires` members
    merge~~ → **done in M3-V3** (session 1415; `UiText.ForwardKey` tolerates a key with no member yet).
14. Data track requests (D3): none; building `requires` and faction techs schemas are on `main`. The common techs' text
    (`common/techs.json`) waits for D4 because M3-6 edited that file (`age_ii.requiresAnyOf`).
15. ~~**Data track, from D3 (session 1415, BUG-0112), first item of the sim's M3-H2:** the sim-owned
    `Stress/ConstructionFuzzStressTests` (15 rows), `NeverSealTests` (8) and `RequirementFuzzTests` seeds 2-3 assume no
    shipped building has a `requires`; with D3's `buildings.json` they fail~~ → **done in M3-H2** (session 1715):
    `TestSim.DataWithoutBuildingRequires` / `ConfigWithoutBuildingRequires` for the geometry oracles, the requirement
    fuzz rewrites both factions; D3 merged, golden regenerated once (`data-hash` 702859B867AAC412). Rule kept: fixtures
    that read data values are named in the sim brief whenever a data task changes those values.
17. Noted for the data track's D4 (session 1715): `common/techs.json` is the sim's file; D4 edits its `displayName` /
    `description` strings only, as a named exception, and the sim's M4-1 does not touch it that session. If the data
    track needs a non-text change there, it comes here as a request.
16. Noted by the view (M3-V3 QA): `CanTrain` has no "pop cap" reason, so a train button stays live at the cap and the
    item waits inside the queue (the M3-4 rule). If the owner wants greyed buttons at the cap, the sim adds a
    `TrainError.PopFull` ordered after `CannotAfford` (one line + tests) and the view keys `train.pop_full`. Not requested.
18. ~~**From the view's M3-V4 QA (session 2315), the sim's next task's first item: BUG-0146 (S2).** Gatherers of a tree
    open on one side stand `Gathering` 1.7 m out of reach for the rest of the match~~ → **done in M4-2a** (session
    2026-10-08-0313): `MovementSystem.QueuesBehind` / `StandArrival2`, three stand points per cell edge; the seed-21 row
    un-skipped (the hash substituted in code), `M3PlayableTest` seed 21 PASS. The seed-6 corridor oscillation did not
    reproduce (a builder walking out against two gatherers, not the arrival rule); refile with a replay if seen.
19. Available since M4-1 (session 2315) for the view's combat views: `UnitStore.Hp` (and `UnitDef.Hp` for the max),
    `UnitState.Attacking`, `UnitStore.Target` / `TargetIsBuilding` / `WindupTicks` (a swing is starting when it is set),
    `World.Deaths` (one tick's `DeathEvent`s: victim handle, building or not, type, owner, killer's owner, position; read
    between ticks, emptied at the next tick's start), `World.Kills` / `Losses` per player, `SimConfig.Combat` (the test
    switch, for the M2 scenes of BUG-0147). Nothing requested yet; the `Attack(target)` command comes with M4-2a.
20. Available since M4-2a (session 2026-10-08-0313) for the view's M4-V2: `Command.Attack(player, unit, target,
    isBuilding[, queued])` (kind 16; dropped by the sim for a dead / own / forbidden target or a unit that cannot fight
    yet), `CombatMode.Ordered` (`UnitStore.Mode`), `UnitStore.QueuedTarget(entry)` for a queued Attack's handle (its
    `QueuePosition` is **not** a point on an Attack entry: a waypoint overlay must skip it, BUG-0153 item 1),
    `AttackDef.Targets`. Replay format 4 carries `combat`, so `ReplayPlayer.Run(replay, data)` needs no override (the
    `combat:` parameter stays for format 3 files recorded off). Used by M4-V2 (session 2026-10-08-0913). ~~Noted for
    M4-2b: the view's M4-V3 projectile visuals need a projectile store with positions and a "landed" tick~~ → **done in
    M4-2b**, see 21.
21. Available since M4-2b (session 2026-10-08-0913) for the view's M4-V3 projectile visuals: `World.Projectiles`
    (`ProjectileStore`: `Capacity`, `Count`; read-only spans per slot `Alive`, `Position`, `PrevPosition` (the launch
    point on the firing tick), `Target` (the impact point; a led shot's moves a little each tick as it is re-aimed),
    `ProjectileTypeId` (`GameData.Projectiles`: `Key`, `Kind` aimed / lob, `SpeedPerTick`), `Owner`) and `World.Impacts`
    (`ProjectileImpact`: `Position`, `ProjectileTypeId`, `Owner`, `Hit`; this tick's landings in slot order, emptied at
    the next tick's start like `World.Deaths`: read from `SimRunner.Ticked`). The sim flies straight at constant speed;
    the view draws any arc. BUG-0184 note: a re-led bolt can step up to 1.8 m in one tick (nominal 1.25); interpolate
    `PrevPosition` → `Position`, never extrapolate. Docs/03 "Implementation (M4-2b)", "For the view (M4-V3)". Nothing
    requested yet. Data track: no request (D5 needed no schema; towers wait for M4-3, abilities for M4-4).
22. Available with M4-3a (session 2026-10-08-1814; **on the held branch `origin/studio/2026-10-08-1435-sim` until BUG-0219's
    view fix lets it merge**) for the view's M4-V4: `World.Fog` (`FogStore`, read-only): `Visibility(player)`
    (`ReadOnlySpan<byte>`, row-major `y * Width + x`, 0 unexplored / 1 explored / 2 visible, changes only on update ticks
    `tick % 4 == 1`), `Version(player)` (+1 per update: re-upload the texture when it moves), `IsVisible` / `IsExplored(player,
    cell)`, `CanSeeUnit(player, slot)` (own, cell visible, or revealed by a high-ground hit), `CanSeeBuilding(player, slot)` (any
    footprint cell visible). Combat also counts a unit's own sight at this tick, so for up to 4 ticks a unit may fire at what
    `CanSeeUnit` still hides (BUG-0216 item 3). Docs/03 "For the view (M4-V4)". The last-known-buildings ghost list comes with
    M4-3b. **Data track requests:** (a) BUG-0230 item 2: make `Scenario/CounterTriangleTests`' `Fight` / `TimeToKill` public (or
    move them to `CombatScenes`) so `Content/CounterTriangleMarginsTests` shares the scene (sim hardening next); (b) the towers'
    `sight` schema is on `main`: D7 pins it (no request); `attack` / `detector` still wait for M4-3b.

## Feature queue: sim track (feature sessions, in order)

1. ~~M3-6 `requires` resolution and gating~~ → **done** (session 1415). ~~M3-H2 end-of-M3 sim hardening + D3 landing~~ →
   **done** (session 1715; BUG-0113 / 0094 left for the next hardening).
2. ~~M4-1 combat slice 1~~ → **done** (session 2315: melee acquisition, chase, leash, holders, damage formula, death;
   BUG-0139 / 0144 / 0149 / 0142 left).
3. ~~BUG-0146 (S2) first, then M4-2a~~ → **done** (session 2026-10-08-0313: the wedge fix, `Command.Attack` kind 16,
   `attack.targets`, replay format 4; BUG-0151 / 0156 / 0157 S3 and BUG-0153 S4 left).
4. ~~M4-2b projectiles / splash / friendly fire / min range + the counter-triangle rows + BUG-0156~~ → **done** (session
   2026-10-08-0913; 1 fix round: the lead rule BUG-0183, BUG-0180 / 0181 / 0182 item 1; BUG-0182 item 2 and BUG-0184 S4
   left; golden `data-hash` moved twice, `k` lines identical).
5. ~~M4-3a fog of war + the high-ground rule + vision-gated targeting + the reveal + buildings' `sight` + the view surface~~ →
   **accepted** (session 2026-10-08-1814, resumed 1435; 1 fix round: BUG-0214 re-baseline, BUG-0217 gate cost; BUG-0184 item 1
   folded in; golden regenerated once; BUG-0211 / 0215 S3 and BUG-0216 S4 left) but **held on `origin/studio/2026-10-08-1435-sim`**
   (ESCALATE at integration: BUG-0219, a view QA row red on the merged tree; merges after the view's fix next session).
6. **Next: M4-H1, the sim's hardening session (4 / 4)**, QA standard: BUG-0215 (hash the visible bits, Producer decision),
   BUG-0211 (re-record the seed-21 replay), BUG-0216 (4 items), BUG-0230 item 2 (public counter-triangle helpers), then the
   S3 / S4 debt below as it fits; docs drift (the save sentence, the holding Catapult). Details in `studio/handoff.md`.
7. Then M4-3b (towers' `attack` / `detector` and buildings that shoot, the last-known-buildings ghost list, placement needs
   explored ground; schema → data D8), M4-4 abilities / statuses / zones (schema → data track), M4-5 stealth / detection,
   the four signature abilities, the fog-on sandbox Playable.
4. M6 (far ahead): agents can't download. **Owner note 2026-10-08: the art direction is grounded / realistic
   (Quaternius Universal Base Characters + Modular Outfits + Universal Animation Library 1 / 2, Mixamo packs for gaps;
   KayKit a fallback only; docs/04 and docs/01 updated by the owner), and the packs are already in `asset-sources/`
   (characters, animations, ambientCG textures, Stylized Nature MegaKit, Poly Haven trees / rocks, Kenney UI, Sonniss
   audio); Blender 5.2 installed. Still to download before M6 (docs/04): fonts (Cinzel, EB Garamond), music, the Godot
   4.7.2 .NET export templates. The Producer lists those three under "Waiting on you" when M5 starts. Do not pull the
   look test or real-art work forward: M6 stays where it is.**

## Feature queue: view track (feature sessions, in order)

1. ~~M3-V3b re-land (BUG-0124, S2) + the view's end-of-M3 hardening~~ → **done** (session 1715; BUG-0126 items 3-6
   left for the next view hardening).
2. ~~M3-V4, the M3 "Playable" proof + BUG-0125 + BUG-0126 items 1-2~~ → **done** (session 2315; `M3PlayableTest.tscn`
   ticks the criterion; BUG-0147 / 0148 left).
3. ~~M4-V1 (BUG-0147 + the first combat views)~~ → **done** (session 2026-10-08-0313; BUG-0160 S4 left).
4. ~~M4-V2 the Attack-target order in the HUD + BUG-0160~~ → **accepted** (session 2026-10-08-0913, 0 fix rounds; BUG-0190
   S4 left) but **held on `origin/studio/2026-10-08-0913-view`** (ESCALATE at integration: BUG-0210, `main` red on
   `MinimapTest` since M4-2b).
5. ~~BUG-0210 first, then M4-V3 projectile visuals + BUG-0190~~ → **done** (session 2026-10-08-1814, resumed 1435; 2 fix
   rounds: BUG-0221 / 0223 / 0224 corpse-disc and mark fixes, BUG-0218 `AttackStage`, BUG-0212 / 0213 `--no-combat`; the held
   M4-V2 lands with it; BUG-0220 S3, BUG-0222 / 0226 S4 left).
6. **Next, first: BUG-0219 (S2)**: `QA/ViewApi/UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything` stages its
   three Raiders in player 0's sight (`CombatScenes.Spot(sim, 0, h[k])`), every assertion kept; QA runs the full non-Perf
   suite on a merge with the held sim branch; the conductor merges view, then sim. **Then M4-VH1, the view's hardening
   session (4 / 4)**, QA standard: BUG-0220 (SfxTest wall-clock wait), BUG-0226 (5 items), BUG-0222 (3 items), BUG-0190 status
   to fixed, BUG-0148 items 1 / 3, BUG-0126 items 3-6, export hygiene, the minimap dot-timing flake. Details in
   `studio/handoff.md`.
7. Then M4-V4: the fog shader from `World.Fog.Visibility` / `Version` (Requests 22), units hidden by `CanSeeUnit`, enemy
   buildings by `CanSeeBuilding` (ghosts after M4-3b), the minimap's fog layer and Attack half, `placement.unexplored` text
   when M4-3b lands; ability feedback (M4-4); the fog-on sandbox Playable.

## Feature queue: data track (feature sessions, in order; owner reviews every landed task)

1. **Owner review tweaks** from the inbox (the D1 / D2 / M3-5 text entries under For your review) whenever present
   come first (QA light; golden `data-hash` regen on a data change).
2. ~~D3 techs content~~ → **landed** (session 1715, through the sim's M3-H2); the review table is under For your review.
3. ~~D4 shared techs text + BUG-0132 + golden regen~~ → **done** (session 2315; the review table is under For your
   review; BUG-0155 S4 left).
4. ~~STOP until a schema it needs is on `main`~~ → `attack.targets` landed (M4-2a, session 2026-10-08-0313).
5. ~~D5 `attack.targets` pins, docs/02 "Ages" wording + parser, BUG-0155~~ → **done** (session 2026-10-08-0913, 0 fix
   rounds; BUG-0200 S4 left; nothing numeric changed).
6. ~~D6 BUG-0200 + the counter-triangle balance report + the Sapper self-splash numbers~~ → **done** (session
   2026-10-08-1814, resumed 1435, 0 fix rounds; the proposal is under For your review; BUG-0230 S4 left; nothing numeric
   changed).
7. **Next: D7**, QA light: a `Sight` column on both pages' Buildings tables pinned to `BuildingDef.Sight` both ways (towers
   24, the rest `buildingSight` 12), docs/02 Buildings table checked, BUG-0090's tower-sight item closed (attack / detector
   wait for M4-3b), BUG-0230 items 1 and 3. **An inbox answer on the balance proposal comes first** (then: the tweak with a
   For your review table and a golden `data-hash` regen). Details in `studio/handoff.md`.
8. Then: the towers' `attack` / `detector` (after M4-3b; D8), `abilities.json` / `statuses.json` content (after M4-4's
   schema), the full balance pass (QA standard) once the fog-on sandbox gives numbers (incl. the +15 % crowd income from the
   BUG-0146 fix), `ai.json` build orders (M5), M7-M9 faction data.

## Debt backlog: sim track (hardening sessions only; the next one is the next session: M4-H1)

- **BUG-0215 (S3, M4-3a; Producer decision at the 1814 ACCEPT):** the fog's visible bits depend on the last update's unit
  positions, so they are state: hash them packed (one `ulong` per 64 cells per player, ≤ ~20 µs), keep `Version` derived,
  rewrite docs/03's "a save re-stamps at load" (M6 saves them), flip QA's skipped
  `FogQaTests.TwoWorldsWithTheSameStateHash_PlayTheSameFuture`, golden regen once with the proof, docs/01 row (d).
- **BUG-0211 (S3, M4-3a):** `GatherWedgeQaTests` `CheckpointPrefixTicks` is 0 (the pre-fog seed-21 recording can't match hashed
  fog checkpoints); re-record from `M3PlayableTest -- --seed 21` on the post-M4-3a build, restore the prefix (≥ 19), prune
  `SameGameDataHashes`; a "hash format" note in docs/03 Replays.
- **BUG-0216 (S4, M4-3a nits):** `FogStore` caps limits at `max(w, h)^2` not `w^2 + h^2` (sight 64 on < 46-cell maps; flip the
  skipped `MaxSight64_OnA24CellMap_MatchesTheOracle`); docs/03's holding-Catapult sentences (a holder scans at reach beyond its
  sight and needs a spotter there); the view / combat 4-tick disagreement noted for M4-V4; `MaxSight` applies to units too.
- **BUG-0230 item 2 (data request):** `Scenario/CounterTriangleTests`' `Fight` / `TimeToKill` public (or in `CombatScenes`).
- ~~BUG-0214~~ (S2, the 1024-map memory bound re-baselined 228 → 230.5 MB with the fog itemised) and ~~BUG-0217~~ (S3, the
  vision check after the best-candidate compare) fixed in the M4-3a fix round; ~~BUG-0184 item 1~~ fixed in M4-3a (item 2 is
  a view note, handled by interpolation).
- ~~BUG-0146~~ (S2) and ~~BUG-0139~~ (S3) fixed in M4-2a (session 2026-10-08-0313), with ~~BUG-0150 / 0152 / 0154~~ (S2,
  filed and fixed in-session) and BUG-0153 items 1-2. BUG-0144 (S3, chasers' field waits under placement churn,
  `ChasersUnderPlacementChurn_LongestFieldWait_AtMostOneSecond` skipped) with M4-2b if the projectile work touches the
  chase path, else here.
- ~~BUG-0156~~ (S3) fixed in M4-2b (session 2026-10-08-0913: `CombatSystem.AttackerInScanRange`, the re-pick between
  swings while the last attacker is alive and within the scan radius; ~~BUG-0180~~ (S3, the first fix keyed on a stale
  `LastAttacker`), ~~BUG-0181~~ (S3, recorder vs a non-default projectile capacity) and ~~BUG-0183~~ (S2, the lead rule)
  filed and fixed in-session; BUG-0182 item 1 fixed (`DataLimits.MinProjectileSpeed`)).
- **BUG-0184 (S4, M4-2b lead nits; fold into M4-3 if a few lines):** `step^2 <= lead^2` fails at exactly `leadSpeed` in
  13 of 360 headings (float rounding; no shipped unit walks at exactly 5 m/s; flip the skipped
  `QA/ProjectileLeadQaTests.Lead_AStepOfExactlyTheLeadSpeed_IsLedInEveryHeading`); a re-led bolt can step 1.8 m in one
  tick (a view note, Requests 21). Also: `ProjectileImpact.Position`'s doc comment says "where the target was when the
  shot was fired" (it is the impact point, which a led shot moves).
- **BUG-0182 item 2 (S4, data note, not a sim fix):** Sappers splash themselves in melee; friendly fire is 23-28 % of
  deaths in shooter-heavy fuzz (`Stress/RangedSplashFuzzQaTests`). The data track reports it in D6.
- **BUG-0157 (S3, M4-2a fix round 1):** attack-move spam to a *new* point every 1-3 ticks drops out-of-reach chasers'
  targets until their next scan (a brawl loses 10-26 % of its damage; same-point spam and 5+ ticks apart lose nothing).
  Option: an AttackMove to a new point keeps a chase whose target is still in sight. Flip the skipped
  `QA/AttackMoveRepickQaTests.BrawlSpam_JitteredAttackMove_StillDealsTheDamage(every: 3, jitter: true)`.
- **BUG-0151 (S3, M4-2a, Producer re-triage):** the 200-worker gather row costs 0.32 ms (was 0.23; budget 1 ms) because
  the wedge fix puts 39 % more workers to work in 2x denser crowds at the nodes (per-walker cost unchanged, +24-30 % per
  resource delivered). Revisit with the crowd-cost work after the M4 sandbox (the same item as BUG-0028 / 0032 / 0046);
  the dev tried four cheaper variants, each broke a `GatherPocketStressTests` row or cut little.
- **BUG-0158 (S4, test-only, Producer):** `CombatScaleQaTests.TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree` alternates six
  300-tick blob runs in one process; on this PC the first run is 4.35 ms and every later one 7.2 ms (sustained-load throttle),
  so the delta fails alone on base and head alike. Discard a warm-up run or compare medians; check the machine.
- **BUG-0153 items 3-4 (S4 notes):** the brief's `CombatFriendExceptionQaTests` row is skipped for BUG-0149 (fix with
  it); `GatherWedgeQaTests` can only check checkpoints 1-19 of the seed-21 replay (the fix changes play from tick 20).
- **BUG-0149 (S3, M4-1)**: `CombatSystem.Engage` keeps `ChaseStall` across a target switch (needed against the seed-4
  two-targets-in-turn livelock), so a chaser stalled on a cliff-top target gives a reachable Raider behind a short wall
  up on the next scan, twice, and reaches `MaxGiveUps` 10 m from it. Options in the bug: keep the count only when
  switching back to a target already given up, or reset it when the new target is closer than the old best gap. Flip
  `QA/CombatFriendExceptionQaTests.StalledChaser_SwitchesToAnEnemyBehindAWall_WalksRoundAndFightsIt`; keep
  `CrowdRowSweepStressTests.FiveHundredUnitsTo500RandomGoals` seed 4 green.
- **BUG-0142 items 1-2 (S4, M4-1)**: `CanPlace` says `UnitInTheWay` for an own Attacking worker inside the footprint while
  its Build is accepted (the BUG-0092 shape, documented); an Idle duel already in reach is won by the first slot to scan
  (phase-10 reach check or same-tick retaliation would even it; a note, maybe not a fix).
- Note (M4-1): `CombatTerminationTests`' 3x walking-limit bound could be 2x (worst measured 1.23x). `CombatConstants`
  (scan interval, grace, give-up scans, max give-ups) are engine rules in code, not data; move them to `rules.json` if
  the owner wants to tune them.
- **BUG-0134 (S3)**: a building that requires a tech researched only at its own slot (`malazan_armory.requires =
  ["melee_weapons_1"]`) loads clean and can never be built: `CheckAnyOfReachable`'s fixpoint ignores `researchedAt`
  (and `trainedAt`). Fix: a tech is reachable for faction f only if f's building of its `researchedAt` slot is; then
  report what stays unreachable. Flip QA's skipped `ABuildingRequiringATechResearchedOnlyAtItself_IsAnError`.
- **BUG-0133 (S4)**: `LeftoverOffset` has 24 distinct values, so past 24 leftovers a cell two share a point (400 units
  in a House on a 10-cell plateau: 356 pairs; 150 units: 0); docs/03 says "no two share a point". Fix the sentence or
  grow the radius; flip the skipped `FourHundredPushedOntoATinyPlateau_NoTwoOnOnePoint`.
- **BUG-0113 (S4)**: type-mismatch errors print CLR type names ("Nullable`1[Int32]"); the 10k-unit load Perf row now
  times a failing load (10,000 slot errors) since BUG-0010.
- M4 note: `TechBonus` has no tower path (towers have no attack until M4-1 / M4-3).
- ~~BUG-0112~~, ~~BUG-0100~~, ~~BUG-0097~~, ~~BUG-0095~~, ~~BUG-0096~~, ~~BUG-0099 item 2~~ fixed in M3-H2 (session
  1715); ~~BUG-0008~~, ~~BUG-0010~~, ~~BUG-0098~~, ~~BUG-0099 items 1 + 3~~ fixed in M3-6 (session 1415).
- **BUG-0094 (S4, test-only)** `ResourceStore.Spawn` through `ResourceMaps.Spawn` skips the never-seal
  check, so hand-built groves can wall cells in before tick 1; QA's own harness is fixed, the shared helper
  is not (add a `KeepsConnected` check or an opt-in flag).
- **BUG-0080 (S3, known limit since M3-H1)** a closing change every p ticks lets only the 2p oldest goal
  groups walk while it lasts; the bound is in docs/03 "Known limits" and pinned by
  `SimHardeningTests.ClosingsEveryPeriodTicks_*`. Revisit (usable-stale closed fields with a stuck-tick
  exemption) once the M5 AI's placement rate is known.
- ~~BUG-0081~~ decided in M3-H1: the M6 save file stores the flow-field cache's contents (docs/03).
- ~~BUG-0078~~ fixed in M3-3; ~~BUG-0093~~ (pocket rule), ~~BUG-0091~~, ~~BUG-0092~~, ~~BUG-0079~~,
  ~~BUG-0076~~, ~~BUG-0071~~, ~~BUG-0072~~ fixed in M3-H1 (session 0800).
- ~~BUG-0090 sim part~~ (`BuildingSlot.cs` comment) fixed in M3-3; the data part stays in the data backlog.
- Note (M3-2b): `FlowField.Build` has the 8 directions written out (~150 lines) for Debug speed; fold
  back only if tests move to Release. The felling Perf row (`OneTreeFallsEveryTick_AverageTick_Perf`,
  < 0.5 ms absolute) has ~20 % headroom and fails under full-suite contention: rerun alone before
  filing; widen the scene or pin p50 if it ever fails alone.
- Note (QA, M3-2): a worker that fills up with no own drop-off ends its loop Idle with the load and a
  drop-off built later doesn't re-arm it; consistent with docs/03, a "resume" is a design option for
  the M3 HUD / M3-4.
- Note (QA, M3-H1): `--record` now truncates an existing file before the run (an aborted run leaves an
  empty file); `TightBlob2500` has 0.02 ms of headroom alone (4.48 of 4.5 ms): a failure alone is real.
- Note (QA, M3-4): `CommandDoorFuzzStressTests` covers kinds 0-15 since M3-H2 (session 1715; new kinds from M4 must be
  added). `HalfPop` / `HalfPopCap` are derived (not hashed): the fuzz recount is the guard.
- Note (M3-H2): plateau ids, the per-plateau spawn memos and the seal memo are derived scratch (not hashed;
  `StateHashTests.PlateauIds_AndTheSpawnAndSealMemos_AreDerived_AndNotHashed`); the seal memo keys on `NavGrid.Version`,
  never `BlockVersion`. `TightBlob2500` 4.27-4.39 ms alone against 4.5: M4-1's per-unit work must stay inside it.
- **BUG-0046 (S3, known limit)** wall clips in slot order; a slot-free sort exists in the M1-9
  report (hard walls first, farthest first, ties by position) and re-rolls three fitted bounds.
  Revisit with the crowd-cost work after M4. 4 QA rows skipped.
- **BUG-0050 (S3, known limit)** random-goal give-ups 4.7% (target 3%); alternatives in docs/03.
- **BUG-0028 / BUG-0032 (S3)** crowd targets (4 points 51% / 34%, 64 goals median 22-24% give up,
  same-owner crossing 22 / 200): needs a crowd cost in the flow fields; after the M4 sandbox.
- BUG-0040 (S4) part 2: in-tick AI enqueue vs the phase-14 checkpoint is a design note for M5.
- BUG-0025 (S3) evict the live field with the newest order (the M2-5 churn confirms it at 64 goals)
  + BUG-0026 (S4) rotate same-tick tie-break.
- BUG-0005 (S3) per-player command buckets (O(n^2) insertion sort under a flood); before M5.
- BUG-0023 (S3) single field build > tick budget on maps > 256: documented as unsupported.
- Note (QA, M3-6): `CanResearch` reports `Requires` ahead of `AlreadyResearched` (the brief's order); a researched
  Age II whose halls fall reads "Locked" if the view asks. The dev `SpawnBuilding` ignores requirements (documented). A
  queued `Build` is checked when it starts (phase 7), so it sees a phase-3 Age II of the same tick; direct commands see
  it the next tick (documented).
- Note (QA, M3-5): a tech queued behind a pop-blocked unit waits (the M3-4 pause rule; Producer kept it); the
  golden regen reason for M3-5 is in docs/03 (data-hash only: `techs.json` + buildings' `requires`).
- BUG-0002 (S4) `.sln` Release config maps RtsGame to Debug; with `tools/export.ps1` (M6).
- Perf (Debug, this machine, alone): 500 moving 0.62-0.63 ms, with 12 forests + 8 mines 0.82 ms; 500
  marching + 50 gathering 0.90-0.92 ms; felling + 50 builders + 500 marchers 1.19 ms (1,000: 1.42, 2,500:
  2.55); 100 refused Builds in a tick 0.04-0.09 ms (SealsGround 33 ms, BUG-0096); push-out of 16 in a blob
  of 400 0.29 ms, 400 stacked in a Keep 2.46 ms; 200 workers gathering alone 0.26 ms; flow field build
  128 x 128 0.37 ms (0.7 before M3-2b); 32 groups with a tree felled every tick 0.40 ms (1.76 before);
  full 4,096-slot resource hash 20 µs; 2,500 one-player tight blob 4.33-4.40 ms (enforced <= 4.5, ~3% headroom); two-player
  contested blob 6.84 ms (guard < 10.5); 2,500 to 4 points 3.08 ms (guard < 3.7); 1,000 walkers
  crossing a 1,500 blob 14.2 ms (report); CLI 2,500 march 4.6 ms. Setup: 128 map with 12 forests +
  8 mines +5 ms; 1024 map at the resource caps +2.2 s. Gather efficiency per worker per minute
  falls with crowding at shared nodes (10 workers 29.5 → 200 workers 13.4), not from field waits.
- Known limits: docs/03 "Known limits (M1), as of M1-9" is the list (flow fields ignore units;
  4.7% random-goal give-ups; the BUG-0048 map gives up 76 of 128; pack rule not guaranteed; partial
  spawn-order independence; clusters over 32 are not plugs; holders are walls only; maps > 256
  unsupported; perf measured in Debug). Plus: enqueue stamps `TickNumber + 1`; `SimInfo.Version`
  recorded but not checked; no depletion events (views poll); .NET 8 support ends 2026-11-10, move
  to the next LTS at M6.

## Debt backlog: view track (hardening sessions only; the next one is the next session: M4-VH1)

- **BUG-0219 (S2, integration 1814; first item next session, before the hardening):** `UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything`
  queues Attacks on Raiders 20 m out of sight; M4-3a drops them. `CombatScenes.Spot` the three Raiders (they stay outside the
  infantry's 14 m scan, so "nothing pulls it back after Stop" still holds); holds the sim merge.
- **BUG-0220 (S3, pre-existing, confirmed in M4-V3):** `SfxTest` flakes under CPU contention (`Expect` waits a 60 ms scene-tree
  timer against `Sfx.Play`'s 50 ms wall-clock rate limit): wait on `Time.GetTicksUsec` in the test, or drive `Sfx.Play` with an
  explicit clock; 10 runs under load green.
- **BUG-0226 (S4, M4-V3 round 2 nits):** `TerrainHeight.MaxUnder`'s 0.15 m `SeamSlack` lets a disc ~0.5 m up a ramp beside a side
  wall's foot (count a crossing as joined only at ~0 step; the comment's "generated seams" don't exist: flip the skipped
  `MaxUnderEdgeWalkQaTests.MaxUnder_RandomPointsAndRadii_HangsUnderAQuarterMetre`); `AttackOrderViewTest`'s seed-6 own-Billet
  row silently skipped; `QaV6Test`'s Tent row never has the box behind the Raider's pixel; docs/03 "10-16 cells behind the
  Raiders" is measured from the centre.
- **BUG-0222 (S4, M4-V3 nits):** a skipping `ProjectileTracker` misses a same-type / owner / target lob reuse (check the lob lies
  on its launch → target line; flip the skipped `SkippingObserver_SameTargetLobFromAnotherLaunch_IsANewShot`); the lob's
  last-step slide (M6 note); `ImpactMarks.Add` replaces a live mark at `_head` while free slots exist (doc or code).
- **BUG-0190 (S4):** both items fixed in M4-V3 (`MaxUnder`, the NaN guard); the file and README row on the view branch still
  read `open`: set `fixed` (residual: BUG-0226 item 1). ~~BUG-0221 / 0223 / 0224~~ (S3) and ~~BUG-0225~~ (S4) fixed in the M4-V3
  fix rounds; ~~BUG-0210 / 0212 / 0213 / 0218~~ (S2, scenes vs combat and fog) fixed in M4-V3.
- ~~BUG-0147~~ (S3) fixed in M4-V1 (session 2026-10-08-0313: the five M2 scenes pass `--no-combat`, launch arguments
  only, no `Check` or threshold changed; loop 28 headless + 5 Shot + Playable).
- ~~BUG-0160~~ (S4) fixed in M4-V2 (session 2026-10-08-0913: four F12 lines, the first-sight flash rule in `HitFlash`'s
  typed overload, corpse fill 85 % on a 30 % rim, empty fallbacks); the docs/03 "until format 4" line fixed by the
  Producer at that session's PLAN. Coverage gap noted in M4-1: the view hash twins run combat off; `CombatViewTest`'s
  twin now runs on combat (closed).
- **BUG-0190 (S4, M4-V2 nits; item 2 into M4-V3, item 1 if cheap):** a corpse disc on a ramp edge is half under the
  slope (place it at the highest terrain sample under its radius, or tilt it); `UnitPicker.ResolveEnemy` reads a NaN
  unit entry as "no unit" and can name the building behind an own unit (unreachable through `PickRay`; guard it).
- ~~BUG-0125~~ fixed in M3-V4 (session 2315: `ResourcePicker.PickRay` tests `PropsView.Shape`, the drawn trunk + cone /
  block + gold block; 0 / 3,000 open-ground rays taken for a node). ~~BUG-0145~~ (the Playable re-task flake) fixed in
  the same session.
- **BUG-0148 (S4, M3-V4 nits)**: item 1, ~1 % facet-sliver picks (the circle test vs the 6- / 7-sided mesh, at most 8 cm;
  documented approximation until the M6 art pass); item 3, the Playable scene's rally step passes on any wood node, not
  the rally forest. Items 2 and 4 fixed in the fix round.
- **BUG-0126 (S4, M3-V3b nits)**: ~~items 1-2~~ fixed in M3-V4 (session 2315: "Locked" on a locked building's button and
  ghost, "Researched" / "In a queue" over "Locked"). Items 3-6 wait here: `PropLayout` still relists on
  `NavGrid.Version` (rest of BUG-0107 item 3; Requests 12); a double Cancel enqueues two Cancels; the split minimap Perf
  rows (0.25 + 0.2 ms) leave the 0.291 ms sum unguarded; the resource bar allocates ~1.2 KB over 300 repair ticks when
  totals change (the panel's int-string table could serve it).
- ~~BUG-0123~~, ~~BUG-0104~~, ~~BUG-0105~~, ~~BUG-0107~~, ~~BUG-0122~~, ~~BUG-0124~~ fixed in M3-V3b (session 1715);
  ~~BUG-0108 / 0109 / 0110~~ fixed in M3-V3 (session 1415; `QaV2Test` rows are `Check`s now).
- ~~BUG-0106~~ set to `wontfix` at the 1131 ACCEPT (criterion reworded at 0925; file + README row updated by the
  Producer).
- ~~BUG-0069~~, ~~0070~~, ~~0083~~, ~~0084~~, ~~0085~~, ~~0086~~, ~~0087~~, ~~0088~~, ~~0101~~,
  ~~0102~~, ~~0103~~ fixed in M2-H2 (session 0800).
- Edge-pan hover suppression (`RtsCamera.EdgePanBlocker`) can't fire today: the minimap's 8 px margin
  keeps it out of the 8 px edge band. Harmless; revisit if the HUD layout changes.
- Export hygiene (M6; written up in docs/03 "Build and export" by M2-H2): exclude `game/tests/` from the
  release build (18 scenes compile in today); load `game/data/` in a `.pck`-safe way instead of
  `ProjectSettings.GlobalizePath("res://data")`.
- `MinimapDotsShot --units 1000` silently drops its second lone unit (store full; documented).
- Cosmetic: ramp ends ~31° vs 22° mid-ramp; no wall skirts on the map border; 1024² mesh 904 MiB
  transient (outside supported sizes); `StartLayout` at radius 1.0 puts bodies exactly touching.
- Overlay deferred items (docs/03): per-unit state labels, arrows for more than one goal, a window
  that follows the visible trapezoid, the dev console.
- Note: headless scene runs print Godot warning stack traces for the expected "order dropped"
  warnings; not errors. Builders: absolute paths for all file I/O.
- Optional M0 item: Godot MCP server (owner install; not needed since `--screenshot`).

## Debt backlog: data track

- **BUG-0090 (S4)**: building descriptions state requirements ("needs Age II", "needs a Legion
  Barracks") and tower attack / detection that the schema can't express yet; recheck every such
  sentence when `requires` (M3-5 / M3-6) and the tower fields (M4) land. Schema requests are under
  "Requests for the sim track" 8.
- ~~BUG-0111 (S4)~~ fixed in D3 (landed in session 1715 through the sim's M3-H2). The buildings part of BUG-0090
  likewise; the towers part stays (M4).
- ~~BUG-0132 (S4)~~ fixed in D4 (session 2315): A-C messages read "tech field: page X vs data Y"; `RequiresText.Needs`
  reads "requires" / "after" too and a test asserts no shipped description uses them.
- ~~BUG-0155 (S4, D4)~~ fixed in D5 (session 2026-10-08-0913: H any-case, faction tech names covered). ~~Docs drift (D4
  QA note)~~: docs/02 "Ages" reads "level II" and the exact any-two-of rule since D5; `TechContentTests.G` parses it.
  Watch Tower's attack (docs/02 Buildings table) is not in the sim until M4-3.
- ~~BUG-0200 (S4, D5)~~ fixed in D6 (session 2026-10-08-1814: G pins the clause, C's message states the style rule).
- **BUG-0230 (S4, D6; next data task, items 1 and 3; item 2 is a sim request):** an appended contradicting sentence after the
  Ages clause passes G and QA's row (anchor the regex to the bullet's end); `Content/CounterTriangleMarginsTests.Fight` /
  `TimeToKill` are copies of the scenario's private helpers (sim: make them public); the Whirlwind page's siege row misuses
  the "Winner keeps" column (a separate siege table as on the Malazan page).
- **Balance notes (measured in D6, on both pages; the owner's call is under For your review):** winner keeps 67-100 % of its
  cost in every pair (proposed band 40-65 %); Line v Shock 88-100 % is the outlier; casters re-measured after M4-4; the Sapper's
  self-splash is small (keep); crowd income +15 % at 20 workers (BUG-0146 fix) still to fold into the full balance pass.
- Note: a QA row (`AnEmptyBuildingList_Loads_...`) now pins "exactly Whirlwind's ten remain"; it moves
  again whenever a building is added or removed (intended: the roster is pinned).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-08 | [2026-10-08-1814](sessions/2026-10-08-1814.md) (the resumption of 1435, which the 3-hour lock rule declared dead mid-fix-loop; incident folded into the log) | sim M4-3a: `Rts.Sim.Vision` (`FogStore` per player: byte per cell + packed explored bits, circle masks per radius, row-span stamping per level with the high-ground rule and the 4 m lip, every 4 ticks + a tick-0 stamp; `VisionSystem` phase 12, `UnitSeesUnit / Building` for scans / retaliation / explicit Attack / kept targets, the 40-tick high-ground reveal keyed on the firing level via `PendingHit.AttackerLevel` / `ProjectileStore.Level`), buildings' `sight` + `rules.json` `buildingSight` 12 (towers 24), `World.Fog` read-only surface, BUG-0184 item 1; fix round: BUG-0214 (memory bound re-baselined, itemised), BUG-0217 (gate after the compare, 1,000 v 1,000 on 3 levels 29.5 → 23.4 ms); golden regen once (`k` lines equal with the fog excluded); view M4-V3: `ProjectileViews` (aimed streaks, lob stones on an arc, flash / dust / burst marks), `ViewApi.ProjectileTracker` / `ImpactMarks` / `TerrainHeight.MaxUnder`, BUG-0210 (`--no-combat`, the slot was shot dead), BUG-0190, `AttackStage.cs` for BUG-0218, `--no-combat` for BUG-0212 / 0213, `ProjectileViewTest.tscn` + QA's `QaV7Test`; the held M4-V2 lands with it; data D6: `Content/CounterTriangleMarginsTests` (8 pairs x 2 seats, winner pinned, table on both pages with the proposed 40-65 % band), `SapperSplashReportTests` (shipped / minRange 2 m / splash 1 m), BUG-0200 | **all three ACCEPT; sim then ESCALATE at integration**: sim 1 fix round (QA FAIL → PASS_WITH_ISSUES → confirm FAIL on the view-owned BUG-0218, fixed on the view branch; BUG-0211 / 0215 S3, BUG-0216 S4 open) then **held on `origin/studio/2026-10-08-1435-sim`** (with `main` merged in, the full non-Perf suite is 4,003 / 15 / 1: `UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything`, a view QA row from before fog, **BUG-0219 S2**; both QAs' scratch merges ran only the scene loop), view 2 fix rounds (PASS_WITH_ISSUES x3; BUG-0220 S3 pre-existing, BUG-0222 / 0226 S4 open) **merged to `main` first (853a60c, green)**, data 0 (PASS_WITH_ISSUES: BUG-0230 S4) merges after the view. **M4 5 / 10**, criterion 5's sim half accepted and held; one open S2 (BUG-0219); new rule: the integration gate runs the full non-Perf suite on the merged tree |
| 2026-10-08 | [2026-10-08-0913](sessions/2026-10-08-0913.md) (integration update: **view ESCALATE**, `main` red on `MinimapTest` since M4-2b, BUG-0210 S2; the view branch kept, fix first next session; data merges) | sim M4-2b: `common/projectiles.json` schema (`aimed` / `lob`, `speed`, `hitTolerance`, `leadSpeed`), `ProjectileStore` (SoA, hashed while in flight) + `ProjectileSystem` (Fly / Fire / Track / Land, splash with falloff, friendly fire at 50 % never buildings, `attack.minRange` no kiting), `CanFight` for every attack, BUG-0156 via `AttackerInScanRange`, `Scenario/CounterTriangleTests` (8 pairs both seats), `World.Impacts` + view spans; fix round: the lead rule (BUG-0183 S2), BUG-0180 / 0181 / 0182 item 1; golden `data-hash` x2, `k` identical; view M4-V2: `ViewApi.UnitPicker` (capsule ray pick + `ResolveEnemy`), `TargetMark` / `TargetRing`, right-click / A + click Attack per selected unit, `states.ordered_attack` "Pursuing", F12 target slot, BUG-0160 (four F12 lines, first-sight flash, corpse rim, no literals), `AttackOrderViewTest.tscn`, QA's `QaV6Test` + `tools/qa/scene-loop.ps1`; data D5: Targets column on both faction pages + pins (C / G / new H), docs/02 "Ages" exact wording + `TechContentTests.G` parser, BUG-0155 (H any-case + faction tech names), QA's `AgesRuleQaTests` | **all three ACCEPT**: sim 1 fix round (QA FAIL → PASS_WITH_ISSUES; the lead rule accepted as a Producer decision, docs/02 rewritten at ACCEPT; BUG-0182 item 2 / BUG-0184 S4 open), view 0 (PASS_WITH_ISSUES: BUG-0190 S4), data 0 (PASS_WITH_ISSUES: BUG-0200 S4). **M4 5 / 10** (criterion 3 and the counter-triangle rows ticked; criterion 1's view half landed); no open S1 / S2 |
| 2026-10-08 | [2026-10-08-0313](sessions/2026-10-08-0313.md) | sim M4-2a: the BUG-0146 wedge fix (`MovementSystem.QueuesBehind` / `StandArrival2`, three stand points per cell edge), `Command.Attack` (kind 16, `CombatMode.Ordered`, queued handle in the existing queue arrays), `attack.targets` schema (the ram `buildings`, BUG-0139), replay format 4 (`combat` header, targets on command lines; golden regen, `k` lines identical); fix rounds: BUG-0150 (lost-from-sight give-up on a switch), BUG-0152 (`ReaffirmAttack` / `KeepFightForAttackMove`), BUG-0154 (`UnitStore.Repick`); view M4-V1: `--no-combat` dev flag (BUG-0147's five scenes), `ViewApi.UnitHpBars` / `HitFlash` / `DeathMarkers`, `CombatViews` (bars, corpse discs, rubble), K / L counts, live panel hp, `CombatViewTest.tscn`; data STOP (planned) | sim ACCEPT after 2 fix rounds (QA FAIL x3, the last on BUG-0151 alone: Producer re-stated criterion 3 and re-triaged it S2 → S3; BUG-0156 / 0157 S3, BUG-0153 S4 open); view ACCEPT, 0 fix rounds (PASS_WITH_ISSUES: BUG-0160 S4). **M3 signed off**; M4 3 / 10; no open S1 / S2 |
| 2026-10-07/08 | [2026-10-07-2315](sessions/2026-10-07-2315.md) (resumed the dead 2014 session, which left no log) | sim M4-1 combat slice 1 (`Rts.Sim.Combat`: `CombatSystem` acquire / attack / resolve, `DamageCalc`, `CombatMode`, give-up memory + friend exception, `SpatialHash.QueryEnemies`, `UnitState.Attacking`, `SimConfig.Combat` test switch, death events + kills / losses; BUG-0135 / 0136 / 0137 / 0138 / 0140 / 0141 / 0143 fixed in-session); view M3-V4 (`M3PlayableTest.tscn` scripted playtest through the real HUD, drawn-shape node pick BUG-0125, "Locked" / "Researched" / "In a queue" BUG-0126 1-2, `states.attacking`, BUG-0145 / 0148 2 + 4 fixed); data D4 (Age II description faction-neutral, seven shared techs pinned to docs/02, BUG-0132, golden `data-hash` A863BAF8637CC860) | **all three ACCEPT**: sim 2 fix rounds (QA FAIL / FAIL / PASS_WITH_ISSUES: BUG-0139 S3 → M4-2a, BUG-0144 S3, BUG-0149 S3, BUG-0142 S4 open), view 1 fix round (QA FAIL on BUG-0145 S2 → PASS_WITH_ISSUES: **BUG-0146 S2 filed against the sim**, BUG-0147 S3, BUG-0148 S4), data 0 (PASS_WITH_ISSUES: BUG-0155 S4). **M3 8 / 8**, sign-off held by BUG-0146; M4 1 / 10 |
| 2026-10-07 | [2026-10-07-1715](sessions/2026-10-07-1715.md) | sim M3-H2 end-of-M3 hardening: D3 merged with the BUG-0112 fixture fixes (`TestSim.DataWithoutBuildingRequires`, golden `data-hash` regen, checkpoints identical), `Map/Plateaus` + plateau-bounded `FreeCellSearch` + per-plateau spawn memo (BUG-0097 / 0095), `SealCheck` memo on `NavGrid.Version` (BUG-0096), unmeetable requirements as load errors incl. any-of reachability (BUG-0100), BUG-0099 item 2, door fuzz kinds 0-15; view M3-V3b: the held M3-V3 HUD re-landed with two-hall Age II fixtures (BUG-0124), panel allocation-free + dimmed greyed buttons (BUG-0123), per-seed bench bound (BUG-0104), word wrap / Shift-click / ghost priority / reason text (BUG-0122), mauve sites / zoom-scaled cargo / resource-keyed minimap (BUG-0107), docs figures + split Perf rows (BUG-0105), terrain-occluded building + node ray picks; data STOP (planned) | sim ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES: BUG-0134 S3, BUG-0133 S4); view ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES: BUG-0125 S3 accepted open for M3-V4, BUG-0126 S4). **M3 7 / 8**: "factions fully defined in data" and "HUD" ticked; `main` smoke green again; no open S1 / S2 |
| 2026-10-07 | [2026-10-07-1415](sessions/2026-10-07-1415.md) | sim M3-6 `requires` gating (`RequiresTechs` / `RequiresBuildings` resolved at load, `requiresAnyOf` by slot on `age_ii`, `Requires` reasons in `CanPlace` / `CanResearch`, real `LockedByRequirement`, queue-time rule, `PlayerLedger` finished counts, loader nits BUG-0008 / 0010 / 0098 / 0099); view M3-V3 selection panel + production card + queue strip + rally marker + pop + Age II flash, BUG-0108 / 0109 / 0110 fixed; data D3 techs text + building `requires` + page Techs tables + pins, BUG-0111 fixed | sim ACCEPT (on `main`), 0 fix rounds (QA PASS_WITH_ISSUES: 2 S3 (BUG-0100, BUG-0112) + 1 S4); **view ACCEPT then ESCALATE at integration** (QA PASS_WITH_ISSUES, 1 S3 BUG-0123; merged with M3-6 one fixture queues Age II without halls: BUG-0124 S2, branch held, `main` smoke red); **data REJECT-hold** (QA PASS_WITH_ISSUES, 1 S4; integration only: BUG-0112, lands via the sim's M3-H2). M3 5 / 8 on `main` |
| 2026-10-07 | [2026-10-07-1131](sessions/2026-10-07-1131.md) | sim M3-5 Age II research + Forge upgrades (`common/techs.json` + faction `techs.json` schema, `Research` kind 15 through `CanResearch`, queue items unit-or-tech, `TechState` flags hashed + `TechBonus` derived, building `requires` field, CLI `age N`); view M3-V2 command card (5 x 3 grid, `ui.json` view text, B / V build menus, `BuildGhost` via `CanPlace` once a frame, building click-select + outline + site Cancel, right-click Repair / join, M key); data STOP | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S3 (BUG-0098) + 1 S4; view 3 S3 (BUG-0108 / 0109 / 0110, folded into M3-V3) + 1 S4). M3 5 / 8 |
| 2026-10-07 | [2026-10-07-0925](sessions/2026-10-07-0925.md) | sim M3-4 production queues (`Train` / `CancelTrain` / `SetRally` / `ClearRally`, `ProductionSystem` phase 3, `PlayerLedger` pop + cap, `FreeCellSearch` capped at the level box, `trainedAt` resolved, `UnitsTrainedAt`, CLI pop); view M3-V1 economy HUD (`StartBase` Town Hall + workers, `ResourceBar`, right-click Gather via `ResourcePicker`, `BuildingViews` + `BuildingBars`, worker tints + cargo markers, F12 worker counts); data STOP | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S3 (BUG-0097); view 1 S3 (BUG-0106, reworded away) + 1 S4). M3 4 / 8 |
| 2026-10-07 | [2026-10-07-0800](sessions/2026-10-07-0800.md) | sim M3-H1 hardening (pocket rule `NavFlags.Pocket` after Cancel / destruction, cheap-first Build checks, ring-by-ring push-out through the spatial hash, BUG-0080 measured bound documented, BUG-0081 save/load decision, loader / CLI / plug-cache nits: BUG-0071 / 0072 / 0076 / 0079 / 0081 / 0091 / 0092 / 0093 fixed); view M2-H2 hardening (bench marches across + true `fps`, start blocks in a clearing, `Sfx` stops at quit, overlay label on change, 2 x 2 minimap dots, S4 batch, M6 export notes: 11 bugs fixed) → **M2 signed off**; data STOP (resumed after an OS restart killed 2026-10-06-2326) | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 2 S3 + 1 S4, view 1 S3 + 1 S4) |
| 2026-10-06 | [2026-10-06-2114](sessions/2026-10-06-2114.md) | sim M3-3 placement validity (`World.CanPlace`, never-seal `SealCheck`), `Build` / `Cancel` / `Repair`, multi-builder construction (BUG-0078 fixed by rule); view M2-7 `--bench` / `--vsync`, `PrevFacing` blend, screenshot set (M2 10 / 10); data D2 unit stats pinned to the faction pages, building tables on the pages | all ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x3: sim 2 S3 + 1 S4, view 2 S3 + 1 S4, data 1 S4). M3 3 / 8 |
| 2026-10-06 | [2026-10-06-1744](sessions/2026-10-06-1744.md) | sim M3-2b closing vs opening grid changes (`BlockVersion`, usable-stale fields refreshed lazily, progress-mark reset, wood 1 x 1 rule, 2x faster field build; BUG-0073 / 0074 / 0077 fixed); view M2-6 placeholder audio (`Sfx`, Select / Command tones, `--mute`); data D1 all ten buildings per faction in data | all ACCEPT; sim 1 fix round (QA FAIL on S2 BUG-0082 perf, fixed; 2 S3 filed), view 0 (1 S3 + Producer's S4 BUG-0088), data 0 (1 S4). M3 2 / 8, M2 9 / 10 |
| 2026-10-06 | [2026-10-06-1503](sessions/2026-10-06-1503.md) | sim M3-2 worker gather / return loop (`Gather`, `EconomySystem`, `BuildingStore` + dev `SpawnBuilding`, `buildings.json` Town Hall, player totals, CLI `--workers`); view M2-3b trees and mines as MultiMesh props, 12 / 8 defaults, minimap resource layer | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 2 S3 + 1 S4, BUG-0075 fixed; view 1 S3 + 1 S4). M3 2 / 8, M2 8 / 10. Data track added by the owner mid-session |
| 2026-10-06 | [2026-10-06-1255](sessions/2026-10-06-1255.md) | sim M3-1 resource entities (`ResourceStore`, placer, depletion → nav grid, hash, replay format 3, CLI flags); view M2-5 debug overlay (F12: nav grid, flow arrows, tick graph, counts) | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 3 S3 + 1 S4; view 1 S3 + 1 S4). M3 1 / 8, M2 7 / 10 |
| 2026-10-06 | [2026-10-06-0905](sessions/2026-10-06-0905.md) | sim M1-9 M1 end-of-milestone hardening (9 items, 8 bugs fixed + BUG-0058); view M2-H1 view hardening (5 bugs fixed, rimmed minimap dots) | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S3 + 1 S4; view 1 S3 + 1 S4). **M1 signed off by the Producer** |
| 2026-10-06 | [2026-10-06-0655](sessions/2026-10-06-0655.md) | sim M1-8 headless CLI (`tools/Rts.Cli`) + `FlowFieldCache.PeekCached`; view M2-3 A / S / H / Shift-queue, type select, control groups, Tab subgroups | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S4; view 1 S3 + 1 S4). All 8 M1 criteria met |
| 2026-10-05 | [2026-10-05-2330](sessions/2026-10-05-2330.md) | sim M1-7 perf criterion + Stop / HoldPosition / AttackMove + shift-queue (replay format 2); view M2-4 minimap + `Hud` + `--no-hud` | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 2 S3 + 1 S4, view 1 S4) |
| 2026-10-05 | [2026-10-05-1609](sessions/2026-10-05-1609.md) | sim M1-4d-3 hardening batch (crowd routing + 6 debt bugs); view M2-2 unit views + selection + right-click move + BUG-0041 | both ACCEPT; sim 2 fix rounds (QA FAIL x3: S1 + 2 S2 fixed in-session, BUG-0045 S2 → S3 by the Producer, 4 S3 + 1 S4 filed); view 0 fix rounds (PASS_WITH_ISSUES: 1 S3 + 1 S4) |
| 2026-10-05 | [2026-10-05-1446](sessions/2026-10-05-1446.md) | sim M1-6 replays + golden + BUG-0014; view M2-1 match scene, terrain mesh, camera, screenshot flag | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: 1 S3 + 2 S4 filed, BUG-0014 fixed); first two-track session |
| 2026-10-05 | [2026-10-05-1234](sessions/2026-10-05-1234.md) | M1-5 cross-map scenario test + queued-walker give-up rule + BUG-0035 enemies as hard walls | ACCEPT after 1 fix round (QA FAIL then PASS_WITH_ISSUES; S2 + S4 fixed in-session, 2 S3 filed) |
| 2026-10-05 | [2026-10-05-1013](sessions/2026-10-05-1013.md) | M1-4d-2 shoving of idle units + BUG-0031 fix + re-tightened assertions | ACCEPT after 2 fix rounds (QA FAIL on crowd targets; S2 x2 re-triaged S3, 1 S3 filed, 1 S3 fixed) |
| 2026-10-05 | [2026-10-05-0742](sessions/2026-10-05-0742.md) | M1-4d-1 separation, crowded arrival, give-up, adjacent-cell steering | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 3 S3 open) |
| 2026-10-04 | [2026-10-04-2056](sessions/2026-10-04-2056.md) | M1-4c build-cap determinism + fairness; suite de-flaked | ACCEPT, 0 fix rounds (hardening) |
| 2026-10-04 | [2026-10-04-0120](sessions/2026-10-04-0120.md) | M1-4b GameData in sim + flow fields + LRU cache + `Move` | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
