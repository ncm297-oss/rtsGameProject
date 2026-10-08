# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-07-2315** (which resumed the dead 2014 session; 2014 left no
log). After the conductor merges sim → view → data, `main` carries M4-1 (melee combat), M3-V4 (the Playable proof, the
drawn-shape node pick, "Locked" wording) and D4 (shared techs text, golden `data-hash` A863BAF8637CC860). **Bug ids next
session:** sim from **BUG-0150**, view from **BUG-0160**, data from **BUG-0170** (disjoint blocks: this session's two
tracks both filed a BUG-0145; the sim's was renumbered BUG-0149).

## Where we are

- M0, M1, M2 Done. **M3: 8 / 8 criteria** ("Playable" ticked by `game/tests/M3PlayableTest.tscn`), every track's
  end-of-M3 hardening done (M3-H1 / H2, M3-V3b, D4), coverage rows ✅. **Not signed off: BUG-0146 (S2, sim)** is open.
  Sign-off at the next ACCEPT once it is fixed and QA-verified.
- M4: criterion 2 (damage formula) ticked; criterion 1 lacks the `Attack(target)` order (M4-2); criterion 4 has death and
  building destruction in the sim, corpses / rubble are the view's.
- Open bugs after the merge: 25: S1 0, **S2 1 (BUG-0146)**, S3 13, S4 11 (see STATE "Open bugs" for the ids).
- Hardening counters: sim 1 / 4, view 1 / 4, data 0 / 4.
- **Integration watch-outs:** (1) `main`'s scene loop has six red M2 scenes after the merge (BUG-0147: DebugOverlay,
  Orders, QaH1, QaH2, QaM24, QaM27: their start armies fight now); the gate (build, tests, smoke) is green; the view fixes
  them first. (2) The attached replay `studio/bugs/BUG-0146-seed21-wood-wedge.replay` is headered with the pre-D4
  `data-hash` 702859B867AAC412; the sim's un-skip must substitute the hash in code (or rewrite the file through
  `ReplayFormat` so the checksum follows), never loosen the row. (3) Three suites plus scenes at once make Perf rows
  fail from contention; a failure counts only alone (`TightBlob2500` is at 4.43-4.51 of 4.6 ms).

## Sim track

### Next task candidate: M4-2a — BUG-0146 (S2) first, then the `Attack(target)` order (feature, QA full, bugs from BUG-0150)

**Part 1, BUG-0146 (must come first: S2 outranks features).** Laborers on a gather loop stand `Gathering` 1.7 m out of
reach (1.25 m) of a tree open on one side, re-walking every 20 ticks to the same spot, for the rest of the match. The
walk goal for a node open on one side is reached as "arrived" at a point outside `Reach` (crowd arrival / `GoalInset`),
so the retry repeats. Fix in `EconomySystem` / the arrival rule (a gatherer's walk ends only within reach, or the retry
picks another approach cell / inset); regression: un-skip `QA/GatherWedgeQaTests.Seed21PlayableReplay_NoGathererStandsOutOfReachForever`
(hash substitution in code for the pre-D4 header) and add a hand-built row (a 1 x 1 tree with three sides blocked, four
gatherers queued). Check the dev-seen two-cell corridor oscillation (seed 6, same bug file) while there. Keep the golden's
`k` lines identical if the fix changes no arrival on the golden's one-player path; if it does, say so and regenerate in
the same commit (the sim owns it this session; data STOPs).

