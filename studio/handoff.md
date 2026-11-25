# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-24-1431** (session 1 / 8 of 2026-11-24, started 14:31,
the ACCEPT ended about 00:20 on 2026-11-25; about 10 hours). Sim **M7-8** ACCEPT (feature, 0 fix rounds, QA full PASS_WITH_ISSUES), view
**M6-VH9** ACCEPT (hardening, 1 fix round, QA standard FAIL → PASS_WITH_ISSUES), data **D35** ACCEPT (feature, 0,
QA light PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters:
**sim 4 / 4 (hardening next), view 0 / 4, data 4 / 4 (hardening next)**. Bug ids next session: **sim 1700-1709,
view 1710-1719, data 1720-1729**. Open S1 / S2: none (S3 39, S4 30). The next session is **1 / 8 of 2026-11-25**.

**Next session in one line (proposal):** sim **the sim's hardening** (no AI-play change, no pin, no hash move; QA
full) · view **M6-V27, Request 58's view half** with BUG-1682 (feature, QA standard) · data **D-H10, the data's
hardening**: the Teblor opening with the switches first (BUG-1612's open half; one hash move if an order ships;
QA standard), then BUG-1690 / 1691.

**The default loop on `main` is green.** Expect **0 failed** (10,203 passed / 0 failed / 22 skipped of 10,225 in 21 m 57 s on the Producer's scratch clone of sim
bdc5d11 + view 68cc9ef + data 2811489). The skips are the standing 20 plus QA's two D35 rows (BUG-1690,
BUG-1691). Any red row is a finding. **The Stress category has no expected red row** (rule 74: QA ran it on the
sim branch, 935 / 2 / 10, both failures green alone; the BUG-1621 row is skipped with its ids). Under load two
Stress rows fail on wall-clock limits and pass alone: `EndGameHardeningTests.PaleHillsSeed2_FourSeats_*` (1,200 s)
and `RampWallQaTests.EveryField_AtExtremes_*` (10 s, BUG-1582).

