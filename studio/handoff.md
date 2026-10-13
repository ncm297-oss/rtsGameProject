# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-13-0527** (second full session of 2026-10-13, 2 / 8; all three tracks
ACCEPT: sim M5-H1, view M6-VH1, data D16; all three hardening / pin tasks, no golden change). Counters after it: **sim 0 / 4, view
0 / 4, data 1 / 4**: the next session is a **feature session on every track**. Bug ids next session: **sim from BUG-0670, view from
BUG-0680, data from BUG-0690.** Inbox: empty at this ACCEPT (no owner answers on D13 / D14 / D11 / D12 / D15 / the colour names yet).

## Where we are

- `main` after this integration (sim e1e34c3 → view d2712e9 → data 520a4d8; the conductor union-merges docs/01 (four 2026-10-13 rows),
  `studio/bugs/README.md` (rows by bug id; keep the `fixed` row where BUG-0541 / BUG-0641 appear twice), `studio/qa/coverage.md` and
  `studio/bugs/BUG-0541-*.md`; no code conflict). The Producer's scratch merge built with 0 warnings, ran the `Content` slice (933 / 0),
  and the view scenes: `FogViewTest` + `QaV3Test` now PASS, **`QaH2Test` + `QaM27Test` still FAIL** (BUG-0661, S2, pre-existing).
- **One open S2 on `main`: BUG-0661** (two view test scenes red since the M5-4a merge: a 10 s march of 19.3 m against a 20 m line on
  seed 1, 11.3 m against 15 m on seed 6 in `QaH2Test`; `QaM27Test` the same 19.3 m). Test-side staging on the new start geometry; the
  thresholds must be re-derived from the scene's intent, not loosened. **The view's first item next session.** No open S1.
- **The game from the menu now:** Skirmish → Start opens with a Town Hall and 5 workers a side on two start locations **on the same
  level, at least half a map apart**, 2 mines + a forest each; the AI gathers from the first minute, scouts at 15 s, finishes every
  house it starts (BUG-0640), and a wave that can't reach its target goes home after a cooldown (BUG-0598); each slot has its own
  colour (8 in `ui.json`, unique per match, all corpse-legible). Still missing for a real game: the sim's defeat rule (Requests 37,
  **M5-4b's first item**), the AI's defense / expansion / abilities / difficulty knobs, victory / defeat on screen (M6-V4).
- **M5 1 / 6, criteria 2 and 3 advanced; criterion 4 (difficulties) has its data pins (D16) and waits on the sim's knobs; M6 criterion
  1 advanced (colour; 2-4 slots and team left) and criterion 7's generator half landed.** Open S3: sim BUG-0651 (2x-scale give-up),
  BUG-0461's movement half, BUG-0144, 0151, 0046, 0050, 0028 / 0032, 0025, 0005, 0023, 0080; view BUG-0662 (the pool reclaim). S4s:
  sim BUG-0650, 0622 item 1, 0510, 0184, 0113 item 2, 0002; view BUG-0663, 0550 item 2, 0521 item 2 (a sim file), 0581 items 2-3,
  0281 item 2; data none open.

## Standing rules (1-17)

1-16 as in the 1930 / 0146 / 0527 PLAN handoffs (full suite incl. Perf alone for sim rule changes; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a
data brief names every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the
Producer the rest; content-independent sim tests; `git checkout HEAD -- path`; the budget is a line; **one system per brief**, the
dev's count excludes QA's files and is reported; **no process killing by image name**).
17. **A dev never loosens a QA row.** A QA test that fails against the dev's change is either a real finding (fix the code) or a
    wrong test (say so in the report with the reason and let QA change it). This session the sim dev turned QA's BUG-0620 replay row
    into a `return` on any read error (a vacuous pass); QA restored the strict assert.
18. **Producer's merged-tree check runs the whole scene loop** (`tools/qa/scene-loop.ps1 -SkipPlayable`) whenever the sim branch
    touches the map generator, `ResourcePlacer` or `ViewApi/StartBase`: BUG-0661 sat on `main` for a session because the 0146 check ran
    only the slices, smoke and two scenes.

---

## Sim track

### Next task candidates (feature session; M5-4b, QA full)

**M5-4b slice 1, Requests 37 first:** `World.IsDefeated(p)` (no live building incl. sites, and the player had one once; never-built
players never defeated), `World.DefeatEvents` (player, tick; like `AbilityEvents`), `Simulation.MatchOver` (at most one undefeated
player among those that ever built), hashed; the AI of a defeated seat stops thinking (sends nothing); replay playback unchanged
(derived from state, no header field). Docs/02 "Game flow" 4. Then, in the same brief only if the first system is small (it should be
~300 lines): **defense** from the enemy army memory (pull the army home when own buildings take damage; workers flee to the hall per
docs/02 "AI opponent" behaviours), or leave defense / expansion / the ability manager / the difficulty knobs (`gatherBonus`,
`focusFire`, `pullBackWounded`, `useAbilities`) for M5-4c. One system per brief (rule 15): Requests 37 + at most one more.

