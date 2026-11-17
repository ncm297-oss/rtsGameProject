# BUG-1464: M6-S10 small findings (held-worker probe looks once; Known-list count in docs; Teblor sentences made false and not listed)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 1 fixed by M6-SH2: `Movement/HeldWorkerProbe` checks every tick past the clock with the Producer's definition) |
| Found | 2026-11-15-0506, task M6-S10 (QA full) |
| System | tests (`Movement/HeldWorkerProbe`), docs (docs/03 "Implementation (M6-S10)", `docs/factions/teblor.md`) |
| Fixed by | |

## 1. `HeldWorkerProbe` checks "held" only on the tick a clock passes 1,200
`HeldWorkerProbe.Watch` returns unless `since == HeldTicks + 1`, so a worker that walks for its first minute without a
delivery and then stands for four is never reported. QA's staged scenes found exactly that shape (`depot22_20` at 0.5: a
Gatherer walks to 1,289 ticks, then stands about 4 minutes, never delivering; `KeepCornerCureQaStressTests` checks on every tick past the clock).
No effect on the gated sweeps today (every gap on seeds 2-56 is at most 63.6 s, so nobody passes 1,800 ticks), but the
probe will under-report the day a gap grows. Expected: check on every tick past the clock, one row per stretch.

## 2. docs/03 and BUG-1460 name the wrong Known rows
docs/03 "Implementation (M6-S10)" ("`MatchCriterionSweepQaStressTests.Known`: generated 4 out ..., Raraku 1 and 4 in") and
BUG-1460's last paragraph ("holds seeds 1 and 4") disagree with the code, which adds Raraku **1, 3, 4 and 6**.

The Known list is shared: `Stress/FarExpandProbeQaStressTests.AProbedMatch_*` reads it and prints, rather than asserts, its
silent-seat and stuck-unit oracles for a Known (map, seed) Normal v Normal row. So the new `("raraku", 1)` entry also turns
those two oracles off for FarExpandProbe's (raraku, 1, Normal) row: it is **green** on the final tree (QA's Stress run), while
the developer's report and BUG-1462 list it as red. Not a hidden loosening beyond what the brief allows (a stall joins the
known stalls with its id), but the report should say that row is now excluded, not red.

## 3. Teblor page sentences made false by re-printed cells, not listed for the data track
BUG-1462 lists the malazan / whirlwind prose; these two Teblor ones are missing:
- `docs/factions/teblor.md` line 329: "the 11th Hut step (step 15) is skipped and the 12th is never reached on this seed",
  beside the re-printed cell that now says "the last Hut step (step 16) is done at a cap of 100; skipped at the hard cap:
  steps 15, 16".
- lines 337-338: "sends its first wave at 340 s ... against a Normal Whirlwind it sends its first wave at 362 s" quoting
  `QA/Content/TeblorOpeningQaTests`, whose rows now read 323 s and 316 s (Age II 448 / 429 s, beaten at 1,022 s, not 910 s).

## Re-check, fix round 1 (2026-11-15-0506, e49be01)
- Item 2: **fixed** in docs/03 (Known = Raraku 1, 3, 4 and 6; `FarExpandProbe` (raraku, 1) listed green) and in the
  developer's unapplied BUG-1460 patch. Checked against `MatchCriterionSweepQaStressTests.Known` and QA's round-0 Stress run.
- Item 3: **fixed**: both Teblor sentences are now on docs/03's data-track list, with cells matching
  `TeblorOpeningQaTests` (6,461 / 6,321 ticks = 323 / 316 s; Age II 448 / 429 s; beaten 20,444 = 1,022 s).
- Item 1: **open, deferred to the Producer**. The developer reports that an every-tick check flags fuzz seed 14 at knob 0
  at tick 1,240 on a Teblor Gatherer 0.03 m from its node, Gathering (cargo 14 of 15), which delivers at 1,272. Plausible
  (a 120 m walk leaves the clock past 1,200 when the load starts) and QA did not reproduce it. The fix needs "held" to
  exclude a worker in reach and gathering. QA's own every-tick check in `KeepCornerCureQaStressTests` (moved at most 2 m
  in 600 ticks, past 1,200 without a delivery) has the same gap and would flag that worker too; its staged rows are green
  today, so nothing there is mis-reported yet. Status stays open on item 1 only.

## Producer answer to item 1 (ACCEPT of 2026-11-15-0506): what "held" means on an every-tick check
A worker is **held** when, on any tick at or after 1,200 ticks without a delivery (node alive, `GatherNode` set), it
stands within 2 m of where it was 600 ticks earlier **and in those 600 ticks it was never in reach of its node or of
a drop-off** (reach = `EconomyConstants.Reach` from the footprint). A worker in reach that is gathering its load or
depositing is working, not held (fuzz seed 14 at 0: 0.03 m from its node, cargo 14 of 15). The probe moves to the
every-tick check with this exclusion in the sim's hardening; `PastTheWindowWalking` (0, 14) is accepted as listed.
