# Handoff: brief for the next session

Written by the Producer at the ACCEPT step of session **2026-11-09-1336** (session 2 / 8 of 2026-11-09; sim M5-H6 + data D26a,
view STOP at PLAN). **Both tracks ACCEPTED.** Sim **M5-H6, the end-of-M5 sim hardening**, after one fix round (BUG-1162) and two
Producer rulings on brief conflicts (BUG-1163 waived → S3, the data track's Hard tuning item; BUG-1164 closed): the house rule is
in, the hunt looks at the free start locations from its second hunt on, the far leg is capped and bounded; Pale Hills 2 / 5 un-skipped
at 49,565 / 55,485, seed 1 50,140, the literal row 23,322, the tally 29 / 11 / 2; golden `30C51BB619A7E650` unchanged, formats 8 / 9
unchanged. Data **D26a** (the pages' "How the AI adapts" + the pins + BUG-1156 measured; no `game/data` edit, the hash unmoved).
**The sim's end-of-M5 hardening is done: M5's sign-off waits only on criterion 6 (the owner's play).** Counters after: **sim 0 / 4,
view 1 / 4, data 3 / 4.** Bug ids next: **sim 1165, data 1182, view 1165.** Open S1 / S2: none.

## Standing rules (1-48)

Unchanged 1-47 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle; 43: every sim task that
can change Normal AI behaviour names which M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table
in the same commit, and a row that stops ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on
a scratch clone and integrated first, a new task starts on a fresh branch; 45: a brief whose task changes Normal AI behaviour says
what to do when a pinned criterion row stops ending; 46: a branch held twice gets a terminal acceptance rule in its third brief, so
no branch lives a fourth session; 47: QA's re-check runs the default loop on the commit that includes QA's own suites, and a QA file
that measures allocation declares `[Collection(SerialCollection.Name)]`). **New 48 (2026-11-09-1336, BUG-1163 / BUG-1164):** at
PLAN the Producer checks every acceptance criterion against the brief's own out-of-scope list; a figure the scope cannot move (a
tally the levers don't touch, a digest a knob-less rule must change) is written as "measure and report", never as a pass / fail
clause. Both M5-H6 conflicts were the brief's, not the code's, and cost a QA FAIL verdict and a ruling.

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rule 42 before filing. The full non-Perf run is
  ~8,850 rows: ~1 h 15 m alone (QA: 22-28 m default + ~1 h 07 m Stress / Soak on a clone). The default (non-Stress) loop is
  **~20-28 min** (the AI match rows are in it).
- QA runs the full non-Perf **once** per session (at the re-check, on the fixed commit, **with its own files in**) and the
  42-match tally once.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
  **This session both branches edit `docs/factions/malazan.md` and `whirlwind.md` in different hunks** (sim: rule-40 cells;
  data: the new "How the AI adapts" sub-section): sim first, then data.
- A developer must never kill another track's Godot or `testhost` process.
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it.
- **A brief that relaxes a judge must name what the relaxed query cannot see** (M5-H5's lesson). **A brief's criteria must be
  deliverable inside its scope** (rule 48).
- **Pinned matches on `main` after M5-H6:** `MatchCriterionTests` (Vathar **23,322** seat 1 / Raraku **28,231** / Pale Hills
  **50,140**, bound 72,000 / generated seed 2 **17,053**, bound 24,000 / FFA **56,896** / 2v2 **26,070**),
  `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithinItsBound` (Pale Hills 50,140 / Vathar 23,322), `MapSlice2StressTests`
  (`KnownStalls` Pale Hills 3 / 4 / 6 = **33,484**, generated 6 25,941, Raraku 5 **36,004**; **Pale Hills 2 = 49,565 and 5 =
  55,485, un-skipped**), `StandardPresetPinQaTests` (7 digests, unchanged), QA's `NonHardMatchPinQaTests` (8 digests; 4 moved),
  `HardKnobsPinTests` (unchanged), the Malazan / Whirlwind page tables (9 + 1 cells re-printed under rule 40),
  `D21OpeningsQaTests` / `DH6ThresholdQaTests` + `KnownBeatenAlive` (seed 19 "never" again), the golden header, QA's
  `MatchCriterionSweepQaStressTests.Known` (generated 4 only, BUG-1151; Raraku 1 / 5 out, BUG-1152 not fixed but not shown),
  the hunt rows on seed 1 (50,140, one hunt), `HuntStressTests` FFA seed 2 **75,382** / seed 3 53,892 / 2v2 seed 1 64,226;
  `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall` skipped (no free gold with four seats). The 8
  `CounterCompositionTests.TheNonHardPins_*` rows pin **M5-H6's play at strength 0 + knobs 0** (old digests in the comments,
  BUG-1164 ruling). The Hard v Normal tally: **29 / 11 / 2** (Raraku 10 / 4, Vathar 7 / 7, Pale Hills 12 / 0 / 2: Hard seat 0
  seeds 3 / 6, one match, a war Normal wins at 116,335).
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), QA's `FarExpandProbeQaStressTests`
  Pale Hills FFA seed 2 (BUG-1151, the refusal streak at 35,301), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), QA's
  `M5H5HuntAfterDefeatQaTests.A2v2_TheDefenseToo_*` (BUG-1149), `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150),
  `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156), BUG-1161's three rows in `EndGameH6QaTests`.