**Part 2, `Attack(target)` (M4-2a).** `Command.Attack(player, unit, targetHandle, isBuilding)` (kind 16; queueable; a
dead or own target is dropped at apply like a bad Gather), replay format 4 (the target handle in the command line and
`combat` in the header: `SimConfig.Combat` recorded, so `ReplayPlayer.Run(replay, data)` needs no `combat:` argument;
format 3 files still load), the explicit target outranks the scan (no re-pick while it lives; the leash does not apply:
an ordered attacker chases until the target dies or an order ends it, docs/03 "Orders"), `attack.targets` schema field
(`units` / `buildings` / `all`, default `all`; the ram's data says `buildings`: BUG-0139; the data track is told the field
name), the view's F-key path later. Out: projectiles / misses / splash / friendly fire / min range (M4-2b), fog, abilities.
Golden regenerated once for the format bump (the sim's, same commit, `k` lines identical). Size ~700 sim lines + Part 1.

**After:** M4-2b projectiles (travel, miss rule, splash falloff, friendly fire, min range), counter-triangle scenario
tests; M4-3 fog + high-ground vision; M4-4 abilities / statuses / zones (schema → data track); M4-5 stealth / detection.
Sim debt for its next hardening (3 feature sessions away): BUG-0149 (S3, stall count across a target switch), BUG-0144
(S3, chasers' field waits under churn), BUG-0134 (S3), BUG-0142 items 1-2, BUG-0133, BUG-0113, BUG-0094 (S4); the 3x
termination bound could be 2x.

## View track

### Next task candidate: M4-V1 — BUG-0147's six scenes, then the first combat views (feature, QA standard, bugs from BUG-0160)

1. **BUG-0147 (first):** the six M2 scenes (DebugOverlay, Orders, QaH1, QaH2, QaM24, QaM27) assume start armies that
   never fight. Give `LaunchOptions` a `--no-combat` flag that sets `SimConfig.Combat = false` on the match (the sim's
   test / tooling switch, decision 1; a dev flag, not a game option: say so in docs/03) and run those six scenes with it,
   or `--units 0` where the scene does not need armies; expectations are not loosened. Confirm the whole loop is 28 / 28
   on `main` (27 + nothing new, or +1 if a scene is added). Also check `M3PlayableTest` passes on `main` with the sim's
   worker rule (the Producer's scratch merge ran it: result in the 2315 session log).
2. **Combat views (M4 "Death, corpses" criterion, view half):** unit hp bars (drawn only when hp < max, like the
   buildings'), a hit flash on the victim, death: the view freed from `World.Deaths` with a 10 s corpse marker (a flat
   placeholder), building rubble marker on a building death, the kill / loss counts on F12 and in the resource bar
   (`World.Kills` / `Losses`), the selection panel's hp live (`UnitStore.Hp`), "Attacking" state text (already in
   `ui.json`). `ViewApi` read-only. A headless scene with a scripted 20 v 20 brawl checking bars, flashes, corpses and
   the counter against the sim each frame; the hash twin.
3. Out: the fog shader (M4-3), projectiles' visuals (M4-2b), the Attack-target cursor (after M4-2a lands: the F key
   path with `Command.Attack`).

**After:** projectile views, the Attack cursor and targeting feedback, fog shader + building ghosts (M4-3), ability
feedback (M4-4). View debt: BUG-0148 items 1 and 3 (S4), BUG-0126 items 3-6 (S4), the edge-pan note, export hygiene (M6).

## Data track

### Next: STOP unless the inbox has text tweaks (then QA light)

No schema the data track needs is on `main` yet: M4-2a's `attack.targets` (the ram's `buildings`), M4-3's tower attack
/ sight / detector fields (BUG-0090's remaining item), M4-4's `abilities.json` / `statuses.json`. Owner review tweaks
from the inbox (the D1 / D2 / D3 / D4 text under For your review) come first whenever present. With the first of those
tasks: the **docs/02 "Ages" wording drift** flagged by D4's QA ("level-2 Forge upgrades" → "level II"; "Requires two Age I
production buildings or a Forge" → the exact "any two of"), shipped with the `TechContentTests` parser that reads those
bullets (the data track's tests pin them, so the edit and the test move together). Then the M4 balance pass (QA
standard) after the sandbox; `ai.json` build orders with M5.

## Watch-outs (all tracks)

- **Golden:** only the sim regenerates this session (format 4 and / or a BUG-0146 arrival change), once, in the commit
  that needs it, with the reason; the view and data touch neither the golden nor `game/data/**`.
- **Merge order:** sim, view, data. Shared-file touches expected: docs/03 (one subsection per track), docs/01 (one row
  each, appended), `studio/bugs/README.md` and `studio/qa/coverage.md` (append-only, keep both sides). Every builder:
  `git diff --stat origin/main` before reporting; absolute paths only; never touch the owner's main checkout; never kill
  processes you did not start (a machine-wide `Stop-Process godot` in 2014 killed another track's run).
- **Sessions:** a lock's age alone does not prove the previous session dead (2014's conductor was alive 6 minutes after
  this one started); check for running processes before resuming, and long sessions should refresh the lock.
- `CLAUDE.md` still says "Current milestone: M1" (owner's file; suggested text under For your review, M1 entry).
- `TightBlob2500` 4.43-4.51 ms of 4.6: M4-2's per-unit work must stay inside it; measure alone.
