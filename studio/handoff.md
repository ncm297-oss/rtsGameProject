# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-26-2108** (written on 2026-10-27, so **2026-10-27 starts at 0 / 8**; the
2026-10-26 session was a one-off dispatched by the owner). Base for the next session: `origin/main` after the conductor merges sim
(`studio/2026-10-26-2108-sim`, cde7952 + the Producer's studio commit), then view (6f12759), then data (95d1ebc). **All three ACCEPT.**
**Counters after: sim 0 / 4, view 0 / 4, data 1 / 4** (the two hardenings done; the data re-print was a feature session).
**Open S1 / S2: none** (BUG-1027 closes with this merge; BUG-1040 S2 was found and fixed inside the session). **`main` is green again** once
all three branches are merged: the sim's four stress rows and the data's five pinned rows were the only red.

## Where we are

- **M5 4 / 9, M6 3 / 10** (hardening sessions tick nothing). Golden `data-hash` **E108AB2A7C66C58D** unmoved. Save format **2**; **new format
  bound:** `SimConfig.MaxCommandCapacity` = 65,536 (the save header, `Replay.Validate` and `SimConfig.Validate` share it; the game uses 4,096).
- **Debt closed this session:** sim BUG-0912 / 0913 (1-5) / 0914 / 0915 / 0940 / 0941 / 0990 / 1023 / 1027 (sim half) + 0960 item 2's two paths;
  view BUG-1030 / 1031 / 1032 / 1002 / 0970 / 0955 / 0954 / 0922 / 0521 (closed); data BUG-1011, BUG-1027 (data half). **Left open:** sim BUG-0960
  (one int32-sweep throw in `FlowFieldCache.Get`, offset 2525 value −1), BUG-1022 (measured, not shipped: no variant met the rule), BUG-0911 /
  0889 / 0144 / 0113 item 2 / 0704 item 3 / 0913 items 6-7 (not reached); view BUG-1060 (S4: index rows done by the Producer at this ACCEPT;
  BUG-0521's status word; a no-LFS clone's *first* smoke run still red, documented); data BUG-1080 (S4 wording), BUG-1010 (S3, sim-side).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780, "densest
  cluster"; the downloads (fonts, music, export templates); **new:** the 3-hour stale-lock window (see STATE "Wanted").

## Standing rules (1-37)

Unchanged. Confirmed at this ACCEPT under **rule 36** (the save API freeze): a forged command capacity over the bound now reads
`InvalidHeader` (was `Malformed`), the code the docs/03 refusal table already gave a capacity out of range; no new code, no new signature.
**Note (not a rule):** `ViewApi/AttackAlerts.cs` is the view's own file (M6-V12); a behaviour fix inside it is fine as long as the hash twin
holds (QA's `AttackAlertsRingQaTests.AlertsFedEveryTick_*`), so "sim/ diff empty" in a view brief means "nothing outside `ViewApi/`".

---

## Sim track

### Current session plan

(None yet: the next PLAN writes it. Counter 0 / 4 → a **feature** session.)

### Next task candidates (in order)

1. **Requests 38 (teams) + 43 (starting resources):** the two `SimConfig` additions the skirmish setup still needs (M6 criterion 1's "team"
   and the "Starting resources are not built yet" line in docs/02 "Game flow" 2). Teams: `SimConfig` team per seat, alliance honoured in
   combat targeting, fog sharing (decision: shared vision or not, docs/02 says), victory (a team wins when every other team is out). Starting
   resources: standard / high purses, `rules.json`-driven, in the save and replay headers (format bump; rule 36 means a new version, not a
   changed signature). Likely two slices; QA full (fog / victory / save).
2. **A map file format for M6 criterion 7's three hand-made maps** (the view can't build them without one): a heightmap + resources + start
   locations file under `game/data/maps/`, loaded by the sim, hashed in the header. Plan after 38 / 43 unless the owner asks for maps first.
3. M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground scouting, hunting the last
   units: BUG-0953's sim half, seed 11); BUG-1010's lever (D-R32 re-read the thresholds: keep 18 stands, so the lever is a sim rule such as
   "send what you have when the base is being lost", tried once as defend-then-counter and backed out: 0 / 6); D14 c; M5-5.

### Watch-outs (sim)

- `SimConfig.MaxCommandCapacity` 65,536 is now a hard bound: a test or tool asking for more throws at `Validate` (the CLI caps its queue).
- `AiSaveScenes` probes the Blackboard (rule 37); a manager change that moves a probe fails `AiMatch_SavedAtFixedTicksAndProbedMoments_*`:
  adjust the row's tick, not the probe. The AI's state is not hashed: a manager change moves no `k` line but moves the pairing reports
  (rule 31: the data track re-prints).
- `ReservePairingsStressTests.KnownNoAgeTwo` names four sides; a sim change that gets one of them to Age II prints "take it out": take it out.
- `VisionGatePerfTests.Brawl250v250_DesignBudget_FreshProcessShortRun_Under4Ms` reads 3.50-3.90 ms against 4 ms: ~10 % margin; it spawns
  `dotnet test` children (120 s timeout). A failure alone on a quiet machine is real; under another track's run it is not.

---

## View track

### Current session plan

(None yet. Counter 0 / 4 → a feature session **if** anything is unblocked; else STOP cheaply.)

### Next task candidates

Every M6 item left waits on something: teams on Request 38, starting resources on 43, the three maps on a map format, the art pass on the
owner's look call, the music / fonts / export templates on the owner's downloads, the sound re-cuts on the owner's listening note. **If
none has landed at the next PLAN, the view STOPs** (a cheap STOP, no counter change). Small items that could ride along with a real task:
BUG-1060 items 2-3 (the `wontfix` word on BUG-0521; the no-LFS first-run note), BUG-0550 item 2 (the art pass).

### Watch-outs (view)

- `ViewApi/SaveSummary` hard-codes the header's shape; a sim change to `SaveHeader.Write` (a format bump for 43) breaks the list's tick column
  silently: `SaveSummaryQaTests` catches it; re-run `SaveLoadTest` + `QaM6V14Test` + `QaM6VH4Test` on the merged tree when `Save/` changes.
- `smoke.ps1` now matches `-cmatch` and skips `[n]` frame lines, and skips the wav importer's lines only when `.wav` files are LFS pointers
  (prints a `WARN`). A normal clone: unchanged gate. A fresh no-LFS clone still fails its first run (looktest art), passes from the second.
- `StatsScreenCheck.LineReadable` threshold 1.4:1 on the outline: set just under the shipped Violet (1.45:1). A palette change in `ui.json`
  is the owner's taste; don't raise the threshold without it.
- `scene-loop.ps1 -Filter` is a file glob; the loop is **73** scenes now (`QaM6VH4Test` added), ~22 min.

---

## Data track

### Current session plan

(None yet. Counter 1 / 4.) **STOP cheaply** unless the inbox carries the owner's D21 answers (names, descriptions, the Malazan standard wave
20 → 16), D19's table review, the Shock pass, `goldShare`, or BUG-0780; or a new schema lands (teams / starting resources need no faction
content; D14 c and M7's rosters do).

