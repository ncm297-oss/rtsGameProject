# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-11-0634** (second full session of 2026-10-11, 2 / 8; all three tracks
accepted, integration sim 6973fca → view 7a289dd → data 4b0c496). Counters after it: **sim 2 / 4, view 2 / 4, data 3 / 4** (all
feature sessions). Bug ids next: **sim from BUG-0590, view from BUG-0600, data from BUG-0610.** The next PLAN writes the real
briefs; what follows is where we are and the candidates.

## Where we are

- `main` after this integration: build 0 errors, **1 warning** (`game/tests/QaShellTest.cs:62` CS8602, the view QA's scene: BUG-0581
  item 4, first line of the next view task); the Producer's scratch merge of all three branches: the full non-Perf suite 5,486 / 12
  skipped / 0 failed, smoke PASS (both passes). Golden `cross_map_seed1.replay` is **format 5** (one `seat 0 malazan
  human normal` line), `data-hash 5760E83CB301B62E`, every `k` line equal, 1 player, no AI seat.
- **M5 1 / 6** (criterion 1 ticked at this ACCEPT; 2 advanced). **M6 criterion 1 advanced** (the shell's first slice).
- The AI on `main`: `SimConfig.Seats` + `AiPlayer` (executor, economy, production) through the command door; D14's real build orders.
  **It cannot scout:** on a generated map it never learns a forest and starves of wood by pop 36 (QA's note). `ai.json` and
  `rules.json` `ai` are **outside `ContentHash`** until M5-3 (Producer decision: one golden regen there).
- Three Producer decisions recorded in docs/01 this session (the sim's row): AI commands recorded in replays, playback never runs
  the AI; seats in `SimConfig` + replay format 5; `ai.json` into the hash in M5-3. Owner may revisit.
- **No open S1 / S2.** New S3: BUG-0574 (sim, the economy manager's dropped-node marks), BUG-0580 (view, a mouse release during the
  pause). S4: BUG-0573, BUG-0581.

## Standing rules (1-12)

1-10 as in the 0314 handoff (full suite incl. Perf alone for sim rule changes; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; every agent's scratch folder `<track>-<session>-<purpose>` with absolute
paths; a data brief names every sim-track test that pins a value it changes).
11. **Bug statuses (changed at this ACCEPT):** the dev may not edit `studio/**`. The dev's report names the status it proposes
    for each bug it touched; QA sets the status of what it verifies; the Producer sets the rest at ACCEPT. Briefs say "status
    proposed by the dev, set by QA".
12. **Content-independent sim tests:** a sim task whose tests read `game/data/factions/**` content writes them against its own
    scene data or property checks, never a shipped count (BUG-0571: ten tests pinned a one-step placeholder the data track was
    replacing the same session).
13. Restore a file checked out from another commit with `git checkout HEAD -- path` (`git checkout <commit> -- path` stages it).
14. The sim's budget line is a line: M5-2 shipped ~3,200 against ~1,500 by not slicing item 5. Say "slice 5 and 6 to the next task
    at 1,500" and check it at ACCEPT.

## Sim track

### Next task candidate: M5-3, the AI fights and scouts · feature · QA full · ~1,500 lines incl. QA (slice: scout + attack first)

1. **Scout manager** first (the wood starvation): at match start one worker (or the cheapest unit) walks a fixed sweep (the map's
   quadrants from the own hall outward, or toward the mines the generator lays out) until forests, mines and the enemy's hall are
   known; the Blackboard keeps last-seen enemy buildings and the last-seen enemy army position (M5-2 deferred both). Through
   `Command.Move` only. Then the economy manager has a forest to send workers to.
2. **Military manager:** army = the production manager's units; rally near the own hall (a point on the side toward the enemy once
   known); the attack wave when `attack.minArmyPop` and `minArmyCost` are met: attack-move on the nearest known enemy building; retreat
   (a `Move` home) when the wave has lost `retreat.lossFraction` of its pop; Hard's `pullBackWounded` sends a unit under `hpFraction`
   home; `focusFire` as the data flag says (or M5-4). Rebuilding a lost hall / house through the executor's `build` path.
3. **`ai.json` + `rules.json` `ai` into `ContentHash`**, one golden regen (`k` lines equal: no AI seat in the golden; both files' values
   change the hash, `DataContentHashTests` rows). The data track edits no `ai.json` this session.
4. **BUG-0574** (S3) in the economy manager's marks (directions in the bug); **Requests 36 a:** composition weights by pop (weigh
   `(live + queued) x unitPop / weight`, or document counts).
5. Out: Easy / Normal / Hard tuning (M5-4), the ability manager, scouting ramps / high ground before attacking up (M5-4 if the
   budget runs out), the AI-vs-AI 20-minute test (M5-5; a short AI-vs-AI headless row (2 seats x 6,000 ticks on a generated map,
   both AIs gather wood and train an army) is in scope as the proof the AI no longer starves).

**QA focus:** the AI never reads hidden state while it scouts (the oracle with scouts walking); attack waves on a remembered building
that is gone; retreat loops (attack, retreat, attack) bounded; a wave against towers; 2 AI seats x 6,000 ticks x 3 seeds invariants
and playback; the hash regen's proof; think cost with an army of 200.

### Watch-outs
- `AiPlayer.Send` stops sending for the think when the queue is full: a military manager that sends one Attack per unit will hit
  `CommandCapacity` (408) with big armies; use attack-move on a group or batch per think.
- `Blackboard.NodeDropped` keyed by slot + generation never clears (BUG-0574).
- `AiPlayerTests` use their own quiet / short build orders (rule 12); keep it so.

## View track

### Next task candidate: M6-V2 + M5-V2, the lobby's seats and the sandbox AI seat · feature · QA standard · ~1,000 lines incl. QA

1. The setup's faction dropdowns (`GameData.Factions[f].DisplayName`) per slot and the AI slot's difficulty (Easy / Normal / Hard;
   `ui.json` keys under `setup`, the enum has no display names) → `SimConfig.Seats` built in `Match.Start` (`PlayerSeat(faction,
   SeatController.Ai, difficulty)` for slot 2, human for slot 1); `--ai <easy|normal|hard>` and `--faction <slot> <id>` dev flags so
   the sandbox scenes can run against the AI; the match end printed as a dev line when one side has no hall and no unit (not the
   victory screen: M6 criterion 2 needs a sim rule, a request).
2. **BUG-0580** (S3) folded in: clear held-button state on pause / resume; the two `QaShellTest` probes become `Check`s.
3. BUG-0581 item 1 (the "N × N" item from `ui.json`) and **item 4 first** (`game/tests/QaShellTest.cs(62,15)` CS8602: the one
   warning `main` carries after this integration; a null check); docs/03's smoke row says the gate runs both passes (QA's note).