**At the PLAN, first:** (1) the inbox (waiting for answers, none blocking: D33's and D34's tables, "buildings ok",
"whirlwind colours ok"; tweaks come before roadmap work; "teblor opening: D33's order" restores
`Content/Fixtures/teblor_standard_d33.json`'s order and is a hash move); (2) the default loop on `main`;
(3) **bug index upkeep the ACCEPT left on purpose** (three branches append to `studio/bugs/README.md`): check that
the README rows of this session's bugs (1660-1663, 1680-1682, 1690-1691) and of the ones the view closed (1630,
1600, 1601, 1631, 1632, 1381, 1602) say what their files say, and the older four (BUG-1612 "open, half fixed",
BUG-1613 and BUG-1510 "fixed, D34", BUG-1421 "open, item 1") if still stale; (4) **CLAUDE.md's "Current milestone"
line is stale** (on "Waiting on you" since this ACCEPT; neither the Producer nor the conductor may edit it);
(5) **the data's task moves the data hash and the sim's hardening must not**: name the files in each brief; the
sim's hardening takes no `game/data/common/**` change and no AI-play change, so the data's Teblor rows are
measured on a quiet sim (rule 41); (6) the data's pin list is cut to the Teblor's (rule 76): the nine pins sim QA
found by applying docs/03's rows (the two golden headers, `StillStealthLoaderTests` which states the shipped hash
as a literal (make it read the golden header: rule 73), the D33 / D34 tests, the Teblor opening fixture,
`QA/Content/TeblorOpeningQaTests`, teblor.md's house note); the sim's `Stress/ReserveSwitchReportTests` and
`Ai/TeblorSwitchOrders` forge the shipped Teblor file and must tolerate it already carrying switches (rule 58:
tell the sim's hardening to check on a copy with order A applied); (7) the view's task reads `World.DroppedOrders`:
the sim half is on `main` after this session; the "Orders full" text needs a `ui.json` key (schema: sim; text:
data): check at PLAN whether `ui.json` can already hold it, else the view shows the Build reasons only and the key
goes to Requests; (8) session length: 10 h this time with no pin move; the data's 64-match rows on three
difficulties are the long pole next (D34's took most of a day beside a sim QA full): keep the sim's hardening
small and tell its QA that Stress runs once.

## Current session plan

Written at the PLAN of session **2026-11-25-0924** (1 / 8 of 2026-11-25; base 40efa16). Three tracks: sim **M7-H3**
(hardening, QA standard), view **M6-V27** (feature, QA standard), data **D-H10** (hardening, QA standard). Bug ids:
sim 1700-1709, view 1710-1719, data 1720-1729. Rulings behind the briefs: docs/01 2026-11-25 (PLAN row).

**Who touches what under `game/data/` (never two tracks on one file):** sim: nothing. View: `game/data/common/ui.json`
only (one new key, `orders.queue_full`; the file is read by the view's own loader `game/scripts/UiText.cs` and is not
in the data hash). Data: `game/data/factions/teblor/ai.json` only. **One hash move at most, the data's.**

**All tracks:** record the PID of every `dotnet test` / Godot you start and end only those (rules 52 / 69); a scratch
clone is named after the task (`m7h3-...`, `m6v27-...`, `dh10-...`), created new (rule 63); "build 0 / 0" means
`dotnet build RtsGame.sln --no-incremental` after the last edit (rule 64); run the full default loop
(`dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf&Category!=Stress&Category!=Soak"`) after the last edit or say
in the report's first lines that you did not (rule 62); bug-file updates come as a patch that `git apply` accepts
(BUG-1662 item 3); open questions go in a "Questions for the Producer" block in the docs section the task owns
(rule 67). Expected default loop on the base: 0 failed, 22 skipped.

### Sim track: M7-H3, the sim's hardening (tests, docs and comments; no rule changes)

**Goal.** Work the sim's debt that changes no game rule: the test rows that fail only on a busy PC, stale labels and
comments, two doc gaps the view and data tracks read this session, and proof that the sim's reserve-switch tests
survive the data track shipping switches in the Teblor file. Nothing a player or a computer player does may change:
no state hash, no pinned match, no data hash moves.

**Scope (in).**
1. **BUG-1662 item 1:** `Orders/DroppedOrder.cs`'s doc for `Unit` and docs/03 "For the view track (Request 58)" say
   the unit may have died later in the same tick (stale when read, as `DeathEvent.Victim`). Doc text only.
2. **BUG-1582** (ruling: a hang guard, not a speed limit): `QA/RampWallQaTests.EveryField_AtExtremes_*` runs the
   generation on a dedicated thread (not the thread pool) and waits with a limit long enough for a starved machine
   (at least 120 s; say the figure and why); a real hang must still fail the row, not hang the run.
3. **The 1,200 s child limit** of `EndGameHardeningTests.PaleHillsSeed2_FourSeats_*_TwoProcesses_*` (ruling: the
   limit counts the child's own processor time, same 1,200 s; a wall ceiling above it that you state). Apply the same
   helper to any other two-process row that kills its child on a wall clock (list them).
4. **BUG-1539** (ruling): `LocalMovementTests+Serial.Perf_UnitsConvergingOnOnePoint_AvgAndWorstTick` takes the least
   worst tick of three runs against the unchanged 8 ms; the average bound is untouched. No threshold changes.
5. **BUG-1620** (ruling, docs/01 2026-11-25 (1): stays as built): docs/03 "Implementation (M8-3)" Known limits (3)
   says "a full pack (1.3) rounds an exact half down (45 x 1.3 = 58.5 deals 58)"; the skipped row
   `QA/HitBonusQaTests.AFullPack_45x1point3_RoundsHalfUpTo59_AsByHand` becomes an active row that pins 58 and names
   the limit; `StackedFactors_DisagreeWithAHandCalculation_*` stays. **No change to `Combat/**`.**
6. **BUG-1557's label:** `QA/ScoutCapQaTests.KnownWorkerLosses` and its comment say BUG-1557, not BUG-1462. The
   cause (an AI economy finding) is not this task's.
7. **Stale comments** (comments only): `Stress/AiDeterminismTests` and `Stress/HuntStressTests` carry M6-SH2's
   figures (docs/03 "Implementation (M6-S12)" item 6 has the current ones); docs/03 "For the data track (M8-3)"
   still says the description reading "knows neither field yet" (BUG-1691's grouped note; D35 built it).
8. **BUG-1433 item 3:** `Movement/GatherOverlapFuzzTests.Run` asserts "the scene cannot be built: no Keep" instead
   of throwing a bare exception; the comment names every left-out seed.
9. **Rows found by file position:** the `[0]` / `techs[0]` / `abilities[0]` lookups of shipped rows in
   `FrenzyRulesLoaderTests`, `StatusDurationTechTests`, `DataContentHashTests` and `QA/M7H1QaTests` look the row up
   by id (the Shadow roster will add rows).
10. **The `null` policy, written down (ruling, docs/01 2026-11-25 (3); no loader change):** docs/03 "Data format"
    gains one paragraph: an optional block (`forestStealth`, `regen`) written `null` reads as left out; an optional
    number or switch (`reserveArmyShare`, `throughReserve`, `noReserve`, ...) written `null` is a load error. One
    test row a side pins it, by table over the fields you find.
11. **Rule 58 for the data track's landing:** on a scratch copy of the shipped data whose Teblor `standard` already
    carries (a) order A, (b) `throughReserve` on the Shaman step only, (c) `throughReserve` on every train step, run
    `Ai/ReserveSwitchTests`, `Ai/ReserveSwitchOffTwinTests`, `Ai/ReserveSwitchDeterminismTests`,
    `Data/ReserveSwitchLoaderTests`, `QA/ReserveSwitchQaTests`, `QA/DroppedOrderQaTests` and, once,
    `Stress/ReserveSwitchReportTests` and `Stress/M78ReserveSwitchQaStressTests`. Any row red only because the
    shipped file carries a switch is made tolerant (tests only: look the step up, then add or edit; a row that means
    "shipped has none" compares against a copy with the switches removed). Report the table of rows x copies.
12. **Rule 67:** the Producer's answers to M7-8's Questions 1-4 (docs/01 2026-11-24 ACCEPT (1)) written under the
    questions in docs/03 "Implementation (M7-8)".

**Out of scope.** Any change under `sim/Rts.Sim/` other than item 1's doc comment; any
AI-play change (BUG-1660, 1661, 1555, 1554 / 1556, 1558, 1537, 1161, 1149, 1150, 1152: the later AI-play session);
BUG-1463 (movement, needs a design); BUG-1490 (a save format change); everything under `game/**` and `tools/**`;
`sim/Rts.Sim/ViewApi/**` (the view's this session); `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`,
`docs/factions/**` and the golden replay headers (the data's this session: do not touch `GatherWedgeQaTests`'
header comment, `FactionOrderTests.TheShippedContentHash_*`, `QA/PackQaTests.TheShippedDataHash_*`).

**Acceptance criteria.**
1. `git diff <base>...HEAD -- sim/Rts.Sim` shows comment / doc-comment lines only; `-- game tools` is empty.
2. No pin, fixture, golden or replay file is in the diff; the golden replay test and `HardKnobsPinTests` are green
   and untouched.
3. Item 2: the `RampWallQaTests` extremes rows pass 3 of 3 runs while another full default loop runs beside them
   (say what ran); a generation forced to spin (a test seam or a cancelled token) fails its row inside the limit.
4. Item 3: the two-process row passes beside a running default loop (once) and alone (once); a child that spins
   past the processor-time limit is still ended and the row fails (shown on a tiny limit in a test of the helper).
5. Item 4: the 500-unit row alone (rule 42) passes 5 of 5 runs; the 8 ms and the average bound are unchanged in the
   diff. If it cannot (the PC's slow state: `ASweepRetarget` near 85 us), report the fifteen worst ticks and stop:
   measure and report, not pass / fail.
6. Item 5: the row is active, pins 58, and its message names docs/03's limit; `git diff -- sim/Rts.Sim/Combat` empty.
7. Items 6-9: the label reads BUG-1557; no `BUG-1462` left in `ScoutCapQaTests`; the four classes find shipped rows
   by id (the rows that used a position lookup stay green on a copy of the data with a new first tech and a new
   first ability: show it once).
8. Item 10: docs/03's paragraph and the two table rows; no file under `sim/Rts.Sim/Data` in the diff.
9. Item 11: the table; every listed class green on copies (a), (b) and (c) and on the shipped data.
10. Items 1, 7, 12: the doc lines are there (quote them in the report).
11. Build 0 errors / 0 warnings; default loop 0 failed / 22 skipped (the standing 20 + D35's two; minus one if
    item 5's skip is lifted: say the new count); smoke PASS.

**Design references.** docs/03 "Implementation (M7-8)" (Request 58, "For the data track", the questions),
"Implementation (M8-3)" Known limits, "Data format"; docs/02 "Damage formula" (round half up: the documented limit
is against it and must say so); docs/01 2026-11-25 PLAN row and 2026-11-24 ACCEPT row (1); the bug files 1662,
1582, 1539, 1620, 1557, 1433.

**Tests required.** The helper's own tests (dedicated-thread guard, processor-time child limit); the active 58 row;
the two `null` table rows; the by-id proof; the rule-58 table. Perf: only the one row, alone.

**Constraints.** No behaviour change (CLAUDE.md rules 2, 4: nothing in the tick or the hash moves); never loosen a
Perf threshold; tests use xUnit's own asserts; no new NuGet package; the developer cannot write `studio/**`.

**QA focus (standard).** Prove "nothing moved": four shipped AI matches x 30,000 ticks hash-equal on base and head
(`Stress/M78ShippedDigestQaStressTests` is the tool: same digests on both trees). Attack the guards: do they still
catch a real hang and a real slow tick (mutate: a generator that spins, a child that spins, a tick padded to 20 ms
in two of three runs and in three of three)? Is the 58 row an honest pin and not a loosened one? Re-run item 11 on
a copy of your own (the Anointing step moved ahead of the three train steps, plus switches). Suites: the task's
classes, the default loop, the Stress rows the task touches (the two-process rows, the two switch Stress classes
once), the one Perf row alone. **Not the whole Stress or Perf category** (rule 53): tell the Producer if anything
suggests it is needed.

### View track: M6-V27, tell the player when a queued order is dropped (Request 58's view half)

**Goal.** When a Shift-queued order cannot start at its turn the unit just stands there and the player is not told.
The sim lists such orders since M7-8 (`World.DroppedOrders`). Show it: for the local player's unit, the Refused sound
and a short line over the unit saying why a queued building was not started ("Can't afford", "Blocked", ...), and
"Orders full" when a ninth queued order is refused. With it three small leftovers of M6-VH9.

**Scope (in).**
1. A read-only accessor in `sim/Rts.Sim/ViewApi/` over the dropped-order span (handed in by the view: `ViewApi`
   source may not name `World` or `UnitStore`): for a given player, the notices to show (unit handle, whether it is
   still alive with that generation, a refused Build's `PlacementError`, or queue-full). No sim state written.
2. The view reads it once per sim tick run, as it reads `Deaths`. For an entry of the **local player's** unit:
   a Build with `Placement != None` -> the Refused sound and the `ui.json` `placement` text for that error over the
   unit; `QueueFull` -> the Refused sound and the new `ui.json` `orders.queue_full` text. Every other kind (a Move to
   nowhere, a Gather with the node gone, ...): nothing yet. One sound a tick however many entries; one line a unit;
   the line follows the unit, lasts a fixed time on the view's clock (your figure, a view constant, in docs/03) and
   is drawn like the other world-space overlays (readable at zoom 20 to 60).
3. `game/data/common/ui.json`: one new section `"orders": { "queue_full": "Orders full" }` (the Producer's wording,
   docs/01 2026-11-25 (4)), read by `UiText` like the other sections (a missing key is a load error naming it).
   **The only file under `game/data/` this task edits.**
4. Nothing is shown or played for another player's units (enemy or ally), for a unit that is dead when read
   (BUG-1662: check alive and generation; no line may land on a slot another unit has taken), or while watching a
   replay (Load Game -> Watch).
5. **BUG-1682:** a left click where an enemy building is the nearest drawn thing on the ray selects nothing instead
   of the own building behind it (the BUG-1630 pattern in the bug file). Enemy units in front: unchanged, out of
   scope.
6. `FinishSelect` walks `PickResource` once a click, not twice (no behaviour change).
7. Commit `game/tests/QaM6VH9FixTest.cs.uid` (run an import; the tree must be clean after an import and the scene
   loop).
8. docs/03 "Implementation (M6-V27)"; the Producer's answers to M6-VH9's Questions 1-5 (docs/01 2026-11-24 ACCEPT
   (2)) written under those questions (rule 67); docs/02's controls text if it describes queued orders.

**Out of scope.** Any sim file outside `sim/Rts.Sim/ViewApi/**` and its tests under `sim/Rts.Sim.Tests/ViewApi/`;
`DroppedOrder.cs` (the sim edits its doc this session); texts for other order kinds; saving notices; building or
unit models, `tools/**`; any other `game/data/**` file; a see-through outline for hidden units.

**Acceptance criteria.**
1. Accessor unit tests: only the asked player's entries; a refused Build carries its error; queue-full is flagged;
   an entry whose unit died in the same tick, and one whose slot was reused, are reported not alive; 100 calls on a
   full list allocate 0 bytes; `TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls` green.
2. A new scene on the shipped data: a worker with a Shift-queued building it cannot pay for at its turn -> in the
   frame after that tick the Refused sound has played once and the line over the worker reads `ui.json`
   `placement.cannot_afford`; a second case with the site blocked reads that error's text.
3. A unit with 8 queued orders is sent a ninth queued order past the view's own pre-check -> one Refused sound and
   the line reads `orders.queue_full`. No C# file contains "Orders full" or any placement text (grep in the report).
4. Twelve workers refused in one tick: one sound, twelve lines; every line is gone after the stated time; with no
   entries a frame allocates 0 bytes in the notice code; with twelve, state the bytes.
5. A refused unit killed in the same tick: no error, no line; a slot reused by a new unit shows no line.
6. An enemy's and an ally's entries, and every entry kind other than a refused Build / queue-full: nothing shown,
   nothing played. In replay playback: nothing shown, nothing played.
7. `ui.json` without `orders.queue_full` fails to load with an error naming the key (a forged copy; look the key up
   before removing it: rule 58); the shipped data loads.
8. BUG-1682: `QaM6VH9FixTest`'s (O) rows print 0 "select an own building behind a drawn enemy building" on every
   seed and zoom it runs; its other counts (own building in front, unit in front, the oracle's agreement) unchanged.
9. `PickResource` runs once per click (a counted row or the code path shown).
10. `git status` is clean after an import and the scene loop; the `.cs.uid` is committed.
11. Two pictures looked at before the report: the "Can't afford" line over a worker at zoom 40 (the default) and at
    zoom 20, 1600 x 900, in the scratchpad, named in the report (the Producer copies them to `studio/review/`).
12. docs as in scope item 8.
13. Build 0 errors / 0 warnings; default loop 0 failed / 22 skipped; smoke PASS; scene loop all green, first pass or
    with the known load-flaky rows named (handoff watch-outs) and green alone.

**Design references.** docs/03 "Implementation (M7-8)", "Request 58: dropped queued orders" and "For the view track
(Request 58)"; docs/02 the order-queue rules (Shift appends, 8 orders a unit); docs/01 2026-11-24 ACCEPT (1)(b), (c)
and 2026-11-25 (4); CLAUDE.md rules 7 and 8.

**Tests required.** `sim/Rts.Sim.Tests/ViewApi/` tests for the accessor; one new Godot scene for criteria 2-7; the
(O) row of `QaM6VH9FixTest` re-read for 8.

**Constraints.** The accessor reads and never writes sim state, the tick or the hash (the ViewApi rule); no gameplay
state in the view (the line's timer is presentation only); player-facing text only from `ui.json`; no allocation per
frame in the steady state; the developer cannot write `studio/**`. The sim track edits `DroppedOrder.cs`'s doc
comment this session: do not touch that file.

**QA focus (standard).** The notice against the sim's truth: fuzz 16 seeds of workers with Shift-queued buildings,
gold drained and sites blocked at random, and check every own entry gives exactly one line with the right text and
every foreign or dead entry none (an oracle of your own over `World.DroppedOrders`). Timing: an entry in the tick a
save is loaded, at game speed 2x and paused (does a line outlive a pause sensibly, do two refusals of one unit a few
ticks apart restart the line rather than stack), a unit refused then selected, a line over a Teblor giant (1.5x) and
at the closest and farthest zoom, in a fogged area (own units are always seen). The click: BUG-1682's fix against the
M6-VH9 oracle on new seeds; a right click on the enemy building in front still attacks it; a click on an enemy
building's site and on a remembered (ghost) enemy building in front of an own one. Suites: the task's tests, the
default loop, the scene loop; run timing-sensitive scene rows after the other tracks' suites have ended when you can
(rule 77).

### Data track: D-H10, the data's hardening: the Teblor opening with the reserve switches, then two check gaps

**Goal.** BUG-1612's open half: since D34 the Easy Teblor computer player researches its upgrade again but rarely
brings Destrier Riders, War-dogs or Shamans. The sim now lets a build-order step be marked `throughReserve` /
`noReserve` (M7-8). Find the order of switches (and, if needed, steps) in the Teblor `standard` opening that meets
the bar on Easy, Normal and Hard, and ship it; if none does, ship nothing and report the table. Then close the two
check gaps D35's QA found (BUG-1691, BUG-1690).

**Files.** `game/data/factions/teblor/ai.json` (the only data file; the `standard` opening's `buildOrder` only: no
other opening, no faction-level knob, no text unless the tooltip becomes untrue), `docs/factions/teblor.md`,
`docs/factions/shadow.md`, `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`. Handed to you by
name for the hash move (rule 51), and only if an order ships: the golden replay's header and checksum with the
reason in `QA/GatherWedgeQaTests`' header comment, `Replays/cross_map_seed1.replay`'s header if it carries the data
hash. Nothing else outside your folders: no C# under `sim/Rts.Sim`, no other sim test.

**Scope (in), in this order.**
1. **Baseline first.** Re-measure the shipped order on today's `main` with D34's full rows (64 Teblor seats a
   difficulty: four maps, seat-swapped, seeds 1-8, to 15:00; `QA/Content/D33TeblorRowQaStressTests` /
   `Content/D34TeblorStepOrderReportTests` are the tools). It must give D34's figures (defeated / 2nd Longhouse /
   Shaman / Breaker / Rider / War-dog / Anointing: Easy 1 / 0 / 16 / 30 / 4 / 27 / 28; Normal 9 / 23 / 15 / 14 /
   22 / 37 / 17; Hard 11 / 23 / 22 / 39 / 33 / 46 / 23). If it does not, stop and report: that is a finding about
   `main`, not a new baseline.
2. **Candidates** (docs/03 "For the data track (M7-8)" has the JSON rows; steps by D35's reading: 21 Destrier
   Rider, 22 War-dog, 24 expand, 27 Shaman x2, 28 research armor_1): **A** `throughReserve` on the Rider, War-dog
   and Shaman steps; **S** on the Shaman step only; **RS** on the Rider and Shaman steps; **AR** = A with the
   Anointing's research step (and the Carver it needs) moved ahead of the three train steps or timed earlier; **AN**
   = A with `noReserve` on the armor_1 research step; plus up to six of your own. **Never `noReserve` on the expand
   step** (BUG-1663: it means "never expand"). Screen on reduced rows (seeds 1-2, labelled as reduced), then run the
   full rows on all three difficulties for every candidate the screen does not rule out.
3. **The bar (docs/01 2026-11-24 ACCEPT (3)(c); full rows, every figure, all three difficulties: rule 75).** Easy:
   Anointing >= 20, Rider >= 20, War-dog >= 40, Shaman >= 25, Breaker >= 20, defeated <= 2. Normal: Anointing >=
   17, Rider >= 22, War-dog >= 37, Shaman >= 20, Breaker >= 12, 2nd Longhouse >= 18, defeated <= 10. Hard:
   Anointing >= 20, Rider >= 40, War-dog >= 46, Shaman >= 22, Breaker >= 35, 2nd Longhouse >= 18, defeated <= 12.
   **The first candidate that meets every figure ships. If none does, nothing ships** (`git diff -- game/` empty),
   the table goes on teblor.md with the nearest candidate and what it misses, and BUG-1612 stays open.
4. **If an order ships (one hash move):** write it in one change; `Content/Fixtures/teblor_standard_dh10.json` and
   the fixture test follow it (the d34 fixture stays); regenerate the golden header and checksum; re-base D34's hash
   tests the way D34 re-based D33's (**no new literal of the shipped hash anywhere: rule 73, read it from the golden
   header**); teblor.md's AI build order section, a dated "The unit mix (BUG-1612, D-H10)" section with the table,
   and the opening's tooltip only if it is no longer true (quote old -> new). The report says "saves and recordings
   from before this session stop loading".
5. **The pin list (rules 71 / 76), one a line with "re-measured: yes / moved to ... / unchanged / cannot move" in
   your report.** Before choosing, apply the candidate on a scratch clone and run the default loop plus every test
   class that seats a Teblor AI (grep `FindFaction("teblor")` and `"teblor"` under `sim/Rts.Sim.Tests`, Stress
   included; list the classes you found). Known: the two golden-header pins
   (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`:
   they read the header and should follow the regeneration), `Content/D34HashMoveTests`, `QA/Content/D34QaTests`
   (`D34Hash`), `QA/Content/D34HashMoveQaTests`, `Content/TeblorOpeningFixtureTests`,
   `QA/Content/TeblorOpeningQaTests`, the D33 tests that read the shipped order, teblor.md's house note, and any
   content class of rule 68's list that seats a Teblor. (`Data/StillStealthLoaderTests` reads the hash and holds no
   literal: checked at this PLAN; the last handoff's note was wrong.) Classes that seat only Malazan / Whirlwind: name them "cannot move: no Teblor seat" and do not run them.
   **A sim-owned class outside your folders that turns red** (the sim's switch tests `Ai/ReserveSwitch*`,
   `Data/ReserveSwitchLoaderTests`, `QA/ReserveSwitchQaTests`, `Stress/ReserveSwitchReportTests`,
   `Stress/M78ReserveSwitchQaStressTests`): do not edit it; name the rows in the report's first lines (the sim's
   hardening makes them tolerant this session; the Producer checks the merged tree).
6. **BUG-1691** (ruling): the page keeps "Passive **Shadowed**"; `PassiveContentTests` learns that a stand-still
   stealth sentence ("Stealthed after <n> s without moving") is the unit's `stillStealth` field, compared to its
   `delay`; un-skip `QA_TheShadowedSentence_LandsGreenOnItsStillStealthRow`.
7. **BUG-1690** (ruling): `Content/UnitHitBonusText` reads a percentage as a hit bonus only when the unit has the
   matching field or the sentence has first-shot or pack words ("other ... within"); other percentages are left to
   the passive and status pins; un-skip `QA_TheWarDogsHamstringSlow_IsNotReadAsAPackBonus`.
8. **BUG-1602's page half, if the budget allows** (say in the first lines if dropped): malazan.md's and
   whirlwind.md's art notes say what is drawn since M6-VH9 (the sand robe, maroon veil, rust outfit: docs/04).

**Out of scope.** Any other faction's files, `game/data/common/**` (the view adds one `ui.json` key this session:
do not touch that file), any stat, cost or text other than the tooltip clause above, the Shadow roster, C# outside
the two content test folders, the hour-long Teblor Stress report classes beyond the pin list.

**Acceptance criteria.**
1. The baseline row reproduces D34's 21 figures exactly (or the task stops with the difference reported).
2. teblor.md carries one table: every candidate's reduced row and every full row, labelled, all seven figures, all
   three difficulties, with the bar's figures beside them and each miss marked.
3. The shipped order meets every figure of the bar on the full rows, **or** `git diff <base>...HEAD -- game/` is
   empty and the report's first line says "none meets the bar" with the nearest candidate named. No `noReserve` on
   an expand step in any shipped file (a content test pins that).
4. If shipped: the data hash old -> new stated; the golden header and checksum regenerated with the reason in the
   `GatherWedgeQaTests` comment; the pin list with a verdict a line; no bound raised, no row skipped to make it
   green; no literal of the new hash in any test (grep in the report); the d34 -> dh10 fixture pair.
5. BUG-1691 and BUG-1690: the two QA rows un-skipped and green; `PagePinReadinessTests` 0 problems;
   `EveryShippedUnitDescription_StatesItsHitBonuses` green; a true Hamstring sentence and a wrong pack percentage
   both judged right (rows).
6. The review table for the owner in the report (step, switch or position, old -> new, one-line why; the seven
   figures old -> new on each difficulty; the tooltip quoted if changed), or "no game data changed".
7. `Content/**` and `QA/Content/**` green in the default loop on your branch.
8. Build 0 errors / 0 warnings; default loop 0 failed, skipped 20 (22 minus the two un-skipped rows) **except** the
   sim-owned rows named under scope item 5's last clause, each listed; smoke PASS.

**Design references (the numbers must match).** docs/01 2026-11-24 ACCEPT row (3) (the bar, the two rulings) and
(1)(e); docs/03 "Implementation (M7-8)" "For the data track" (the fields, what each switch does, the two-seat
table); docs/02 "AI opponent" (build orders, the reserve); docs/factions/teblor.md "AI build order (Normal)",
"Opening 1: `standard`", "Easy's Anointing (BUG-1612, D34)", "Difficulty"; docs/factions/shadow.md (G1's row).

**Tests required.** A report class for the candidates (D34's shape); the fixture test; the "no `noReserve` on an
expand step" content row; the two un-skipped QA rows and the rows of criterion 5; data validation green.

**Constraints.** File order is behaviour in `ai.json` (a step behind a waiting expand step spends only above the
reserve unless switched). Leave a switch out rather than write `false`. One hash move, in one commit. Long runs:
record every PID; say the exact filter you ran for each row in the report (D35's QA lost two hours to a wrong
filter). The developer cannot write `studio/**`.

**QA focus (standard).** Re-run the shipped (or nearest) order's full rows yourself and compare match for match.
**If the developer says "none meets the bar", try at least two orders of your own before agreeing** (rule 75; D34's
QA found one). If one ships: seat-swapped rows and a fifth map or seeds 9-12 (does the bar hold off the measured
rows, or was it fitted?); a 30-minute row on two maps (does the seat still expand and still end its matches:
defeated and 2nd Longhouse at 30:00 against the shipped order); the loader refuses the file with a switch on the
wrong step; saves from the base are refused with the `ui.json` text, new ones load. The pin list: run every class
on it, Stress pin classes included, and grep for any class seating a Teblor that is not on it (rule 74). BUG-1690 /
1691: sentences of your own (a status percentage, "within 6 m" in an on-death sentence, a wrong `delay`). Suites:
the task's filters, the default loop, the Stress classes on the pin list; not the whole Stress category.

**Rulings the next briefs must carry (docs/01, 2026-11-24 ACCEPT row):**
- Sim, the later AI-play session (not the hardening): BUG-1661 first and alone (the hunt resumes from a razed
  find), then BUG-1660 (`ai.minWorkers`, start at 4, measured apart), then 1555, 1554 / 1556 with 1490, 1558; one
  pin move; un-skip `FarExpandProbeQaStressTests.VatharCrossingSeed2_FourSeats_*` with the cure.
- Sim, the hardening: BUG-1662 item 1 (`DroppedOrder`'s doc and docs/03 "For the view track" say the unit may
  have died later in the tick), BUG-1620, BUG-1582, BUG-1539, the 1,200 s end-game row, and whatever older S3 / S4
  in STATE's sim backlog changes no AI play.
- Data: the bar for the Teblor opening is docs/01 2026-11-24 ACCEPT (3)(c), every figure on Easy, Normal and Hard
  (rule 75). Variants S / A / C; `noReserve` on the expand step is "never expand" (BUG-1663) and is not a
  candidate without 30-minute rows. Known from the sim's two-seat sample: order A costs Easy the Anointing; the
  cure is probably which steps carry `throughReserve` (try the Shaman step alone, or the Anointing's research
  step moved ahead). QA tries an order of its own before agreeing that none meets the bar.
  BUG-1691: the passive pin learns the `stillStealth` sentence, the page keeps "Shadowed". BUG-1690: a percentage
  is a hit bonus only when the unit has the field or the sentence has first-shot / pack words.
- View: the five answers are "accept" (1, 2, 5), "leave to the owner's packs" (3), "owner's taste, listed" (4).
  A see-through outline for hidden units is on the queue only on the owner's word.

## Where things stand

- **`main` after this session = sim bdc5d11 + view 68cc9ef + data 2811489** (plus this ACCEPT's studio and docs
  files). The Producer's scratch clone `prod1431-accept`: build `--no-incremental` 0 / 0, default loop **10,203 passed / 0 failed / 22 skipped of 10,225 in 21 m 57 s**,
  smoke **PASS**, scene loop **122 of 122, first pass**. Merge conflicts: `studio/qa/coverage.md` and
  `studio/bugs/README.md` only (keep both sides); docs/03 auto-merges.
- **The data hash did not move: A732F387C2BB7E4D.** Saves and recordings keep loading. Formats unchanged (save 10,
  replay 9 / 6).
- **Reserve switches (M7-8):** `ai.json` `buildOrder` step fields `throughReserve` (a `train` step only) and
  `noReserve` (`build` / `research` / `expand` only); on the wrong action a load error, even at `false`;
  `GameData.ContentHash` adds a marker and two bits only when one is `true`. `BuildOrderExecutor.Run` is the one
  place that reads them. No shipped opening carries one. docs/03 "Implementation (M7-8)" and "For the data track
  (M7-8)" (the JSON rows for the Teblor file; the report table is two data points a cell).
- **Dropped queued orders (M7-8, Request 58's sim half):** `World.DroppedOrders` (`Orders/DroppedOrder.cs`:
  `Unit, Owner, Kind, Placement, QueueFull`), emptied at the start of every tick, not hashed, not saved, every
  order kind. The handle may be dead when read (BUG-1662). No `ViewApi` accessor yet.
- **The Vathar stall (BUG-1621 / BUG-1610), traced:** seat 2 is left with one worker and nothing trains a second
  (BUG-1660); seat 3's hunt forgets its sweep after each razed find (BUG-1661). Not the gold-gone family.
  `Stress/VatharStallTraceTests` prints the table (end 88,998, hash 06F14EE840630135).
- **Picking (M6-VH9):** `BuildingPicker.PickRay` / `PickGhostRay` have overloads with per-type drawn heights;
  `SelectionController.BuildingOnRay` / `GhostOnRay` (`DrawnOnRay`) walk the pick boxes along the ray and return
  the first drawn shape (a model at its triangles, `BuildingViews.ModelRayDistance`); a hit only in a model's box
  air comes back at `AirDistance`. `HiddenByBuilding` keeps a unit behind a model from being click-picked. A scene
  that clicks near a model must aim at a pixel that is really open ground (`ReplayPlaybackTest` is the model).
- **Figures (M6-VH9):** the Priest's robe `#a39577`, veil `#5e1a20`; the Zealot's outfit `#6e3a26`, head-wrap
  `#5a1c1e`; a manifest model may carry `"oneShot": { "attack": 0.28 }` (the Sapper), written into the `.glb`'s
  extras; `UnitViews.OneShotClipTime` places the clip from the sim's windup / cooldown counters. Overlay lines =
  max(highest bone + 0.18, played props' top + 0.10) x scale. The art pass's binaries 38,856,086 bytes.
  **122 scenes** (`ViewHardenVh9Test`, QA's `QaM6VH9Test`, `QaM6VH9FixTest`; the last has no `.cs.uid` yet: an
  import writes an untracked one).
  `DebugOverlayTest`, `QaM25Test`, `MinimapTest` take the best of ten runs. Bench alone (the Producer, merged tree): 400 figures + 60 building
  models 6.24 ms (M6-V26: 7.00), 400 capsules 0.79, 1,000 figures 16.36 (17.57).
- **Shadow page and checks (D35):** shadow.md lists G1-G3 as built; `Content/UnitHitBonusText`,
  `HitBonusPagePin`, `HitBonusPinTests`; `PassiveContentTests` knows `nearbyOwn`. Request 56 G4-G8 open.
- **The Teblor `standard` (unchanged since D34):** 64-seat rows (defeated / 2nd Longhouse / Shaman / Breaker /
  Rider / War-dog / Anointing): Easy 1 / 0 / 16 / 30 / 4 / 27 / 28; Normal 9 / 23 / 15 / 14 / 22 / 37 / 17; Hard
  11 / 23 / 22 / 39 / 33 / 46 / 23. Steps (D35's reading): 21 Destrier Rider, 22 War-dog, 24 expand, 27 Shaman x2,
  28 research armor_1.
- **Pins:** unchanged this session (no pin class, fixture or golden in any diff).
- **Skipped / known rows:** the default loop's skips are the standing 20 plus QA's two D35 rows. Stress: the
  BUG-1621 row, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, the five `KeepCornerCureQaStressTests`
  rows skipped.
- **Requests:** 55 done; 58 sim half done, view half next; 56: G1-G3 built, G4-G8 open.
- **Scratch clones left in this session's scratchpad** (harmless; the owner's housekeeping list): `prod1431-plan`,
  `prod1431-accept` (the Producer's), `m78-base` (holds an uncommitted scratch test file), `m78-qa*`, `m6vh9-*`,
  `d35-qa`. The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout.
- **Usage at the ACCEPT:** weekly 9 % / 9 % (stop at 95 %; the owner lifted the 60 % hold on 2026-11-24).

## Standing rules (1-77)


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
**56:** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
**57:** a QA regression scene or test that is red by an open S3 reaches `main` only by an explicit ruling at ACCEPT
(named in the session log and STATE's Tests row), with the fix as the first item of that track's next brief; until then
every loop's expected result names that one row. Two sessions red = the fix becomes the whole task.
**58:** a test helper that forges a data row must tolerate the shipped data already carrying it (look the id up,
then add or edit), and when docs/03 tells the data track which rows to write, the sim's QA applies them on a scratch
clone and runs the task's classes (BUG-1280).
**59:** a developer who writes a fix for "the data track's rows break our tests" runs its own new tests on a copy
carrying those rows before reporting (M7-6's fix broke on exactly the rows it was written for: BUG-1310 / 1311, one fix
round). A sim test about "every shipped row" reads the count from the shipped file, never a literal.
**60 (lifted 2026-11-13-2306):** BUG-1321 is closed for 400 figures (10.1 ms alone); model types may be imported again.
**61:** before more than ten unit types are models, a view task measures a 1,000-figure crowd of mixed types alone
(rule 42) and reports what 16.7 ms would take (merged surfaces, a shared skeleton or baked animation); every art brief
reports the 400- and 1,000-figure bench with the capsule figure from the same window and reads the "while 90%+ alive"
column. An LFS size criterion names "the art pass's binaries", never "total LFS".
**62:** a developer runs the full default loop after its last edit, or its report says in its first lines that
it did not (two sessions running a developer fixed one row and re-ran only the affected classes).
**63:** a scratch folder's name starts with its track and task (`m7v4-...`, `d31-...`), is created new, and no
agent writes into, cleans or resets a folder it did not create (2026-11-14-0616: the view developer reset another
agent's clone named `clone`; nothing committed was lost). A brief that asks for a scratch clone says so.
**64:** "build 0 / 0" in a report means `dotnet build RtsGame.sln --no-incremental` after the last edit (an
incremental build hid two warnings in a new test file: BUG-1380, one fix round).
**65 (new):** QA's re-check ends with `dotnet build RtsGame.sln --no-incremental` on the commit that holds QA's own
files, and its report says so (2026-11-14-1246: QA's new `QaM6V21Test` carried a CS8600 warning that the developer's
0 / 0 could not have seen: BUG-1412). A warning in a QA file with the developer's commit clean is S3, fixed first in
that track's next task; in the developer's own files it stays S2 (BUG-1380).
**61 (amended 2026-11-14-1246):** the measurement is done (M6-V21: 1,000 mixed figures 26.9 ms alone; merged surfaces
are worth about 4-5 ms, a shared skeleton or baked animation up to about 8 ms, both together about 14-15 ms). The owner
asked for the Teblor figures first (inbox 2026-11-14), so the checkpoint moves: **merged surfaces are the view task
right after the Teblor humanoids**, and every art brief still reports the 400- and 1,000-figure bench alone.
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.
(`SameGameDataHashes` is the name of the rule in that class's comments, not a test method: do not write it as a
criterion's test name again.)
**66 (new, 2026-11-14-2156):** at PLAN every invariant criterion ("no worker ...", "every pair ...", "within N s") is
checked two ways before it is written as pass / fail: **does it hold today with the feature off** (if not, or if the
out-of-scope list forbids what would make it hold, it is written "measure and report, against off": rule 48), and
**can it hold together with every other criterion** (M6-S9: criterion 5's Stop clause against criterion 4, criterion
7's 60 s window against "off is identical"; both cost a fix round and an ACCEPT by ruling). A criterion QA shows to be
the brief's error is corrected in the session log by name, never passed silently.
**67 (new):** a developer's open questions for the Producer are written where QA can read them (a "Questions for the
Producer" section of the page or docs section the task owns, each with the recommendation); the Producer's answers
go to docs/01 and the next task writes them in (D-R38: Q1-Q11 lived only in the returned report).
**61 (amended again 2026-11-14-2156):** the order after the Teblor humanoids is the owner's Shift-queue request, the
view's hardening (4 / 4), then merged surfaces; 400 mixed figures of eleven types are at 11.1-11.4 ms alone (worst 1 % 15.4-15.7).
**68 (new, 2026-11-15-0506):** a brief never writes "the default loop green" beside rows it forbids the developer to
fix. When a task re-prints cells that another track's prose pins read, the brief hands those pins and sentences over
by name, or writes the criterion as "red only in these named rows, ruled at ACCEPT". At PLAN the rehearsal's red list
is sorted into "re-pin", "re-stage" and "another track's" before the criteria are written (M6-S10: criterion 14
against 10 and 12; five default-loop rows reached `main` red by ruling). A fix round with nothing to act on but a
Producer ruling may be skipped by the conductor; the session log says so.
**69 (new, 2026-11-15-1536):** a developer or QA agent ends only the processes it started itself, by the process ids it
recorded when it started them; never by matching command lines or names (M6-S11's developer force-stopped 34 processes
whose command line matched "Total tests" / "Passed!|Failed!", some possibly another track's; nothing was lost, by luck).
A brief that runs long suites says "record the PID of every `dotnet test` / Godot you start".
**70 (new, 2026-11-15-2136):** when a QA FAIL leaves nothing a developer can act on without a Producer ruling, the
conductor calls the Producer, who answers with rulings and a fix brief instead of a verdict; the last fix round applies
them (tests and docs only when possible, so no Stress re-run is needed). `main` takes no red default-loop row by ruling:
a row the ruling cannot make green is skipped with its bug id in the repo's `Skip = "BUG-..."` form, its cure first in
the next brief. An owner bug that changes gather or AI behaviour moves every AI pin through divergence (BUG-1520 did,
alone): plan it only where a pin move is planned, and tell the developer to measure its effect apart from the rest.

**68 (amended 2026-11-16-2036, BUG-1552):** the content classes handed to a sim developer whose task moves Normal AI
play while the data track is stopped are seven: `Content/AiOpeningPairsReportTests`,
`Content/HolyWarFirstWaveReportTests`, `Content/AiSpendingReportTests`, `QA/Content/DR35ProsePinQaTests`,
`QA/Content/TeblorOpeningQaTests`, `QA/Content/D21OpeningsQaTests`, `QA/Content/D20PinQaTests`, with the pages' cells
they read (Age II tables, "Houses at the cap", "Spending on the menu's map", the holy war's first wave, the Teblor
opponent sentence).
**71 (new, 2026-11-16-2036):** a pin-move brief lists the pin classes one a line, and the developer's report carries
the same list with "re-measured: yes / moved to ... / unchanged" beside each, Stress pin classes included (M6-S12:
the four-seat FFA pin was on the list and never run: BUG-1559, S2, one fix round). Such a task gets 20 bug ids
(M6-S12 used all ten). When rule 45 moves a row onto a seed another row already plays, the report says so.
**72 (new, 2026-11-17-0816):** after a fix round that changed tests or docs only, QA's re-check runs the touched
classes, the default loop and the `--no-incremental` build; it does not re-run Stress or Perf (M8-2's re-check spent
4 h 46 m on a full loop for three test files). Rule 70's "no Stress re-run" holds for every tests-only round.
**73 (new, 2026-11-17-0816):** no new test pins the shipped data hash, or the bytes / digest of a save made on
shipped data, as a literal (rule 49 widened: BUG-1580, S2, one fix round). The hash is read from the golden header;
"byte for byte what `main` writes" is proven in-session against the base and kept as a differential row
(`StillStealthSaveBytesTests` is the model). A brief never states the shipped hash as a pass / fail literal in a
session where another track moves it.


**74 (new, 2026-11-18-0926):** "the Stress category has no expected red row" is said only after the Stress
category ran on the merged tree. `FarExpandProbeQaStressTests` was not on rule 71's list and went red with D33
unseen for a session (BUG-1621). Rule 71's list gains `Stress: FarExpandProbeQaStressTests`, and at PLAN the list
is checked against a grep for every test class that plays a full AI match.
**75 (new):** a bar in a data brief ("ship only if ...") names every figure the bug is about on every
difficulty it is about. D34's bar guarded Easy's upgrade and Normal's / Hard's unit kinds, so the order that met it
cost Easy its Riders (36 -> 4) with the bar green. And when a developer reports "none meets the bar", QA tries
an order of its own before agreeing (it found one: BUG-1641).
**76 (new):** a pin-move list is cut to what can move. A change to one faction's `ai.json` lists the classes that
seat that faction plus the hash pins and the golden's header; the other classes are named as "cannot move: no
seat of this faction" and are not run (D34 ran nineteen classes that seat only factions 0 and 1).


**77 (new, 2026-11-24-1431):** an art or view brief's frame-time criterion is judged on a quiet window. When the
developer's and QA's bench both ran beside another track's test process (M6-VH9: capsules x1.45-2.0 slower than
the last session's, so every figure row was "within the window's factor"), the Producer runs the pinned rows alone
on the merged tree at the ACCEPT and the log carries that figure; a brief tells the view's QA to run the bench
after the other tracks' suites have ended when it can. And a criterion of the form "only these files change" is
checked at PLAN against every other criterion that asks for a new mark in a file (M6-VH9's 11 against 6: rule 66).


## Watch-outs (all tracks)
- **New this session: a click is tested against what is drawn.** `SelectionController.BuildingOnRay` /
  `GhostOnRay` replace the box-only picks in `EnemyAt`, `ContextTarget`, `PickResource`, `PickBuilding`; a unit
  wholly behind a model is not click-picked (`HiddenByBuilding`). A scene that clicks beside a Keep or Barracks
  model must pick a pixel on open ground or select the unit directly (`M3PlayableTest`'s seed-6 builder,
  `ReplayPlaybackTest`). Older scenes that pinned the 3 m box were adapted: `BuildingScaleViewTest`, `QaM7V4Test`,
  `QaV3Test`.
- **New this session: `ai.json` steps may carry `throughReserve` / `noReserve`** (no shipped step does). A test
  that forges the Teblor `standard` (`Ai/TeblorSwitchOrders`, `Stress/ReserveSwitchReportTests`, QA's
  `M78ReserveSwitchQaStressTests`) reads the shipped file: when the data track ships switches there, those
  helpers must tolerate them (rule 58). `noReserve` on an expand step = never expands (BUG-1663, by ruling).
- **New this session: `World.DroppedOrders`** is output like `Deaths`: emptied each tick, not hashed, not saved;
  an entry's unit may be dead when read. `SaveRestoreQaTests` lists `_droppedOrders` as not saved.
- **Session length:** 1431 ran about 10 hours with no pin move (sim QA full: default loop 2 h 56 m, Stress
  3 h 35 m under load; the data's QA first ran the wrong filter and lost 2 h: say the filter in a light brief).
- **New this session: a unit type may carry `attack.fromStealthBonus` and a `nearbyOwn` passive.** No shipped
  unit does. A test that forges them uses `Vision/HitBonusScenes` (rule 58). With either anywhere in the data,
  `World.HitBonusInData` is true and every hit by a field type reads `HitFactor.Of` (one status scan; for a pack
  type one grid query into `World.ShoveNeighbors`, so never call it inside movement's phases 8-9).
  `PassiveLoaderTests` pins four triggers; `DataContentHashTests` pins `UnitPassive`'s nine properties.
- **New this session: two building types are models.** A scene that reads a finished Garrison Keep's or
  Barracks' box mesh, material or bar height sets `BuildingViews.UseModels = false` before binding or reads
  `ModelOf` / `BarY` (`BuildingScaleViewTest` and `QaM7V4Test` are the models). Picking follows the drawn shape since M6-VH9
  (BUG-1630). A building's model is found by its id under `game/assets/models/<faction>/<id>/`; the data has no
  `model` field for buildings. `QaM6V26Test` passes with its known-bug rows printed; `--strict` makes them fail.
- **New this session: the Teblor `standard` has the Carver and the Anointing at steps 19 / 20 (10:00).** In
  `ai.json`, research and build steps ignore the reserve while train steps obey it, and a step behind a waiting
  expand step spends only above the reserve (Request 55): **file order is behaviour**, and it changes with the
  sim's next task.
- **The data hash moved (A732F387C2BB7E4D, D34):** every save and recording fixture from before is refused. No
  hash move is planned next session (the sim's AI follow-up moves pins, not the hash, unless it adds an `ai`
  knob to `game/data/common/**`: then it is the session's one hash move and the data track ships no data).
- **Session length:** 0926 ran about 21 hours. The sim developer's last full loop took 6 h 34 m beside the other
  tracks; the view's and data's QA were done hours before the sim's. Perf was evidence this time (214 / 0 / 3
  alone).
- **New this session: a unit type may carry `stillStealth`.** No shipped unit does. A test that forges it uses
  `Vision/StillScenes` (rule 58). With the field anywhere in the data, every world allocates `UnitStore.Still`, the
  save gains a `STIL` part and a held unit's Stealthed entry shows `TicksRemaining` = timed part +
  `StatusSystem.Holders`: a test that reads a Stealthed entry's ticks on forest or stand-still data must subtract the
  holders.
- **New this session: the AI's openings changed (D33).** `cadre_rush` waves at 16 pop, `holy_war` at 14, the Teblor
  `standard` has 29 steps and trains a Breaker, a Rider and a War-dog pack before its expand step. In `ai.json` a
  step after a waiting expand step spends only above the expand's reserve (Request 55): **file order is behaviour**.
  The FFA criterion row is Vathar seed 1; the retrain twin is Raraku seed 5.
- **`ModelViewTest` pins the capsule types as an exact set of six** (the criterion's own list). A new unit type
  without a model (the Shadow roster, a test faction on shipped data) turns it red: the task that lands one hands the
  view that list (rules 54 / 55 rehearsal).
- **Session length:** 0816 ran about 26 hours (three default loops of 1-2.7 h, two Stress runs of 2.6 h and 4.8 h,
  the data's pin runs; QA started per track as builds finished). Rule 72 removes the second Stress run. Perf was
  evidence only in the first inspection (the re-check hit the PC's slow state; base failed the same rows).
- **Smoke can fail once on stale import state** (`Can't find file 'uid://...'` / `Unrecognized UID` lines in a
  worktree's `.godot` cache after another branch's assets were merged): the re-run passes. Two developers saw it this
  session; it is not a finding unless the second run fails.
- **Pinned matches after D33** are listed under "Where things stand"; the older "Pinned matches on `main` after
  M6-S12" bullet below is right except for those rows.
- **The default loop is green on `main`**; the Stress category has no expected red row since 1431 (the BUG-1621 row is skipped with its ids).
  `main` builds **0 errors / 0 warnings**.
- **New this session: the AI's rally point may differ from `hall + rallyDistance`.** A test that stages an AI seat
  and expects its army at the nominal point must keep one-cell lanes (between its own buildings too) more than
  `ai.rallyReach` away, or read `Board.Rally`. A rebalance now goes to the node nearest the worker's nearest finished
  drop-off; a blind seat (phase 2, short of a resource, no live node known, an idle worker) sends a scout on the
  first think of an `ai.scoutAfter` window: a staged seat that must sit still needs a known node of both kinds or no
  idle worker.
- **A construction site is not a drop-off** (M6-SH2). A test or scene that places a drop-off building and expects
  deliveries there must finish it first; a loaded worker with only a site standing waits in state `Returning`. The AI
  retrains a worker when it has none: a test that keeps a seat worker-less must also keep it without the gold
  (`HuntTests.TheRingLeg_*` is the model).
- **Unit models are one surface now.** A scene that pinned a surface count or a team surface index reads
  `UnitModels.AtlasOf` / the one `team` material (`ModelViewTest`, `TeblorModelTest`, `QaM7V5Test`,
  `OverlayLineTest` were updated). A new model type goes through `tools/art/build_unit.py` `build_atlas`; the role
  PNGs no longer exist. Team texels are lit Burley (1-4 levels brighter than before M6-V24): a pixel pin from
  before is stale.
- **Session length:** 2036 ran about 11 hours (Stress 4 h 17 m beside the default loop; the view's default loop
  2 h 43 m under that load). The Perf category was not evidence (the PC's slow state: `ASweepRetarget` 78 us).
- **Session length:** 2136 ran about 22 hours (the Stress category 3.8 h and 4.0 h beside the default loop; the
  default loop 43 m to 2.5 h under load). M6-S12 is QA full with a pin move again: see "At the PLAN" item 4.
- **New this session: a gather loop ends for a queue** (`EconomySystem.EndsForQueue`, only when `QueueCount > 0`). A
  test that parks a worker on a loop with a queued order must put it in reach of its node with a drop-off standing
  (`ViewApi/OrderPathTests.QueuesBehind_*` is the model), or the queue starts at once. The AI never queues.
- **`EndGameHardeningTests.PaleHillsSeed2_FourSeats_*_TwoProcesses_*` has a 1,200 s wall limit** and failed under a
  contended Stress run (3 m 11 s alone): a Stress red there counts only when it fails alone.
- **Scratch clones left behind (permission prompt denied):** `qa1536\m6vh8-qa-clone` and `m6s11-qa-clone` in session
  e9f70028's scratchpad; harmless, on the owner's housekeeping list.
- **`gatherOverlap` 0.5 is in the shipped `rules.json`.** A test that means "the rule off" removes the field from a
  copy (`GatherOverlapScenes` writes or removes it itself; rule 58); "the base's data" in a fixture means the field
  taken out (`D32PreFixtureTests`). Any value above 0 turns the gather floor on.
- **Session length:** 0506 ran about 9.5 hours (the sim's QA full: default loop 1 h 42 m and Stress 2 h 3 m beside
  the other tracks). Keep the data task light next session and tell the view's QA to run timing rows alone.
- **The view's scene loop is 122 scenes** (`ViewHardenVh9Test`, QA's `QaM6VH9Test` and `QaM6VH9FixTest` are new). `BenchTest` (7 s),
  `MinimapTest` (0.5 ms), `DebugOverlayTest`, `QaM25Test`, `QaM27Test` and `QaH1Test` flake under load and pass alone.
- **`ViewApi` source may not name `World` or `UnitStore`** (`TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls`):
  `OrderPath` is built from the store's arrays, handed in by the view (`OrderPathView.NewReader`).
- **Fifteen unit types are models** (the eleven before plus Cadre Mage, Sapper, Priest, Zealot); six are capsules
  (`ModelViewTest` prints "15 models, 6 capsules" and names them: Wickan Lancer, Horse Raider, Catapult, Battering
  Ram, Destrier Rider, War-dog). A count of model types is read from `tools/asset-manifest.json`, never a literal. A
  model file holds the 1.84 m base figure; the data `scale` is applied by the view. Bars and marks sit over the figure
  as drawn (`UnitViews.OverlayLines`); robes, veils and satchels are rigid boxes on a bone (a leg shows through).
- **Session length:** 2026-11-14-2156 ran about 7 hours (builders and QA about 6; one sim fix round; the lock was
  refreshed four times). QA full on the sim beside a standard and a light pass still gave default loops of 1 h 4 m
  to 1 h 15 m against 22 m alone. Next session the sim is on QA full again with pin re-measures: keep the data task
  light and tell the view's QA to run its bench last.
- **The third faction's order of work now:** M6-S10 (the wedge + the gather value; every opening measurement is
  redone after it) -> data **D33** (BUG-1420) as the start of the balance pass -> the sim reads the Frenzy report
  again (Request 53 part two). The Teblor humanoids are done; the Rider and the dog wait on the owner's packs. M7
  criterion 4 is ticked after the balance pass; the end-of-M7 hardening follows it.
- **Three factions ship now.** A fixture that means "the two original factions" uses `TestSim.TwoFactionData`,
  `TestDataDir.CopyOfShippedTwoFactions()` / `CutToFirstFactions(2)` (sim) or `ThirdFactionForge` (view, cut to two).
  A pin about "every shipped unit" must be true for the Teblor too (a bite, a pack, a scale, a self-cast aura, radius
  0.75, regeneration).
- **Radius 0.75 is a fourth radius class** (+131,072 bytes on a 1024 map; the shipped-data memory row allows it by
  class count). A single-type 0.75 m crowd arrives about 40% less often than a 0.4 m one on the crowd sweep's maps
  (`Stress/WideCrowdStressTests`, a report with no bound): 500 to 4 points 172.6 v 288.1 mean arrivals, 128 to 64 goals
  58.4 v 112.1. If a sim task changes movement, re-run it and print both columns.
- **The scene loop is all green: 111 scenes** (`OverlayLineTest` and QA's `QaM6VH8Test` are new; `QaM6V21Shots` and `ModelShots` are windowed tools, not in the loop). `QaM6V20Test`
  samples after a forced collection (objects flat at 3,252); BUG-1350 is fixed by cause, so an ERROR line there is a
  finding. Rows that flake under load are listed below; any other red row is a finding.
- **Model animation is manual:** every model's `AnimationPlayer` is in manual mode and `UnitViews._Process` advances it
  through `CrowdAnimator` (48 nearest every frame, the rest 1 frame in 3). A scene that calls `Sync` directly and wants
  motion calls `AdvanceAnimations(delta, eye)` too. A model shows no worker tint (`ShownTint` still records the state).
  Since M6-V21 the figures follow the game speed (`UnitViews.AnimationRate`: the runner's speed, 0 while the tree is
  paused); a replay that has reached its end is not paused, so its figures keep moving in place (a small view item).
  Each model type has its own `AnimationLibrary` (state names -> the shared clips, from the clip map in the `.glb`'s
  glTF extras); a slot's type and model are set before the fog check.
- **Unit models are on by default** (`UnitViews.UseModels`; `--placeholder-art` forces capsules). Five scenes pin capsules
  and set it false: `EconomyViewTest`, `QaM6V3Test`, `QaM7V3cTest`, `ScaleViewTest`, `ShellTest`. A new scene that reads a
  slot node's own mesh or material must do the same or read `ModelMeshOf`. The model files are Git LFS: a clone needs
  `git lfs pull` then an import (a pointer file gives the capsule and one warning, never an `ERROR`).
- **`tools/import-assets.ps1` needs Blender 5.2 and `asset-sources/` (the main checkout's)**; its outputs are committed and
  a re-run is byte-identical (QA, fresh clone, 180 s). `tools/asset-manifest.json`, `tools/import-assets.ps1` and
  `tools/art/**` are the view track's from now on (the rest of `tools/**` stays the sim's): say so in each brief.
- **The Teblor rows are on `main`.** A Frenzy status id sorts after `stealthed`, and `teblor_frenzied` is the last row
  of `common/statuses.json`: a brief that adds a status says where it sorts (`StatusLoaderTests.Shipped_Regenerating_*`
  pins the sorted order on purpose). The two golden data-hash pins
  (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`)
  are sim-owned: a data brief that moves the hash is handed them by name (rule 51).
- **Two tracks must not both move the data hash in one session** (each regenerates the golden's header and checksum:
  a certain conflict). A sim task that ships a value in `game/data/common/**` and a data task that ships a faction
  file are planned in different sessions, or one of them lands test data only.
- **Every data change moves the golden header now** (the Teblor use `damageBonus`, `onEnd`, `statusDuration`, a
  building `scale`, `carry` and a per-opening share): batch data changes into one hash move a session, and say in the
  report that older saves and recordings stop loading.
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

- **Pinned matches on `main` after M6-S12 (every one re-measured once, old values in "was" comments; docs/03
  "Implementation (M6-S12)" item 6 lists old -> new):** the Hard v Normal tally **34 / 7 / 1** (Raraku 13 / 1 / 0,
  Vathar 12 / 2 / 0, Pale Hills 9 / 4 / 1); `MatchCriterionTests` (Vathar **seed 1** 23,244 / Raraku seed 3 27,394 /
  Pale Hills seed 1 33,230 / generated **seed 1** 21,876 / FFA 38,690 / 2v2 61,686; `HalfWay` 11,622);
  `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, the eight `CounterCompositionTests.TheNonHardPins_*`,
  `ReserveTwinTests`, `SharedVisionFfaPinTests`, `OutcomePinM73bQaTests`, the premap AI replay, `save_ai_seed2`,
  `CliTests.Match_TheLiteralRow_*`; unchanged: `HardKnobsPinTests`, `seed21_playable.replay`, the golden. Stress:
  `AiDeterminismTests` (`HuntSeed` 2: ends 51,218, 2 hunts; its comments still carry M6-SH2's figures),
  `HuntStressTests` (`MidHuntSeed` 2; **the four-seat FFA oracle row seed 2**, 59,951, the same match as
  `FourAiSeats_OnPaleHills_Seed2_KnownStall`: drop that twin at the next pin move), `M6Sh2DeterminismStressTests`
  (the retrain twin on **Raraku seed 3**), `M6Sh2QaStressTests` (the save sweep on **Raraku seed 2**),
  `StarvedSaveTwoProcessQaStressTests` (Raraku seed 1), `ReservePairingsStressTests` (`KnownNoAgeTwo` (2, 0, 1, 1)),
  `MatchRowsM6S10Report` (seeds 1-8 on the three maps: all end). The next sim task that changes Normal AI behaviour
  or gather movement re-measures all of them (rules 43 / 45 / 71).
- **Known stalls / skips with ids on `main`:** the four-seat Pale Hills FFA seed 1 (BUG-1068 / 1116 / 1559: the
  map's gold is gone by 30,000), seeds 3 and 4 (BUG-1536, the owner's question), seeds 6 and 7 (no end by 144,000,
  measured in M6-S12's fix round, same shape); the five `KeepCornerCureQaStressTests` rows (BUG-1463);
  `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`
  (BUG-1156), BUG-1161's three rows, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's `QaM7V1Test`
  zoom-40 1.5 head row; `DebugOverlayTest` and `QaM25Test` 1 ms rows flake under load.
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.

---

## Sim track

### Where we are
M7-8 on `main`. Counter **4 / 4: the hardening is next.** New debt: BUG-1660, BUG-1661 (S3; the two causes behind
BUG-1621 / BUG-1610, which stay open with their Stress row skipped), BUG-1662 (S4). BUG-1663 closed by ruling.
Carried: BUG-1620, 1582, 1555, 1554 / 1556, 1557 (S3), 1558 (S4), 1463, 1536 (the owner's question), 1537, 1539,
1490, 1434, 1433, 1161, 1149, 1152, 1150, 1010. The Producer's answers to M7-8's Questions 1-4 are in docs/01
(2026-11-24, the ACCEPT row (1)): keep, keep, not saved, BUG-1661 first and alone.

### Next task candidates
1. **The sim's hardening (QA full; no AI-play change, no pin move, no hash move, no `game/data/**`):** BUG-1662
   (the two doc lines: an entry's unit may be dead when read; the switch report says it measures two seats),
   BUG-1620 (round the pack's factor from an exact sum, or state the limit and drop the row), BUG-1582
   (`RampWallQaTests`' 10 s wall bound under load), BUG-1539, `EndGameHardeningTests`' 1,200 s child limit,
   BUG-1557 if it changes no AI play, the stale comments in `AiDeterminismTests` / `HuntStressTests` (comments
   only). Check `Ai/TeblorSwitchOrders` and the two report classes on a copy of the data that already carries
   order A (rule 58: the data track ships switches the same session). Write M7-8's answers into docs/03 (rule 67).
2. **The AI-play session (feature, QA full, one pin move; 20 bug ids; the data track measures no opening beside
   it):** BUG-1661, then BUG-1660, BUG-1555, BUG-1554 / 1556 with BUG-1490 (a save format change: rule 50),
   BUG-1558. Each cure measured apart (rule 70).
3. **M8-4:** Request 56 G4 (the Hounds' 7-alive limit) and the next of G5-G8, on test data only.
4. Requests 54, 53 part two. A movement task for BUG-1463 only with a design first.

---

## View track

### Where we are
M6-VH9 on `main`. Counter **0 / 4.** 122 scenes. Closed: BUG-1630, 1601, 1631, 1632, 1381, 1680, 1681; BUG-1600
but for item 4 (measured, left); BUG-1602's docs/04 half. New debt: BUG-1682 (S4). The Producer's answers to
Questions 1-5 are in docs/01 (2026-11-24, the ACCEPT row (2)).

### Next task candidates
1. **M6-V27, Request 58's view half (feature, QA standard):** a read-only `ViewApi` accessor over
   `World.DroppedOrders`; for an own unit's dropped Build the Refused sound and the `ui.json` `placement` text over
   the unit (the keys exist: `cannot_afford`, `blocked`, `unit_in_the_way`, ...); a full queue's line needs a text
   key (if `ui.json` has no place for it, show nothing for `QueueFull` and write the Request); the entry's unit may
   be dead; nothing for other order kinds yet. With it: BUG-1682, `PickResource` once a click, commit
   `game/tests/QaM6VH9FixTest.cs.uid`, write M6-VH9's answers into docs/03 (rule 67).
2. **M6-V28, more building models** (the other eight Malazan buildings; about 1,900 lines with its scene; the
   bench alone: rule 77).
3. The two cavalry, the Destrier Rider, the War-dog, the Catapult and the Ram once packs are on disk (owner).
4. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...", "priest veil: ...",
   "buildings ok", "whirlwind colours ok" / "priest: white robe", "outline hidden units").

---

## Data track

### Where we are
D35 on `main`. Counter **4 / 4: the hardening is next.** New debt: BUG-1690, BUG-1691 (S3; two QA rows skipped
with the ids). Open: BUG-1612 (its unit-mix half; the switches are on `main` now), BUG-1421 item 1, BUG-1611 (S4),
BUG-1602's page half. The Producer's rulings and the Teblor bar are in docs/01 (2026-11-24, the ACCEPT row (3)).

### Next task candidates
1. **D-H10, the data's hardening (QA standard; one hash move if an order ships; files:
   `game/data/factions/teblor/ai.json`, `docs/factions/teblor.md`, `docs/factions/shadow.md`, the two content
   test folders):** (0) the owner's answers, if any. (1) BUG-1612's open half: the Teblor `standard` with the
   switches against the bar, D34's shape; variants S / A / C and others; never `noReserve` on the expand step
   without 30-minute rows; if none meets the bar, ship nothing and report the table. The pins of a Teblor change:
   the nine sim QA listed (handoff top, item 6). (2) BUG-1691, BUG-1690 by the rulings. (3) BUG-1602's page half
   (the art notes say what is drawn) if the budget allows.
2. The hour-long Teblor Stress report classes D34 did not re-run, Q2 on seat-swapped rows, malazan.md's "Age II
   against the Whirlwind" paragraph.
3. The Shadow roster as Request 56's schemas reach `main` (G1-G3 are there; G4-G8 are not); the "Orders full"
   text once its key exists.