### Next task candidates

1. The owner's answers above, each a small page + `ai.json` / data task with a review table (golden regen once).
2. **BUG-1080 (S4):** two page wordings (seed 32 "still standing" at 3 pop / cap 0; whirlwind.md's unmeasured "wood runs short 0-50 from
   13:00"): fold into the next page-touching task.
3. Rule 31 re-prints whenever a sim task moves the AI's play (38 / 43 should not; M5-4c slice 2 and M5-4d will).

### Watch-outs (data)

- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} both print "take it out (BUG-1010)"
  when their case passes: take them out then.
- `cadre_rush` v `raider_rush` seed 18 (alive, no wave at 20:00) is shown only by the non-asserting DH6 table row; making that row strict
  needs an allowance for it (QA's note).
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells` from the 1st / 2nd / 3rd-seed runs.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/03: each track appends its own rows; the conductor unions them (this session all
  three touched docs/03 and the index: sim → view → data).
- Three tracks run: three test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- Bug ids next: **sim from 1042, view from 1061, data from 1081** (the PLAN re-allots; QA briefs quote the allotment verbatim).
- The owner's old recordings refuse to play since E108AB2A7C66C58D (two `rules.json` fields); the seed-21 golden was regenerated then.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase (machine-local); the owner's call on the window
  itself is listed in STATE "Wanted".