Watch-outs (sim): a new `Command` kind, if any, goes into `CommandDoorFuzzStressTests`; `MatchOver` must not read the view's
`ViewApi/MatchOver` (which prints the dev line today: the view keys off the sim's events once they exist); `AiVersusAiTests` is the
place for "one side wins" (criterion 5, M5-5); BUG-0651's direction (a stopped chaser with no cached field holds the count) is a
few-line fold-in if an M5-4b task touches `CombatSystem.ReachableOnFoot`, else the next hardening; `TightBlob2500` sits at 4.47-4.51
of 4.6 ms alone: any per-tick work in combat / movement needs a measurement.

### Debt for the next sim hardening (counter 0 / 4)
BUG-0651 (S3), BUG-0650 (S4: a per-site join count, give up / back off), BUG-0622 item 1 (`StartBase.Plan` anchor wrap, a view-owned
file the sim may take by name), BUG-0510, BUG-0144, BUG-0113 item 2, the 0461 movement half; Requests 33 (a text-taking `DataLoader`
overload for M6 export).

---

## View track

### Next task candidates (feature session; QA standard)

1. **BUG-0661 first (S2, red on `main`):** `QaH2Test` seeds 1 / 6 and `QaM27Test` re-staged on the merged tree: re-derive each march
   threshold from what the scene proves (a 10 s march at the unit's speed from the new start block; the seed-6 11.3 m is a ramp /
   forest in the way: move the block or the goal), reason written in the test, no threshold loosened without one. Run the whole loop.
2. **BUG-0662 (S3)** if a few lines: `GroundDiscPool.Reclaim` takes only a node whose key is lower than the key being taken (callers
   take keys ascending), else grow; a `QaM5V1Test` / `QaVh1Test` Check that stable keys keep their nodes. **BUG-0663** wording.
3. Then the feature: **M6-V4** (victory / defeat banner + the post-match stats screen, criterion 2) **only if Requests 37 is on
   `main`** (it won't be until the sim's M5-4b merges: the view can build the screen against `ViewApi/MatchOver`'s current dev watch
   and switch the trigger when the sim's events land, if the Producer judges that cheap); otherwise **M6-V5's slot part** (2-4 slots
   in the setup; the generator's 3-4 start locations exist since M5-4a; a team rule is Requests 38 to file for the sim) or the
   **settings screen** (criterion 4: resolution, window mode, VSync, quality, volumes; keybinds later), which needs nothing from the sim.

Watch-outs (view): the dev's count excludes QA's files; `ui.json` edits are allowed when the brief names them (sim-owned dir);
`AiSeatTest`'s gathering Check reads a one-tick snapshot and is stable only on its own Hard match (BUG-0663 note): a new scene
checks "first gathering tick ≤ 400", not a snapshot.

### Debt for the next view hardening (counter 0 / 4)
BUG-0662 (S3, if not folded in), BUG-0663, BUG-0550 item 2 (M6 art), BUG-0581 items 2-3, BUG-0281 item 2 (M6 fog look), the M6
export items (Requests 33, `tools/export.ps1`).

---

## Data track

### Next task candidates (feature session; counter 1 / 4; QA light)

1. **Inbox answers first** (D13's ×1.1 / ×1.5 / hp pass, D14's build orders, the D11 / D12 text tables, D15's house-rule choice, the
   colour names): each a data edit + page pin + one golden header regen (`data-hash` only; every `k` line equal, asserted).
2. Else **D17, the Teblor's `ai.json` + faction page AI section** against the schemas on `main` (difficulties, a 26-step build order,
   compositions by pop, `attack` / `retreat`), pinned by the existing `AiContentTests` A-K rows (they take any faction with an
   `ai.json`): the first M7 data, allowed ahead because it needs no new schema. **Or**, smaller: the docs/02 "AI opponent" Normal line
   stating "doesn't focus fire or pull back wounded" (the D16 QA note: Normal's `false` flags rest on "fair play at a relaxed pace") as
   a Producer default + K row.
3. STOP cheaply if neither applies and a schema is missing.

Watch-outs (data): no `ai.json` number moves without an inbox answer; `rules.json` is sim-owned; D16's J compares post-tick-conversion
values (a sub-half-tick file edit passes: documented gap, not a bug); the sim's M5-4b may add `ai.json` fields (defense / expansion
knobs): if so, the data brief that session names them and the sim ships the minimum entries.

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing.
- `studio/bugs/README.md` and `studio/qa/coverage.md`: each track appends only its own rows (unions at the merge); a bug file two
  tracks both close in one session gets two appended sections (fine) and the Producer sets the status.
- **Merge order sim → view → data.** The sim's M5-4b will regenerate the golden header if it adds a replay field (format 7) or hashes
  new state (`k` lines move once, say why); nobody else touches `Replays/`.
- Rules 16-18 above.