- **On the three map files seeds 3 / 4 / 6 are one match** (the seed only picks the openings).
- **Criterion 6 (the owner wins on Easy, loses to Hard) is owner-only:** M5 cannot be signed off by the Producer until the owner
  writes the result in the inbox. Everything else in M5, including the sim's hardening, is done.
- Every AI rule change moves Pale Hills digests (both seats expand there): rule 43's re-measure is the full list above.
- Perf margins: `NextSweep`'s worst-case re-target is 35.2 µs (bound 50) with every start visited; `AiPerfTests` 8 seats avg
  0.525 ms (p95 0.863). A perf gap QA named: no `AiPerfTests` row has eight seats walking their legs on a map with free starts.
- **BUG-1158's cap is the match's total losses** (as the scout's cap): late in a match the first leg death ends the leg for good.
  Accepted as a known limit (a per-leg count needs a saved field). Revisit only if a measured match shows it mattering.

---

## Sim track

### Where we are
M5-H6 ACCEPTED (1 fix round). **Counter 0 / 4.** Every sim-side M5 criterion is met and the end-of-M5 sim hardening has run; M5's
sign-off waits on the owner's criterion 6. Open sim debt: BUG-1161 (S4), BUG-1149 (S3), BUG-1150 (S4), BUG-1151 / 1152 (S3),
BUG-1116's sweep-order row, BUG-1068's FFA seed 1, BUG-1058, BUG-1044's rest, BUG-1067, the movement S3s (BUG-0911 / 0889 / 0144),
`HasMapId`'s note, BUG-1124's record, BUG-1010's lever, the `ReopenLost` masking note (not measured), the eight-legs perf row.
A fresh branch next (nothing held).

### Next task candidates (the next PLAN picks; the inbox first)
1. **Recommended: M7's first sim system** (feature, QA standard; full if it touches combat / economy rules): docs/05 M7 (Teblor)
   "scale and per-unit population systems proven (1.5× visuals, 2-3 pop, half-step pop support)". **Check first** how much
   exists: the sim already counts population in halves (`PlayerView.HalfPop` / `HalfPopCap`, `rules.json` `halfPopCap`), so
   the sim part may be "per-unit `popCost` in `units.json` with half steps, validated, hashed, read by production and the AI"
   plus a `scale` field the view reads (a ViewApi accessor; the 1.5× visual is the view's). Schemas before content (the data
   rule): the sim ships the fields with the minimum entries its tests need; no new faction id in C#. Read docs/02 "Population"
   and the Teblor page before writing the brief; size ≤ 1,500 if the docs settle it, else one slice ~800.
2. **Otherwise the giant armor class + regeneration** (M7's second item; a generic armor class and a per-unit regen field, data
   hooks only).
3. BUG-1010's lever if small (the Malazan openings that fall to `raider_rush` without a wave: a defend-then-counter rule).
4. Riders only if a few lines and the task touches that code: BUG-1161 items (the reach filter on the start's nearest mine;
   the proxy out of sight; the house rebuilt twice), the eight-legs perf row.

Brief notes: any M7 sim task that changes Normal AI play (a pop cost the AI reads) names the pins it expects to move (rule 43 /
45); a pure schema task should move nothing (prove it: the 8 digests + the golden). Keep off `ai.json` (rule 41) while the data
track's Hard tuning (BUG-1163) is open, or plan them in different sessions.

---

## View track

### Current session plan (2026-11-09-1336): STOP (cheap)
Inbox New was empty; nothing changed for the view. Every remaining M6 criterion needs the owner (the art call, music / fonts /
export templates, ".NET 10 installed", a friend's playtest); the view's debt (BUG-1126 / 1147 / 1115 item 4 / 0860 / 0830 / 0890 /
0861) is not a session's worth and the counter is 1 / 4.

### Where we are
STOP (cheap). Counter 1 / 4. M6 5 / 10.

### Next task candidates
- An owner item from the inbox (the art call, the SDK, templates, sound notes): a feature session, QA standard.
- **When M7's sim schema lands** (a `scale` field): the view's 1.5× visual scale from data (M7's first item, the view half).
- Otherwise STOP (cheap). **Not yet, noted:** a stats-screen line for the AI's counter memory ("adapted to your army") only if
  the owner asks.

---

## Data track

### Where we are
D26a ACCEPTED (0 fix rounds). **Counter 3 / 4.** The pages carry "How the AI adapts"; no `game/data` edit; BUG-1156 measured:
(C) recommended (no data change; the docs/02 sentence is in, written by the Producer at ACCEPT). The Producer's default on the
owner question "keep Normal 0.5 / Hard 1.0, or stronger?" is **keep** (stronger shows nothing in Age I while the producer is
saturated; listed under For your review). New: BUG-1181 (S3: the far-expansion bullet's wording on both pages; docs/02's half
fixed by the Producer at ACCEPT; the page half is the data's) + its S4 nits.

### Next task candidates (the next PLAN picks; the inbox first)
1. **Recommended: D-R35, the pages' prose after M5-H6's re-print + BUG-1181's page half (hardening-type re-print, QA light,
   docs only).** Files: `docs/factions/malazan.md`, `docs/factions/whirlwind.md`, `Content/**`, `QA/Content/**`; **not**
   `game/data/**`. Scope: (1) **BUG-1181's S3:** both "Far expansion" bullets say the 384 m reach applies to every mine, a start
   location's included (match docs/02's sentence as the Producer re-wrote it at the 2026-11-09-1336 ACCEPT; keep
   `AiAdaptsContentTests`' regex `ai.farExpandReach 384 m` matching); (2) **the prose above the Malazan "Age II against the
   Whirlwind" and "Fast Age II (BUG-0982)" tables** still quotes the pre-M5-H6 defeat times where the sim re-printed cells under
   rule 40 (13:50 → **13:47**, 15:03 → **14:16** for `cadre_rush` v `raider_rush` seeds 8 / 14; `crossbow_push` v `holy_war`
   seed 45 now "9:28 (defeated 19:02)"; the Whirlwind twin `raider_rush` v `crossbow_push` seed 19 10:21 → **10:34**; seed 19's
   Malazan cell is "never" again): re-word as D-R34 did, no cell edit; (3) BUG-1181's S4 nits that live in the repo ("Three
   rules" → the right count; the "none" row's failure message; optionally the number guard's equal-value gap); (4) a review
   table (no unit / field changed; the re-worded sentences quoted). The pin must stay green both ways. No match tick, digest or
   tally quoted in the new sub-section.
2. **Then D27, Hard's tuning (BUG-1163; feature, QA standard):** `game/data/factions/*/ai.json` Hard difficulty knobs / openings
   so Hard is ahead of Normal on Vathar Crossing too, measured by the 42-match tally (`HardVersusNormalTallyQaStressTests`) and
   a larger sample if cheap; **rule 43 / 41: the Hard-seat pins (`HardKnobsPinTests`, the Hard page cells, the tally in docs/03's
   M5-5 reading) re-measured on its own branch, no Normal / Easy value touched** (the 8 non-Hard digests must not move); not
   in the same session as a sim task that changes AI play. Design references: docs/02 "AI opponent" Hard block, docs/03
   "Implementation (M5-4c slice 2)" (the knobs), BUG-1163's file.
3. The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780;
   `counterStrength` if the owner wants stronger). M7's rosters when M7's schemas exist.
