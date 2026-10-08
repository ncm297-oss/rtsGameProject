# Handoff: brief for the current / next session

Written by the Producer at the ACCEPT of session **2026-10-07-1715** (fifth full session of 2026-10-07, cap 8; the sixth
may run today). Both held branches landed: **D3 through the sim's M3-H2** (BUG-0112 fixed, golden regenerated once:
`data-hash 702859B867AAC412`, checkpoints byte-identical) and **M3-V3 through the view's M3-V3b** (BUG-0124 fixed, the
view's end-of-M3 hardening done). `main`'s smoke gate is green again. **M3 is 7 / 8**: only "Playable" is left (the
owner's word, or the view's scripted run). No open S1 / S2. Bug numbers next session: sim from **BUG-0135** (view
from BUG-0145, data from BUG-0155).

## Where we are

- `main` after the merge: M0, M1, M2 Done; M3 7 / 8 (resources, gather loop, placement + construction + repair,
  production + rally + pop, Age II + unlocks + Forge upgrades, **factions fully defined in data**, **HUD**). Open bugs
  26: S3 8 (sim: BUG-0134, 0080, 0046, 0050, 0028 / 0032, 0025, 0005, 0023; view: BUG-0125), S4 the rest (sim 0094 /
  0113 / 0133 / 0040 / 0026 / 0002; view 0126; data 0090 / 0132; M0 optional MCP).
- Hardening counters after this session: **sim 0 / 4** (M3-H2 was its end-of-M3 hardening), **view 0 / 4** (M3-V3b
  was its), **data 3 / 4** (D4 next is the data track's end-of-M3 hardening: text polish + BUG-0132).
- **M3 sign-off** can happen at the end of next session if (a) the view's scripted Playable run passes (or the owner
  writes "M3 playable ok") and (b) D4 lands: then every track's hardening is done, coverage rows are ✅, no S1 / S2.
  Sign off per the Producer's authority, write the retro, set M4 to Next.
- Producer checks at this ACCEPT: both diffs read; both branches built (0 warnings); both non-Perf suites rerun; the
  sim's Perf / golden / hash / allocation rows alone; the view's smoke and a scene loop with D3's data; `git merge-tree`
  of the two branches (conflicts only in `studio/bugs/README.md` and `studio/qa/coverage.md`; the sim worktree's copies
  are the **union** of both sides, so the conductor takes the sim / `main` version on those two files and on
  `docs/01-vision.md` if it conflicts).

## Sim track

### Next task: M4-1 — combat slice 1: attack orders, target acquisition, the damage formula, melee damage, death (feature, QA full, ~800 lines: a new system, so the first slice only)

**Design is complete and the data already exists**: docs/02 "Combat" (Stats, Damage formula with the worked example
`9 x 0.6 x 1.3 = 7.0 → 7 - 1 = 6`, the type x class table, Death), docs/03 "Combat implementation" (melee damage queued
and applied in phase 11; the phase list at the top of docs/03), `game/data/common/damage_table.json` (armor classes and
the type x class multipliers) and `units.json` (`armorClass`, `attack.value / type / cooldown / range / windup /
bonusVs / splash / minRange / friendlyFire / projectile`). **No schema change should be needed; verify at PLAN.** If one
turns out unavoidable, split it into a later slice: the data track regenerates the golden next session (D4), and only
one track may move `data-hash` per session.

Suggested scope for slice 1 (the PLAN decides): `Attack(unit)` command kind + the existing `AttackMove` acquiring
targets (nearest enemy in sight / range; sight from data), chase within a leash, retaliation when hit while Idle,
cooldown + wind-up timing in ticks, melee hits applied in phase 11 through the formula (`DamageFormula.Compute` with a
unit test for the worked example and every table cell), `TechBonus` applied to attack / armor (M3-5's promise), death
(unit freed, pop released, kill event / counter for the view), buildings damaged by attacks (the `Damage` seam exists),
hash + replay (new per-unit fields hashed; golden checkpoints may move if the default replay has fights: it doesn't,
armies only march; say so), AllocationTests row, Perf row (500 vs 500 melee brawl under the 4 ms budget), twins + fuzz.
Out: projectiles / misses / splash (slice 2), fog (M4-3), abilities / statuses (M4-4), stealth, towers' attack.

**Watch-outs:** the spatial hash is the only legal target query (no O(n^2)); no allocation per tick (target lists in
preallocated scratch); SimMath only; `Attack` is a unit order like `Move` (clears the queue unless Shift); the counter
triangle scenario tests are M4's own criterion (slice 2+); the view's `UnitViews` reads hp for bars later (ViewApi only
when the view asks). docs/03 gets "Implementation (M4-1)"; the "Known limits" list grows if chase has a leash.

### After M4-1 (sim)
M4-2 projectiles (travel, miss rule, splash falloff, friendly fire), M4-3 fog + high-ground vision, M4-4 abilities /
statuses / zones + the four named abilities, M4-5 stealth / detection, the counter-triangle scenario tests; a sim
hardening at 4 feature sessions or at M4's end. Debt waiting for the next sim hardening: BUG-0134 (S3: a building gated
behind a tech of its own slot loads clean; fix = `researchedAt` in the reachability fixpoint), BUG-0133 (S4: push-out
offsets repeat past 24 a cell: fix the docs sentence or grow the radius), BUG-0113 (S4), BUG-0094 (S4, test-only).

## View track

