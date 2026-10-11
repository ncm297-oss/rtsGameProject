# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-10-2334** (sixth and last full session of 2026-10-10; the next
session falls on 2026-10-11, 0 / 8). All three tracks accepted (sim M4-H3, view M4-VH3 on `main`; data D12 **accepted and held
on `origin/studio/2026-10-10-2334-data` @ c0dd350**: it conflicts with M4-H3 in two test files and the conductor may not
hand-resolve code). **M4 signed off** (docs/05 retro, docs/01 row; the sign-off stands with D12 held: accepted content waiting
on a mechanical rebase); **M5 (AI opponent) is Next.** **The data track's first item next session is the D12 rebase** with the
Producer-recorded resolution (session log table; reproducible: `StealthLoaderTests.cs` = D12's two hunks on top of `main`'s
file, `StealthContentHashTests.cs` = `main`'s file as is, `studio/bugs/README.md` union by id; the resolved copies are also in
the 2334 session's scratchpad `resolved/`).

## Where we are

- M0-M4 Done. M5 0 / 6: (1) `PlayerView` facade, the AI reads nothing hidden (a test enforces it); (2) the build-order executor
  from `ai.json` + economy / production / military / ability / scout managers; (3) expansion, defense, attack waves, retreat,
  rebuilding, scouting ramps and high ground before attacking up; (4) Easy / Normal / Hard per docs/02 "AI opponent"; (5) an
  AI-vs-AI headless 20-minute match completes without errors and one side wins; (6) the owner can win on Easy and lose on Hard.
- Counters: sim 0 / 4, view 0 / 4, data 1 / 4. Next hardening after 4 feature sessions per track (or sooner for an S2).
- **Golden** `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`: `data-hash` **889D6BEA3A49F08E on `main`** (verify at the PLAN);
  the D12 rebase moves it to **C4C16BD39408471A** (`checksum` D3392E846CAC0118, every `k` line equal; already in D12's
  `SameGameDataHashes`). Whoever else moves it appends to `GatherWedgeQaTests.SameGameDataHashes`. **The sim's M5-1 must not touch
  `game/data/**`, the golden, or the two stealth test files** so the rebase stays trivial.
- **Memory bound** 231,920,000: 231,908,912 used (**11,088 B margin**). **The next per-slot array re-baselines** (itemised in the
  brief and the row's comment; M5's AI state is the likely one). Say so in every sim brief.
- **Perf:** `EmptyTick(2500)` gate 550 µs (481-489 alone); `TightBlob2500` 4.5 ms with ~0.1 ms headroom alone. A Perf failure counts
  only alone and above base. BUG-0510: the EmptyTick scene is a pile-up, not an idle tick (QA's to fix before the AI adds per-player work).
- **Bug ids next session:** sim from **BUG-0540**, view from **BUG-0550**, data from **BUG-0560**.
- Standing rules (1-8 unchanged: full suite incl. Perf alone + the merged scene loop for vision / combat / loader changes; a new
  `UnitState` ships its `ui.json` line; whoever moves `data-hash` adds it to `SameGameDataHashes`; `ui.json` keys named in the
  brief; a sim behaviour change a view scene asserts on gets a named item in the view brief; a new `Command` kind goes into
  `CommandDoorFuzzStressTests`; per-slot memory itemised; the shipped `data-hash` pinned in the golden only). **New rule 9 (from
  BUG-0530):** a brief names every test file another track's change will break and the final assertion both agree on; both tracks'
  briefs carry the line, and the brief says which branch ships the edit (the one that merges first) so no merge conflict arises.
- **Integration order is always sim → view → data** (the conductor's rule); plan around it, never against it.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file; drift noted, not mine to change without a decision).
- Open S3 (13): BUG-0511, 0512 (next sim hardening, first two items), BUG-0461 movement half, 0144, and the old crowd limits
  (0005 / 0023 / 0025 / 0028 / 0032 / 0046 / 0050 / 0080 / 0151). S4 (12): sim 0510 / 0513 / 0113 item 2 / 0184 / 0002 / 0026 /
  0040; view 0520 / 0521 / 0281 item 2; data none. No S1 / S2.

## Sim track

### Next task candidate: M5-1, `PlayerView` + the `ai.json` schema · feature · QA full · ~1,200 lines incl. QA

**Goal.** The AI's only window on the world and the file its build orders will come from, so the data track can start writing
`ai.json` while the sim writes the managers. No AI decisions yet.

**Scope.**
1. `Rts.Sim.Ai.PlayerView(world, player)`: read-only; units / buildings / resource nodes / ghosts / explored cells / visible cells
   / own ledger, techs, population; every enemy unit answer goes through `Fog.CanSeeUnit` (which applies `World.HiddenFrom`:
   stealth), every enemy building through `CanSeeBuilding` or the ghost list; own units always; no `World` reference escapes.
   docs/03 "AI" (the Blackboard reads only the `PlayerView`), docs/02 "AI opponent" (the AI plays by the same rules).
2. The test for criterion 1: a fuzz over 3 seeds x 2,000 ticks comparing every `PlayerView` answer against a brute-force
   fog-and-stealth oracle (0 leaks), and an architecture test that `Rts.Sim.Ai` never references `World` outside `PlayerView`.
3. `game/data/factions/<id>/ai.json` schema (docs/03 "Data format": think interval, build order steps (build / train / research /
   expand at a count or a time), army compositions, attack thresholds, retreat rule, per difficulty) with validation,
   `DataValidationTests`, and the minimum entries the loader tests need (one step per faction); whether `ai.json` enters
   `ContentHash` is the dev's call (if it does: one golden regen, `k` lines equal, the hash appended).
4. Perf: a `PlayerView` snapshot for 8 players at 2,500 units under 0.3 ms a tick, 0 B steady state (pre-sized per-player arrays:
   itemised; **this is the re-baseline** if per-slot).
5. Out of scope: any manager, any command the AI sends, BUG-0511 / 0512 unless the task touches their code.

**QA focus.** Leaks: a unit that becomes hidden mid-tick (stealthed in phase 5, a storm's blocker, a ghost dropped on sight), the
tick-0 stamp, players past 2, a `PlayerView` kept across ticks; the brute-force oracle every tick; determinism (reads leave the
hash unchanged); 0 B.

### Alternatives (if the owner's inbox asks otherwise)
BUG-0511 + 0512 as a small hardening task (both have skipped QA rows and clear directions); BUG-0144.

## View track

### Next task candidate: M5-V1, the lost-cast cue + the flat rings (+ export hygiene if budget remains) · feature · QA standard · ~1,000 lines incl. QA

**Scope.** (1) The HUD reacts to `AbilityEvent.Dropped` (Requests 34): the caster's cast bar / cast-point ring ends with a short
red flash on the caster and the existing order-refused sound; any text from `ui.json` (name the keys in the brief). `AbilityViewTest`
rows: a walk-in blocked by enemies drops after 3 s and shows the cue; a cast cancelled by a new order shows nothing; the twin hash
unchanged. (2) BUG-0520: cast-point rings and resolve flashes as `GroundDisc`s (a small pool); the cliff row from `QaVh3Test`'s
layout. (3) BUG-0521 item 1. (4) If budget remains: exclude `game/tests/` from the release build (an export preset; docs/03 "Build
and export"); the `.pck`-safe data load waits on Requests 33. Out of scope: `game/data/**`, replays, `DataContentHashTests.cs`,
any `sim/**` beyond read-only `ViewApi` additions.

**QA focus.** The cue fires once per drop (QA's `ADroppedCast_FiresOneDroppedEvent_*` is the sim side); no cue on the player's own
cancel; `M4PlayableTest` seeds 1 / 6 still pass (it works round BUG-0461 with plain Moves); the ring pool allocation-free; the
merged loop 42 / 42.

### Later
M5-V2 an AI seat in the sandbox (`--ai <difficulty>`, seat 1, the match end printed) once M5-1 and the first managers are on `main`;
a remembered site drawn as a foundation (`Fog.Ghosts` `Site`); then the M6 shell.

## Data track

### First, always: the D12 rebase · ~50 lines · QA light (the suite is the gate)

The data branch `origin/studio/2026-10-10-2334-data` @ c0dd350 is accepted. The data dev merges `main` (after the sim's and
view's 2334 commits) into it and resolves: `sim/Rts.Sim.Tests/Data/StealthLoaderTests.cs` → D12's side of both conflict
hunks (test name `Shipped_TheKindsAndRulesLoad_AndOnlyTheCastersDetect`, assertion `u.Slot == UnitSlot.Caster ? 10f : 0f`;
`main`'s BUG-0481 row and `Remove` helper stay); `sim/Rts.Sim.Tests/Vision/StealthContentHashTests.cs` → `main`'s file as is
(D12's only change there, "10" → "12", is already in `main`'s "12" / "13" mutants; D12's "revealed gone" mutants no longer load
under BUG-0481); `studio/bugs/README.md` and `studio/qa/coverage.md` → union / keep both. D12's golden and `SameGameDataHashes`
line stand. Then `dotnet build` 0 warnings and the full non-Perf suite green (the Producer's scratch merge of exactly this:
980 / 3 / 0 on the affected filters), and the branch is ready to merge. This is a named exception to "no C# outside the data
test folders" for those two files only. Acceptance: the two files equal the recorded resolution; suite green; `main`'s golden
header reads C4C16BD39408471A after the merge. **If it merges first in the session (data before sim this once) nothing else
changes; otherwise the sim's M5-1 must not touch those files.**

### Then: inbox tweaks first; else the balance pass · feature · QA standard · ~800 lines incl. QA

**Scope.** The counter-triangle margins (both pages' tables, `Content/CounterTriangleMarginsTests`) into the **40-65 % winner-cost
band** (the Producer default from D6, docs/02 "Balance" + the D6 table; the owner's answer overrides if it is in the inbox at the
PLAN): change only attack / armor / hp / cost in `game/data/factions/malazan/units.json` and `whirlwind/units.json` (no speed,
radius, sight, detector or text; the Line v Shock outlier first); re-pin every page cell; the review table (unit / field / old → new
/ one-line why) in the report. **Named exceptions:** the golden `cross_map_seed1.replay` re-record (hp is hashed, so `k` lines move
if any unit in that march changes hp; state which) + `SameGameDataHashes`; and, by rule 9, the sim-track tests that pin shipped
numbers (`Data/UnitLoaderTests`? the dev lists them at the start and the brief names them). QA standard: the fight harness numbers
(`CounterTriangleScene`), `DataValidationTests`, `DescriptionProblems` 0.

**Design references.** docs/02 "Unit classes", "Balance" / the counter triangle; docs/factions/malazan.md and whirlwind.md unit
tables and the "Winner hp left" column; the D6 entry under For your review.

### After that
`ai.json` build orders for both factions once M5-1's schema is on `main` (QA light; the economy / army / attack numbers per
difficulty from docs/02 "AI opponent"); M7-M9 faction data later.

## Watch-outs (all tracks)

- At the PLAN: `main` does **not** carry D12 (golden 889D6BEA3A49F08E, no `detector` in `units.json`); the data track rebases it
  first (above); the sim and view briefs say nothing touches the two stealth test files, `game/data/**` or the golden. The M4
  sign-off is recorded as standing with D12 held; if the rebase cannot land green, reopen it at that ACCEPT.
- The roadmap now has the M3 and M4 retros (the M3 one was missing; written at this ACCEPT). The M2 retro has a duplicated
  "Decisions made" paragraph (M1's lines) left as found.
- Scratch worktrees: the Producer's `producer-merge` worktree (in the 2334 session's scratchpad, detached at a scratch merge
  commit; the permission system refused its removal) is one more housekeeping entry: `git worktree remove --force <path>` then
  `git worktree prune`. 15+ owner-housekeeping entries remain in `git worktree list`.
- Perf under three suites fails on base and head alike (`TightBlob2500`, `EmptyTick(2500)`); a failure counts only alone and above base.
