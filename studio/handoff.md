# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-18-1814** (on 2026-10-19; the third full session of 2026-10-18, so **2026-10-19
starts at 0 / 8**). Base for the next session: `origin/main` after the conductor merges sim (2bbe236 + the Producer's studio commit) then view
(021b157). **Counters: sim 4 / 4, view 4 / 4, data 0 / 4 → the sim's and the view's next sessions are hardening; the data track's next task
is the rule-31 re-print (D-R32).** Open S1 / S2: **BUG-1027 (S2)**, the nine known-red rows on `main` after the M5-4g merge; it outranks
everything and both halves are the first items below. Bug ids: **sim from BUG-1028, view from BUG-1033, data from BUG-1040** (put the
allotment in the QA briefs verbatim: this session the view's QA used 1022-1024, colliding with the sim's).

## Where we are

- **M5 4 / 9, M6 3 / 10.** BUG-0981 fixed (the AI reaches Age II against a live opponent); M6 criterion 7's "generator in the setup" done
  (the three hand-made maps and mirrored terrain left; no map format yet). Golden `data-hash` **E108AB2A7C66C58D**. Save format **2**.
- **`main` is red on nine rows (BUG-1027):** five data-owned pinned rows (rule 31) + four `ReservePairingsStressTests` rows written to the
  waived criterion. Expected; the cause and both fixes are known. Everything else was green in QA's full run (7,590 / 11 / 17: those nine +
  a wall-clock Perf row green alone + BUG-0914's probe).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780, "densest
  cluster"; the downloads (fonts, music, export templates).

## Standing rules (1-37)

Unchanged. 31 = a sim change that moves the faction pages' measured cells gets the data track's re-print (this time as a planned task, D-R32,
not at the integration). 35 = the sim may rewrite data-track fixtures mechanically when its schema change breaks them. 36 = the save API
freeze. 37 = the cross-track fixture fix. **New this session (a note, not a rule):** when the conductor asks that a rule-31 re-print be
next-session work, the Producer files the red rows as a bug (BUG-1027's shape) so `main`'s state is on record and the Gate can stay GO.

---

## Sim track

### Current session plan

```
TASK_ID: M5-H4
TASK_TITLE: Sim hardening: restate BUG-1027's four stress rows, then the debt backlog by value
SESSION_TYPE: hardening (counter 4 / 4 → 0 / 4)
QA_TIER: full
```

**Goal.** Make the sim's half of `main` green again (the four `Pairing_Twins_AgeTwoForEverySideAlive` rows), then work the debt backlog in
value order inside the size budget (≤ 1,500 lines, as many items as fit).

**Scope, in order.**
1. **BUG-1027 (sim half):** `Stress/ReservePairingsStressTests.Pairing_Twins_AgeTwoForEverySideAlive` asserts the waived criterion. Restate:
   every side alive at 20:00 has Age II **except a named allowance list** (`(1,1,3)` seed 19 Malazan `crossbow_push`: under the wave size all
   match; `(1,0,1)` seed 32 Malazan `crossbow_push`: wood-starved; `(2,0,1)` Whirlwind `standard` seed 13 and `(1,0,2)` seed 9: no gold node
   known), each with its base state quoted in a comment; plus the totals printed and asserted over the 27 rows (no-wave sides = 3, Malazan
   waves ≥ 79, Malazan / Whirlwind defeats ≤ 8 / 5). Keep the twin, hash-every-50-ticks and command-count checks exactly. An allowance that
   starts passing must print "take it out" (the `KnownBeatenAlive` pattern).
2. **BUG-1023 item 4:** criterion 2's restated twin: `reserveShift` 0 on a scene with no reservable step (every step affordable or not
   due) is hash-equal to the base build (prove the schema default inert where the reserve doesn't engage). **Item 3:** `ExpansionMine` honours
   `Board.NodeDropped` as `KnowsNode` does (a regression test from seed 37's case).
3. **BUG-0990:** `SaveHeader.MinBodyBytes` gains terms for the projectile and zone capacities, and bounds (or documents) the command capacity;
   `SaveRefusalTests` rows for each forged alone and together (≤ ~1 MB allocated before `Malformed`).
4. **BUG-0912:** the far-corner scan gate from 8 alternated pairs with a bound near 0.35 ms; a short fresh-process gate row for the 4 ms
   500-unit design budget on the sustained clock (docs/03's Producer note).
5. **BUG-0914 (S3) + BUG-0915:** the H3 0-byte probe (2,568 B under load; make it serial or find the allocation) and the `buildingSight` loader
   row flake (19 / 19 alone: a shared `TestDataDir`?).
6. **BUG-1022 (measure, then decide):** a hysteresis on the gather shift (keep the shifted share until the reserved step is paid or changes)
   or shifting only the idle pass; re-run the 27 runs and keep the `switches/min`, `shareFlips` and gathered-by-10:00 numbers in docs/03's table;
   ship it only if gathering by 10:00 recovers without losing Age II or waves (else record the numbers and leave it).
7. If the budget allows: BUG-0960 items 2-3, BUG-0911, BUG-0889, BUG-0144, BUG-0941, BUG-0940, BUG-0913's nits, BUG-0113 item 2, BUG-0704 item 3.
- **Out of scope:** BUG-1010 (feature queue), BUG-1026's gate form (a tuning pass with the 27-run table, not a hardening item), any data-owned
  row (the data track's D-R32 runs beside this session: **don't touch `Content/` or `QA/Content/`**), the view, `ai.json`.

**Acceptance criteria.** (1) The four rows green with the allowance list and the totals asserted; a mutated allowance prints "take it out".
(2) The restated twin green; `ExpansionMine` test green. (3)-(6) each item's own regression test, failing first where it's a bug. (7) The full
non-Perf suite green **except BUG-1027's five data-owned rows if D-R32 hasn't merged yet** (list them); Perf rows green alone. (8) docs/03
updated per item; bug files set `fixed` with the test names; the golden unmoved unless an item says why.

**Design references.** docs/03 "Implementation (M5-4g)" (the measured / variants tables; the restated criterion); "Save format (M6-S2)" (the
pre-check); "Re-baselined for this PC" (the gates); each bug file.

**Tests required.** The restated stress rows, `ReserveTests` + a twin row, `SaveRefusalTests` rows, the Perf gate rows, the flake fixes'
proofs (10 runs under load), the churn table in docs/03.

**Constraints.** No per-tick allocation; no `Dictionary` iteration; the AI reads `PlayerView` only; `Tick()` untouched; no Godot; rule 36 (the
save signatures and codes); files: `sim/**` except `Content/` and `QA/Content/`, docs/03, the bug files.

### QA focus (sim)

Full checklist, hardening flavour: (a) the restated rows can't be loosened by accident: mutate one allowance to a passing side and confirm
"take it out" prints; (b) the shift-0 twin really runs the reserve path zero times (count `ReserveStep >= 0` thinks = 0); (c) BUG-0990's
bound against real saves of empty worlds at large capacities (never refuses a real save); (d) the flakes: 10 full-suite-under-load runs of the
two rows; (e) BUG-1022's variant on the 27 runs with the same `ROW` columns, twins hash-equal; (f) Perf rows alone and paired A/B on head v
base. Clone into `simqa<id>-*`.

### Next task candidates (after this one)

1. Requests 38 (teams: `SimConfig` teams, alliance in combat / fog / victory) and **43 (starting resources standard / high)**: the two
   `SimConfig` additions the setup still needs.
2. M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground scouting, hunting the last
   units: BUG-0953's sim half, seed 11); BUG-1010's counter rule re-tried once D-R32 has re-read the wave thresholds (the variants table says
   the thresholds were set against a production-first AI); D14 c; M5-5.

### Watch-outs (sim)

- D-R32 runs beside this session and edits `Content/` + `QA/Content/` and the faction pages; the sim's hardening must not touch them (rule 31).
- `AiSaveScenes` probes the Blackboard (rule 37); a manager change that moves a probe fails `AiMatch_SavedAtFixedTicksAndProbedMoments_*`:
  adjust the row's tick, not the probe.
- The AI's state is not hashed: a manager change moves no `k` line but moves the pairing reports (rule 31 again).
- Three test hosts share the machine; wall-clock Perf rows rerun alone before counting.

---

## View track

### Current session plan

```
TASK_ID: M6-VH4
TASK_TITLE: View hardening: BUG-1030 + BUG-1031 first, then the view's debt backlog
SESSION_TYPE: hardening (counter 4 / 4 → 0 / 4)
QA_TIER: standard
```

**Goal.** Close the two S3s M6-V15 left on the setup and Load Game, then the view's debt backlog by value (≤ 1,200 lines).

**Scope, in order.**
1. **BUG-1030:** the refused row scrolled into view (`Tree.ScrollToItem` / `EnsureCursorIsVisible` in `LoadGameScreen.Select`, or after the
   select in `Main.ShowLoadNote`); `QaM6V15Test.RefusedRows`' `QA NOTE: BUG-1030` becomes a `Check` (the visibility is already computed).
2. **BUG-1031:** `ForestsSpin.Apply()` / `MinesSpin.Apply()` at the top of `SkirmishSetup.MatchArgs` (Godot 4.3+); an emptied box + Enter
   redraws the pick; the `TypedCounts` note becomes a `Check` (want 11).
3. **BUG-1032:** a per-map hint or cap for the mines count (design call: the simplest honest thing is to clamp the cap to what the generator
   fits on the picked map size, else say "about N placed" in the log only); pin the `(6.5, 6, 6)` clamp row to Godot's rounding.
4. **BUG-1002:** a lone `.replay` in the save folder listed and deletable; a tick-0 save's camera on the base; the pause menu's save lines
   off the command card.
5. **BUG-0970** (`AttackAlerts.Upgraded(k)` on a full ring), **BUG-0955** (`smoke.ps1` on a no-LFS clone: `-cmatch` + the wav importer's
   case), **BUG-0954 item 4**, **BUG-0922** (nits), the stale `QaM6V11Test` header comment, BUG-0743 item 3 (wontfix candidate: say so),
   BUG-0521 items 2-3 (unreachable: close with the reason).
- **Out of scope:** any `sim/**` change (a read-only `ViewApi/` accessor only if truly needed, say which), the art pass (owner), the hand-made
  maps (no map format), teams / starting resources (Requests 38 / 43), the sound re-cuts (the owner's note first).

**Acceptance criteria.** (1) `QaM6V15Test` green with the two notes turned into `Check`s; (2) `SaveLoadTest` / `ShellTest` green; (3) each
other item's regression row in its scene (failing first where it's a bug); (4) loop green (72 + any new rows), smoke PASS, 0 warnings, `sim/`
diff empty; (5) docs/02 / docs/03 updated where behaviour changed; bug files `fixed` with the scene / method names; one windowed 1280 × 720 run
of the setup and of Load Game with a long list, screenshots looked at.

**Design references.** docs/02 "Game flow" 2 and 6; docs/03 "Implementation (M6-V14)" / "(M6-V15)"; each bug file.

**Constraints.** Views hold no gameplay state; nothing writes the sim; text in `ui.json`; `Main.SaveDir` null under a test host (every scene
sets its own folder); files: `game/scripts/**`, `game/tests/**`, `game/data/common/ui.json`, `tools/qa/smoke.ps1` (BUG-0955), docs/02, docs/03,
the bug files.

### QA focus (view)

Standard: the refused row with 200 rows at the bottom, top and middle after the fix (visible every time, the pick exact); the typed count on
a one-frame click at the floor, the cap and a junk string; a resize of the Load Game screen with a picked row; BUG-1002's lone `.replay`
with 200 saves; smoke on a no-LFS clone after BUG-0955; the loop twice. Clone into `viewqa<id>-*`.

### Next task candidates (after this one)

The sound re-cuts when the owner's listening note arrives; teams when 38 lands; starting resources when 43 lands; `tools/export.ps1` when the
templates arrive; criterion 6's music when the track arrives; the hand-made maps when a map format exists; the art pass when the owner answers.

### Watch-outs (view)

- `ViewApi/SaveSummary` hard-codes the header's shape; a sim change to `SaveHeader.Write` breaks the list's tick column silently
  (`SaveSummaryQaTests` catches it). The sim's hardening may touch `SaveHeader.MinBodyBytes` (BUG-0990): the bound, not the layout; re-run
  `SaveLoadTest` + `QaM6V14Test` on the merged tree if `Save/` changed.
- `scene-loop.ps1 -Filter` is a file glob; `QaM6V15Test` takes ~41 s headless.
- A clone without `git lfs pull` fails `LookTest` / `SfxTest` clip rows and smoke (BUG-0955 is on this session's list).

---

## Data track

### Current session plan

```
TASK_ID: D-R32
TASK_TITLE: Rule-31 re-print after M5-4g (BUG-1027's five data-owned rows) + BUG-1011 / BUG-1010's page line; the D21 answers folded if present
SESSION_TYPE: feature (counter 0 → 1 / 4; a re-print is cheap)
QA_TIER: light
```

**Goal.** Make the data-owned half of `main` green again and keep the faction pages true to the AI's play after the reserve.

**Scope.**
- Re-print from the rows' output (run them; don't loosen them): `Content/AiSpendingReportTests.GeneratedNormal_PagesAndDocs03_QuoteTheReport`
  (both pages' spending paragraphs / cells and docs/03's quoted lines), `QA/Content/D20PinQaTests.HouseNote_*` (malazan, whirlwind),
  `Content/AiOpeningPairsReportTests.Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable` (**re-read the keep-18 rule** from the new table: the
  reserve changed `cadre_rush`'s play; if the rule now picks another number, that is a data change → `ai.json`, golden regen with the reason,
  and a For your review table; don't keep 18 by habit), `QA/Content/DH6ThresholdQaTests.CrossbowPushVersusRaiderRush_FourthAndFifthSeeds`
  (seed 25 `crossbow_push` is alive at 20:00 with no wave and 1 pop: a named allowance like `KnownBeatenAlive`, with BUG-1010's id, or the row
  restated to the measurement).
- `D21OpeningsQaTests.KnownBeatenAlive`: check whether seed 19 still needs it (QA says yes: alive, no wave); the "Age II at" lines per opening
  and seed on both pages from the pairs rows (QA's `ReservePairingsStressTests` prints every side; the Producer's session log has the headline
  numbers); the Malazan peak army a third smaller (903 → 596 pop summed over 27 runs) said in the malazan page's spending paragraph; the
  BUG-0981 pattern paragraph on both pages updated (fixed by M5-4g, the residue named).
- **Fold BUG-1011's five wording nits and BUG-1010's page line** (quote the 5-seed counts, name seed 25).
- **If the owner's D21 answers are in the inbox:** apply them in the same task (names, descriptions, the Malazan standard wave 20 → 16), golden
  regen once, a review table.
- **Out of scope:** any C# outside `Content/` and `QA/Content/`; the sim's four stress rows (the sim's hardening); any new number without the
  rule's say-so.

**Acceptance criteria.** (1) The five rows green on the merged tree; (2) `Content` + `QA/Content` + `DataValidation` + `ReplayGolden` green;
(3) the pages' cells equal the rows' output (the rows pin them); (4) the golden unmoved unless a number changed, in which case the commit body
says which and why and For your review has the table; (5) BUG-1011 / BUG-1010's lines done; (6) a For your review entry (the Producer writes
the owner-facing one from the report).

**Design references.** docs/02 "AI opponent" (the reserve bullet), docs/03 "Implementation (M5-4g)" (the measured table), "Spending per opening
(D21)", the malazan page's "Fast Age II against the raider rush" section and its rule; BUG-1027, BUG-1011, BUG-1010.

**Tests required.** The five rows and the D21 / D-H6 suites; QA light per rule 23 (a re-print).

**Constraints.** The data track's files only (`docs/factions/**`, `sim/Rts.Sim.Tests/Content/`, `sim/Rts.Sim.Tests/QA/Content/`,
`game/data/factions/**` only if the rule moves a number); QA light; the numbers must match docs/02 and the pages.

### QA focus (data)

Light: the cells against the rows' output; the keep-18 rule re-derived independently from the new table; the allowance's "take it out" line;
the golden; nothing outside the track's files.

### Next task candidates

1. The owner's D21 answers if they arrive after this task; D19's table review, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780: each waits on
   the owner's yes / no.
2. Else STOP cheaply until D14 c / M7's schemas land.

### Watch-outs (data)

- The sim's hardening runs beside this task and must not touch `Content/` or `QA/Content/`; if it moves `AiSaveScenes` ticks that is not the
  data track's problem.
- A Normal / Hard AI picks by seed: name the opening each measured row played.
- BUG-1026's Easy / Hard notes (Easy seed 2 Malazan 2 waves and defeated; Hard seed 3 Whirlwind no wave) are sim tuning, not page facts:
  mention them only if the pages quote those rows.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/02, docs/03: each track appends its own rows; the conductor unions them.
- Three tracks run this time: three test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure
  counts only when it fails again alone.
- Bug ids: sim from 1028, view from 1033, data from 1040; QA briefs quote the allotment.
- The owner's old recordings refuse to play since E108AB2A7C66C58D (the two new `rules.json` fields); the seed-21 golden was regenerated.