### Next task: M3-V4 — the M3 Playable proof (a scripted run of the full base + Age II through the real HUD), BUG-0125, BUG-0126 items 1-2 (feature, QA standard)

**Goal.** Close M3's last criterion without waiting for the owner: a `game/tests` scene (`M3PlayableTest.tscn`, "M3
PLAYABLE TEST PASS") that plays the owner's playtest script from STATE "For your review" (M3-V3 entry) through the real
HUD at 8x speed: box-select the five workers, right-click the mine (gather), Town Hall card → Q x 3 (queue), click the
third square (cancel + refund), rally on a forest, B / E Barracks placed on green and built, B / A Armory, B / Q Billet
(cap 10 → 18), Age II greyed "Locked" until both halls finish, W → research, "Age II" flash, Armory card Melee Weapons
live / Melee Weapons II "Locked", V / W Engineers' Yard (locked before Age II, placeable after), Sapper trained there,
panel reads the +1 after Melee Weapons. Every step through injected input on the real scene, each checked against the
sim's state the next frame; a hash twin of the same command stream in xUnit. **Then** BUG-0125 (S3, a regression from
M3-V3b's node pick: pick the drawn cone / block, not the whole 2 x 2 x 3.5 m column; flip the QA measurement rows to
checks: open ground taken for a node = 0) and BUG-0126 items 1-2 (a locked building's ghost reads "Locked" and its
Place button is greyed with `placement.requires` when `CanPlace` says `Requires`; Age II reads "Researched" / "In a
queue" when `HasTech` / queued, whatever the sim's first reason). Items 3-6 of BUG-0126 stay for the view's next
hardening. Out: M4 views (hp bars for units, attack feedback) until the sim's M4-1 lands; any `ui.json` key the card
already reads (new keys fine); the sim outside `ViewApi/`.

**Acceptance (draft):** the scene passes headless on seeds 1 and 6 and prints the tick each step completed; the run
reaches Age 2 with a Sapper alive within a fixed tick budget; smoke PASS; every scene PASS; `PickRayQaTests` "open
ground taken for a node" 0 and "column pick wrong" well under the ground pick's 21.8 %; the D3-locked ghost reads
"Locked"; non-Perf green; `ViewApi` read-only (hash twin); no `game/data/factions/**` in the diff.

When it passes, tick "Playable" in docs/05 with the scene named; the owner's own playtest stays wanted as feedback.

### After (view)
M4 view work once the sim's M4-1 is on `main`: unit hp bars and hit flashes, attack animations (placeholder), death
(unit view freed, a 10 s corpse marker), the kill counter on the F12 overlay; then the fog shader with M4-3. View
debt for its next hardening: BUG-0126 items 3-6, the edge-pan note, export hygiene (M6).

## Data track

### Next task: D4 — common techs text + BUG-0132 (hardening: the data track's end-of-M3 debt; QA light)

`game/data/common/techs.json` **strings only** (`displayName` / `description` of Age II and the six Forge upgrades),
pinned to docs/02 "Tech" / "Forge upgrades" and quoted for the owner; the M3-5 placeholder wording is in STATE's M3-5
entry. **Named exception to the ownership rule** (recorded under For your review at this ACCEPT): `common/techs.json` is
the sim's file, the data track edits only its text fields this session and the sim task (M4-1) does not touch it.
BUG-0132: the D3 pin messages name both values and the side (`TechContentTests` A-C, `RequiresText.Needs` reads
"requires" / "after" wording too). Check first whether descriptions are in `ContentHash`: if so, the data track owns the
**golden regeneration** this session (`RTS_REGEN_GOLDEN=1`, `data-hash` + `checksum` only, every `k` line
byte-identical); the sim must not move `data-hash` this session. Any owner tweak from the inbox to the D1 / D2 / D3 /
M3-5 text comes first. Out: any numeric field, any faction file, C# outside `Content/` and `QA/Content/`.

### After (data)
STOP until M4's schemas land: `abilities.json` / `statuses.json` (M4-4), the tower attack / sight / detector fields
(M4-1 / M4-3; BUG-0090's remaining item), then the M4 balance pass (QA standard) after the sandbox; `ai.json` build
orders with M5.

## Watch-outs (all tracks)

- **Golden rule:** one track regenerates `cross_map_seed1.replay` per session. Next session that is the **data
  track (D4)** if descriptions are hashed; the sim's M4-1 uses the shipped schemas and moves no `data-hash`. If both
  must move it in one session, the conductor merges data first and the sim builder merges `origin/main` and regenerates
  last; avoid that by planning.
- **Merge order:** sim, view, data as the skill says. Expected shared-file touches: docs/03 (one subsection per
  track), docs/01 change log (one row each, appended), `studio/bugs/README.md` and `studio/qa/coverage.md` (append-only,
  keep both sides). Every builder: `git diff --stat origin/main` before reporting, to show no leak into another track's
  files.
- **Gate-change rule (from BUG-0112 / BUG-0124):** when a sim task changes a gate (`CanTrain` / `CanPlace` /
  `CanResearch` / data validation) or a data task changes values that fixtures read, the other tracks' QA merges the
  sim branch (or `main` once it has landed) before its final run, and the Producer reruns the merged result.
- Two suites and a scene loop run at once: Perf rows can fail from contention; a failure counts only alone.
  `TightBlob2500` sits at 4.36-4.39 ms against 4.5 (thin margin); M4-1's new per-unit work must keep it under.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" (suggested text under For your review, M1 entry).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths; never kill processes
  you did not start.
