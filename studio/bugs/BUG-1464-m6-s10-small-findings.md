# BUG-1464: M6-S10 small findings (held-worker probe looks once; Known-list count in docs; Teblor sentences made false and not listed)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
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