4. A headless scene: lobby with the AI slot on Hard → the match ticks with an AI seat → the AI's first commands apply (count > 0 by
   tick 200) → the recorder's replay has `seat 1 whirlwind ai hard` and plays back with every seat human.
5. Out: 2-4 slots, colour, team (M6-V3), victory / defeat, save / load, settings. Expect a weak AI (no scouting) until M5-3 lands.

**QA focus:** a seat line for a faction with no `ai.json` (none shipped: a hostile data clone); the dropdowns' picks surviving quit to
menu; `--ai` with a junk value; the AI seat under the human's input flood (`QaShellTest`'s leak rows with an AI seat); 10 quit cycles
with an AI seat (the `AiPlayer` goes with the sim).

## Data track

### Next task candidate: inbox answers first, else D15, D14's QA rows + the AI economy check · feature · QA light · ~400 lines

1. Inbox answers (D13 ×1.1 / ×1.5; D14's table; D11 / D12 wordings) come first, as always. A ×1.5 revert is two values + the pins +
   one golden regen; a D14 tweak is one row in the file and the page table.
2. **`QA/Content/AiContentQaTests`** (the D14 attack list, never run): an id of the other faction, an Age II unit in the Age I
   composition, the same at + do + id twice, a `pop` not a multiple of 0.5, a `research` step for a tech no building of the faction
   researches, a deliberate one-line edit of each page table failing by step number, the composition percentages vs the pages'
   "Composition target" line, the wave cost vs the mix's cost (1,544 / 861 recomputed from `units.json`).
3. **The house-rule overlap** (STATE Requests 36 d): a headless row on the flat AI scene with D14's Malazan order: houses after
   N thinks vs the cap; report only (a proposal under For your review), no `ai.json` edit this session (the sim's M5-3 regenerates the
   golden with `ai.json` in the hash).
4. Files: `sim/Rts.Sim.Tests/QA/Content/**`, `docs/factions/**` (notes only). No `game/data/**` change. Then D-H4 (counter 4 / 4).

**QA focus:** the one-line-edit rows must fail by name on both sides (file and page); no row may read a count that M5-3's `ContentHash`
change would move.

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load (two this session); rerun alone before filing.
- `studio/bugs/README.md`: each track appends only its own rows (two conflicts this integration, both unions).
- The sim's M5-3 golden regen and the data track: **the data branch must not touch `ai.json` or `sim/Rts.Sim.Tests/Replays/`** this
  session; the view must not touch `DataContentHashTests.cs`.
- The view's M6-V2 reads `SimConfig.Seats` (on `main` now); the sim's M5-3 must not change `PlayerSeat`'s shape without a request.
