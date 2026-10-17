# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" (further down) is non-blocking: what the studio built or decided
on its own, explained in terms of what you'd see in the game.

_Last updated: 2026-10-16 (session 2026-10-16-1654 ACCEPT): **the view landed, the sim is held one round.** View **M6-V5**: **the skirmish setup takes 2-4 players** (Add player / Remove player; you are always slot 1, slots 2-4 are AIs each with its own faction, difficulty and colour; four bases on four start locations; Victory when you are the last standing, Defeat the moment your side is out while the AIs fight on, with Continue watching; the stats list every seat). Teams are not in yet (they need a sim change). Sim **M5-4f** (the proportional defense, quicker gathering, the house-step skip, the spell-targeting fix) is **built and reads right, but one stress-test row went red at the quicker gathering** (40 workers on a one-sided mine: a queued worker stands 3-4 s out of reach, BUG-0790): **held on its branch for its QA round; nothing of it is on `main` yet.** Data: STOP (waits for M5-4f). **Your 2026-10-14 assessment is still under For your review ("Producer assessment, 2026-10-14"; a pointer sits at the top of that section).** Next: sim M5-4f's QA round, view replay playback (or the look test), data waits._

## Waiting on you

- **Nothing blocking.** M5 2 / 9, M6 1 / 10 (criterion 1 advanced: 2-4 slots in, team left); counters **sim 1 / 4, view 1 / 4, data 0 / 4**; **no open S1 / S2**;
  golden `data-hash` on `main` **AB811AD47F68D0AE** (unmoved; the held sim branch carries C059AD620DE581F1). Next: sim M5-4f's QA round (BUG-0790 ruled first), view replay playback, data waits for M5-4f.
- **Where my comments on your 2026-10-14 feedback are:** the For your review entry **"Your review answers and first playtest ... (Producer assessment, 2026-10-14)"**:
  search this file for `Producer assessment, 2026-10-14`; a pointer entry at the top of For your review says where each item stands (all built; the proportional
  defense built and held for its QA round). Still wanting your yes / no: a **Shock pass**, the AI's **gold share** 0.6 → 0.5, and (new) **BUG-0780** ("accept BUG-0780" or "Catapult pass").
- **Wanted, not blocking:** (1) the answers above; (2) **a speed-benchmark check after a fresh boot** (BUG-0764: 17-20 benchmark rows fail on this PC on every branch
  alike, so they can't catch a regression): with nothing else running, `dotnet test sim/Rts.Sim.Tests --filter Category=Perf`, paste the "Failed:" count into the inbox;
  (3) **try on your desk**: edge-pan beside the second monitor, Settings → Keybinds (rebind Attack-move to K, play a minute), and **the new 3-4 player setup**
  (Skirmish → Add player twice → Start); (4) `CLAUDE.md`'s milestone line is fixed on the held sim branch and lands with M5-4f; (5) housekeeping: 20+ scratch worktrees
  incl. `producer-0847-merge` in the session scratchpad (`git worktree remove --force <path>`, then `git worktree prune`); (6) **before M6's art / export** (your downloads; docs/04): the fonts (Cinzel, EB Garamond), music, the Godot 4.7.2 .NET export templates.

## Now

| Field | Value |
| --- | --- |
| Sim: milestone | **M5 AI opponent, 2 / 9 criteria** (criteria 1-2 ticked; 3, 4, 5 advanced; **7 built, not landed**). On `main`: `PlayerView`, seats, the executor and six managers, the owner's win rule (M5-4e) + the collapse (M5-H2), `MatchOver`, start locations, `rules.json` `ai` 15 knobs, the surround rule (M5-P1 / M5-H2), the fog's storm mask, the AI's no-stacking casts. Golden format 6, `data-hash` **AB811AD47F68D0AE**, `k` lines unchanged since M4. **Held on `studio/2026-10-16-1654-sim` (8ac7b43), not on `main`: M5-4f** = the sized defense (`ai.defendRatio` 1.5 / `defendMinValue` 150 / `recallHallHp` 0.5), D15 C, BUG-0702 item 1, `gatherRate` 0.85 / 0.75 with the seed-21 re-record (`data-hash` there **C059AD620DE581F1**), the CLAUDE.md milestone line |
| Sim: next task | **M5-4f's QA round on the held branch** (requested at the 1654 ACCEPT; the next session's sim task if the conductor didn't run it): QA full as the brief planned (the sized-answer rows through the command log, a second seed-21 recording diffed, Perf A/B alone, the `EconomyManager` cache's identical picks, the `defendMinValue` 5,000 re-stagings checked) **+ a ruling on BUG-0790** (the red `GatherPocketStressTests` `(MineOneSide, 40)` row: a worker with a free spot in reach that stands = a sim fault → the row skipped with the id, S3 to M5-H3; a full face = queueing → QA re-stages the check with the reason); the dev idle unless QA files. Then the Producer's ACCEPT, the merge (the whole scene loop on the merged tree), the data track's re-print. After: pathing slice 2, M5-4c slice 2, Requests 39 (+ 38), **Requests 42 (the save format)**, D14 b, M5-4d, D14 c, M5-5; M5-H3 (BUG-0764 first) after four. Brief in `studio/handoff.md` |
| Sim: gate | **GO** |
| View: milestone | **M4 views Done; M6 1 / 10** (criterion 4 ticked 2026-10-16; **criterion 1 advanced 2026-10-16: 2-4 slots in, only team left**). M5-V1, M6-V1-V4, M6-VH1, M6-V6, M6-V7, M6-V8, M6-VH2, **M6-V5** on `main` (Add player / Remove player, 2-4 slots, an AI per slot with its own faction / difficulty / colour, `--players` + per-slot `--ai`, four bases on four start locations, the N-seat banner / stats; BUG-0770 closed). Criterion 1 waits on team (Requests 38); criterion 2 on Requests 39's columns; criterion 3's save half on the sim's save format (Requests 42) |
| View: next task | **Replay playback (M6 criterion 3's replay half; the Producer confirms at PLAN against the look test)** (counter 1 / 4 → 2 / 4) · QA standard, ~800 lines: every shell match writes `user://replays/<date>-<seed>.replay`; Main menu → Load Game lists them (header columns) → Watch re-runs the match from the file's seed / seats / commands through the public `Rts.Sim.Replays` API, checkpoints compared as they pass (a mismatch = `ui.json`'s data-mismatch text), the camera free, no orders accepted, the banner at the end; `--replay <path>`; `ReplayPlaybackTest` + `ShellTest` rows. View-only. Then the look test (the owner's packs are in `asset-sources/`), save / load when Requests 42 lands, teams (38), the stats columns (39), export (the owner's templates). Brief in `studio/handoff.md` |
| View: gate | **GO** |
| Data: milestone | M3 Done (D1-D4); D5-D8, D-H1-H5, D10a-c, D11-D19 on `main` (**D-H5**: report nits, the D18 reports re-run: "wood binds at some marks only, not the limit overall"; no data change). Golden `data-hash` **AB811AD47F68D0AE**. Counter **0 / 4** |
| Data: next task | **An inbox answer first** (D19's table, the Shock pass, `goldShare` 0.6 → 0.5, **BUG-0780** accept / Catapult pass) · QA light; **else STOP cheaply** (no schema waiting; M5-4f not landed yet). The session after M5-4f lands: re-print the D-H5 spending paragraphs + the house notes (D15 C) + both pages' Defense lines re-worded for the sized response (1.5 / 150 / 0.5 quoted), ~200 lines. Brief in `studio/handoff.md` |
| Data: gate | **STOP unless the inbox has an answer** (resumes by itself the session after M5-4f lands) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | **1654 ACCEPT (Producer):** view branch a898967 build **0 warnings**, **smoke PASS** both passes (seats line unchanged: "seats malazan human, whirlwind ai normal"), `FreeForAllTest` 6.8 s / `QaM6V5Test` 28.9 s / `AiSeatTest` 11.7 s / `ShellTest` 7.1 s **PASS alone**; `git diff -- sim/` empty, so the view branch is the merged tree (the sim held). Sim branch 8ac7b43 build 0 warnings; the task's slice **407 / 407**; **the red row reproduced** (`GatherPocketStressTests` `(MineOneSide, 40)`: seeds 2 / 8, 68 / 84 ticks, bound 40; green on `main` at the old rates); the dev's full non-Perf run twice 6,446 / 1 failed / 7 skipped, Perf A/B clean (`AiPerfTests` 0.77 / 0.77 ms). QA (view): non-Perf 6,417 / 6,425, 0 failed; **loop 61 / 61**; smoke PASS; Perf red under load with an empty sim diff (not a finding). QA (sim): not run (the round). **Previous: 0847 ACCEPT:** scratch merge of all three built 0 warnings, the full non-Perf suite 6,417 / 6,425 (0 failed), smoke PASS, loop 59 / 59 |
| Tests | **1654:** Producer: both diffs read whole (the sim's `DefenseManager` / `Blackboard` / `MilitaryManager` / `AiPlayer` / `BuildOrderExecutor` / `AbilityManager` / `EconomyManager` / `Data/*` / `rules.json` / CLAUDE.md / docs 01-03 / `ProportionalDefenseTests` and every test hunk; the view's `LaunchOptions` / `Match` / `SimRunner` / `SkirmishSetup` / `SetupText` / `Main` / `ui.json` / `AiSeatTest` / docs 02-03); the runs above. QA (view): a 1,168-row flag matrix, a 516-row setup matrix, a 3,000-stream `--colour` fuzz against an independent oracle + a 3,000-step setup fuzz, 29 real menu-args starts (each hall on its start location, > 20 m apart, each seeing a mine, distinct colours), a 4-seat hash twin (3 AIs) 24 / 24 checkpoints to tick 2,400, minimap dots per seat, three extra match-over paths, junk through the real `Main`, `settings.cfg` md5 unchanged with a rebound key. **Previous: 0847** (its session log) |
| Open bugs | **S1: 0, S2: 0**, S3: 14, S4: 15. **New open (sim, Producer-filed from the dev's report):** **BUG-0790** (S3: the `GatherPocketStressTests` row red at `gatherRate` 0.85 / 0.75; sim crowding fault or the check's geometry, QA rules in M5-4f's round), **BUG-0791** (S4: the executor repeats a failing site search every think; the storms fuzz's measured think 0.5 → 1.6 ms, ungated). **Closed:** view ~~BUG-0770~~ (S4, M6-V5). **Fixed on the held sim branch (closes when it lands):** BUG-0702 item 1. Still open: BUG-0764 (S3, Perf gates), BUG-0780 (S3, the owner's call), BUG-0144 (S3), BUG-0113 item 2, BUG-0704 items 2-3, BUG-0550 item 2, BUG-0521 items 2-3, BUG-0281 item 2, BUG-0663 (S4) |
| Sessions today | **2026-10-16: 2 / 8 done** (0847, 1654). Counters after: **sim 1 / 4, view 1 / 4, data 0 / 4** |
| Last session | 2026-10-16-1654 · sim M5-4f (feature, QA full **not run**: the dev stopped BLOCKED on one red QA row; **REJECT = held for its round**, branch pushed) · view M6-V5 (feature, 0 rounds, QA PASS, ACCEPT, merged alone) · data STOP (planned) · BUG-0770 closed, BUG-0790 / 0791 filed; golden on `main` unmoved; M5 2 / 9, M6 1 / 10 (criterion 1 advanced) |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 (sim track) | 8 / 8 | **Done** 2026-10-06 (Producer sign-off after the M1-9 hardening; retro in docs/05) |
| M2 (view track) | 10 / 10 | **Done** 2026-10-07 (Producer sign-off after the M2-H2 hardening; retro in docs/05; your playtest under For your review is feedback, not a gate) |
| M3 (all three tracks) | 8 / 8 | **Done** 2026-10-08 (Producer sign-off after every track's hardening and the BUG-0146 fix; retro in docs/05; open S3 / S4 listed there; your ten-minute playtest is still wanted as feedback) |
| M4 (all three tracks) | 10 / 10 | **Done** 2026-10-11 (Producer sign-off in session 2026-10-10-2334 after every track's end-of-M4 hardening (M4-H3, M4-VH3, D-H3); D12, the data track's last M4 content, landed in session 2026-10-11-0314; coverage ✅ Unit / fuzz / Determinism on every M4 row; retro in docs/05 with the open S3 / S4 carried; your windowed run of the sandbox battle is feedback, not a gate) |
| M5 (sim, with a small view part and the data track's `ai.json`) | 2 / 9 (criteria 3, 4 and 5 advanced; three owner-requested criteria added 2026-10-14) | **In progress** since 2026-10-11 (M5-1 to M5-4e and the M5-H1 hardening on `main`: `PlayerView`, seats, the executor / economy / production / scout / military / defense / ability managers (criterion 2 ticked 2026-10-13), the AI data in the hash, the generator's start locations, the defeat rule + `MatchOver` (M5-4b), **the owner's win rule (M5-4e, 2026-10-14)**; D14's real build orders, D15's rows, D16's difficulty pins, D17's wording, D18's measurements; criterion 3 waits on expansion / rebuilding / high-ground scouting / hunting the last units (M5-4d), 4 on the three Hard knobs (M5-4c slice 2), 5 on M5-5's 20-minute test, 6 on the owner's play, 7-9 on M5-4f / D14 b / D14 c) |
| M6 (view, with the sim's seats and the owner's downloads) | 1 / 10 (criterion 4 ticked 2026-10-16; criteria 1, 2 and 7 advanced) | **Started ahead** 2026-10-11 (M6-V1: main menu, skirmish setup with seed / map size, pause menu; M6-V2: faction and difficulty per slot, `--faction` / `--ai`; M6-V3: a colour per slot, the menu's match as a hall + workers; M6-VH1: colours unique per match and corpse-legible; M6-V6: the settings screen minus keybinds; M6-V4: the Victory / Defeat / Draw banner and the stats screen with today's counters; M6-V7 (2026-10-14): the cursor confined during a match + its setting, control groups hold a building; **M6-V8 (2026-10-16): keybinds (Settings → Keybinds, 31 keys, `settings.cfg` `[keys]`) + the resource click: criterion 4 ticked**; **M6-V5 (2026-10-16): 2-4 player slots** (Add player / Remove player; each AI slot its own faction, difficulty and colour; four bases on the generator's four start locations; `--players`, per-slot `--ai`; BUG-0770 closed): criterion 1 waits only on team (Requests 38); criterion 2 on Requests 39's columns (gathered, trained, built / lost, Age II time, the army graph); criterion 3's replay half is the view's next task, its save half the sim's Requests 42; criterion 7's generator start locations landed with the sim's M5-4a / M5-H1, the three hand-made maps and mirrored terrain left) |
| M7-M9 | — | Planned |

## For your review

Non-blocking. Each entry says what was built or decided, what you'd notice in the game, and how
to change it. To change anything, write it in `studio/inbox.md`, for example "use formations
instead of clusters" or "make giving up take 2 seconds".

### Where my comments on your 2026-10-14 feedback are (pointer, Producer, 2026-10-16)

You asked mid-session where the Producer's take on your review answers and playtest notes went. It is the entry **"Your review
answers and first playtest: what changes, where each item lands, the D14 verdict, and a design for a defense that pulls home only
what a raid is worth (Producer assessment, 2026-10-14)"**, further down this section (search this file for `Producer assessment,
2026-10-14`; newer entries are added above it, so it moves down one entry per task). Its items today: Easy casts, ×1.5, the
building-damage set, the win rule, the cursor confinement, building control groups, the resource click, keybinds and the
surround-a-building pathing are **built**; the proportional defense (M5-4f, with quicker gathering) is **built and held on its branch for its QA round** (2026-10-16-1654; nothing of it on `main` yet);
the variable strategies and counter-composition are M5 criteria 8-9 (after M5-4f); the Ages → tech-tree design is after M5's AI
work; the two proposals (a Shock pass; the AI's gold share) still want a yes / no from you. That entry stays where it is until you
acknowledge it in the inbox.

### You can now play against two or three AIs at once: the skirmish setup takes 2-4 players, each AI with its own faction, difficulty and colour (view track, M6-V5, 2026-10-16)

**What was built.** The skirmish screen has two new buttons, **"Add player"** and **"Remove player"**. The setup opens with
two slots as before (you and one AI); Add player shows a third and a fourth row, each an AI with its own faction list,
colour list and difficulty list (Easy / Normal / Hard, Normal by default). You are always slot 1. Colours stay unique: each
list greys out the colours the other players hold, and if you pick a taken one the game moves you to the first free colour.
A removed slot remembers its picks if you add it back. Start builds a base for every player on its own start location (the
map generator already made 3 or 4 mirrored start spots, each with two gold mines and a forest in reach), so a 4-player map has
four bases a quarter turn apart.

**What you'll see.** Skirmish → Add player twice → pick, say, Whirlwind Hard, Malazan Easy, Whirlwind Normal → Start: four
Town Halls with five workers each, four colours on the minimap and on the units. It is a free-for-all: the AIs fight each
other as much as you. The banner reads **Victory** only when you are the last side standing; **Defeat** the moment your side is
out, even while the other AIs are still fighting, with **Continue watching** to see who wins; **Draw** if the last sides fall
together. The stats table lists every seat with its time of defeat. From the command line: `--players 4 --ai 3 hard` (the
old `--ai hard` still means slot 2, so nothing existing changed).

**Checks.** QA ran 1,168 flag combinations and 516 setup combinations, two 3,000-step fuzzes of the colour rule, 29 real starts
on three map sizes (every hall on its start spot, more than 20 m apart, each seeing a gold mine), a 4-player match run twice to
the same fingerprint at every checkpoint, and the three match-over paths above. No bugs found. The two test scenes that still
read your real `settings.cfg` were moved onto their own throw-away files (BUG-0770 closed; your file's checksum was unchanged
over the whole run with a rebound key in it).

**Rough edges.** **No teams yet**: the "team" column from the design needs a small sim change first (Requests 38; the AI would
also need to know who its allies are), so every match is free-for-all. The dev-only `--units` army flag only arms players 1 and
2 (3 and 4 get a base and no army; documented). One cosmetic warning in the log says "Ignoring --colour: slot 2 ..." even when
slot 2 never asked for a colour (the next view clean-up, M6-VH3).

**How to change it.** The button labels are `game/data/common/ui.json` (`setup.add_slot` "Add player", `setup.remove_slot`
"Remove player"). The default difficulty for new AI slots is Normal (an inbox line changes it). If you want the slot cap higher
than 4, that is the map generator's start-location limit (sim); say so in the inbox.

### When a side is knocked out its leftover buildings fall with it, a soldier pinned against a cliff by idle friends slides free, and the AI stops stacking sandstorms (sim track, M5-H2 clean-up, 2026-10-16)

**What was built (a clean-up: no new feature, no number changed, the recorded matches unmoved).**
1. **The collapse.** Under your win rule a side is out when it has no unit left and nothing that can train one, but its houses,
   towers, Forge and half-built sites used to stand on and keep working: its tower kept shooting and scoring kills, its research
   finished. Now, on the tick a side is out, every building it still has crumbles like a razed one: rubble on the ground, the
   ground reopens, anything queued in it is gone.
2. **Rings shared by two sides.** Two players attacking one building (a 3-4 player match later) used to read each other's
   standing spots as taken; each side now has its own ring, and a spot is judged by who is actually standing on it. A sealed
   pocket's unreachable spots are no longer tried first (attack-moving into a walled House: 9 give-ups → 0).
3. **The pinned Sapper.** The M4 playable scene's Sapper stood still through eight right-clicks because the army had been
   pulled back on top of it against a cliff: the sidestep round idle friends cancelled its way along the wall. A unit in that
   spot now slides on past them (replayed in a bare sim: 0.07 m in 8 orders before, 55 m after).
4. **The AI's spells.** Three Priests no longer drop three sandstorms on one spot in one think (one storm, the others keep their
   casts; a storm that never landed is retried as before), and the AI's thinking no longer slows down with every live storm (8
   storms: 1.1 ms → 0.27 ms per think). A site no worker can reach is given up on after three tries instead of draining the
   economy forever.

**What you'll see.** Win a skirmish against the AI: the moment its last soldier and its last unit-making hall are gone, its houses,
towers and sites all turn to rubble at once instead of standing as a ghost base you'd have to hunt down, and no tower keeps
shooting your army after the banner. The end-screen counts: a collapse is **neither a kill for you nor a loss for the AI** (see the
decision). Pathing: an army parked on top of a unit against a cliff no longer freezes that unit. Spells: an AI with several
Priests spends its storms on different clumps rather than one.

**Producer decision (owner may revisit).** A collapsed building counts as neither a kill nor a loss in the post-match stats: the
stats count what the enemy destroyed, and a collapse is the consequence of a defeat already decided. The alternative, crediting
them to the winner, would pad kill counts by a whole base in every win. One inbox line changes it ("count collapses as kills").
Two smaller closures: BUG-0651 (one unit of 1,000 gave up a reachable building at twice the supported crowd) **no longer
reproduces** on today's chase code and is closed with its test kept as a guard; a defense radius covering the whole map is
documented as a legal "turtle" play style, not a bug.

**Rough edges.** BUG-0702 item 1 (a mage with two enemy groups either side casts at neither; goes with the proportional defense
task), BUG-0704 items 2-3 (wording and two test rows weaker than their names; next sim clean-up), BUG-0144 and BUG-0113 item 2
not reached again, **BUG-0764 (new, S3): 17-20 speed benchmarks fail on this PC on every branch alike, so they can't catch a
regression until re-baselined** (a fresh-boot check from you is wanted, see Waiting on you), and `CLAUDE.md` still says
"Current milestone: M1" (one line; the next sim session fixes it unless you prefer to). Around a small House the last two or
three attackers still jam at the far corners (17-18 of 20): pathing slice 2. The memory bound of the biggest test map moved up
by 168 KB (one ring layer per player plus the fog's storm mask), itemised.

### The keybinds file refuses look-alike combos, test scenes stop sharing one settings file, and the M4 playable scene runs on a fixed clock (view track, M6-VH2 clean-up, 2026-10-16)

**What was built (a clean-up: nothing new on screen).** A hand-edited `settings.cfg` key such as "Meta+K", "Cmd+K" or "Kp+K" used
to be read silently as plain K; it is now one reported problem and the default key, and a key value that can't survive a save
is refused up front (1.1 million codes checked, 261 real keys, none lost). The Settings screen greys out VSync and shows the real
state when a developer flag forces it, and never writes that flag into your file. The benchmark now frames the whole army before
boxing it (seed 6's column used to be half off screen). The "Last seen" amount on a clicked mine is exact at 2x speed. The two
settings test scenes each use their own folder, so two test runs at once can't trip each other; and a start location by the
map's west or north edge no longer puts a hall across the map (hand-built maps only; the menu's maps were never affected). The
M4 playable test scene now advances the clock by a fixed 1/60 s per frame, so every click lands on the same tick however busy
the PC is: the two "failed once under load" cases (BUG-0745) are closed, together with the sim's fix above.

**What you'll see.** Nothing different in play. If you ever edit `settings.cfg` by hand and mistype a key, the game tells you
which one and uses the default.

**Rough edges.** BUG-0770 (S4): two test scenes still read your real `settings.cfg`, so a rebind on your desk could fail them
(the game itself is unaffected; next view clean-up). BUG-0744 item 3: one bad hand-edited key can reset a whole row of grid keys
(documented as the chosen behaviour). A side effect worth knowing: the seed-6 benchmark runs at a wider zoom now, so its frame
times aren't comparable with earlier runs.

### Reports re-run on the new numbers: wood is no longer the AI's limit overall, and the Catapult is under twice a Raider's damage per cost against halls (your call) (data track, D-H5 clean-up, 2026-10-16)

**No number, name or description changed; the data fingerprint is unmoved (AB811AD47F68D0AE).** Three report methods were
tightened (the siege ratio is now checked against both line units, the AI spending report stops counting once the idle side has
lost, and it runs on the menu's own map with 8 mines), a self-check that had gone dead was repaired, and the kill-time report now
fails loudly if a building "falls" by the new collapse rule instead of being razed (it had silently measured a 0.1-second
"kill" of a lone House: BUG-0761, S2, found by the sim's QA and fixed by the data track in its fix round; today's numbers are
real razings again).

**What the measurements say now (20 attackers, no defenders, flat ground; damage per gold spent, relative to a line unit):**

| Siege group | vs 20 Heavy Infantry: House / hall / Town Hall | vs 20 Raiders: House / hall / Town Hall |
| --- | --- | --- |
| 2 Catapults (700 gold) | 3.9× / 2.3× / 2.3× | 2.8× / **1.7×** / **1.6×** |
| 2 Sappers (224) | 5.7× / 4.8× / 4.8× | 4.1× / 3.5× / 3.3× |

Kill times: 20 Raiders raze a House / hall / Town Hall in 9.8 / 19.5 / 35.8 s (10 Raiders: 13.8 / 33.0 / 62.6 s); the band you
set (20 / 60 / 120 s) holds 12 / 12 and the "siege at least 2× a Heavy Infantry" rule holds everywhere. **BUG-0780 (S3, your
call):** against the Raider, the cheapest and fastest line unit, the Catapult is only 1.7× / 1.6× as cost-efficient on a hall
and a Town Hall. Options: (a) **accept** (my recommendation for now: the Raider's own speed and price make it the outlier, and
a Shock pass, if you order one, moves it anyway); (b) a **Catapult pass** (cost 700 → about 600 for the pair, or damage up about
20 %, measured first). Write "accept BUG-0780" or "Catapult pass" in the inbox. The AI spending report: on the menu's map the
AI now razes an idle base at 8-10 minutes, so only the 5-minute mark counts, and at 1 of those 4 marks it had wood under 100 with
gold over 300: "wood binds at some marks only, not the limit overall" (D18 read "still the limit" with 9 of 16, counting marks
after the win). A thin sample, so the `goldShare` 0.6 → 0.5 proposal is still a proposal, not a finding.

**Rough edges.** None open for the data track but BUG-0780 above. Process: the regen-reason convention for the recorded matches'
header is written down (BUG-0750 closed).

### Your soldiers now surround a building instead of queueing behind each other: 17-20 of 20 strike within ten seconds, where 9-11 did before (sim track, M5-P1 "pathing slice 1", 2026-10-15)

**What was built.** Your playtest note 5. Every building now has a ring of standing spots round its walls (two per wall cell on each
side, one off each corner: 20 round a House, 28 round a hall, 36 round a Town Hall). A melee soldier sent at a building takes the
nearest spot nobody of yours holds and walks there; if every spot is taken it waits just behind and grabs the first one a death or a
retreat frees. Nobody gives a building up any more because it is "stuck behind friends": an order on a reachable building keeps on
in every mode (your right-click, attack-move, the AI's waves). Archers and crossbowmen are unchanged (they stop at range anyway).

**What you'll see.** Select 20 Heavy Infantry, right-click an enemy hall: they spread round three or four sides and almost all of
them swing within ten seconds (measured: House 17-18 of 20, hall 20 of 20, Town Hall 19-20 of 20; before, 9-11 of 20 and the rest
shuffled behind). Attack-move an army past a lone House: it is razed with nobody walking off. Combined with the data track's faster
building damage (next entry), a 20-man line now razes a House in well under 20 s instead of over a minute.

**Producer decision (owner may revisit).** The spots a soldier "holds" steer the next step but are left out of the match fingerprint
(the hash that proves two runs are identical), because putting them in would change every recorded checkpoint for no change in play.
The cost: when save / load arrives (M6), the save must store those spots too, and a later test must compare them directly. Recorded
in the technical doc; the alternative (hash them now) is a one-session change if you prefer belt and braces.

**Rough edges.** Round a small House the last two or three attackers can still jam at the far corners (17-18 of 20, not 20); routing
round your own standing archers is the next pathing slice. Two small nits (BUG-0732: a sealed pocket's unreachable spots are tried first
in a test-only layout; with three or more players a rival's spots count as taken) go to the next sim clean-up. **The faction pages'
siege-vs-infantry times moved with this rule and are re-printed by the data track as this lands** (BUG-0734; no change in play).

**How to change it.** An inbox line: "two spots per cell is too tight" (one constant), "hash the spots now" (one session), or a
different fill order ("far sides first").

### Click a gold mine or a tree to see what is left, and every hotkey is rebindable from Settings → Keybinds (view track, M6-V8, 2026-10-16)

**What was built.** Your playtest note 8: a left click on a mine or a tree selects it alone (your units are deselected, no order is
sent) and the selection panel shows its name and "2500 Gold left" / "40 Wood left", live while any of your units or buildings can see
it. For a node in explored but unwatched ground it shows "Last seen: 2498 Gold left", the amount from the last time you saw it; a node
you see emptied or felled drops out of the selection. Also M6's keybinds: Settings has two tabs now, General and **Keybinds**, with a
row for each of the 31 keys (Attack-move A, Stop S, Hold H, Move M, Tab, the B / V build menus, control groups 1-9 and the 15 command-card
grid keys). Click a key, press the new one; a key another command uses is refused with a line saying which; "Default keys" puts them
back. Saved in `settings.cfg` under `[keys]`; the command card's hints follow your keys. Esc, Shift / Ctrl / Alt, the arrow keys, F10
and F12 stay fixed; mouse buttons and the camera keys aren't rebindable yet.

**What you'll see.** Start a skirmish, click the gold mine by your hall: "Gold Mine / 2500 Gold left", counting down as your workers
mine it. Walk every unit away and watch the fog dim it: "Last seen: ...". Open Settings → Keybinds, click Attack-move, press K: A does
nothing now, K + click attack-moves; try to bind Stop to K and the line says K is already Attack-move.

**Rough edges.** BUG-0744 (S4): a hand-edited `settings.cfg` with "Meta+K" reads as plain K without a warning, and one bad hand-edited
key can reset a whole row to defaults (the tab itself can't produce either); BUG-0743 item 3 (S4): the "last seen" amount is sampled
once per frame, so at 2x speed it can be one gather older than what you actually saw. Both in the next view clean-up. BUG-0745 (S3):
one scripted playtest scene (the Sapper's Cusser step) failed once while three test suites shared the PC and passed alone; the sim
checks its saved replay next session to tell a test-timing fault from a real movement fault.

**How to change it.** The key names and the "N Gold left" / "Last seen:" wording are in `game/data/common/ui.json` (`keys.*`,
`hud.resource_left`, `hud.last_seen`); your own keys live in `settings.cfg`. An inbox line for anything else ("make the camera keys
rebindable", "show the amount on the minimap").

### Your three numbers are in the game: buildings die three to five times faster, the line units' anti-cavalry bonus is back to ×1.5, and the Easy AI casts spells (data track, D19, 2026-10-15)

**What changed (every number, with why).** All three are your 2026-10-14 decisions; the data track changed 27 lines in four data files
and nothing else.

| What | Field | Old → new | Why |
| --- | --- | --- | --- |
| Heavy Infantry, Raider (both line units) | bonus vs Mounted | ×1.1 → **×1.5** | Your call: D13's cut to ×1.1 barely moved the Line-v-cavalry fight; the template value is back |
| Easy AI, both factions | uses abilities | no → **yes** | Your call: Easy casts too (it still thinks slowly and attacks with small waves) |
| Damage table, Structure column | melee multiplier | 0.4 → **0.7** | Lever set (C) from D18's kill-time report (your note 6: buildings took 3-5 minutes) |
| Damage table, Structure column | pierce multiplier | 0.2 → **0.35** | same |
| Damage table, Structure column | magic multiplier | 0.4 → **0.6** | same (siege stays 3.0) |
| Town Hall, Watch Tower (both factions) | armor | 5 → **2** | Lever set (C): every building's armor halved, rounded down |
| Infantry / Ranged / Shock / Caster Hall, Forge, Siege Works | armor | 4 → **2** | same |
| House, Camp | armor | 3 → **1** | same |

No name or description changed. The design doc and both faction pages carry the new numbers and the history in one clause each.

**What you'll see.** With the new numbers **and** the surround rule above (measured together on the merged tree): 20 Heavy Infantry raze
a House in **12 s** (was 72 s), an infantry hall in **23 s** (was 203 s), a Town Hall in **45 s** (was over 5 minutes); 20 Raiders 10 / 19 /
36 s. (The data track's own measurement before the surround rule landed read 16 / 44 / 69 s; the pages are re-printed with the merged
numbers as this lands.) The kill-time band I set (a House within 20 s, a hall within 60 s, a Town Hall within 2 minutes; siege at least
twice as cost-efficient) is met in all 12 cells. Siege is still the fastest way to raze, but by less: a Catapult razes a Tent in 56 % of
the time the same cost of Heavy Infantry needs (it used to be 9 %); against a hall or a Town Hall two Catapults are 2.3x the infantry's
damage per cost (the band's floor is 2x: watch this one if the owner wants siege to matter more). Crossbowmen barely hurt halls (their bolts sit on the 1-damage floor), by
design. A Heavy Infantry hit on a Horse Raider does 15 instead of 11; the Line-v-Shock fight reads 94 / 88 / 100 / 95 % of cost kept
(still above the 40-65 % band, as D18 predicted). An Easy AI's Cadre Mage or Priest now casts at the first chance it sees a group.

**Two proposals for you (one inbox line each, nothing applied).** (1) **A Shock pass:** D18 showed no line-unit number brings the
Line-v-cavalry fight into the band, so the lever is on the cavalry side: sweep the Horse Raider's and Wickan Lancer's hp and cost
upward (a measured pass like D18's, then your pick). Say "do the Shock pass". (2) **The AI's gold share** (`goldShare` in `ai.json`,
0.6 → 0.5): the AI is still short of wood and long on gold in every report. Say "lower the AI's gold share" (the sim track's
`rules.json` task would carry it with the `gatherRate` change).

**Rough edges.** BUG-0750 (S4): the Andii page still quotes "115 vs a Town Hall" (118 now), one old "armor 5" line in the technical
doc, two tests that can no longer fail (they checked the levers against the old shipped values); the next data clean-up. The golden
replay's data fingerprint moved once (C6FC120A55AE4A0B → AB811AD47F68D0AE); every recorded checkpoint is unchanged.

**How to change it.** The numbers are in `game/data/common/damage_table.json`, `game/data/factions/*/buildings.json`, `*/units.json`
and `*/ai.json`; or an inbox line ("armor back to 4 on the halls", "Easy doesn't cast").

### The win condition is yours now: a side is out when it has no units left and nothing that can train one; houses, camps, the Forge and towers don't have to be hunted down (sim track, M5-4e, 2026-10-14)

**What was built.** Your playtest note 9, as the rule. A player is out the moment it has had a building, has **no unit alive** and
**no finished building that trains units** (the Town Hall or any of the five unit halls; for the Whirlwind that includes the Holy
Camp, Raider Camp and Archer Camp). Houses, the drop-off camp (Depot / Supply Cache), the Forge, towers and half-built sites keep
nobody in. A summoned unit counts while it lives. A side that never had a building still can't lose (the test sandboxes). The
end-screen, the stats and the AI going quiet all key off the same flag, so nothing else changed.

**What you'll see.** Raze the enemy's Town Hall and every hall while one of their workers is still alive: no Victory yet; kill the
worker (or it dies trying to rebuild): Victory on that tick. Lose your own hall but keep workers and money: you can still rebuild
and are not out. Win while the loser's houses and towers still stand: the banner shows; the houses stay for now (see the decision
below). The dev console line now reads "Match over: player 1 is defeated".

**Caveat you already know.** As in Age of Empires, a last hidden worker keeps a side alive; the AI's waves target buildings, so an
AI-vs-AI match can now stall on one until the sim's M5-4d adds "hunt the last units". If that annoys you in play, an inbox line "a
side with no hall for 3 minutes is out" is a 20-line rule.

**Producer decision (owner may revisit).** The question the new rule opened: what do a loser's remaining buildings do? Today they
keep acting (its towers shoot and score kills, its Forge finishes research), which in a 3-4 player match would let a player who is
already out knock someone else out. I chose: **when a player is out, its remaining buildings collapse on that tick**, like razed
ones, nobody credited the kill (recorded in the design doc; built at the sim's next clean-up session, BUG-0703). The alternatives
were "they stand as inert ruins" (a special state every system must know) or "they fight on until razed" (the knock-out problem).
To change it: an inbox line "the loser's buildings stay as ruins" or "they keep fighting".

**Rough edges.** BUG-0703 (S3, above, next sim clean-up); BUG-0704 (S4, test wording and two weaker test rows, same session). Two
bugs QA found in the fix round are closed: 14 old test scenes placed a House before any unit and would silently have been "out"
(BUG-0705 / 0706; they now keep a worker alive).

### Edge panning works beside a second monitor, control groups can hold a building, and the match-over screens were restaged for the new win rule (view track, M6-V7, 2026-10-14)

**What was built.** Note 1: the game saw no cursor at the edge because the mouse had left the window onto your second monitor. Now,
**while a match runs** (unpaused, window focused) **the cursor stays inside the window**, so every edge pans; the pause menu, the
match-over banner, the settings screen, the main menu and Alt-Tab all free it. A Settings row **"Confine cursor to window"** (On by
default) turns it off. The very last pixel column / row counts as the edge, so a cursor pinned at the border pans. Note 7: Ctrl + a
digit with a **building** selected stores that building in the group (a group holds units or one building, never both); the digit
selects it again from anywhere, with its command card; a double-tap centres the camera on it; a razed building drops out of its
group. Also: the "K / L" line under the Victory banner now agrees with the stats screen (BUG-0710), and a dead piece of code is gone.

**What you'll see.** Start a skirmish, push the mouse toward the other monitor: the cursor stops at the window edge and the map
scrolls. Press Esc: the cursor is free to leave. Select your Billet, Ctrl + 1; walk the camera away; press 1: the Billet is selected
with its Train buttons; press 1 again quickly: the camera jumps to it.

**Please try it once (wanted, not blocking).** The confinement itself (Windows' cursor clip at the window edge, release on Alt-Tab)
could only be verified headless and by code read this session, because other tests shared the machine. One try on your desk: a
match, mouse to the second monitor, Esc, Alt-Tab. Say in the inbox if anything misbehaves (for instance a cursor stuck after a crash).

**Rough edges.** One test scene and two test rows (`AbilityViewTest`, `M6V7QaTests`; BUG-0711) were staged under the old win rule
(a building placed before its side has a unit, which now puts that side out): fixed at the merge by a small view round, or the view's
first item next session; no effect on the game. Keybinds are the next view task (with the resource click).

### Four measurements and no number changed: the trees near your base are fine, the AI is still short of wood, buildings take 3-5 minutes to kill today and one lever set fixes it, and the Line-v-cavalry fight can't be brought into the band by the line unit's own numbers (data track, D18, 2026-10-14)

**1. Trees near the bases (your note 2).** On every seed 1-7 of the 128 map each start location has **19-61 trees within 30 m**
(1,900-6,100 wood) and 21-85 within 60 m; the nearest tree is 16-20 m away. The menu's default match is seed 1: 23 and 22 trees
within 30 m, 41 and 62 within 60 m. So the start is not short of trees; what you felt is the forest **running dry** 10-15 minutes in
(the AI's report below shows the same). Levers if you want more: a second start forest per base (the map generator, a sim change,
~40 lines; say "second forest") or more trees per forest.

**2. The AI's wood on a real map (D17's re-measure).** Seeds 1 and 7, both factions, Normal, 20 minutes, the other side idle:
**wood is still the limit** (at 9 of 16 checkpoints the AI held under 100 wood with 300+ gold; e.g. Whirlwind seed 7 at 15 min: 1,918
gold, 85 wood). One lever is in the AI's own file: it sends too many workers to gold (`goldShare`); lowering it is one number per
difficulty. Say "lower the AI's gold share" and D19 includes it; otherwise it waits for your Easy / Hard play. **Update (D-H5,
2026-10-16):** re-run on D19's numbers with the surround rule, on the menu's own map (8 mines), the AI razes the idle base at 8-10
minutes, so only the 5-minute marks count: 1 of 4 was wood-bound, "wood binds at some marks only, not the limit overall". A thin
sample; the `goldShare` proposal stands as a proposal.

**3. How long buildings take to kill (your note 6), 20 attackers, no defenders, flat ground.**

| Attackers | House today → with (C) | Hall today → (C) | Town Hall today → (C) |
| --- | --- | --- | --- |
| 20 Heavy Infantry | 72 s → **16 s** | 203 s → **44 s** | over 5 min → **69 s** |
| 20 Raiders | 72 s → **14 s** | 203 s → **36 s** | over 5 min → **57 s** |
| 20 Crossbowmen | 54 s → 27 s | 131 s → 131 s | 263 s → 263 s |
| 2 Catapults (700) | 6 s → 6 s | 21 s → 21 s | 41 s → 41 s |
| 2 Sappers (224) | 14 s → 14 s | 32 s → 32 s | 65 s → 62 s |

The band I set (20 Age I line units: a House in 20 s, a hall in 60 s, a Town Hall in 2 min; siege at least twice as cost-efficient):
today 6 of 12 cells; **set (C) meets all 12**: the damage table's Structure column melee 0.4 → 0.7, pierce 0.2 → 0.35, magic 0.4 →
0.6 (siege unchanged), **and** every building's armor halved. (A) alone (the multipliers) reached 9 / 12, (B) alone (armor) 6 / 12.
Siege stays 3.5-9x more cost-efficient than infantry against buildings. Crossbowmen barely change against halls: their bolts sit on the
1-damage floor, which is the usual RTS rule (ranged units are poor against stone); say so if you want it otherwise. Only 9-11 of the 20
infantry even strike within 10 s today because they queue behind each other: that is the sim's next task (pathing: surround a building).
**D19 applies (C) next session unless you write another set in the inbox.**

**4. Line v Shock with ×1.5 back (D13's table).** The band (the winner keeps 40-65 % of its cost) is **out of reach of the line
unit's hp, armor and cost**: the Heavy Infantry keeps 88 % of its cost against Horse Raiders at ×1.1 already and 94 % at ×1.5; the
Raider keeps 89-100 % against Wickan Lancers. Of 60 sets swept (hp 100-140, armor 1-3, gold 50-80, the pairs), the nearest is hp
100 / armor 1 (63 % / 75 %), which would also weaken the line units against archers. The lopsidedness comes from the cavalry's own
numbers, not the bonus. **Recommendation:** D19 restores ×1.5 as you decided and leaves hp / armor / cost alone; if you want cavalry to be
less of a free kill for line infantry, the lever is a **Shock pass** (Horse Raider / Wickan Lancer hp, damage or cost), a data task of
its own: say "Shock pass" and it is D20.

**Also.** Both faction pages' Defense line says where fleeing workers run ("the nearest drop-off among those more than 16 m from the
threat"). No number, no name and no description changed; the data fingerprint is unmoved. Rough edges: BUG-0721 (S4: three report
method nits, no change to the conclusions), the data clean-up after D19.

### Your review answers and first playtest: what changes, where each item lands, the D14 verdict, and a design for a defense that pulls home only what a raid is worth (Producer assessment, 2026-10-14)

**Short version.** Nothing you asked for is out of reach, and most of it is small. Three things land this session (the new win
condition, the edge-pan fix with control groups for buildings, and the measurements behind the balance changes), the rest over the
next three or four sessions, in the order below. One change needs a caveat (the win rule can end with you hunting a last hidden
worker); one is deferred on purpose (dropping the Ages: design later, but the AI's new strategy files are written so that it costs
nothing extra); nothing is pushed back.

**Your answers, one by one**

| Item | Changes the plan? | What happens, where, when, size |
| --- | --- | --- |
| Easy casts too; aim for the densest cluster | Yes (reverses a Producer default) | Data: `useAbilities` true for Easy in both faction files + the design doc's Easy line (D19, next session, a few lines). Sim: the "densest cluster" target rule replaces today's "middle of the group" with the next ability pass (BUG-0702 item 1, with the proportional defense or the knobs slice; ~100 lines) |
| Match end screen OK; match can be lost OK; D11 / D12 OK; colours OK | No | Those reviews are closed; the entries stay until their milestone is signed off |
| The defense must not be trickable (a small raid recalls a wave) | Yes: new M5 criterion | Sim: **proportional defense**, designed below, built next session (M5-4f, ~700 lines + tests, QA full) |
| D13: back to ×1.5, then the hp / armor / cost pass with a table | Yes | Data: D18 (this session) sweeps Line hp / armor / cost with ×1.5 restored and prints the table; D19 (next) applies ×1.5 and the numbers (yours from the inbox, else the sweep's best) with one fingerprint update. Both pages re-printed |
| D14: variable strategies, attack and defence at once, counter-picking, vision-limited, think cost | Yes: three new M5 criteria (docs/05) | **Verdict below.** Sim + data over ~3 sessions each, interleaved with M5's remaining criteria |
| D15: option C | Yes (small) | Sim: the executor skips a house step once the population cap is reached; folded into M5-4f next session (~30 lines + a test). The faction pages' house notes updated by the data track after |
| D17: wait and re-measure | No (it was the default) | Data: D18 re-runs the spending report on a real generated map this session; a lever only if it still shows wood starvation |

**Your playtest notes, one by one**

| # | Note | Changes the plan? | What happens, where, when, size |
| --- | --- | --- | --- |
| 1 | Edge-pan fails on the second-monitor side | Yes (bug) | View, **this session**: the cause is that the mouse leaves the window onto the other monitor, so the game sees no cursor at the edge. Fix: during a match the cursor is **confined to the window** (released on pause, menus, settings and when the window loses focus), with a Settings toggle "Confine cursor to window" (default on), and the very last pixel counts as the edge. ~150 lines. You verify on your two-monitor desk |
| 2 | Not enough trees near the base | Maybe (seed or generator) | Data, this session: D18 counts the trees within 30 m and 60 m of every start location on seeds 1-7. If it is the generator, the sim's next hardening gives each base a second start forest (`MapGenParams`, ~40 lines); if it is the seed, nothing changes |
| 3 | Gathering a little quicker | Yes (numbers) | Data, D19 next session: gold 0.7 → **0.85**/s, wood 0.6 → **0.75**/s per worker (about +20 %), docs/02's Economy table, one fingerprint update. Say other numbers in the inbox if you like; it waits a session only because this session's sim task already moves the fingerprint file and two tracks may not |
| 4 | Drop the Ages; grow a tech tree instead | Yes (direction, later) | Recorded in docs/01's change log as a direction. **Deferred by design**: planned as a design task after M5's AI work (so the AI's build orders aren't rewritten twice), then data + a small sim clean-up (`Age` fields go, the stats' "time to Age II" column becomes "key tech time"). Until then the strategy files (D14 b) are written **age-agnostic** (an army mix switches on a tech or a time, never on "Age II"), so the drop costs no rework there. The `requires` schema already lets a building or tech unlock another, which is most of what a tree needs |
| 5 | Pathing: infantry stuck behind crossbowmen; units stuck behind each other at a building | Yes (two sim slices) | Sim, after the proportional defense: **slice 1 "surround a building"** (attackers pick a free side of the footprint instead of queueing behind friends; ~800 lines, uncertain, QA full), **slice 2 "flank through your own line"** (a melee unit whose path is blocked by own standing ranged units routes round them; the crowd-routing family BUG-0028 / 0032 / 0046). Each slice gets a measured before / after (how many of 20 attackers reach a hall in 10 s) |
| 6 | Buildings take forever to kill | Yes (numbers) | Data: D18 this session **measures** (20 Heavy Infantry, 20 Crossbowmen, 10 Raiders, 2 Catapults, 2 Sappers against a House, a hall, a Town Hall; today and under candidate levers) and proposes values that meet this band: **20 Age I line units raze a House in ≤ 20 s, a production hall in ≤ 60 s, a Town Hall in ≤ 2 min; siege stays at least twice as cost-efficient against buildings**. Today a Heavy Infantry does **1 damage** per swing to a Town Hall (10 × 0.4 − armor 5 → the floor of 1), so 20 of them need 3 minutes and 20 Crossbowmen 4.5 minutes; the lever is the damage table's Structure column (melee 0.4, pierce 0.2, magic 0.4) and the buildings' armor. D19 applies next session. Note 5's slice 1 helps too: today half your army stands behind the other half |
| 7 | Control groups, buildings included | Yes (small) | View, **this session**: Ctrl + 1-9 already stores units; now a selected **building** too (one per group: Ctrl + digit with a Billet selected, then the digit selects it again from anywhere, double-tap centres the camera on it). ~250 lines |
| 8 | Click a resource to see what's left | Yes (small) | View, next session: left-click a mine or tree → the selection panel shows its name and "2,340 gold left" (only while you can see it; the last amount you saw otherwise). ~250 lines |
| 9 | New win condition | Yes (rule) | Sim, **this session** (M5-4e, ~500 lines, QA full): a player is out once it has **no unit and no finished unit-producing building**; houses, camps, the Forge, towers and unfinished sites don't count. A side that never had a building (test sandboxes) still can't lose. **Caveat**: as in Age of Empires, a last worker hiding in a corner keeps its side alive; the AI's waves target buildings, so the sim's M5-4d adds "hunt the last units" (attack-move to where they were last seen, then sweep). If the hunt annoys you, an inbox line "a side with no production building for 3 minutes is out" is a 20-line rule |

**D14 feasibility verdict: feasible, worth doing, in stages.** The AI's cost today is 0.4 ms per think for eight Hard seats over 2,600
units (gate 1 ms), and every AI read goes through its fog-limited window (a test fails the build if AI code names the world directly), so
every stage below is vision-limited by construction and adds work proportional to the units it can see, not a search.
(a) **Proportional defense** (next session): below. (b) **A strategy per match** (~1 sim session for the file format, ~1 data session for
the openings): `ai.json` holds several named openings per faction (build order, army mix, attack thresholds, e.g. Malazan "legion push" /
"fast casters" / "towers and siege"; Whirlwind "early raid" (the 4:30 Raider raid the page sketched) / "horde" / "priest timing"); the AI
draws one with its seeded dice at the start (Easy always the first), so matches differ and replays still reproduce. Zero think cost.
(c) **Counter-composition** (~1 sim session + a small data table): the AI remembers which enemy unit types it has seen in the last few
minutes (each unit once, fading) and shifts its army mix toward their counters from the template table (Line counters Shock, Ranged
counters casters and light units, Shock counters Ranged, casters counter clumped Line, siege counters buildings) by a strength per
difficulty (Easy 0, Normal moderate, Hard strong). Cost: a few hundred operations per think. (d) **Attack and defence at once, not
discrete waves** (2-3 sim sessions, **after** M5's remaining criteria): squads with roles (a home guard sized by the last raid, a main
army that gets reinforcements streamed to it instead of waiting for the next wave, a harass squad of fast units sent at undefended
workers it has scouted). (a)-(c) are new M5 criteria in docs/05; (d) is queued after M5-5 so M5 doesn't balloon; you decide after
playing (a)-(c) whether (d) is worth it. What I would **not** do: a planner that searches moves ahead (think cost, and it would play
like a machine, not an opponent).

**Design: proportional defense ("partial recall by threat", M5-4f).** Today any raid (one building hit, or any enemy soldier within 48 m
of the hall) sends the **whole** army home for 20 s and aborts a wave: exactly the exploit you spotted. The new rule:
1. **Measure the threat.** Each think the AI already counts the enemy soldiers it can see within 48 m of its hall. It now also adds up
   what they are worth (their gold + wood cost). A building being hit by something it can't see (a catapult behind the fog) counts as a
   fixed minimum threat (`ai.defendMinValue`, 150 = about two line units).
2. **Size the answer.** It commits defenders worth `ai.defendRatio` × the threat (1.5: a 120-cost Horse Raider pulls at most 180 worth,
   two soldiers), never less than `defendMinValue`.
3. **Who goes, in order.** First the **home army** (units not in the wave: rallying, fresh from the halls), nearest the threat first.
   If that isn't enough, **part of the wave**: the members nearest home, as long as the wave keeps at least its start threshold (the size
   it needed to be sent at all); a wave that would drop below that comes home whole (a half-wave achieves nothing). The **whole wave**
   also comes home when the hall itself is under attack and below half hp (`ai.recallHallHp`, 0.5). New units from the halls keep
   joining the defense while the threat holds.
4. **What stays.** Workers still flee a threat within 16 m and return 20 s later; the 20-second hold after the last hit; defenders
   attack-move to the threat and go back to the rally point after.
5. **Why it isn't trickable.** A small raid costs you more than it costs the AI: it answers with 1.5× your raid's worth and keeps its
   attack going. Dancing a raider in and out re-sends nothing (the point only moves when the threat really moves). A raid it can't catch
   (kiting cavalry) holds two soldiers at home, not the army. A big raid gets the big answer it deserves. The knobs are three numbers in
   `rules.json` (`defendRatio`, `defendMinValue`, `recallHallHp`), one fingerprint update, and tests pin: a 2-unit raid against a 20-unit
   wave out → the wave continues and ≤ 3 units come home; a 15-unit raid → the wave returns; a hit with nobody seen → the minimum.
   Same rule on every difficulty for now (a per-difficulty ratio is a one-field schema change if Hard should defend tighter).

**Order of work from here** (inbox requests before roadmap items, as the rules say): this session sim M5-4e (win rule), view M6-V7
(edge-pan + building groups + BUG-0710 + the match-over scenes re-staged for the new rule), data D18 (reports). Next: sim M5-4f
(proportional defense + D15 C + the densest-cluster target), view the resource click + keybinds, data D19 (apply ×1.5, Easy casts,
building damage, gather rates, Line v Shock numbers). Then sim pathing slice 1 and 2, the Hard knobs (M5-4c slice 2), the stats counters
(Requests 39), D14 b's format, M5-4d (expansion, rebuilding, re-scouting, hunting), D14 c, the 20-minute test, your Easy / Hard play.
The Ages design task after that. Nothing of M6's view work is displaced except by one session.

**How to change any of it.** An inbox line per item: "defendRatio 2", "gather 0.9 / 0.8", "House must take 30 s", "skip the Ages design
until M6", "do pathing before the defense".

### The AI casts spells now: a Normal or Hard opponent sets fire to your clumps, blasts your buildings and calls sandstorms onto your army; Easy never casts (sim track, M5-4c slice 1, 2026-10-13)

**What was built.** The last of the AI's managers: the one that decides when and where to cast. After everything else in its
think (defense, build order, scouting, economy, production, the army), every AI unit that has a spell ready and is free (not already
casting, not walking to a cast, not the scout, not fleeing, not on its way home from a wave) may get one cast order a think, through
exactly the same door your own Q-click uses. **Where:** it looks at the enemy units it can see within the spell's reach, takes the
middle of that group, keeps only the enemies within the spell's radius of that middle, and casts there if **at least 2** of your units
are inside the blast (a number in the rules file). If the point is a little beyond the spell's range it is pulled back to the range,
so the caster never walks into your line. A spell that hurts buildings (the Sapper's Cusser) goes at the nearest enemy building it
can see when no group of your units qualifies. A spell that would hurt its own side (the Cusser again) is **never** thrown with one of
its own units in the blast, the Sapper included. It can't hear "no" from the rules, so it judges a cast by the spell's cooldown
starting: if the cast didn't happen it tries again next think, at most three times at one spot. Only Normal and Hard cast; Easy's
"doesn't use abilities" is now real (proven: 0 casts in 5 minutes beside a casting Normal twin).

**What you will see.** Skirmish vs "The Whirlwind" on Normal or Hard: when its wave with Priests meets your army, a **dust storm lands
on the thickest part of your line** (your units in it are Blinded and Slowed, as before). Vs the Malazans: a Cadre Mage **sets the
ground ablaze under two or more of your soldiers** the moment they bunch, and a Sapper **lobs the Cusser at your house or tower** when
nothing better is in reach, never into a melee its own soldiers are in. Spread your units out and the mages wait; walk a lone unit past
and nothing happens. On Easy none of this happens.

**Producer decisions.** (1) *The target rule* above (the middle of the group in reach, at least 2 enemies in the blast). The
alternative, "the densest cluster", is better when your army is in two groups (see the rough edge below) and may come later. (2)
*No friendly fire ever*, rather than "worth it when the enemy group is big": simpler and never embarrassing. (3) *Casting is judged
by the cooldown, three tries at one spot*: a code guard, not a balance number. (4) *The three remaining Hard knobs* (25 % gather bonus,
focus fire, pulling back wounded) were **deferred** to the next slice: the manager alone passed the size limit set for it, and the
builder stopped and said so, which is what the limit is for. (5) *The AI's speed test turns the storms' vision-blocking off* so it
measures the AI and not the fog (BUG-0701 below); a rule now says any such test must say so and the hidden cost gets its own row.

**Rough edges.** BUG-0701 (S3): each live storm adds about 0.03 ms to every AI think (the fog checks every unit against every storm);
fine at the handful of storms a real match has, fixed in the sim's next clean-up. BUG-0702 (S4): three targeting nits: your army split
in two groups either side of a mage gets no fire at all (the middle is empty ground); three Priests may stack three storms on one spot
in the same instant (wasteful, not wrong); a group standing exactly at the far edge of reach is never cast on. Hard plays like Normal
plus casting until the next slice lands its three knobs.

**How to change it.** `game/data/common/rules.json` → `ai.castMinTargets` (1-50; 1 = cast at any lone unit, 5 = only at real
clumps). "Cast at the densest cluster", "never two storms on one spot" or "let the Cusser hit its own side when the prize is big"
would be inbox notes.

### The match ends on screen: a Victory / Defeat / Draw banner, a stats table and "Continue watching"; the two red test scenes are fixed and `--resolution` works again (view track, M6-V4, 2026-10-13)

**What was built.** The window now watches the sim's defeat rule. The moment **you** (always the first slot) have no building left, a
**Defeat** banner pauses the match; when the enemy's last building falls, **Victory**; if the last two sides fall in the same instant,
**Draw**. One banner per match, with three buttons: **Stats**, **Continue watching** and **Main menu**. **Stats** is a table with the
match length at the top and one row per player: a colour swatch, the faction name, "You" or the AI's difficulty, then **Kills**,
**Losses**, **Gold in hand**, **Wood in hand** and **Defeated at** (the clock time the player fell, or "Standing"). **Continue
watching** hides the banner for good and lets the match run on (after a defeat your clicks do nothing, but you can watch the AI finish;
Esc is the pause menu as before, which now offers Stats too). **Main menu** ends the match. Also: the two test scenes that were red on
`main` since the last session are fixed (BUG-0674, BUG-0680), and a settings file no longer overrides a `--resolution` or
`--fullscreen` flag you pass on the command line (BUG-0681).

**What you will see.** Skirmish vs Hard, send nothing, wait: when its wave razes your last building the game freezes under a dimmed
"Defeat", three buttons in the middle. Click Stats: "Match length 6:40", your row with Kills 3 / Losses 14 and "Defeated at 6:40", the
AI's row "Standing". Back, then Continue watching: the match resumes, you can scroll around and watch. Or raze the AI's base yourself
(it has only a hall and houses early) and get "Victory" with the AI's row showing when it fell.

**Producer decisions.** (1) *Defeat shows the moment you fall, even if other sides still stand* (3-4 player matches later); the
alternative, waiting for the whole match to end, would have you watching a game you lost with no word. (2) *The banner pauses the match*
like the pause menu; the alternative, a small non-blocking message, would let the last fight run on while you read. (3) *The stats show
only what the sim counts today.* The design doc's full list (resources gathered, units trained, buildings built and lost, time to Age
II, an army-value graph) needs counters the sim doesn't keep yet; they are on the sim's list (Requests 39) and drop in as columns.
Gold and wood are what the player **holds** now, labelled "in hand" so nobody reads them as "gathered". (4) *A small piece of
stand-in code* the view used before the sim's rule existed is kept this one session because QA's tests still cover it; it is dead and
goes next view task (BUG-0710).

**Rough edges.** BUG-0710 (S4): the small "K / L" counter in the top-right corner, visible through the banner, is one tick behind the
stats table (the pause lands inside the killing tick); cosmetic, next view task. In dev runs only, a player who never built anything
can "win" when another falls; the menu's match always gives everyone a hall, so you won't see it.

**How to change it.** `game/data/common/ui.json` → `match_over`: every word ("Victory", "Defeat", "Draw", "Continue watching",
"Gold in hand" ...) and the clock format. "Don't pause on the banner", "show the banner only when the match is over" or "add a
Rematch button" would be inbox notes.

### The design doc now says outright that Normal doesn't focus fire or pull back wounded, both faction pages describe the real defense, and a spending report found the AI runs out of wood, not gold (data track, D17, 2026-10-13)

**What changed (no number in the game moved).**

| Where | Field / line | Old → new | Why |
| --- | --- | --- | --- |
| docs/02 "AI opponent" | the Normal bullet | "fair play at a relaxed pace, follows the build order on time, uses abilities." → "..., uses abilities, **doesn't focus fire or pull back wounded units.**" | the shipped Normal AI has both flags off; the test that pins them now leans on words that say so (Producer default, owner may revisit) |
| docs/factions/malazan.md | "Defense" | "pull the army home when buildings take damage; Laborers flee to the Garrison Keep." → the real rule: the army comes home when a building near the Keep is hit or an enemy soldier is seen within 48 m; a wave out turns back; Laborers within 16 m of the threat run to the nearest finished drop-off away from it | matches what M5-4b built (docs/03) |
| docs/factions/whirlwind.md | "Defense" | "counter-attack the enemy base with Horse Raiders when its army is away." → the same real rule for the Holy Camp / Supply Cache; the counter-raid kept as **"Later idea (not in M5)"**, marked not built | nothing in the AI raids today; the idea is kept, not promised |

No `displayName` or `description` changed; nothing the player reads moved.

**The spending report** (a permanent test that prints a table; both factions, Normal, the flat test scene, 20 minutes, the other side
idle):

| Faction | At 5 / 10 / 15 / 20 min: gold in hand | wood in hand | army (pop) | Age II? | Waves | Halls' queue empty |
| --- | --- | --- | --- | --- | --- | --- |
| Malazan | 60 / 686 / 2,104 / 2,928 | 10 / 10 / 50 / 5 | 8 / 25 / 41 / 51 | no | 5 | 75 % of the time |
| Whirlwind | 116 / 470 / 1,860 / 2,360 | 107 / 25 / 11 / 25 | 7 / 28 / 43 / 59 | no | 6 | 79 % of the time |

**What it means.** The D15 worry was "gold piles up". It does, from about 10 minutes, but the cause is **wood**: the build order's
Age II and upgrade steps cost 100-200 wood each, and the AI's production and houses spend the wood first, so those steps never come
due and the gold has nothing to buy. Neither AI reaches Age II in 20 minutes. One caveat from QA: the flat test scene has only 12
trees a base and they run out (0 wood left on the map at 20 min for the Malazans), so part of the late shortage is the scene, not the
AI.

**Producer decision (default, owner may revisit).** Keep the build orders as they are and **re-measure on a real generated map**
first (the data track's next task); only then pick a lever. The levers are on the sim side, not in the faction files: "reserve the
wood for the next due build-order step" or a higher `ai.goldShare` (the share of workers on gold, so fewer on gold and more on wood).
If you'd rather act now, say which in the inbox.

**Rough edges.** BUG-0720 (S4): one wording nit on both pages ("a Depot more than 16 m from it" reads as "from the Keep"; it means
"from the threat"); the matching stale quote in the technical doc is already fixed.

**How to change it.** The Normal wording is docs/02 (a test will name the field if the phrase goes); "add a reserve rule" or "raise
goldShare to 0.5" would be inbox notes.

### The match can be lost now, and the AI defends its base: its army comes home when you raid it, its workers run for cover, and a player whose last building falls is out (sim track, M5-4b, 2026-10-13)

**What was built.** Two rules. (1) **Defeat.** A player who has no building left (an unfinished one counts as a building) and had at
least one before is **defeated**: from the next moment every order of theirs is ignored (move, build, train, cancel, everything), their
AI stops thinking, and the match is **over** once only one of the players who ever built a building is still standing. Their units
don't vanish: whatever they were already doing (a walk, a fight) they finish, and they fight back if attacked. A side that never had a
building (a sandbox with only soldiers) can never be defeated, so the old test scenes keep working. (2) **Defense.** Until now the AI
ignored you hitting its base. Now, when one of its buildings near its hall loses hit points, or any enemy soldier is seen within 48 m of
its hall, the AI **defends** for the next 20 s (and 20 s after the last hit): its attack wave, if one is out, turns around and comes
home; it starts no new wave; every free soldier is sent to the threat (the enemy group's centre, or the building that was hit); and
its workers within 16 m of the threat run to the nearest safe drop-off (a hall or storehouse away from the fight). Twenty seconds after
each worker fled it goes back to work, but not at a mine or forest right next to the threat. The three distances and the time are
settings in the rules file, not code.

**What you will see.** Skirmish vs Hard: send a few soldiers at the AI's base early. The moment they hit a house or get near the hall,
the AI's gatherers scatter to its hall, its army (wherever it was, even marching at you) comes back and attack-moves onto your
soldiers; once your raid is dead or gone, its workers return to the mines and its next wave comes later than it would have. Lose all
your own buildings and your clicks do nothing any more ("Match over:" prints in the log; the on-screen banner is the view's next task).

**Producer decisions.** (1) *A defeated player's units stay* (the design doc only says "no buildings left"); the alternative, removing
them, would hide how the last fight ended. (2) *Orders are dropped where they apply, not refused at the door*: replays stay
byte-identical and the window never gets an error for a click after the loss. (3) *The defense trigger and point rules* above (48 m /
20 s / 16 m, the nearest drop-off away from the fight, the threat point only moves when the enemy has really moved twice in a row) were
set by the brief and the builder; the owner may tune any number. (4) *A soldier walking home to defend does not stop to answer hits
on the way* (measured: it still gets home alive against three chasers); the alternative (attack-move home) was the bug the first
round found: the army just stayed in your base fighting.

**Rough edges.** BUG-0674 (S3): one older test scene expected a player to rebuild after cancelling its only building, which now
counts as a loss; test-side, the view fixes it first next session. BUG-0675 (S4): a `defendRadius` set to the maximum (256 m) makes an
AI that only ever defends (valid data, a style; a docs line comes with the sim's next clean-up). A raider the AI can't kill keeps it
"defending" (no attack wave) as long as it stays, by design; its economy no longer stops for that.

**How to change it.** `game/data/common/rules.json` → `ai`: `defendRadius` (m, 8-256), `defendFor` (seconds, 0-600), `workerFleeRadius`
(m, 1-64). "Units of a defeated player vanish" or "a player with units but no buildings loses after N minutes" would be inbox notes.

### There is a Settings screen now (resolution, window mode, VSync, graphics quality, three volumes, edge-pan speed), saved between runs; the two test scenes red since the start locations are fixed (view track, M6-V6, 2026-10-13)

**What was built.** (1) **Settings**, reachable from the main menu and from the pause menu (Esc in a match → Settings). Fields:
resolution (a list of common sizes plus your current one), window mode (Windowed / Borderless / Fullscreen), VSync (On / Off),
graphics quality (Low / Medium / High: Low renders the 3D view at 75 % with hard shadows, Medium is how the game looked until now,
High adds 4x anti-aliasing and softer shadows), master / music / sound-effects volume (0-100), and edge-pan speed (0.5-2.0, how fast
the camera scrolls when the mouse touches the screen edge; the arrow keys are unchanged). **Apply** applies at once; **Back** (or Esc)
applies and returns; **Defaults** restores everything. The picks are saved in `user://settings.cfg` (a small text file in Godot's
user folder) and read at boot before the menu shows; a missing or damaged file means the defaults, with a note in the log, never a
crash. Every word on the screen comes from `ui.json`. Keybinds are a later slice. (2) **BUG-0661 fixed:** two bench test scenes had
been failing since the start locations landed because they measured "the centre of every unit I own" over a 10-second march, and
since M5-4a your five starting workers stand off-screen by the hall and never march (and on one seed half the army block is off
screen); they now measure only the units the order was given to, against half of what a free march would cover, with the reason
written in the test. Two more stale test oracles found on the way were fixed (BUG-0682, BUG-0683), and the ground-disc pool no longer
rebuilds stable discs when it is full (BUG-0662).

**What you will see.** Main menu → Settings: pick 1920 × 1080 and Fullscreen, Apply: the window changes at once; Back, Quit, run
again: it starts fullscreen. In a match, Esc → Settings → drag Sound effects to 0: the select / order clicks go silent; set Edge pan to
2.0: the camera flies when the mouse touches the edge, while the arrow keys scroll as before.

**Producer decisions (builder defaults, owner may revisit).** The defaults are the game as it looked before: 1152 × 648 windowed,
VSync on, Medium, volumes 100, edge pan 1.0. Volume 0 is true silence (-80 dB), 50 is about half as loud (-6 dB). `--vsync` and
`--mute` on the command line still win over the file for that run (dev / test flags). The quality presets are the builder's three
(docs/03 has the table); the alternative, exposing every rendering option, is more than the game needs.

**Rough edges.** BUG-0680 (S3): a settings file with a 20-digit number logs an engine error at boot (the value still falls back to
its default); QA's new test scene fails on purpose until it is fixed, so the test loop shows one red scene; the view's first item next
session. BUG-0681 (S3): the saved window size overrides the `--resolution` dev flag, so the benchmark runs at the saved size; next
session if a few lines. BUG-0684 (S4): four small notes (shared test paths, the screen shows the file's VSync while `--vsync`
overrides it, a code path no button reaches, the bench orders half of seed 6's army). The sim's one-line addition to `ui.json`
("Defeated", the build-ghost's reason for a defeated player) merged cleanly beside the new `settings` section.

**How to change it.** The words: `game/data/common/ui.json` → `settings`, `menu.settings`, `pause.settings`. The defaults, ranges and
presets: code (`GameSettings`, `SettingsApplier`); an inbox note changes them. Your own picks: the screen, or delete
`settings.cfg` from Godot's user folder (`%APPDATA%\Godot\app_userdata\RtsGame`) to reset.

### The AI finishes every house it starts and no longer freezes a wave; both bases start on the same level, half a map apart; two movement fixes; twelve bugs closed, nothing in the game's numbers changed (sim track, M5-H1, 2026-10-13)

**What was built.** A clean-up session on the rules. (1) **The AI finishes its buildings.** Last session's report found the Malazan
AI abandoning two barracks sites for 15 minutes. The cause was a one-tick blink: a worker walking to its site stops short of it in a
crowd and is "idle" for exactly one tick before it starts building; if the AI happened to think on that tick, it sent the worker off
to gather and nobody came back. Now the AI reads such a worker as "building", and any own site that nobody has been working on for
three of its thinks gets a worker sent back (an idle one if there is one, else the nearest gatherer). In the 30-minute report the
Malazan AI now finishes 11 of 11 houses (was 9 of 11) and its population cap reaches 98 (was stuck at 82); with every site's first
builder killed on purpose, both factions still finish every site within 3 s of the kill. (2) **A wave that can't reach its target goes
home.** An attack wave whose soldiers all stand around a target none of them can hit (a hall fenced in by forest) used to stand there
for the rest of the match; now, after a full attack cooldown (60 s) with nobody fighting, it goes home and the AI attacks again later.
(3) **Fairer start locations.** Both bases now stand on the same terrain level (no more "you on a plateau, the AI below") and at least
half a map apart (the closest pair on a small map is now 129 m, was 70 m). Measured on 100 seeds per map size: 0 mismatched pairs
(was 15 of 100 on the small map). (4) **Odd map settings are refused, not crashed:** a saved game or replay whose start settings can't
fit the map is refused by name instead of crashing the generator. (5) **Two movement fixes from M4's backlog:** a spell-caster walking
into range no longer waits forever because a friend is fighting *beside* it rather than in its way; and 500 soldiers ordered onto a
reachable building through a crowd never give up the order (444 of 500 used to). Plus six small items (the AI's rebalance keeps the
worker carrying the least; short resource counts short every base alike; test and docs nits).

**What you will see.** Skirmish on any seed: your hall and the AI's are on the same level, far apart; the AI's base grows all game
(its late-game army is no longer capped); its waves come, retreat and come again instead of parking by a fenced target; select a
Cadre Mage and cast at the far side of your own fighting army: the cast lands or is reported lost, it doesn't hang.

**Producer decisions.** (1) *The "three thinks" wait before the AI re-sends a worker is a code constant, not a data knob*, like the
"three tries" wave rule: it debounces an order that just applied and tunes no balance number (owner may revisit: say "make the AI's
site resume a data setting" and it moves to `rules.json`). (2) *Start locations must share a level and keep half a map apart*: the
design asks for symmetric layouts; until the terrain itself is mirrored (M6 map work) this is the fairness the generator can give,
at the cost of a redraw on crowded layouts (measured: no extra fallbacks to the flat map on the three sizes with two players).

**Rough edges.** BUG-0651 (S3): at twice the supported crowd (1,000 soldiers on one building) one soldier can still give up its
order; next sim clean-up. BUG-0650 (S4): the AI keeps sending a worker every three thinks to a site that can't be reached at all;
only reachable by hand-placing such a site (the game never places one), noted for the clean-up. The golden replay did not change.

**How to change it.** The wave cooldown: `game/data/factions/<id>/ai.json` `waveCooldown`. The start rules (same level, half a
map apart) and the resume wait: code constants; an inbox note changes them.

### The "AI gathers from the menu" and "every base sees a mine" checks are hard now, two colours were retuned so every corpse is readable, colours are unique per match, and the sandbox flake is fixed (view track, M6-VH1, 2026-10-13)

**What was built.** (1) The two notes the view's tests printed last session ("the AI should be gathering by 20 s", "every hall
should see a gold mine") are now hard checks that fail the build; on the combined code the AI's first worker is gathering 3 s into
the match on all 18 tested openings, and all 36 halls see a mine (BUG-0604 closed). (2) Two of the eight player colours broke the
game's own corpse-readability rules (a corpse is drawn as a darkened disc of the player's colour; it must not be too dark, and two
players' corpses must look different): **Crimson `#A3192B` → `#B82633`** (a little brighter) and **Jade `#2E8B57` → `#2EA35A`** (a
little livelier, so Iron and Jade corpses no longer look alike). Names unchanged; a permanent check now covers all 8 colours and all
28 pairs, so a future recolour that breaks the rules fails by name (BUG-0632 closed). (3) **Colours are unique per match:** in the
setup, the colour the other slot holds is greyed out in your list, and if the lists collide your pick moves to the first free
colour; from the command line, two slots on one colour keep the AI on its default (BUG-0631 closed). (4) The scripted sandbox fight
(`M4PlayableTest`) flaked once under load because its last step read a building's position *after* the Cusser's blast had destroyed
it; it reads before the cast now, and its seed-6 opening frames the army above the HUD (BUG-0603 and BUG-0660 closed; 10 of 10 runs
under load). (5) Two small ones: the ground-disc pool no longer grows when one frame's discs are simply swapped for others, and a
re-bound combat view sizes its health array properly.

**What you will see.** Skirmish setup: pick Cobalt for yourself and the AI's list shows Cobalt greyed; pick the AI's colour for
yourself and the AI jumps to the first free colour. In a fight, Crimson corpses no longer read as black blobs, and Iron v Jade
corpses are told apart at a glance.

**Producer decisions.** (1) *Data first, then a guard*: the two colours were retuned in the data file (names kept) rather than
changing how corpses are drawn; the alternative (a lighter corpse shade for everyone) would have changed every colour's look.
(2) *Unique colours per match* is the design doc's rule now (docs/02 "Game flow"); the alternative (allow duplicates, as last
session) made a mirror match unreadable. Owner may revisit either: an inbox note ("Crimson back to the old value", "allow duplicate
colours").

**Rough edges.** BUG-0661 (S2, pre-existing): two older test scenes have been failing since the start locations landed (a 10 s march
measures 19.3 m against a 20 m line; a seed-6 march is blocked at 11 m): test staging, not the game; the view's first item next
session. BUG-0662 (S3): the disc-pool fix can, on a full pool, move a stable disc to another node and rebuild its mesh (correct on
screen, a little wasted work); next session if a few lines, else the next clean-up. BUG-0663 (S4): a command-line warning's wording.

**How to change it.** The colours: `game/data/common/ui.json` `setup.colour_1` .. `colour_8` (name and `#RRGGBB`); two equal
values, a too-dark corpse or a too-similar pair are refused by the tests. Or an inbox note.

### The three AI difficulties are now tabled on both faction pages and pinned to the design doc; the Easy AI "doesn't use abilities" (data track, D16, 2026-10-13)

**What was built.** Test and page work only; no number in the game changed. Each faction page has a new "Difficulty" table showing
exactly what the shipped data says for Easy / Normal / Hard, and permanent checks tie every cell to the data file both ways (edit a
cell or a file value and a test fails naming the faction, the difficulty and the column: 48 page edits and 48 file edits were tried)
and tie the design doc's stated numbers to the files (Easy thinks every 2 s, Hard every 0.5 s with a 25 % gather bonus, focus fire and
pulling back wounded units only on Hard, abilities off on Easy). The design doc's Easy line said "rarely uses abilities" while the data
is a plain on / off flag; it now says "doesn't use abilities".

**The table (identical on both pages today):**

| Difficulty | Thinks every (s) | Build-order slack (s) | Wave size × | Gather bonus | Focus fire | Pulls back wounded | Uses abilities | Expands after (s) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Easy | 2 | 30 | 0.6 | 0% | no | no | no | 900 |
| Normal | 1 | 5 | 1 | 0% | no | no | yes | 480 |
| Hard | 0.5 | 0 | 1.25 | 25% | yes | yes | yes | 300 |

(Build-order slack: how long a step may wait after it is due. Wave size ×: scales how big an army the AI wants before attacking.
Expands after: the earliest the AI builds a second base. Note: the sim doesn't act on focus fire / pull back / abilities / expand yet;
those are M5-4b's knobs. The table pins the data so that work has fixed targets.)

**What you will see.** Nothing new in the game. The Easy AI's behaviour is unchanged (it never used abilities).

**Producer decisions.** (1) *"Rarely" → "doesn't"* for Easy: a rate would need a new data field and the ability manager that
doesn't exist yet; the owner may ask for "rarely" back once M5-4b builds ability use (then it becomes a `useAbilities` rate).
(2) The QA noted that the design doc never says outright that Normal doesn't focus fire or pull back wounded (the data says no; the
pin rests on "fair play at a relaxed pace"): I'd state it in docs/02 as the next small data item unless you want Normal to micro.

**Rough edges.** None open. A known gap: a file edit smaller than half a tick (e.g. 0.5 s → 0.51 s) passes the page pin because
the game itself can't see it.

**How to change it.** The numbers: `game/data/factions/<id>/ai.json` `difficulties`; then the page table (or the test tells you
which cell). Wording: `docs/02-game-design.md` "AI opponent".

### Every base now starts on a proper start location: two gold mines and a forest in reach, and the enemy's base is the mirror image of yours (sim track, M5-4a, 2026-10-13)

**What was built.** The map generator now picks the two bases' positions before it scatters forests and gold mines. A start
location is a round patch of open, flat ground (about 16 m across, plus room for the mines) at least 20 m inside the map
edge; yours is in the west half, the enemy's is the exact point mirror of it through the map centre (3- and 4-player layouts
are quarter turns, ready for the later lobby). Each base then gets its own two gold mines of 2,500 gold within 18 m and a
forest of at least 12 trees within 24 m, placed on the side away from the way out so the trees don't fence you in. The Town
Hall stands in the middle of the patch; the five workers face the nearest mine. The AI's test map uses the same rule, so its
scout's "look where my base's mirror is" guess now really finds you. Saved games and replays record the new map settings (a
new file format; older replays still play exactly as recorded, with no start locations).

**What you will see.** Skirmish → Start: your hall sits on clear ground with two mines a short walk away and a forest right
behind; the AI's base is the same distance from the centre on the far side. The AI's workers chop wood in the first minute
(on seeds 1-3 its first wood comes in at 24-45 s; last session it took 3-3.7 minutes on two of those seeds). On the 192 and
256 maps the bases sit by the west and east edges; on the small 128 map a crowded layout can push the start patch toward the
middle (seed 1, the default, used to put the bases 75 m apart with yours on a plateau; since M5-H1 both bases share a level
and sit at least half a map apart: see rough edges).

**Producer decisions.** (1) *Start locations are "terrain", not part of the saved fingerprint*: like the heightmap they follow
from the seed and the settings, so recording them twice would add nothing; this also means the proof-of-determinism replay's
checkpoints did not change. (2) *The mine distance is 18 m, not the 14-18 m I asked for*: a mine must stand outside the
16 m clear patch, so anything under about 17 m is impossible; the builder picked the smallest round number. (3) *"No start
locations" is allowed as a setting* for tiny test maps and old replays; the game's menu never uses it. (4) *The defeat rule
(a player with no buildings left loses) was not built this session*: the start locations alone used the budget, and my plan
only allowed the second system if room was left. It is the first item of the next sim feature task; until then the game
prints a developer line when a side's last building falls but shows nothing.

**Rough edges.** ~~BUG-0621 (S3)~~ **fixed in M5-H1** (both bases on one level, at least half a map apart; mirrored terrain itself
stays M6 map work). ~~BUG-0620 (S3)~~ **fixed in M5-H1** (unfit map settings are refused by name). BUG-0622 (S4): items 2 and 4 fixed
in M5-H1 (short resource counts short every base alike); item 1 (a hand-placed start location at the very edge wraps the hall to the
other edge; not reachable from the menu) stays for a later clean-up.

**How to change it.** The mines' count and gold per start: `game/data/common/rules.json` `startMines`. The patch size, the
edge margin and the two distances are generator defaults in code (`MapGenParams`): an inbox note changes them ("start forest
within 30 m", "bases at least 120 m apart").

### The menu's match starts the way the game is meant to, and each side has its own colour (view track, M6-V3, 2026-10-13)

**What was built.** Skirmish → Start no longer drops 100 soldiers a side into a brawl. It opens with a Town Hall and five
workers each (population 5 of 10), the camera on your hall, and nothing else: you build up from there, and so does the AI.
The developer sandbox is still there behind the `--units` flag. Each slot has a colour list: eight named colours in the data,
**"Iron", "Ochre", "Crimson", "Cobalt", "Jade", "Violet", "Rose", "Bone"**, shown with a swatch; you default to Iron, the AI to
Ochre (the old Malazan and Whirlwind colours), and everything that used to take the faction's colour (units, buildings,
ghosts, corpses, shots, storm rims, minimap dots, health bars) now takes the player's. A mirror match (Whirlwind vs Whirlwind)
is two colours again. Also fixed: holding the mouse on the minimap into the pause no longer leaves the camera dragging after
Resume (BUG-0600, the last of that family); a mistyped size format in the text file is refused at load (BUG-0602); and the
smoke test's `--skirmish` start is now exactly the menu's default match (an AI on Normal), found and fixed within the session
(BUG-0630).

**What you will see.** Skirmish → the new Colour column → pick Cobalt for yourself and Crimson for the AI, "The Whirlwind" +
Hard, Start: your five workers stand by a blue hall facing a gold mine, a forest behind you; press F12 or watch the minimap
and the red dots across the map are the AI's. About 15 s in its scout leaves; a few minutes later its first raid comes.

**Producer decisions.** (1) *`--skirmish` means "the menu's Start with nothing changed"*, so the smoke test exercises the AI
seat on every run; a developer who wants a human second seat drops the flag. (2) *Both slots may pick the same colour today*;
next session makes colours unique per match (the setup greys the taken one) unless you say otherwise. (3) *The on-screen
"match over" banner is postponed to the next view feature task*, because the sim's defeat rule isn't in yet (see the entry
above).

**Rough edges.** ~~BUG-0632 (S3)~~ **fixed in M6-VH1** (Crimson and Jade retuned, names unchanged, a whole-table check; the M6-VH1
entry above has the values). ~~BUG-0604 (S3)~~ **fixed in M6-VH1** (the notes are hard checks now). ~~BUG-0631 (S4)~~ **fixed in
M6-VH1** (colours unique per match).

**How to change it.** The colour names and values: `game/data/common/ui.json` `setup.colour_1` .. `colour_8` (a name and a
`#RRGGBB`); two equal values are refused at load. Or an inbox note ("rename Bone to Ivory", "make colours unique").

### The data track's fourth clean-up: nothing in the game changed, but re-running the house report on the new AI found a real AI bug (data track, D-H4, 2026-10-13)

**What was built.** Test-only work: the house-rule report (which prints how the AI houses its army) no longer asserts
anything beyond "it ran" and reads the AI's internals only through four named fields, so the sim's AI work can't break the
data track's tests by surprise (BUG-0610 closed). The four balance-table cells the sim re-printed last session were confirmed
pinned to both faction pages. Then the report was re-run on last session's smarter AI: the Whirlwind seat is fine (7 houses
placed, 7 finished in 10 minutes), but the Malazan seat places 11 Billets in 30 minutes and finishes only 9: two building sites
are abandoned from minute 14 on, its population cap sticks at 82 instead of 98, and its army stops growing while 4,500 gold
piles up. That is **BUG-0640 (S3)**, filed against the AI; the sim fixes it next session (the AI needs a "go back and finish
that site" rule). No data, page or saved-fingerprint change, so there is no review table this time.

**What you will see.** Nothing new; this is why the Malazan AI's late game feels capped today.

**How to change it.** Nothing to change; your answers on D13 / D14 / D11 / D12 / D15 are still wanted and become the data
track's next task.

### Process note: a QA agent killed processes machine-wide (studio, 2026-10-13)

The data track's QA ran `taskkill /F /IM python.exe` while filing its bugs, which ended two `python.exe` processes on this PC
that were not its own. If something of yours stopped in the early hours of 2026-10-13, that was why. Standing rule 16 now
says: agents never kill processes by name, only a process they started themselves, and they report a stuck tool instead of
sweeping it.

### The AI scouts, gathers its army in front of its hall and attacks you in waves; a shoved melee unit can hit buildings again (sim track, M5-3, 2026-10-12)

**What was built.** Three things the AI couldn't do last session. **A scout:** 15 seconds in, the worker nearest the AI's hall
walks a fixed tour of the map (eight points 40 m round its base, then the spot opposite its base across the map centre, where
your base usually is, then a coarse grid of the rest) and goes back to work as soon as it has seen a forest, a gold mine and one of
your buildings. **An army with a plan:** its soldiers gather 12 m in front of its hall on the side facing your nearest known
building; when the army reaches the size its faction page asks for (Malazan 20 pop and 1,544 of value, Whirlwind 14 and 861;
Easy wants 60 % of that), every soldier marches on that building; a razed target sends them to the next one it knows; if it
has lost half its army it runs home and waits at least a minute before trying again. **Its data is in the saved fingerprint:**
a replay now records which AI numbers it was played with, so an edited build order can't silently replay an old game.
Also fixed on the way: a bug from the fighting milestone where a sword-and-shield unit pushed by the crowd right up against a
building could never strike it (the game thought it stood "too close"; only the Catapult has a too-close distance). That moved
four numbers on both faction pages' balance tables (Raider v Wickan Lancer, hp left and time), nothing else.

**What you will see.** From the menu: Skirmish, pick "The Whirlwind" and Hard for the AI, Start. About 15 s in, one of its
workers leaves the base and walks the map (you'll meet it near your base a minute later). Its soldiers stand in a loose group
in front of its hall. A few minutes in, if it gets wood (see the rough edge), a column of raiders and archers heads for your
nearest building and attacks it. Lose it half its men and they turn and run. Today the menu match still opens as the developer
sandbox (100 soldiers a side already in contact), so the AI's workers die in the first minute and you won't see a proper
build-up until the next view task lands (see the M6-V2 entry).

**Producer decisions.** (1) *The scout's timing target was loosened and the real fix moved to the map.* I had asked for wood
income by 100 seconds on every test map; the builder measured that on two of three test seeds the nearest forest is 70 m from
the base and even a worker sent straight there needs 52 s, so no blind tour can meet it. The AI now gets its first wood at 1-3.7
minutes depending on the seed (before this session: never). The map generator has no proper start locations yet (every base is
meant to have 2 gold mines and a forest in reach); that is the next sim task and the target moves there. Alternative: a
smarter "wood-seeking" scout; rejected because the map is the real problem. (2) The AI rallies with ordinary move orders rather
than a building's rally flag, so the gathering point follows what it learns. (3) A soldier gets at most 3 attack orders toward a
target it isn't getting closer to; that number is a code guard, not a tunable in the data. (4) One wave a minute at most,
`waveCooldown` in `game/data/common/rules.json`.

**Rough edges.** BUG-0592 (S3): wood late on some seeds, fixed by the start locations next session. BUG-0598 (S3): a wave sent
at a building fenced in by trees (no soldier can reach a side) stands there for the rest of the match; next sim task. BUG-0594
(S4): the worker the AI moves from gold to wood drops a partial load; small. The AI still doesn't defend its base, use spells,
expand, rebuild or re-scout (M5-4).

**How to change it.** `game/data/common/rules.json` `ai`: `scoutAfter` (s), `scoutRadius` / `scoutGrid` (m), `rallyDistance` (m),
`waveCooldown` (s). Per faction `game/data/factions/<id>/ai.json` `attack` / `retreat`. Or an inbox note.

### You can pick each side's faction and the AI's difficulty from the menu; pausing no longer leaves a drag stuck (view track, M6-V2, 2026-10-12)

**What was built.** The skirmish screen's two greyed lists are live: "You" and "AI" each pick any loaded faction ("Malazan
Empire", "The Whirlwind"), the AI slot picks Easy / Normal / Hard (default Normal), and Start builds the match with those seats
(the names come from the faction data, the three difficulty words from `game/data/common/ui.json`). Your picks survive Back and
Quit to menu. Developers and tests get the same through `--faction 1|2 <id>` and `--ai easy|normal|hard`; a bad value is warned
about and ignored like a bad seed. A match now prints `Match over: player 1 has no buildings left (tick n)` to the console when
a side loses its last building (a developer line, not a screen: the real rule is the sim's, requested as item 37). Fixed: holding
a mouse button into the pause and releasing it there no longer leaves the camera or a selection box following the cursor after
Resume (BUG-0580); the "128 × 128" items come from `ui.json` (BUG-0581); the one build warning is gone.

**What you will see.** Skirmish → the lists open → pick "The Whirlwind" for yourself and "Malazan Empire" + Hard for the AI →
Start: you command the Whirlwind roster (the build menu, card and colours follow), the AI plays Malazan on Hard (it thinks twice a
second instead of once). Esc, release a held button on the menu, Esc again: nothing drifts.

**Producer decision.** *The menu's match will open as the design says, next view task:* a Town Hall and five workers each, no
soldiers. Today it is still the developer sandbox (100 soldiers a side placed in contact, population 163 of a cap of 10), which
is BUG-0604's first half; the sandbox stays available through `--units`. Alternative: keep the brawl as the default; rejected
because the first thing you'd see against the AI is not the game.

**Rough edges.** BUG-0604 (S3): the brawl opening and a start base that often sees no gold mine (the sim's start locations, next
session). BUG-0601 (S3): pick the same faction for both sides and both armies are the same colour: per-player colours are the next
view task. BUG-0600 (S3): the same pause bug on the minimap (hold the left button on the minimap, pause, release, resume: the
camera jumps); next view task. BUG-0603 (S3): the sandbox battle test flaked once under heavy load. BUG-0602 (S4): nits.

**How to change it.** The difficulty words in `game/data/common/ui.json` `setup`; faction names in each faction's data; or an inbox note.

### The build orders are now guarded by 139 permanent checks, and the "too many houses" question is measured: nothing wasted, keep as is (data track, D15, 2026-10-12)

**What was built.** Tests only; nothing in the game changed. Every way an edit to a faction's build order could go wrong now fails
a test by name: a step naming the other faction's unit, an Age II unit in the Age I army mix, the same step written twice, a
population trigger that isn't a multiple of a half, a research no building can do, a count over 200 or a 257th step, any one cell
of the page's build-order table disagreeing with the file (26 steps × 4 cells × both factions, both directions), the army-mix
percentages, and the wave's minimum army value recomputed from the unit costs (1,544 / 861). The second half was a report on D14's
flag: the Malazan order builds 12 Billets by population steps, and the AI also has a rule to build a house whenever it is within 2
population of its cap, so were houses being built twice?

**What the report found (30 minutes, Malazan, Normal, the flat test scene).** Every house came from a build-order step; the
automatic rule never fired once (each step lands with the cap 8 population ahead). The cap never reached 100 because that scene
runs out of trees at 18 minutes (population stalls at 82 of 98); the 12th Billet step then simply stays pending at no cost. Gold piles
up to 6,004 unspent.

**Producer decision.** Keep the order as shipped (proposal A). Alternatives: B, cut the later house steps (nothing to gain: they cost
nothing while pending and the rule would fire instead); C, have the executor skip a house step once the cap is reached (a sim
change for no measured waste). The unspent gold is the interesting number: it says the AI's spending, not its housing, is the next
thing to tune, which M5-4's difficulty pass will look at. Say in the inbox if you want B or C instead.

**Rough edges.** BUG-0610 (S4): the report test reads a few AI internals and hard-codes its measured numbers in a comment;
tidied in the data track's next clean-up session.

### The AI makes its first decisions: it follows its build order, sends workers to gold and wood, builds houses and trains its army, all through the same door your clicks use (sim track, M5-2, 2026-10-11)

**What was built.** An AI seat. When a match is set up with a seat marked "AI" (the lobby can't do that yet: the view wires it
next session; tests and the sandbox do it today), the sim runs an AI player for it. Every second or so (Easy every 2 s, Hard
every 0.5 s) it looks at the match through its own fog-limited window (M5-1), then does three things in order: it works its
faction's build order (the steps D14 wrote: "at population 6 build a Billet", "at 6:30 research Age II"), it sends every idle
worker to the nearest gold mine or forest it knows (3 on gold for every 2 on wood) and starts a house when the population is
within 2 of the cap, and if the build order asked for nothing this turn it trains the army unit its composition is shortest of.
Every order it gives is an ordinary command, exactly as if you had clicked it, so the rules apply to the AI the same way they
apply to you: a Build on ground it hasn't explored is refused, a Train at a building it doesn't own is dropped, it pays the
same gold.

**What you will see.** Nothing yet in the window (the lobby's AI slot is greyed until the next view task). In a headless test
match with an AI seat: within the first minute the AI's five workers split between the mine and the trees, the first Billet
goes up at population 6, the Barracks at 10, soldiers start training once a hall stands. **One thing it can't do yet:** scout.
On a generated map the forest is outside its starting sight, so it mines gold, runs out of wood at about population 36, and
stops building. That is M5-3's scout manager, the next sim task.

**The last fog leak is closed (M5 criterion 1 ticked).** A tree felled or a mine emptied where the AI can't look stayed
visible to the AI's window one tick too soon in M5-1 (BUG-0540). Now each side remembers the last node it saw in each spot
until it sees that ground again, which is exactly how your own screen draws trees. This memory is not part of the saved
fingerprint (it follows from what each side saw, so equal matches hold equal memories).

**Producer decisions (owner may revisit):** (1) **The AI's commands are recorded in a replay like yours.** A replay plays back
without running the AI at all, so a replay still works after the AI's code changes. (2) **Match seats.** A match now carries one
seat per player: faction, human or AI, difficulty. Old replays still play (they get today's defaults). (3) **The build-order
file's numbers are not yet part of the data fingerprint** that replays check (M5-3 adds them, together with the first attack
code, so the data track's D14 and this task didn't both move the golden replay in one session). (4) **Expansion waits for both
the build order's time and the difficulty's `expandAfter`** (Easy 15 min, Normal 8, Hard 5), so Easy expands late even though
D14's step says 8:00.

**Rough edges:** BUG-0574 (S3, the next sim task): if you raze the AI's last hall while its workers carry gold, it wrongly
writes off the mines they were heading to and never mines them again. BUG-0573 (S4): two tiny notes. The AI's army mix
compares unit counts, while the design pages give percentages of population (a 3-pop Catapult at "5 %" gets over-trained);
the sim track fixes that next session (Requests 36).

**How to change it.** The generic knobs are in `game/data/common/rules.json` under `ai` (`goldShare` 0.6, `houseWhenPopWithin`
2, `placementSearchRadius` 32 m, `buildingSpacing` 2 m, `expandHallReach` 40 m); each faction's build order and army mix are
in its `ai.json` (the D14 entry below); the difficulty timings in the same file under `difficulties`. Or an inbox line.

### The game has a front door: a main menu, a skirmish setup and a pause menu (view track, M6-V1, 2026-10-11)

**What was built.** Run `& $env:GODOT --path game` with no flags and you get a **main menu** (Skirmish, Quit). Skirmish opens
the **setup**: a seed box (type a number, or press Random), a map size list (128, 192 or 256 cells a side), two slots, "You"
and "AI", each showing the faction it plays (Malazan, Whirlwind) and the AI's difficulty, both greyed "Coming soon" because
the sim's seats landed only this session: the next view task wires them. Start launches the match exactly as before. In the
match, **Esc pauses**: the world freezes (the sim doesn't tick, no click reaches the map), a dark pane shows Resume and Quit
to menu; Quit to menu throws the whole match away and returns you to the menu, and Skirmish starts a fresh one. If you have
an order armed (A, M, a spell), a build menu open or a placement ghost up, Esc cancels that first, as it always did; the next
Esc pauses. Every script and test that passes a flag (`--seed`, `--units`, `--screenshot`, `--bench`, ...) still starts its
match at once with no menu; `--skirmish` is the flag for "the default match, no menu", and `--map-size 192` picks a bigger map.

**Also:** a construction site you saw and then lost in the fog is now drawn as a flat, darkened foundation instead of a
finished building, and you can right-click it. The flaky M4 sandbox test (BUG-0560, 1 run in 12 under load) is fixed: 16
of 16 runs pass under load now.

**What you will see.** The menu title reads "RtsGame", the project's name, until you name the game (an inbox line; it is one
word in `game/data/common/ui.json` under `menu.title`). A 192 or 256 map is the 128 map's layout (the two halls about 55 m
apart, 8 mines, 12 forests) with more empty ground around it; spreading the bases and resources out is a later map task
(BUG-0581 item 2).

**Producer decisions (owner may revisit):** (1) the match is paused by freezing the whole scene tree (Godot's own pause), so
nothing under the pane can move or hear a click; (2) the setup keeps your last seed and map size while the game runs; (3) the
no-flag smoke check now boots to the menu only, so the gate runs a second pass with `--skirmish` to keep the match under test.

**Rough edges:** BUG-0580 (S3, the next view task): hold the middle mouse button (or drag a selection box), press Esc, let go,
press Esc again: the camera (or the box) follows the mouse until you click again, because the release happened while the
match was frozen. BUG-0581 (S4): the map size items "128 × 128" are the one piece of on-screen text that doesn't come from
`ui.json` (numbers and a sign only; moved into the file next session).

**How to change it.** Every word on the three screens is in `game/data/common/ui.json` under `menu`, `setup` and `pause`.
The three map sizes are a list in `LaunchOptions.MapSizes` (an inbox line to change them).

### The AI's real build orders: 26 steps a faction, army mixes and attack thresholds, pinned to the faction pages (data track, D14, 2026-10-11)

**What was built.** Each faction's `game/data/factions/<id>/ai.json` held one placeholder step; now it holds the build order
its design page sketched, as 26 numbered steps, and the page's sketch is rewritten as a table that matches the file line for
line (a test fails by step number if either side drifts). Steps run side by side: each AI turn, every step whose trigger has
come (a time, or a population) that isn't done yet gets one command, in file order, so an earlier step gets the gold first.

**Malazan (Normal):** at 0:00 train 15 Laborers (to 20 with the 5 you start with); Billet at pop 6; Quartermaster's Depot
at 8; Legion Barracks at 10; Billet at 12; Crossbow Range at 14; Armory at 16 and Melee Weapons I; then a Billet every 7 pop
from 18 to 81 (12 Billets in all); at 6:30 Age II and 10 more Laborers (to 30); 7:30 Cadre Tower; 8:00 Engineers' Yard and
the second Garrison Keep near the nearest free mine; 8:30 Wickan Corral; 9:00 Moranth Supply; 9:30 Watchtower. **Army mix
(by population):** Age I 35 Heavy Infantry : 30 Crossbowman; Age II adds 10 Wickan Lancer, 10 Cadre Mage, 10 Sapper, 5
Catapult. **First attack wave** at 20 army pop and 1,544 gold + wood (the cost of 20 pop of the Age I mix); **retreat** when
a wave has lost half, a unit under 30 % hp counts as wounded.

**Whirlwind (Normal):** at 0:00 train 13 Camp Followers (to 18); Tent at pop 6; Raider Camp at 8; Supply Cache at 9; Tent at
11; Archer Camp at 12; Horse Lines at 14; Tent at 16; Smithy at 20 and Melee Weapons I; a Tent every 7 pop from 23 to 79
(12 Tents); at 7:30 Age II and 8 more Camp Followers (to 26); 8:00 the second Holy Camp near the nearest free mine; 8:30
Shrine; 9:00 a second Raider Camp; 9:30 Ram Yard; 10:00 Dryjhna's Prophecy. **Army mix:** Age I 30 Raider : 20 Desert Archer :
15 Horse Raider; Age II adds 20 Zealot, 10 Priest of the Whirlwind, 5 Battering Ram. **First wave** at 14 army pop and 861;
the same retreat line.

| Faction | Field | Old → new | Why |
| --- | --- | --- | --- |
| both | `buildOrder` | 1 placeholder step → 26 steps (above) | the pages' sketches, made concrete |
| both | `compositions` | 2 : 1 placeholder weights → the pages' percentages (Age I only what Age I trains) | the pages' "Composition target" |
| Malazan | `attack` | 10 pop / 800 → **20 pop / 1,544** | the page's "first wave at 20 army pop"; the cost of that pop of the Age I mix |
| Whirlwind | `attack` | 10 pop / 800 → **14 pop / 861** | the page's "first at 14 army pop" |
| both | `retreat` | unchanged 0.3 hp / 0.5 losses | the pages |
| both | `difficulties` | unchanged (Easy 2 s / Normal 1 s / Hard 0.5 s think, Hard gathers +25 %) | docs/02 |

No player-facing name or description changed. Also fixed (BUG-0561): the Andii and Edur line units' pages now say ×1.1 vs
Mounted like the template (with a note that the page held the old ×1.5 before any data existed), and the Whirlwind page's
sentence about D13's ×1.1 says what "nearest" meant (1.3 is the value to try first if you revisit the band).

**Things the file format can't say yet** (the pages note them; the sim's M5-3 decides): the sketches' "expand when the army is
12 pop and no enemy near the base for 60 s" (the step fires at 8:00 instead), the Whirlwind's 4:30 first raid of 6 Raiders + 2
Horse Raiders (the first wave goes at 14 army pop), a rally point or a watchtower at the base choke (the AI builds everything
near its hall). **One overlap to watch:** the build order's 12 Billets / Tents would reach population 106, past the 100 cap,
and the AI's own house rule (M5-2) also builds a house whenever the cap is within 2: the data track counts the houses an AI
ends up with once it can gather wood, and proposes a trim if it over-builds.

**How to change it.** Edit the step's row in the `ai.json` (and the page table, or the test tells you which side moved), or an
inbox line such as "Malazan Age II at 7:00" or "Whirlwind wave at 12 pop".

### The AI has its own eyes now: a fog-limited window on the world that provably leaks nothing, and the file format for its build orders (sim track, M5-1, 2026-10-11)

- **What was built:** the first piece of the AI opponent. Not decisions yet: the *window* the AI will look through. Every AI
  player gets a view that answers only what a human in its seat could know: its own units, buildings, gold, wood, population,
  age, upgrades and training queues always; enemy soldiers only where its own units can see them (hidden units stay hidden
  unless something detects them, exactly as for you); enemy buildings only in sight, or as the same dark "remembered" ghosts
  you see; gold mines and forests only on ground it has explored, and how much is left in one only while it is looking. A
  test walks the whole world every tick for 3 x 2,000 ticks with 4 players, stealth, sandstorms, towers and buildings dying
  out of sight and checks every answer against the fog the slow way: 0 leaks, 0 misses. A second test fails the build if any
  future AI code so much as names the world directly: the window is the only door.
- **Also:** the file format for AI build orders, `game/data/factions/<faction>/ai.json`: per difficulty how often it thinks
  (Easy every 2 s, Normal 1 s, Hard 0.5 s), how late it may run its opening, how big its attack waves are, Hard's +25 % gather
  bonus, whether it focuses fire / pulls back wounded / uses spells, when it expands; then the build order itself (steps that
  fire at a time, a worker count or a population: build X, train Y, research Z, expand), the army mix per age, and when it
  attacks or retreats. Broken files are refused with one message per fault. The two files shipped hold the minimum (train one
  worker; a line + ranged mix); the data track writes the real openings next.
- **What you'll see in the game:** nothing yet. The AI doesn't act until the next sim task. What you won't ever see: an AI that
  "knows" where your army is behind the fog.
- **Producer decisions:** (1) the AI's view *remembers nothing* by itself (what the AI believes about things it can't see is
  the next task's memory system) — except that this leaves one gap, below; (2) `ai.json` doesn't count toward the game's data
  fingerprint until the AI actually acts (so editing build orders can't change a replay yet); (3) the Normal difficulty's think
  rate (1 s) and the attack / retreat thresholds (10 pop or 800 cost; retreat at 30 % hp / 50 % losses) are builder defaults the
  design doc didn't give: data, change them in `ai.json`.
- **Rough edges:** **BUG-0540** (medium): a tree or mine that runs out *while the AI isn't looking* vanishes from its view at
  once, whereas your screen keeps the felled tree drawn until you look again. That is a (small) leak, so M5's first criterion
  stays unticked until the sim's next task fixes it. **BUG-0542** (minor, my ruling): the speed test covers a normal match
  layout (each player's army in its own part of the map, 0.23 ms for eight AIs); with every army mixed over the whole map it
  takes about twice the budget, which is still under a tenth of a millisecond per tick because an AI reads once per think.
  **BUG-0541** (minor): three nits in the loader and the design doc's wording ("rarely uses abilities" vs a yes / no switch).
- **How to change it:** the difficulty numbers and thresholds in `ai.json`; the rules of what the AI may see are docs/02
  "AI opponent" (an inbox line changes them).

### A lost spell now tells you so, spell rings lie on the ground across cliffs, and a release-build preset exists (view track, M5-V1, 2026-10-11)

- **What was built:** (1) when a mage you ordered to cast gets stuck for 3 s and the rules drop the cast (the M4-H3 rule), the
  game now shows it: a short red flash on the ground under the mage, the words **"Cast lost"** over it for half a second, and
  a low two-note "no" sound (the game's first refusal sound). Nothing shows when *you* cancel a cast with a new order, or when
  an enemy's cast is lost. (2) The cast-point ring under a walking mage and the flash where a spell lands now lie on the terrain
  on both sides of a cliff instead of floating flat at one height (the last two flat shapes; the rest were fixed in M4-VH3).
  (3) A Godot export preset ("Windows Desktop") that leaves the 40-odd test scenes out of a release build and packs the data
  files in.
- **What you'll see or feel:** select a Cadre Mage, press Q and click behind a wall of enemy Raiders; the mage walks, gets stuck,
  and after 3 s the red flash, "Cast lost" and the sound tell you the click didn't happen, so you re-order instead of waiting on a
  spell that will never come. On a cliff edge, a Telas Fire's orange ring hugs the plateau top and the low ground both.
- **Producer decisions:** the red colour, the 0.5 s, the words and the sound are placeholders until the M6 art and audio pass;
  "Cast lost" is one line in `game/data/common/ui.json` (`hud.cast_lost`) if you want other words.
- **Rough edges:** **BUG-0550** (minor): the red disc lies under the very crowd that blocked the mage, so with the HUD off it is
  mostly hidden (the words and the sound carry the cue); a taste item for the M6 pass. The export preset is real but an
  exported game can't load its data yet (the data loader reads a folder, not the packed file: a small sim-side request, Requests
  33, planned with the M6 export task); and this machine has no export templates (your download, before M6).
- **How to try it:** `& $env:GODOT --path game res://tests/AbilityViewTest.tscn` runs the scripted drop; or windowed, order a
  spell into a tight enemy crowd.

### D12 landed and the line units' anti-cavalry bonus went from ×1.5 to ×1.1, which barely moved the fight: your one-line choice is wanted (data track, D13, 2026-10-11)

- **What was built:** (a) D12 (both casters detect hidden units at 10 m; its own entry below) is on `main` now, merged exactly
  as recorded. (b) The first balance pass toward the band I set as default from your D6 proposal (the counter's winner keeps
  40-65 % of its cost). One lever only, as the brief allowed: the line units' bonus against mounted units, the same on both
  factions.

| Unit | What | Old → new | Why |
| --- | --- | --- | --- |
| Heavy Infantry (Malazan) | damage bonus vs Mounted | ×1.5 → ×1.1 | the nearest value to the band that keeps every test green |
| Raider (Whirlwind) | damage bonus vs Mounted | ×1.5 → ×1.1 | the same value on both factions (it is the Line template's bonus) |

- **What it did:** almost nothing. Line still beats Shock in all four match-ups, keeping 88-100 % of its cost (was 88-100 %).
  The sweep from ×1.5 down to ×1.0: no value lands in the band; the margins move in whole bodies (the winner loses 0-4 soldiers
  and the loser is wiped out at every value). ×1.0 came nearest (79-94 %) but broke one stress test, so the pass stopped one
  step back. The fights got longer (23 → 30 s), which is the only thing a player would feel.
- **What this tells us (the builder's, QA's and my reading):** the Line-beats-Shock counter rests on the line units' hit points,
  armor and cost per body, not on the bonus. Narrowing the margin needs one of those levers (a later pass, if you want one).
- **Your choice, one inbox line:** (1) **keep ×1.1** (as shipped; the design doc now says ×1.1 with a note); (2) **back to ×1.5**
  (two data values + their pins + one fingerprint update; the data track does it first thing); (3) **"tune hp / armor / cost"**
  (a bigger pass with a new table for you). No names or descriptions changed ("break cavalry charges" is still true).
- **Rough edges:** **BUG-0561** (minor): the two future factions' pages (Andii, Shadow) still say ×1.5 for their line units with no
  note; whirlwind.md's wording of "nearest"; the pick between ×1.1 and ×1.3 is a near tie and the pages don't say what "nearest"
  measured. Folded into the data track's next task. **BUG-0560** (medium, the view track's test script, not the game): the
  sandbox battle test can wait out its clock when the enemy army is wiped before a Telas Fire burns anyone (one run in twelve,
  under heavy load); the view fixes the script next session.

### M4 is done: fighting, fog of war and spells are signed off (all three tracks, 2026-10-11)

- **What M4 is:** everything that makes a battle. Units fight (melee and shots that can miss, splash and hurt your own
  side), die and leave bodies and rubble; the map starts black and clears where your units go, high ground sees down,
  towers shoot, remembered enemy buildings stay as ghosts; spells and statuses (Telas Fire, the Cusser, Sandstorm, the
  Zealot's rage), summons and hidden units exist in the rules; the HUD shows all of it (ability buttons, targeting rings,
  cast bars, status markers, storm discs); and a scripted Malazan-vs-Whirlwind battle with the fog on proves it on every
  frame. Four days over three tracks (2026-10-07 to 2026-10-11).
- **I signed it off myself** (your autopilot setting `stop_at_milestone_end: no`). Conditions held: all ten criteria
  verified, every M4 system covered by unit, fuzz and determinism tests, every track's clean-up session done (the three
  entries below), no serious open bug. The leftovers are listed in the retro (docs/05): three medium ones from this
  clean-up (a mage can stand "walking" beside its own fighters, a huge crowd ordered onto one building partly gives up,
  an unreproduced wobble) and the old crowd-movement limits; all go to the next clean-up sessions.
- **How to try it:** `& $env:GODOT --path game res://tests/M4PlayableTest.tscn -- --shots build/shots/m4-v7` (a minute
  per seed; add `--seed 6` for the other map) watches the whole battle through the real HUD; or run the game windowed,
  select a Cadre Mage, press Q and click to burn the ground, select the Priest of the Whirlwind and press Q for a
  sandstorm, right-click an enemy to attack it. Your windowed impressions are feedback, not a gate.
- **What's next (M5):** the AI opponent. First the AI's own, fog-limited view of the world and the file format for its
  build orders (sim), the "your cast was lost" cue in the HUD (view), the balance pass (data); then the AI's economy,
  army, attacks and difficulties, and an AI-vs-AI headless match.

### A spell's walk no longer gives up behind your own fighters, a lost cast is reported, and an attack order on a reachable building is never dropped (sim track, M4-H3, 2026-10-11)

- **What you'll feel:** select a Cadre Mage standing behind your line, press Q and click Telas Fire on the enemy beyond
  your fighters. Before, the mage shuffled for one second against its own side's backs and then quietly forgot the order
  (seen three times in the sandbox battle). Now it waits behind its own fighting units for as long as they fight and
  casts the moment a gap opens; if it is truly stuck (enemies, a wall), it gives the cast up after 3 seconds instead of
  1, and the rules now tell the HUD about it (the cue itself is the view's next task; today the cast bar just ends).
- Also: right-click an enemy building whose only way up is a ramp that first leads away. Before, after 2 seconds the
  unit stopped 48 m short with no sign; now it walks the ramp round and attacks. An enemy on a cliff-top with no way up
  is still given up after 2 seconds, so nobody paces a cliff for ever.
- Closed as well: three stealth corners (a death-rage spell hiding several friends reached only one; data without a
  "revealed" status loaded; a reveal was dropped when a unit already carried eight statuses), the ghost of an enemy
  building remembers whether it was a construction site (the view can draw it as a foundation later), and a speed test
  on 2,500 idle units that sat on its limit got a 10 % wider limit after a profile showed the test's own scene is the
  cost (most of its units spawn off the map into one corner; BUG-0510, QA's to fix).
- **Producer decisions (owner may revisit):** the waiting rule looks 1.5 m ahead of the mage; the patience is 3 s; a mage
  ringed by its own fighters in an endless fight waits for good (the alternative was a timer; a lost click is worse than
  a late cast). Both numbers are match settings with defaults, so an inbox note ("make a hemmed-in cast give up after
  5 s") changes them. Documented limits, not fixed: the shot store is sized for units, so a tower's shot is lost only
  when 200 shooters all have one in the air (BUG-0271); one remembered ghost per building slot (BUG-0272); a worker
  standing inside its own building site counts as "in the way" for the ghost but not for the order (BUG-0142).
- **Rough edges:** BUG-0511 (S3): a fighter *beside* the mage, not in front of it, also makes it wait, so a mage next to
  its own skirmish line can stand "walking" until that neighbour's fight ends; next sim clean-up (the rule becomes a
  corridor ahead of the mage). BUG-0512 (S3): 500 units ordered onto one building partly give up once they reach the
  crowd round it (on the old rules all 500 gave up at once; 50+ still get through and attack); next sim clean-up.
  BUG-0461's wobble half: a unit stepping 17 cm back and forth each tick on flat ground, seen in one saved replay that
  no longer loads; not reproduced since, documented, open.

### Sandstorm discs and spell rings now lie on the ground across cliffs, and your own storm's rim is teal (view track, M4-VH3, 2026-10-11)

- **What you'll see:** cast a Sandstorm on a cliff edge: the dust disc lies on the plateau top and on the low ground
  below, with a step at the wall, instead of sinking under the higher side and floating over the lower. The targeting
  rings when you press Q (the area circle under the cursor, the range circle round the mage) and the hover ring of a
  self-cast do the same. Your own storm's rim is a light teal; an enemy's is its faction colour (Whirlwind gold), so a
  Whirlwind-vs-Whirlwind match tells them apart. Health bars also cost half as much per frame (one max-hp pass instead
  of two; not visible, just cheaper).
- **Producer / builder decisions:** the discs are built from the terrain's own cells (one tile per ground cell under
  the disc, the circle cut out per pixel) rather than a projected decal, which would also paint the units standing in
  the storm. The teal is a taste default until the M6 team-colour pass; an inbox note changes it. Closed as won't-fix
  with the reason: two neighbours' status markers touch at the farthest zoom, the resource bar rebuilds its text when a
  total changes, and about 1 % of clicks on a tree's edge pick the tree: all wait for the M6 art and HUD passes.
- **Rough edges:** BUG-0520 (S4): the orange ring at a mage's cast point while it walks in, and the half-second flash
  where a spell lands, are still flat across a cliff; next view session. BUG-0521 (S4): three corners nothing reaches
  today. Export hygiene (keeping test scenes out of a release build) is not done; it stays with M6's export task.

### Both casters now detect hidden units at 10 m (data track, D12, 2026-10-11; accepted in session 2334, **on `main` since session 0314**)

| Unit | Field | Old → new | Why |
| --- | --- | --- | --- |
| Cadre Mage (Malazan) | detector | none → 10 m | docs/02 "Caster" class row and "Stealth and detection": all casters detect at 10 m |
| Priest of the Whirlwind | detector | none → 10 m | the same |

- **What you'll see:** nothing yet. No unit can hide before M8 (Shadow). From then on a Cadre Mage or a Priest within
  10 m of a hidden enemy standing on ground you can see reveals it to you (a watch tower does the same at 16 m, and any
  unit within 3 m).
- No name or description changed; the faction pages' notes already said "detector 10 m" and are now pinned to the data
  both ways. The "Stealthed" and "Revealed" status texts are pinned to the design doc too.
- **A process note:** the sim's tests still asserted "no shipped unit detects", so the data branch and the sim branch
  edited the same two test files. The conductor's rule forbids hand-merging code, so the work is accepted but waits on
  its branch; I verified the resolution on a scratch merge and the data track applies it first thing next session (a
  few minutes). From now on a data brief names every sim test that pins the value it changes.
- **How to change it:** `"detector"` in `game/data/factions/<faction>/units.json`, or an inbox note.

### Stealth is in the rules: a hidden unit can't be seen or attacked unless a detector, a watch tower or a unit standing right beside it catches it (sim track, M4-5, 2026-10-10)

**What was built.**
- The game now has a rule for hidden units. A **Stealthed** unit is invisible to its enemies and can't be attacked by them: their
  soldiers won't pick it as a target, won't fight back against it, and a right-click order on it is refused, exactly as if it stood in
  the dark. You always see your own.
- **Detection.** An enemy sees through stealth only when two things are both true: the hidden unit stands on ground that enemy can see
  (fog of war), and it is within reach of one of that enemy's detectors: a finished Watch Tower (16 m), a unit with a detector (the
  design gives every caster 10 m; that number goes into the data next session, D12), or **any** enemy unit within 3 m (so stealth is
  beatable before Age II, when nobody has towers or casters). Once detected it is targeted and shot like anything else.
- **Reveal.** A stealthed unit that lands a hit or finishes casting a spell is **Revealed** for 3 s: every enemy whose sight covers it
  sees and can attack it, and each new hit restarts the 3 s. A summoned unit (M8's Wraiths) can never be stealthed.
- **Nothing you play changes yet.** No Malazan or Whirlwind unit can stealth; the first stealth sources are Shadow's (M8: forest stealth
  and the Shadow Archer standing still). The tests use a stand-in spell and a stand-in detector.

**What you will see in the game.** Nothing new until M8. Because the fog-of-war check is the one place the rule lives, the screen
already hides an undetected stealthed enemy with no extra work. The proper look (your own hidden units drawn translucent, a mark on
a detected enemy, detector rings round towers and casters) comes with Shadow in M8, where the roadmap lists it.

**Producer decisions (owner may revisit; `docs/01-vision.md` row 2026-10-10 "Stealth and detection").**
- *Proximity works like a 3 m detector*, with the same "on ground you can see" condition: a Blinded unit standing next to a hidden
  enemy in the dark does not detect it. Alternative: proximity always detects, whatever the fog.
- *The reveal starts when the blow lands or the spell resolves*, not when the swing starts; a shot still in the air reveals nothing.
- *Only a unit that is actually carrying stealth gets Revealed by attacking.* A plain soldier that attacks and is stealthed a moment
  later is hidden at once. Alternative: every attacker carries a 3 s "Revealed" mark (one status entry per hit for every unit in every
  fight, and a different replay fingerprint).
- *Stealth visuals move to M8* (where the roadmap already lists them), since no unit can be stealthed before then.

**Rough edges (none reachable in play today).** BUG-0480 (S3): a death-triggered passive that applies stealth to several friends
reaches only the first; BUG-0481 (S3): a data file with a stealth status but no "revealed" status would load without complaint and
nothing would ever reveal; BUG-0482 (S4): a unit already carrying eight statuses can't be revealed. All three are planned for the
next sim session (the end-of-M4 clean-up).

**How to change it.** The 3 m proximity and the 3 s reveal are in `game/data/common/rules.json` (`stealth`); the two status texts are
in `game/data/common/statuses.json`: **Stealthed** "Unseen by enemies, who can't target it unless they detect it: a detector near it,
or any of their units right beside it." and **Revealed** "Can't be stealthed while it lasts. Attacking or casting from stealth reveals
a unit for a short while."; a unit's detector radius is a `detector` number in its `units.json` entry (the towers' is already in
`buildings.json`).

### Health bars are right after an upgrade, a summon spell is one click, and the sandbox battle weathers an enemy sandstorm (view track, M4-V7 slice 2, 2026-10-10)

**What was built.**
- **Health bars and the hit flash use a unit's real maximum** (BUG-0450 from last session): after Dryjhna's Prophecy a Zealot at 70 of
  83 shows a bar at 84 %, one at 83 of 83 shows none, and a unit first seen hurt still flashes white once.
- **Summon spells cast with one click**, like self-cast spells: the button or its key casts at once with the first selected caster that
  has the spell ready; the tooltip shows only the cooldown (a summon has no range or radius); when the summoned units' time runs out
  they simply vanish: no corpse disc, and the kill and loss counters don't move. No shipped summon exists before M8; the test uses a
  stand-in spell.
- **The sandbox battle got a ninth step:** the enemy Priest calls a Sandstorm over your army; your units inside get the violet Blinded
  and blue Slowed markers, the orange enemy storm disc is drawn only where one of your units can see into it, and the fog on the ground
  shrinks round your blinded units. The run still checks every frame that nothing hidden is drawn (0 slips in 14 runs) and ends with
  the replay-into-a-bare-copy proof.
- The note on how long the sandbox takes now states the measured range instead of a guessed margin (BUG-0462 item 5).

**What you will see in the game.** Watch the sandbox: `& $env:GODOT --path game res://tests/M4PlayableTest.tscn -- --shots
build/shots/m4-v7` (about a minute per seed; add `--seed 6` for one): after the Zealot duel, the storm rolls over your army and
your soldiers go violet and blue under it. In a normal match you will notice the bars once an opponent researches upgrades (the M5
AI): an upgraded enemy Zealot's bar reads right instead of showing full while hurt.

**Producer decisions.** None new: the summon card follows the self-cast card's rule (M4-V6d).

**Rough edges.** BUG-0490 (S4): the stated length range of the sandbox is already slightly exceeded (the real margin is still 18 %),
and the per-unit maximum is copied twice a frame; both in the next view session (the end-of-M4 clean-up).

**How to change it.** Nothing here is data; an inbox note.

### The data track's third clean-up, and D11 landed: a design page can't claim two different passives for one trigger, and four more tooltip wordings are read right; nothing in the game changed (data track, D-H3, 2026-10-10)

**What was built.** Tests only.
- **D11 is on `main`** (last session's Zealot passives and Frenzied / Blinded wording, held one session): the one-line fix the hold
  needed landed identically from the sim and data branches.
- If a faction page ever states two "on death" (or two "below half health") passives for one unit, the check fails naming both
  sentences: the data holds one per trigger, so one of the two would be a lie.
- Four tooltip wordings that could have hidden a wrong number now bind to the right field: "(cooldown: 45 s)" with a colon,
  "a 12-second storm", "120 siege dmg", and a lingering effect's seconds read from their own phrase ("Blinded for 1 second after a
  unit leaves").

**What you will see in the game.** Nothing: no name, description or number changed (data review table: empty).

**Producer decision (owner may revisit).** The lingering seconds are read from the number's own phrase rather than from anywhere in
the sentence, so "a storm for 12 seconds whose effects linger after it leaves" reads 12 as the storm's length. Cost: three rarer
phrasings ("a 1 s linger after leaving") now fail wrongly (BUG-0500, S4; no shipped text uses them; the next data clean-up, or D12 if
a few lines).

**How to change it.** Not applicable (test code: `sim/Rts.Sim.Tests/Content/AbilityContentTests.cs`, `PassiveContentTests.cs`).

### Upgrades that add hit points now really add them, and the rules can summon temporary units (sim track, M4-4b-3b, 2026-10-10)

**What was built.**
- **Hit-point upgrades are real.** Dryjhna's Prophecy says "+20 HP for Zealots"; until now no upgrade had ever changed a
  unit's hit points (a gap since M3, BUG-0421). Now every Zealot has 83 hit points after the research instead of 63: a Zealot
  alive when it finishes gains the 20 at once (one at 41 of 63 becomes 61 of 83, and is no longer in its frenzy), and new
  Zealots start at 83. With that, all four signature abilities work with their real numbers (M4 criterion 8 ticked).
- **The "below half health" line is exact maths** (BUG-0420): a unit exactly at the line never counts as below it (15 of 100
  at a 15 % line is not below). The shipped 50 % was never affected.
- **Summons.** A new spell kind puts temporary units beside the caster for a set time, then they vanish: no cost, no
  population, full health, and their vanishing counts as neither a kill nor a loss (no corpse, no Martyrdom-style trigger).
  Killed early, they die like anyone. Built for Shadow's Summon Wraiths (M8); **no faction uses it yet**, so nothing you play
  changes. The tests use a stand-in spell (three Heavy Infantry for 20 s).

**What you will see in the game.** Play Whirlwind, reach Age II, research Dryjhna's Prophecy at the Smithy: every Zealot's
health rises by 20 on the spot, hurt ones included; a Zealot now goes into its frenzy only under 41 of 83. **One rough edge you
will notice:** the health bar over a Zealot still thinks its maximum is 63 (BUG-0450): a Zealot at 70 of 83 shows no bar at
all, and one at 62 of 83 shows an almost-full one. The next view session fixes it first thing. Summons: nothing on screen,
no shipped spell.

**Producer decisions (owner may revisit; `docs/01-vision.md` row 2026-10-10 "Hp upgrades and summons").**
- *Maximum and current health move together* when an hp upgrade finishes (the usual RTS rule). Alternative: only the
  maximum rises and the unit stays as hurt as it was; say so if you prefer it.
- *Target-a-unit spells and autocast are deferred* until a faction's page needs them: the design reserves target-unit for
  later heroes and no faction page asks for autocast, and the AI will cast through the same order as you do.
- *Summons land on free ground within 6 m of the caster* (three rings of cells); in a packed crowd the ones that find no room
  are simply not made; at most 8 per spell. A summoned unit stands idle until ordered.

**Rough edges.** BUG-0450 (S3, view, above; next view task). BUG-0451 (S3): one of the speed benchmarks (2,500 idle units in
half a millisecond) sits right on its limit, and did so before this task too; at the end-of-M4 clean-up the studio either
makes the idle path cheaper or widens the limit with the reasons written down. BUG-0452 (S4): a summon spell's data file
must still carry an "affects" field it doesn't use and accepts a "radius" it ignores; same clean-up.

**How to change it.** The upgrade amounts are data (`game/data/factions/whirlwind/techs.json`, Prophecy's `hp` 20); a summon
spell would be an `abilities.json` entry (`kind: summon`, a `spawn` effect with the unit, count and lifetime). The 6 m search
and the cap of 8 are code constants: an inbox note changes them.

**Update (session 1954).** The health-bar rough edge (BUG-0450) is fixed: bars and the first-sight flash use the real maximum
(the M4-V7 slice 2 entry above). BUG-0452 is fixed too (a summon spell's data file no longer needs an `affects` field and refuses a
`radius` it would ignore). BUG-0451 (the speed benchmark on its limit) waits for the end-of-M4 clean-up.

### The Frenzied marker is magenta, a self-cast spell is one click, and the M4 battle is proven by a scripted sandbox fight under fog (view track, M4-V6d + M4-V7 slice 1, 2026-10-10)

**What was built.**
- A **hot magenta marker** over a Frenzied unit, next to the orange Burning, blue Slowed and violet Blinded ones (each colour
  at least 0.4 apart from the others in some channel; checked side by side on a Heavy Infantry and a Raider).
- **Self / aura spells cast with one click**: a spell with no target never shows an aiming circle; the button or its key casts
  at once. While your mouse rests on the button a ring shows where it would land, around the unit that would cast it (the
  first selected unit that has the spell if it is ready, else the ready one nearest it). Shift queues it. No shipped spell is
  of this kind yet (future factions), so the headless test uses a stand-in.
- **The M4 "Playable" proof** (`game/tests/M4PlayableTest.tscn`), after the M3 one: a scripted Malazan-vs-Whirlwind battle
  through the real HUD with the fog on. Both bases with three buildings each, a 17-unit Malazan army and a 13-unit Whirlwind
  one; the script box-selects, presses A and clicks into the dark, meets the enemy, calls a Sandstorm, casts Telas Fire, counts
  kills and losses on the resource bar against the rules, walks a Sapper up to a building for a Cusser and watches the rubble
  appear, and sets two Zealots duelling so a Frenzied one (23 of 63) swings 4-5 times while an unhurt one swings 3 in the same
  three seconds. On every frame it checks that no enemy unit, bar, marker or building is drawn unless one of yours can see it
  (0 slips in about 3,100 frames a run), and at the end it replays every order into a bare rules-only copy: identical. Passes on
  seeds 1 and 6 every time (28 runs by QA, 2 by me). **M4 criterion 10 ticked.**

**What you will see in the game.** Watch it: `& $env:GODOT --path game res://tests/M4PlayableTest.tscn -- --shots build/shots/m4-v7`
(about a minute per seed at 8x speed; add `--seed 6` for one seed), or open the ten stills it saved in `build/shots/m4-v7/`
(first contact under fog, the storm, Telas Fire, the Cusser rubble, the Frenzied Zealot; seeds 1 and 6). Two staging
caveats: the Malazan army carries a Whirlwind Priest of its own (you are always seat 0, Malazan, until the M6 lobby lets you
pick a side, and only a Priest can call a storm), and the Whirlwind side uses Horse Raiders where the brief said "Lancers"
(the Whirlwind has no lancer).

**Producer decisions (owner may revisit).** The magenta is a taste default until the M6 art pass. The self-spell caster rule
(lead first, else the nearest ready) keeps one cast per press. The staging deviations above are accepted; the sandbox is a
test scene, not a map.

**Rough edges.** **BUG-0461 (S3, rules side):** a caster told to cast beyond its own fighting troops gives up after one second
when it can't get through them, and your click is lost with no message (it also jitters in place meanwhile). Seen three times
in this sandbox; the scene works round it (it walks the Sapper with plain moves and re-clicks). Triaged to the end-of-M4 rules
clean-up with a direction: patience for a cast's walk-in behind own units, and feedback when a cast is dropped. BUG-0462 (S4):
the hover ring floats over a cliff edge like the storm discs do (BUG-0430, same fix); the marker rows of two units 2.5 m apart
touch at the farthest zoom; the scene's stated time margin (40 %) is optimistic (one run took 4,212 of 5,500 ticks, 23 %).
BUG-0450 (S3, the health bar's maximum; the sim entry above). Two flakes QA found in the new scene, a recycled unit slot and a
61-tick count window, were fixed in-session (BUG-0460, BUG-0463, both S2, closed).

**Not yet (slice 2).** The summon spell's button (same card, one line), an enemy storm over your own army in the sandbox, the
health bar fix; stealth visuals after the rules' M4-5.

**Update (session 1954).** The sandbox has a ninth step: an enemy Sandstorm over your army (the M4-V7 slice 2 entry above);
the two staging caveats stand.

### The Zealot's two passives and the Frenzied status are pinned to their pages, and two descriptions are reworded (data track, D11, 2026-10-10)

**Status: accepted, not on `main` yet.** After merging, one rules-side test refused it: it had the old replay fingerprint
written in as a fixed number, and D11 changes that fingerprint on purpose (text changed). Nothing in the game is wrong; the
studio changes that one line (the test will read the fingerprint from the replay file instead) and lands D11 first thing next
session. Until then the game still shows the old Blinded and Frenzied wording.

**What was built.** Tests that compare the Zealot's two built-in traits (Frenzy of the Apocalypse, Martyrdom) in the data with
the sentences on `docs/factions/whirlwind.md`, number by number, both ways, so a drifting value fails naming the trait and the
field (QA tried 16 small drifts on each side: all caught). The Frenzied row of the design doc now states the exact rule and is
pinned too; Frenzied is no longer on the "pending" list. Two description texts changed; no number changed anywhere.

**Text that changed (for your review; the game shows these as is).**

| Where | Field | Old → new | Why |
| --- | --- | --- | --- |
| `common/statuses.json`, Blinded | description | "Can barely see: sight drops to 2 m, and it can't **target** anything more than 3 m away." → "...it can't **attack** anything more than 3 m away." | the proposal from D10c (you did not veto): you can still order a Blinded unit at something farther, it walks closer; the rule caps the hit |
| `common/statuses.json`, Frenzied | description | "Attacks faster until it wears off. Only the strongest frenzy counts." → "Attacks faster: less time between swings, though each swing winds up as before. Only the strongest frenzy counts." | the exact rule; "until it wears off" was wrong for the Zealot's own frenzy, which lasts as long as it is under half health; no number because each source sets its own strength |
| `docs/factions/whirlwind.md`, Martyrdom | page text | "Zealots within 6 m gain Frenzied for 5 s." → "Zealots within 6 m gain Frenzied **(+50% attack speed)** for 5 s." | the data's 0.5 had no page source; **Producer default, owner may revisit** ("make Martyrdom +30 %" in the inbox) |
| `docs/02-game-design.md`, Frenzied row | Effect | "Attack speed and/or damage up" → "Attacks faster: the attack cooldown is divided by 1 + the strength; the wind-up is unchanged; only the strongest counts" | the exact effect; the "damage up" half is not built (M7) |

**Rough edges.** BUG-0470 (S4): if someone added a second, contradicting Martyrdom-style sentence to the page, the pin would
not read it; next data clean-up. The replay fingerprint moved once (text only; every checkpoint equal).

**How to change it.** Edit the description in the data file or the sentence on the page and say so in the inbox: whichever
side you change, the test tells the studio which numbers to move.

### The Zealot fights harder when hurt and when a brother falls: the Frenzied status, unit passives and spells cast around the caster are in the rules (sim track, M4-4b-3a, 2026-10-10)

**What was built.** Three pieces of the ability system that the design still owed, all from data files:
- A **Frenzied** status: a Frenzied unit swings faster (its attack cooldown shrinks; +50 % means a 1-second swing every
  0.65 s). Only the strongest frenzy on a unit counts, like slows.
- **Unit passives**: a unit type can carry up to two built-in traits in its data file, with no code per unit. Two kinds
  exist: "while below X % of my health, I have this status" and "when I die, my side's units of these types within R
  metres get this status for T seconds". The **Whirlwind Zealot** has both from its faction page: *Frenzy of the
  Apocalypse* (below 50 % health: Frenzied +50 %) and *Martyrdom* (a dying Zealot gives Frenzied +50 % for 5 s to Zealots
  within 6 m; never to itself, Raiders or enemy Zealots).
- A **self / aura spell kind**: a spell with no target that lands around the caster where it stands when the cast finishes.
  No shipped spell uses it yet (it needs a faction-page row first); it is there for later factions.

**What you will see in the game.** Nothing new on screen this session except faster swings: in a brawl a Zealot under
half health attacks every 0.65 s instead of every second, and when one Zealot dies the Zealots around it speed up for five
seconds. The Frenzied marker over the unit (a colour next to the orange Burning, blue Slowed and violet Blinded) comes next
session (view M4-V6d). Zealots need Age II, so you will meet this in the fog-on sandbox fight (view M4-V7) more than in
today's test scenes.

**Data that changed (for your review; the data track pins these to the faction page in D11).**

| Where | Field | Old → new | Why |
| --- | --- | --- | --- |
| Status `frenzied` (new, `common/statuses.json`) | name / description | — → "Frenzied" / "Attacks faster until it wears off. Only the strongest frenzy counts." | docs/02 "Status effects" Frenzied row; the text states no number on purpose (the data track pins a wording in D11). **Since D11 (2026-10-10-1314): "Attacks faster: less time between swings, though each swing winds up as before. Only the strongest frenzy counts."** |
| Whirlwind Zealot | passive 1 | — → below 50 % hp: Frenzied +50 % | whirlwind.md line 55 (Frenzy of the Apocalypse) |
| Whirlwind Zealot | passive 2 | — → on death: Frenzied +50 % for 5 s to Zealots within 6 m | whirlwind.md lines 56-57 (Martyrdom; the page gives 6 m and 5 s, not the strength: +50 % is a Producer default. **Since D11 the page states "+50% attack speed", pinned both ways**) |

**Producer decisions (owner may revisit; recorded in `docs/01-vision.md`, row 2026-10-10 "Frenzied and the Zealot's passives").**
- *"Attack speed" means the swing rate:* the pause between swings shrinks; the wind-up (the moment before the blow lands)
  stays. The alternative (shrink both) makes the first blow land sooner too; say so if you want it.
- *The "damage up" half* of docs/02's "attack speed and/or damage up" waits for M7 (the Teblor's Blood-oil Frenzy is its
  first user).
- *Martyrdom's strength is +50 %*, the same as the Zealot's own frenzy, because the page gives none.
- *Exactly at the line is not below:* a Zealot at 31 of 63 health is Frenzied, at 32 it is not.

**Rough edges.**
- **BUG-0421 (S3):** Dryjhna's Prophecy says "+20 HP for Zealots", but no tech has ever raised a unit's hit points (a gap
  since M3-5 that this task made visible): after Prophecy a Zealot still has 63 health and goes Frenzied at 41, which is
  65 % of its real health, not 50 %. **Fixed in M4-4b-3b (session 2026-10-10-1314)**: units spawn with the bonus and
  living units gain it the moment the tech lands (the entry at the top).
- BUG-0420 (S3): for fractions like 15 % a unit exactly at the line can count as below (a rounding error in the maths);
  the shipped 50 % is exact and unaffected. **Fixed in M4-4b-3b** (the line is exact maths now).
- BUG-0422 (S4, nits): a Zealot trained in the same instant a brother dies is missed by Martyrdom (a documented rule, like
  splash); the memory budget's safety margin was restored to 72 KB rather than kept at 19 KB.

**How to change it.** The numbers are data: `game/data/factions/whirlwind/units.json`, the Zealot's `passives` block
(`hpFraction` 0.5, `magnitude` 0.5, `duration` 5, `radius` 6) and `game/data/common/statuses.json` for the Frenzied text.
"Make Martyrdom +30 %" or "Frenzy below 40 %" in the inbox and the data track moves it.

### You can see the sandstorm now: a dust disc on the ground where a storm is, your own always and the enemy's only where your units can see it; Blinded units get a violet marker (view track, M4-V6c, 2026-10-10)

**What was built.** A flat, translucent sand-coloured disc on the ground for every live storm the player may see, with a
darker rim (your own storms) or a rim in the enemy's colour (theirs), fading out over the storm's last second; and the
Blinded marker's own colour (dark violet) beside the orange Burning and deep blue Slowed ones.

**What you will see in the game.** Select a Whirlwind Priest, press its Sandstorm key, click: when the cast finishes a
6 m sand disc appears there and sits for 12 s, going transparent over its last second. An enemy Priest's storm under the
fog is invisible until one of your units sees its edge (its sight reaches within 2 m of the storm) or stands inside it,
then the disc appears; a storm on a plateau above your men stays hidden until they see its rim. With fog off (`--no-fog`)
every storm is drawn. A unit caught in a storm shows a violet marker beside its blue Slowed one. Windowed test scene:
`AbilityViewTest.tscn` (seeds 1 and 6; `-- --shots <dir>` saves `ability-seed1-storm.png`, `-storm-enemy.png`,
`-blinded.png`).

**Producer decisions (owner may revisit).**
- *Where an enemy storm shows* (docs/02 "Zones"): wherever one of your units sees any ground within the storm or its 2 m
  edge band; ground you merely remember shows nothing (the fog remembers terrain and buildings, not live effects).
- *Colours* (taste defaults until the M6 art pass): sand fill at 35 % opacity, a deeper-sand rim for your own storms, the
  owner's team colour for an enemy's.
- *A seen enemy storm is drawn whole*, even where part of it lies over black unexplored ground (M6 fog-look pass if it bothers you).

**Rough edges.**
- **BUG-0430 (S3):** a storm that straddles a cliff: the disc is one flat plane at the height of the storm's centre, so on
  the higher side it is hidden under the ground and on the lower side it floats 4 m up. The end-of-M4 view clean-up (the
  disc must hug the terrain, like the corpse discs beside ramps do).
- BUG-0431 (S4): in a Whirlwind mirror match your rim (deep sand) and the enemy's (Whirlwind gold) look alike; with the
  M6 team-colour pass.

**How to change it.** The colours, the 35 % opacity and the 2 m edge band are constants in the view code
(`AbilityViews`, `ZoneDiscs`); write "make enemy storms red" or "show storms only where my units stand" in the inbox.

### The data track's second clean-up: a description's numbers are now checked by what the words say they are; nothing in the game changed (data track, D-H2, 2026-10-10)

**What was built.** Tests only. The check that keeps a spell's or status's description honest now reads each number by
its words: "12 seconds" must be the duration, "every 45 s" the cooldown, "18 m" the range or radius, "30 %" the strength,
"120 siege damage" the damage. Before, "for 18 seconds" on Sandstorm passed because 18 happened to be its range
(BUG-0410, closed). Two older QA notes closed on the way.

**Data that changed.** None: no unit, building, tech, spell or status value or text changed; the saved data fingerprint
is untouched by this task (the sim's M4-4b-3a moves it this session, see above).

**Rough edges.** BUG-0440 (S4): four wordings no shipped text uses ("cooldown: 12 s" with a colon, "18-second", "dmg",
a linger phrase in a longer sentence) still bind to the wrong field; the next data clean-up. Two known gaps stay as
documented: distance words are one role (range vs radius are not told apart), and "for N s" cannot tell a spell's own
duration from the duration of the status it applies.

**Next for the data track (D11, next session):** pin the Frenzied status and the Zealot passives to the faction page,
reword the Frenzied description, and adopt the Blinded wording ("can't attack anything more than 3 m away") unless you say no.

### Ten rules-side bugs fixed: a sandstorm hides exactly who it slows, a tower shooting down from a hill gives itself away, chasers stop running after unreachable enemies, and attacking a remembered building ends the moment you see it is gone (sim track, M4-H2, 2026-10-10)

**What was built.** The sim's clean-up session: no new feature, ten known bugs closed (eight whole, one in two of its
three parts), each with a test that failed before the fix. Nothing in the data files changed; the saved replay
fingerprint is untouched.

**What you will see in the game.**
- Drop a **Sandstorm** on an enemy group: everyone whose body centre is inside the 6 m storm is hidden from you, and your
  crossbowmen will not shoot them; a unit a hand's width outside the edge is visible and shootable. Before, the storm used
  the map's grid squares for hiding but the unit's own position for blinding, so a unit 5 m inside could still be shot
  while one just outside vanished (BUG-0360). A storm that has just appeared hides at once (no 3-tick gap, BUG-0363), and
  storms far from a fight no longer slow every unit's target search (BUG-0363).
- A **watch tower on a plateau** that shoots your army below now shows itself for 2 seconds per hit, like a unit on high
  ground does, so your archers can shoot back (BUG-0270). Before, a tower above you stayed invisible while it fired.
- **Chasers give up.** Attack-move past enemies standing on a cliff top your men cannot reach: after switching between
  unreachable targets 10 times (2 s) without landing a blow, a unit gives up and walks on, instead of pacing along the
  cliff forever (BUG-0241). A unit whose friend is fighting that target keeps trying.
- **Right-click a remembered enemy building** (a dark ghost) that has since been destroyed: your units walk until one of
  them sees the ground, the ghost vanishes and the order ends at that same moment (BUG-0311). Before, the order ended
  about a metre early, the ghost lingered 19-26 s and a fresh attack on it was refused.
- **Spam-clicking attack-move** while a brawl is on: units already on an attack-move that are chasing an enemy they can
  see keep chasing instead of dropping it with every click; a brawl keeps at least 90 % of its damage on average (measured
  94.8 %), no 1-20 tick click rhythm under 80 % (BUG-0157).
- Under the hood: a one-tick massacre of every unit and building no longer crashes (BUG-0330); a replay refuses a
  test-only setting playback could not rebuild (BUG-0361); a wrong-shaped number in a data file gets the right error
  message (BUG-0242).

**Producer decisions (owner may revisit; recorded in `docs/01-vision.md`, row 2026-10-10 "Sim hardening rules").**
- *Who a storm hides:* by the unit's own centre, the same test the blinding uses. The alternative (hide by grid square)
  kept the 1.4 m band of disagreement at the rim.
- *Kept chase on a redirect:* only for units already on an attack-move leg whose target they can still see. The builder
  tried keeping every chase (including a unit that turned to fight back where it stood) and found crowd-blocked units left
  standing idle in reach of enemies; the narrower rule keeps the first attack-move sending them off as before.
- *Give up after 10 target switches* (2 s) with no blow landed, unless a friend fights the target. The same engine
  constant as the existing "10 scans without progress" rule.
- *A tower's reveal lasts 2 s*, the same as a unit's high-ground reveal.

**Rough edges.** BUG-0390 (S3): one of the view's automated test scenes (`QaGhostViewTest`) assumed the old, slower ghost
behaviour and now fails one check; the game is right, the test is stale. It is rewritten at this session's integration
(the conductor dispatches it) or first thing in the view's next session. BUG-0391 (S4, two wording nits in code
comments and tests): the next sim hardening. Not reached this session, waiting for the end-of-M4 hardening: BUG-0302 b / c
(a memory audit; the margin is now 19 KB, the next feature re-baselines it), BUG-0275 (a remembered construction site is
drawn as a finished building), BUG-0271 / 0272 (two rare cases the builder recommends documenting as limits), BUG-0144,
BUG-0142, BUG-0113 item 2, BUG-0094 (test-only).

**How to change it.** The give-up count (10 switches) and the reveal time (2 s) are engine rules in code
(`CombatConstants`, `VisionConstants`), not data; say "make towers show for 4 s" or "give up after 5 switches" in the
inbox and the sim track moves it. The storm's radius and the Blinded numbers are data (`whirlwind/abilities.json`,
`common/statuses.json`).

### Shift-queued spells go to different mages, the Slowed marker is readable, test scenes no longer crash on exit, and felled trees stay on the map until you look again (view track, M4-VH2, 2026-10-10)

**What was built.** The view's clean-up session: three bugs closed whole and three in part, no new feature.

**What you will see in the game.**
- Select **two Cadre Mages while they are walking**, hold Shift, press Q and click two spots: each mage queues one Telas
  Fire and both land after their walk. Before, both clicks went to the same mage and the second spell was silently lost
  on cooldown while the other mage idled (BUG-0370). With both mages busy, a third Shift-click does nothing (no sound: the
  usual rule for a click that sends no order).
- The **Slowed marker** over a unit (you will see it on enemies in a Sandstorm) is now a deep saturated blue that reads on
  sand at normal zoom; it was a pale blue-grey that vanished (BUG-0371).
- A **spell flash** the fog hid when it happened never pops up later mid-fade when the fog lifts; a flash first drawn
  late (a slow frame) runs its whole half second instead of appearing half-faded (BUG-0371).
- A **tree the enemy fells out of your sight** stays drawn (darkened) on the map and on the minimap until one of your units
  sees that spot again; the fog shows what you last saw, as the design says (BUG-0281). A **minimap right-click** acts on
  the dot as it is drawn, so an enemy that moved a step since the last minimap refresh is still the one you clicked
  (BUG-0281). Props no longer re-list when a building goes up (BUG-0126).
- Under the hood: the automated test scenes used to crash about 6 % of the time on exit when the PC was busy, which made
  the test loop rerun them; every scene now shuts down cleanly (BUG-0251, 30 of 30 runs under load).

**Producer decisions (owner may revisit).** The Slowed blue (a strong royal blue) is a taste default until the M6 art pass;
corpses and rubble that appear out of your sight are still drawn in explored fog (BUG-0281 item 2) and wait for the M6
fog-look pass, where the whole "last seen" picture is handled at once.

**Rough edges.** BUG-0400 (S4): three small test gaps (the sim-safety test now covers the new helpers, thanks to QA;
one fix has no test because the situation cannot occur yet). Not reached: BUG-0250 item 2 (a documentation wording about
lobbed shots), BUG-0148 item 1 (a 1 % sliver in clicking a unit's edge), BUG-0126 items 5 / 6, the export clean-up notes.
BUG-0390 (see the sim entry) is a test scene rewrite at integration.

**How to change it.** The marker colours are code constants in `game/scripts/AbilityViews.cs` (`SlowColor` and friends)
until the art pass; say "make Slowed lighter" in the inbox. The last-seen tree rule is in `sim/Rts.Sim/ViewApi/SeenResources.cs`.

### Sandstorm and Blinded are pinned to their pages, the three wording tweaks are in the game, and Sandstorm's text now says "your own units" (data track, D10c, 2026-10-10)

**What was built.** The design-page check now reads Sandstorm's row (every column: range, radius, cast, cooldown, duration,
who it affects, that it blocks vision, which statuses it applies and how strong) and the Blinded row (sight 2 m, reach
3 m) and compares them with the data in both directions; a drifting number on either side fails a test by name. No
ability or status is on the allowance list any more. BUG-0380 is fixed: an extra damage claim on a page that the data
does not have now fails. The three wording tweaks shown to you last session shipped as proposed. The saved replay
fingerprint (`data-hash`) moved once for the text, with every checkpoint unchanged.

**What changed (every text, quoted).**

| Where | Field | Old → New | Why |
| --- | --- | --- | --- |
| `common/statuses.json`, Slowed | description | "Moves more slowly for a while." → "Moves more slowly until it wears off. Only the strongest slow counts." | states the stacking rule |
| `malazan/abilities.json`, Telas Fire | description | "...enemy units in the area burn for 10 magic damage a second for 4 seconds. Buildings are unharmed." → "...enemy units in the area are Burning, taking 10 magic damage a second for 4 seconds. Buildings are unharmed." | names the status the marker shows |
| `malazan/abilities.json`, Cusser | description | "...Your own units in the blast take half, the Sapper too." → "...Your own units caught in the blast, the Sapper included, take half; your buildings are safe." | says own buildings are never hurt |
| `whirlwind/abilities.json`, Sandstorm | description | "...enemies outside can't see into the storm. Whirlwind units are unaffected." → "...enemies outside can't see into the storm. Your own units are unaffected." | the rule is "enemy units": in a Whirlwind-vs-Whirlwind match the enemy's Whirlwind units *are* affected, so the old words were wrong there |
| `docs/factions/whirlwind.md`, Sandstorm row | Effect | "Non-Whirlwind units inside are Blinded and Slowed 30%. Enemies outside can't see into the storm. Whirlwind units are unaffected" → "Enemy units inside are Blinded and Slowed 30%. Enemies outside can't see into the storm. Your own units are unaffected. No effect on buildings" | same reason; the buildings clause is what the check reads |
| `common/statuses.json`, Blinded | description | unchanged: "Can barely see: sight drops to 2 m, and it can't target anything more than 3 m away." | see the proposal below. **Adopted in D11 (2026-10-10-1314): now "...it can't attack anything more than 3 m away."** |

No number changed anywhere.

**One proposal for you (Producer decision: adopted with the next data text batch unless you say no; shipped in D11, session 2026-10-10-1314).** Blinded's text
"can't *target* anything more than 3 m away" → "can't *attack* anything more than 3 m away". Reason: you can still order a
Blinded unit to attack something farther away (it walks closer); what the rule caps is the hit. The change waits for the
session after the Zealot passives land, because that sim task edits the same data file.

**Rough edges.** BUG-0410 (S4): the description check accepts a wrong number if it equals another of the same entry's
numbers in a different role (Sandstorm's "for 18 seconds" would pass because 18 is its range); fixed with the next data
task. A Perf timing row failed under the three tracks' load during QA and passed on a clean base the same way (contention,
no sim code changed).

**How to change it.** Edit the `description` in the data file (the game shows it as is) and tell the studio in the inbox
so the replay fingerprint is regenerated; edit the page row and the test names whichever side disagrees.

### Sandstorm is in the rules: the Whirlwind Priest calls a storm that blinds and slows the enemies inside and hides them from everyone outside (sim track, M4-4b-2, 2026-10-10)

**What was built.** Zones: a spell can now leave a timed circle on the ground that keeps working after the cast. The first
one is Sandstorm, the Whirlwind signature, read entirely from data: the Priest of the Whirlwind casts it up to 18 m away,
takes 1.2 s to do so, and leaves a 6 m storm for 12 s (45 s cooldown, 30 s once Dryjhna's Prophecy is researched). Every
enemy unit inside is **Blinded** (sees only 2 m around itself and cannot aim at anything more than 3 m away) and **Slowed**
by 30 %; both wear off one second after it steps out. Enemies outside the storm cannot see into it: their units inside it
vanish from the map for the player watching from outside, unless that player has a unit standing inside, which then sees
its own 2 m. The Whirlwind side sees into its own storm normally. Blinded is a general status (a new kind, "blind") so a
later spell like Darkness is data only.

**What you'll see.** Nothing new on screen yet: the storm itself gets its disc and the Blinded marker its colour in the
view's next feature task (M4-V6c). The rules already run: in a test match a Malazan Crossbowman 10 m from the storm's
centre loses sight of the Raiders inside and stops shooting them; a Malazan soldier who walks into the storm sees only the
ground around its feet and only hits enemies within arm's reach; a Priest standing in a storm can still cast at full range
(aiming a spell at the ground is not "targeting"). Because the ability buttons are data-driven, a selected Priest already
shows a "Sandstorm" button and casting it works; Blinded units show a pale marker (the generic colour) until M4-V6c.

**Producer decisions (owner may revisit).** (a) Sandstorm affects **enemy units** (the page says "non-Whirlwind units";
the game has no alliances yet, so the two mean the same today). (b) The storm's effects linger **1 s** after a unit leaves
(a data number per status, not code). (c) The vision rule above: outsiders see nothing inside, an enemy's own unit inside
sees its Blinded 2 m, the owner sees everything. (d) Blinded's 2 m / 3 m live in `statuses.json`, not in code. Builder
details accepted: a storm's effects are applied *after* the normal status countdown so the 1 s linger is exact; a unit
"inside" is one whose centre is within the circle; if 64 storms are already live (never in a real match), a new cast
resolves but leaves no storm; a Blinded unit still *acquires* out to 3 m even though its map sight is 2 m (combat and the
screen disagree in that 1 m band, as they already did on hills).

**Player-facing text added (data).** Sandstorm: "Calls up a sandstorm for 12 seconds: enemy units inside are Blinded and
Slowed by 30%, and enemies outside can't see into the storm. Whirlwind units are unaffected." Blinded: "Can barely see:
sight drops to 2 m, and it can't target anything more than 3 m away." _(Since D10c and D11: Sandstorm ends "Your own units
are unaffected." and Blinded reads "...it can't attack anything more than 3 m away.")_

**Rough edges.** BUG-0360 (S3): the storm hides *ground cells* by their centres while the statuses go by the *unit's*
centre, so in a band about 1.4 m wide at the rim a Raider that is Blinded can still be seen and shot from outside, and one
just outside can be hidden; fixed at the sim's clean-up next session (units will be hidden by their own centre). BUG-0361
(S3, tests and tools only): a replay recorded with a non-default storm capacity would not play back; a one-line guard next
session. BUG-0363 (S4): many far-away storms slow every unit's target search a little (+23 % with 16 storms live); for up
to 3 ticks after a storm appears an archer outside may still fire one more shot into it; a tower's shot already winding up
still lands. Known gap: the Catapult (minimum range 4 m) cannot fire at all while Blinded. Perf headroom is thin: an
empty 2,500-unit tick costs 473-486 µs of its 500 µs budget.

**How to change it.** All Sandstorm numbers are in `game/data/factions/whirlwind/abilities.json` (`range`, `radius`,
`castTime`, `cooldown`, `duration`, the two statuses' `duration` and the slow's `magnitude`); Blinded's 2 / 3 are `sight` /
`reach` in `game/data/common/statuses.json`. An inbox note like "storms should last 15 s" or "blinded units should see 3 m"
is enough; "the storm should also hurt" would be a new effect for the sim track.

### You can see who is burning or slowed and where a spell landed; the cast bar reads from its first moment, and Shift queues several casts (view track, M4-V6b, 2026-10-10)

**What was built.** Small coloured markers float above each unit for every status on it: flame-orange for Burning,
blue-grey for Slowed, pale for anything else (Blinded until it gets its own colour), side by side when a unit has two.
They appear and vanish exactly with the status and hide with the unit under the fog. Where a spell lands, a translucent
yellow disc flashes on the ground for half a second, growing from half the spell's radius to its full size, only if you can
see that ground. The Sapper's Cusser button appeared on its card with no new code (the card reads the data). The cast bar
is thicker, on a dim violet back instead of black, and its fill is never thinner than a nub, so you can tell a cast has
started. With Shift held, a spell click keeps the spell armed so you can queue several casts in a row (right-click or Esc
ends it). Two small memory allocations in the selection panel and the minimap are gone.

**What you'll see.** Open the test scene `game/tests/AbilityViewTest.tscn` (or a match), select a Cadre Mage, press Q and
click on a knot of enemies: a yellow disc flashes where the fire lands, and every burning Raider carries an orange marker
over its head for 4 seconds. Select a Sapper: its card shows "Cusser" on Q, and pressing it draws the 6 m reach ring and
the 3.5 m blast circle. Hold Shift, press Q and click twice on two spots: both casts queue (one per mage) and the ring stays
up until you right-click. With two statuses on one unit the markers sit side by side.

**Decisions made by the builder (Producer accepted; owner may revisit).** (a) Marker colours follow the status *kind*
from data, so a new status of a known kind needs no code; a new kind draws pale until given a colour. (b) The flash is
0.5 s, grows from half the radius over the first 40 % of its life, starts 75 % opaque and fades to nothing; no ring at the
cast's *start*. (c) A spell click that sent nothing (every mage on cooldown) still leaves targeting even with Shift held.
(d) No status names are shown yet (a tooltip would use the data's display names).

**Rough edges.** BUG-0370 (S3): if the selected mages are *walking* when you Shift-queue two casts, both go to the same
mage and the second is silently dropped when its turn comes (it is on cooldown); the fix is the first item of the view's
clean-up next session. BUG-0371 (S4): the Slowed marker is hard to see against sand at normal zoom; a flash hidden by the
fog can appear mid-fade if the fog lifts just then; a flash first drawn late jumps a little at low frame rates. The storm
disc and Blinded's marker colour come with M4-V6c.

**How to change it.** Colours and sizes are constants in `game/scripts/AbilityViews.cs` (`DamageOverTimeColor`,
`SlowColor`, `MarkerSize`, `FlashAlpha`, the bar's colours); an inbox note like "make the slowed marker dark blue" or
"flash for a full second" is enough.

### The Cusser's page row is now checked number by number, and three wording suggestions for spell and status descriptions wait for your yes or no (data track, D10b, 2026-10-10; **update: the three suggestions shipped in D10c, see the entry above**)

**What was built.** The test that pins each ability's design-page row to the data now understands the Cusser's kind of
sentence: "120 siege damage", "full damage to buildings" (or "No effect on buildings"), and "friendly fire at 50%" (or
"own units take half") are each read and compared to the data field they describe, and a mismatch names the Cusser and
the field. The Cusser leaves the allowance list, so a drifting number on either side now fails a test by name like Telas
Fire's. Sandstorm stays on the allowance until the data track pins it next session (D10c), so this session's landing
stayed green. A cross-track slip (BUG-0362: a test wording that broke only once Sandstorm had landed) was caught by the sim's
QA on a trial merge and fixed the same session.

**What changed (compact table).**

| Where | Field | Old → New | Why |
| --- | --- | --- | --- |
| `docs/factions/malazan.md`, Cusser row | Effect | "120 siege damage in the area, full damage to buildings (≈355 to a Town Hall), friendly fire at 50%" → "**Enemy units in the area take** 120 siege damage, full damage to buildings (≈355 to a Town Hall), friendly fire at 50%" | says who it hits, like the other rows; numbers unchanged (120 × 3.0 − 5 armour = 355 checked by hand) |
| `game/data/**` | — | nothing | no number or shipped text changed this session |

**Wording proposals (Producer decision: the data track adopts these next session, D10c, unless you say otherwise in the
inbox; the game's text does not change until then).**

| Entry | Shipped text | Proposed text | Why |
| --- | --- | --- | --- |
| Telas Fire | "Sets the ground ablaze: enemy units in the area burn for 10 magic damage a second for 4 seconds. Buildings are unharmed." | "Sets the ground ablaze: enemy units in the area are Burning, taking 10 magic damage a second for 4 seconds. Buildings are unharmed." | names the status the marker shows, so the word on screen matches the tooltip |
| Cusser | "Throws a Moranth munition: 120 siege damage to enemy units and buildings in the area. Your own units in the blast take half, the Sapper too." | "Throws a Moranth munition: 120 siege damage to enemy units and buildings in the area. Your own units caught in the blast, the Sapper included, take half; your buildings are safe." | says that your own buildings are never hurt (true in the rules, unsaid before) |
| Slowed | "Moves more slowly for a while." | "Moves more slowly until it wears off. Only the strongest slow counts." | states the stacking rule a player would otherwise guess at |
| Burning | "Takes magic damage every second until the flames die down." | no change | matches the rules |

A reply like "keep the old Cusser text" or "shorter: drop the stacking sentence" is enough; without one, D10c ships the
proposals. The Sandstorm and Blinded texts (quoted in the entry above) get the same review in D10c.

**Update 2026-10-10 (D10c, session 0624):** the three proposals shipped exactly as written above; Sandstorm's text was
also changed ("Your own units are unaffected") and Blinded's kept, with one further proposal for you: see the D10c entry.
A reply still works: the next data text batch applies it with one replay-fingerprint regeneration.

**Rough edges.** ~~BUG-0380 (S3): if a page row claimed an *extra* hit ("plus 30 magic damage") that the data does not have,
the check would let it pass as long as another damage claim matches; harmless today (one hit per ability), fixed in D10c.~~
→ fixed in D10c (session 2026-10-10-0624).

**How to change it.** Edit the page row or the data file; the test names whichever disagrees. Text requests go in the inbox.

### You can cast a spell from the window: select a Cadre Mage, press Q, click, and the nearest ready mage walks up and sets the ground ablaze (view track, M4-V6a, 2026-10-09)

**What was built.** The command card now shows a unit's abilities as buttons (Q, W, E, R from left to right), with a
tooltip that says what the spell does and its range, area and cooldown in metres and seconds. Press the button (or Q)
and the game enters targeting: a pale-blue ring shows the reach of the mage that would cast, an orange circle under
your cursor shows where the fire will land. Left-click sends exactly one cast, from the selected mage nearest the click
that is ready (not on cooldown). The mage walks into range if it has to, stands still with a violet cast bar filling over
its head, and the enemies in the circle start burning. Right-click or Esc cancels targeting. Shift + click queues the
cast after the mage's current orders.

**What you'll see.** Start a match, select a Cadre Mage (the Malazan caster; train one at the Mage Cadre once you're in
Age II, or use the test scene `game/tests/AbilityViewTest.tscn`). The card's top row has a "Telas Fire" button with a Q
in the corner. Press Q: the ring and circle appear. Click on a knot of enemies: the mage casts (about a second), the
"Casting" label shows in the panel, and the enemies burn for 4 seconds. The button goes dim and counts down "25 s"
until the mage can cast again. With two mages selected, the ready one casts; with both on cooldown nothing is sent and
the button shows the shortest wait. While a mage walks to cast, a small orange ring sits on the spot it's heading for.
The Sapper's new Cusser (next entry) gets its button the same way, since it's all read from the data.

**Decisions made by the builder (Producer accepted; owner may revisit).** (a) With a mixed selection, the card shows
the abilities of the unit type that is "active" (the one Tab cycles to), like its other buttons; Malazan mixes put the
Cadre Mage first, so Q works without Tab. (b) Clicking an enemy's body while targeting aims at that enemy's position
(no "homing"; the spell lands where it stood). (c) Only your own units show the walking-to-cast ring. (d) A targeting
click always leaves targeting mode, even when no mage could cast (nothing is sent). (e) The rings are flat painted
rings on the ground until the art pass (M6).

**Rough edges.** ~~BUG-0342 (S4)~~ fixed in M4-V6b (2026-10-10): the cast bar reads as a bar from its first moment (a
violet back with a bright nub) and Shift + click now stays armed for several casts; the walking-mages gap is BUG-0370 (see
the M4-V6b entry). ~~BUG-0340 (S4)~~ fixed in M4-V6b (the "+N" strings and the minimap lookup). Burning / Slowed markers
and the flash where a spell lands are in since M4-V6b.

**How to change it.** The hotkey letters come from `game/data/common/ui.json` (the card grid); the ring colours and
widths are constants in `game/scripts/AbilityViews.cs` (an inbox note like "make the area circle red" is enough). The
rules in (a)-(d) change with an inbox note.

### The Cusser is in the rules: the Sapper's satchel charge now blasts enemy buildings and units, and your own troops in the blast take half; Moranth Supply shortens ability cooldowns (sim track, M4-4b-1, 2026-10-09)

**What was built.** The second signature ability from the Malazan page works from data: the Sapper throws a Cusser 6 m
with a 3.5 m blast, 1 s to throw, 45 s cooldown, 120 siege damage with no falloff (everything inside the circle takes
the full hit, unlike a catapult's splash). It is the first ability that hurts buildings (a Town Hall at the edge of the
circle takes 355 of its hit points, as the page promised) and the first with friendly fire: your own units in the
blast, the Sapper included, take half. Your own buildings are never hurt. Two general rules came with it: the
`abilityCooldown` effect that Moranth Supply (and Dryjhna's Prophecy) already carried in the tech data now actually
shortens ability cooldowns (Cusser 45 s → 30 s), and a damage-over-time spell's duration must be whole seconds (a
fraction would deal nothing, since burning ticks once a second), checked when the data loads.

**What you'll see.** Nothing in the window until the view's next task adds the Cusser's button to the Sapper's card
(the button system is data-driven, so it should appear as soon as both land together; the view will verify). In the
rules: a Sapper told to cast on an enemy Town Hall walks to 6 m, stands for a second, and the hall loses 355 hp; a Light
unit in the circle loses 60 minus its armour; your own Heavy infantry standing next to the target loses 44; a Sapper that
throws at its own feet hurts itself for 30. A Sapper caught in its own blast dies if low enough, and it counts as your
loss and your kill at once. After researching Moranth Supply, the next Cusser is ready in 30 s instead of 45.

**Producer decisions (owner may revisit).** (a) No falloff on abilities: an area spell is exact to its radius (docs/01
row; splash from catapults keeps its falloff). (b) A tech finishing while a cooldown is already running doesn't shorten
that one, only the next (simplest; the player rarely notices). (c) Friendly fire is per damage effect and only allowed
on "enemy units" abilities; a "buildings" flag is refused on "own units" abilities (two load-time checks beyond the
brief; they catch nonsense data early). (d) Durations of damage-over-time spells must be whole seconds (from 0724).

**Player-facing text added (data).** Cusser, "Throws a Moranth munition: 120 siege damage to enemy units and buildings
in the area. Your own units in the blast take half, the Sapper too." ("Moranth" is a Malazan codename; the pre-release
rename pass is data-only.)

**Rough edges.** BUG-0330 (S3, older than this task): the per-tick death list is sized for units only, so a single tick
in which every unit slot dies *and* a building dies would crash; needs an extreme fight (every unit on the map dying in
the same tick) and goes to the sim's next hardening. docs/02's "Splash and friendly fire" still says "allied and own
units" though the game has no alliances yet (wording only).

**How to change it.** All the Cusser's numbers are in `game/data/factions/malazan/abilities.json` (`range`, `radius`,
`castTime`, `cooldown`, `amount`, `friendlyFire` 0-1, `buildings` true / false); the Moranth Supply bonus is in
`malazan/techs.json`. An inbox note like "friendly fire should be 100 %" is enough.

### The abilities' and statuses' names and numbers are pinned to their design pages, so a drifting number fails a test by name; nothing numeric changed (data track, D10a, 2026-10-09)

**What was built.** A test now reads the "Abilities" table on each faction page and checks every loaded ability against
the data, cell by cell: the unit it belongs to, kind, range, radius, cast time, cooldown, duration, and the numbers in
the effect text ("10 magic damage/s for 4 s", "Enemy units", "No effect on buildings"). The same for statuses: the names
Burning and Slowed must appear in the design doc's status table with the matching effect wording. Rows the game can't
load yet (Cusser's page row until this session, Sandstorm, and the Stealthed / Revealed / Frenzied / Regenerating /
Blinded statuses) sit on a named allowance list; when one lands, the test reports it so the list shrinks. The Malazan
page's Abilities table gained a "Duration" column ("—" for both rows) so both faction pages share one header.

**What changed in the data.** Nothing: no number, no text, no file under `game/data/`.

**Telas Fire's text as shipped (reviewed against the page, no change proposed):** "Telas Fire", "Sets the ground
ablaze: enemy units in the area burn for 10 magic damage a second for 4 seconds. Buildings are unharmed." Burning:
"Takes magic damage every second until the flames die down." Slowed: "Moves more slowly for a while."

**Rough edges.** ~~BUG-0350 (S3)~~ fixed in D10b (2026-10-10): the check reads the Cusser's phrasing ("120 siege damage",
"full damage to buildings", "friendly fire at 50%" or "own units take half"), so the Cusser's row is enforced like Telas
Fire's. ~~BUG-0351 (S4)~~ fixed in D10b (the review table came with the report; the message reads "page '—', data 6 s").
The Producer dropped **D9**
(separate Attack / Detector columns for the towers): the pages' Provides text already carries those numbers and they are
pinned both ways since D-H1; nothing you can't read today.

**How to change it.** Edit the page row or the data file; the test names whichever disagrees. Text or number requests go
in the inbox as before.

### The first spell is in the rules: a Cadre Mage can set the ground ablaze with Telas Fire, and statuses (Burning, Slowed) exist; hidden enemies no longer block your build ghost (sim track, M4-4a, 2026-10-09)

- **What happened:** the simulation can now cast abilities from data. The first one is the Malazan Cadre Mage's **Telas
  Fire** (the faction page's numbers: 16 m reach, a 3 m circle, 0.8 s to cast, 25 s before the next one): every enemy
  soldier in the circle is **Burning** and takes 10 magic damage a second for 4 seconds (40 on light troops; 52 on heavy or
  giant ones, because magic hits them harder per the damage table). Your own units and every building are unharmed. A unit
  ordered to cast from too far away walks until it is close enough, then stands still for the cast; any new order cancels
  the cast at no cost. There is also a **Slowed** status (a unit moves at a fraction of its speed) ready for later abilities.
  **Second fix:** hovering a building over a spot where an *unseen* enemy stands no longer says "Units in the way", so the
  fog no longer leaks their position (BUG-0280).
- **What you'll see:** **nothing yet in the window.** There is no ability button or targeting circle: that is the view's
  next task (M4-V6). Right now only the tests cast it. Once the button exists: select a Cadre Mage, press its hotkey, click
  near enemy infantry; the mage stands for under a second, then every enemy in the small circle burns for four seconds.
- **New data for your review (first entries of two new files; the data track fills them out in D10):**

  | Item | Field | Value | Why |
  | --- | --- | --- | --- |
  | Telas Fire (Malazan, `abilities.json`) | range / radius / cast / cooldown | 16 m / 3 m / 0.8 s / 25 s | docs/factions/malazan.md "Abilities" |
  | Telas Fire | effect | Burning, 10 magic damage a second for 4 s, enemy units only | same row ("no effect on buildings") |
  | Cadre Mage (`units.json`) | abilities | none → `telas_fire` | the first caster |
  | Burning (`statuses.json`) | kind | damage over time, magic | docs/02 "Abilities and status effects" |
  | Slowed (`statuses.json`) | kind | slow (speed x (1 - strength)) | same; no ability uses it yet |

  New player-facing text: **"Telas Fire"**: "Sets the ground ablaze: enemy units in the area burn for 10 magic damage a
  second for 4 seconds. Buildings are unharmed." **"Burning"**: "Takes magic damage every second until the flames die
  down." **"Slowed"**: "Moves more slowly for a while." The unit's state label while casting reads **"Casting"**.
- **Decisions I made (owner may revisit; docs/01 rows of 2026-10-09):** (a) burn damage lands in **pulses once a second**
  (the first a second after the cast lands), each pulse rounded like any hit, rather than a little every tick, so the
  numbers on the faction page stay whole; a Burning refreshed before a second is up keeps its rhythm (a fix round found it
  dealing nothing otherwise, BUG-0301); a burn shorter than a whole second deals nothing (no shipped data does that; the
  next slice refuses such data at load). (b) Casting the same status twice keeps the **longer** time left and the
  **stronger** strength; a unit holds at most 8 different statuses, a ninth is ignored. (c) A build order placed on a
  hidden enemy is **refused when you give it** (nothing paid), rather than when the worker arrives: simpler, and it only
  tells you something is there on a spot you chose to build on. (d) Status kinds are generic mechanics ("damage over
  time", "slow"), so new statuses are data, not code.
- **Rough edges:** no button yet (M4-V6, next view session); the other three signature abilities (Sharpers / Cusser,
  Sandstorm, the Zealot passives) wait for slice 2 (zones, passives) and the data track's content; the AI doesn't cast
  yet (M5). Nits: BUG-0302 (S4, test bookkeeping).
- **How to change it:** the numbers are in `game/data/factions/malazan/abilities.json` and the status kinds in
  `game/data/common/statuses.json`; write "make Telas Fire last 6 s" or "burn should tick every half second" in the inbox.

### Remembered enemy buildings stay on the map as dark ghosts, and you can right-click one to attack it (view track, M4-V5, 2026-10-09)

- **What happened:** the last piece of M4's fog criterion. An enemy building you have seen stays drawn as a **darkened
  ghost** (its shape in a dimmed team colour, no hit-point bar) after your units leave, for as long as nobody of yours looks
  at that ground again. If the enemy tears it down while you aren't looking, the ghost stays until you look: then it
  vanishes. **Right-click a ghost** (or A + click) and your selected units walk there and attack the building if it is
  still standing; if it is gone, the order ends when they see the empty ground. The placement ghost's red **"Unexplored"**
  label is now proven in a test scene, and the seed-21 scripted playtest recording (used by a test to guard an old
  wood-gathering bug) was re-recorded under the "build on explored ground" rule.
- **What you'll see:** scout the enemy base with one Horse Raider, pull it back: their hall, towers and barracks stay on
  screen as dark boxes on the dim explored ground, and on the minimap's fog layer. Right-click a dark box with your army
  selected: the red ring sits on it and the army marches there. Note: a remembered construction *site* is drawn as a
  finished building (BUG-0275 item 1; the rules don't remember "was a site" yet).
- **Decisions I made:** a ghost is the owner's team colour at 40 % brightness (the M6 look pass may change it); a ghost is
  picked before anything drawn behind it; no minimap ghost squares (optional, dropped for time).
- **Rough edges:** (1) a building destroyed **in plain sight** can flash as a ghost for up to 3 ticks (150 ms) because the
  rules' memory list updates every 4th tick (BUG-0310, S3, view: the next view hardening or M4-V6 if a few lines). (2) An
  attack order on a ghost whose building is gone ends about a metre before your units actually see its ground, so the ghost
  stays for another 20-25 s next to your idle army and a new right-click on it is refused (BUG-0311, S3, **sim**: planned
  with the next sim task that touches vision, M4-5 stealth, or the next sim hardening).
- **How to change it:** the ghost shade is a constant in `BuildingViews`; write "ghosts brighter" or "draw ghosts on the
  minimap" in the inbox.

### The data track's first clean-up: two old bugs closed and the towers' descriptions pinned to their numbers; nothing numeric changed (data track, D-H1, 2026-10-09)

- **What happened:** housekeeping in the content tests. (1) The balance-table check now compares the faction pages' columns
  by name, so a reordered column no longer hides a stale number (BUG-0290). (2) Every sentence in a tower's description that
  says it shoots or spots hidden enemies is now checked against the tower's actual `attack` / `detector` data, both ways: a
  tower that shoots must say so, and a building that says so must shoot (BUG-0090, open since the first building text in
  D1, now closed; the shipped sentences were already right, so no text changed). (3) The page-table readers the content
  tests share moved into one helper file; the printed tables are byte-identical before and after.
- **What you'll see:** nothing changes in the game. No number, name or description moved.
- **Decisions I made:** none beyond the brief.
- **Rough edges:** none filed. Next for the data track: D9 (an Attack / Detector column for the towers on both faction
  pages), then D10 (the abilities content against the new schema, after the view gives abilities a button).
- **How to change it:** your balance answer (the D6 entry) is still the one thing the data track waits for.

### You can see the fog of war now: the map starts black, clears where your units walk, dims behind them, and enemies vanish when nobody of yours is looking (view track, M4-V4, 2026-10-09)

- **What happened:** the fog that went into the rules last session is drawn. Run the game (`& $env:GODOT --path game`): the
  ground beyond your base's sight is black; where a unit walks it clears; when the unit leaves, that ground stays visible but
  darkened and greyer (you have "explored" it). Enemy soldiers, their hit-point bars, their arrows and the puffs where arrows
  land appear only where one of your units or buildings can see; so do enemy buildings (for now they vanish again when you
  look away; next session they stay as dim "remembered" outlines, see below). Trees, mines, bodies and rubble are drawn under
  the same fog: gone on black ground, dim on explored ground.
- **What you'll see and feel:** scout with one Horse Raider and watch the black roll back; walk away and the enemy camp blinks
  out (nobody is looking); the **minimap** has the same black / dim / clear layer under its dots, and enemy dots show only where
  you can see. **Right-click an enemy dot on the minimap** and your selected soldiers attack that enemy (before, that was only a
  move). Right-click a spot where an enemy is hidden and they just walk there. Clicking on the 3D map works the same: you can
  only target what is drawn.
- **Try it in two minutes:** start a match, select your army, right-click far into the black: the ground clears as they go. Then
  pull them back and watch the explored ground dim and the enemy disappear from screen and minimap.
- **Decisions I made (not in the design doc, the M6 art pass may change them):** explored ground is drawn at 40 % brightness and
  half greyed; the minimap's explored shade is 60 % black. A soft one-cell gradient at the fog's edge, no animation. Hit-point
  bars, shots and impact puffs follow the hiding rule exactly; an arrow fired by a hidden archer appears as it flies into
  your sight (the shot's own position decides, not the archer's).
- **Rough edges:** (1) **Hidden enemy soldiers still block your building placement** (BUG-0280): hovering a building over a
  spot with a hidden enemy says "Units in the way", which gives them away. The sim fixes it first thing next session. (2) A
  tree cut down by the enemy in your explored (dim) fog disappears at once, and a new body in dim fog appears although you
  couldn't have seen it (BUG-0281; the design says dim ground should show the *last seen* state; with the M6 look pass).
  (3) Minimap dots refresh five times a second, so for up to 150 ms a dot and the right-click rule can disagree (BUG-0281).
- **How to change it:** the shades are two constants (`FogView.ExploredBrightness` 0.4 and the minimap alpha); write "make
  explored ground brighter / darker" or "no desaturation" in the inbox. `-- --no-fog` on the command line draws the match
  without fog (a developer flag, not a game option).

### Watch towers shoot, your side remembers enemy buildings it has seen, and you can only build on explored ground (sim track, M4-3b, 2026-10-09)

- **What happened:** three rules from the design doc are in the simulation.
  1. **Towers that shoot.** A finished Watch Tower (Malazan) or Lookout Tower (Whirlwind) picks an enemy soldier it can see
     within 18 m and shoots a bolt / arrow every 2 s for 10 pierce damage (the design doc's numbers), aiming ahead of walkers
     like archers do. It prefers soldiers attacking it, then fighters, then workers, nearest first; it never shoots buildings; a
     tower still under construction doesn't shoot; the Forge's Ranged Weapons upgrade adds +1 / +2 to its bolts (the design doc
     says "pierce units and towers").
  2. **Remembered enemy buildings.** Each side keeps a list of enemy buildings it has seen (type, place, owner). It stays on the
     list while the ground is in your fog, even if the building is destroyed meanwhile; it is dropped the first time you look at
     that ground again and the building is gone. You may order an attack on a remembered building you can't see right now:
     your units walk there and attack when they see it; if it turns out to be gone, the order ends when they see the empty
     ground. (This is the data the view will draw as dim "ghost" buildings next session.)
  3. **Build only on explored ground.** Placing a building needs every cell of its footprint explored by you; the ghost turns red
     with **"Unexplored"** otherwise, and a build order there is refused free of charge.
- **New data for your review (the sim shipped the towers' numbers; the data track pins them next):**

  | Building | Field | Old → new | Why |
  | --- | --- | --- | --- |
  | Watch Tower (Malazan) | attack | none → 10 pierce, every 2 s, range 18 m, wind-up 0.4 s, projectile bolt, targets units only | docs/02 "Buildings" row; wind-up is the ranged default |
  | Lookout Tower (Whirlwind) | attack | none → 10 pierce, every 2 s, range 18 m, wind-up 0.4 s, projectile arrow, targets units only | same row; the Whirlwind projectile |
  | both towers | detector | none → 16 m | docs/02 "Stealth and detection"; stored now, used when stealth arrives (M4-5) |

  No name or description changed.
- **What you'll see:** build a Watch Tower (Age II, the advanced build menu) near your gold mine and send an enemy raider past
  it: a bolt leaves the tower's centre every two seconds and the raider's bar drops. Try to place a House on black ground: the
  ghost is red and says "Unexplored". The remembered buildings are drawn as dark ghosts since M4-V5 (session 0724).
- **Decisions I made (owner may revisit; docs/01 row of 2026-10-09):** (a) a tower's hit does **not** make the victim fight
  back by itself, and a tower shooting down from a cliff is **not** revealed to the victim's side (the "attacker from high
  ground is revealed for 2 s" rule is stored per unit, not per building; BUG-0270, S3, next sim hardening): soldiers take a tower
  only when their own scan finds it within their sight, so a cliff-top tower can kill unanswered. Trade-off: cheaper and
  simpler now; the alternative is a per-building reveal, about 2 KB and a few lines. (b) Ranged Weapons reaches towers because
  the design doc says so. (c) One remembered entry per enemy building slot: in the rare case a destroyed building's slot is
  reused for a new building you then see, the old memory is replaced although you never looked at its ground (BUG-0272, S4).
- **Rough edges:** the "Unexplored" rule broke seven of our own test scenes and one recorded replay that used to build far from
  their workers (all fixed or re-staged this session; the replay is re-recorded next session, BUG-0273). A remembered
  construction site will be drawn as a finished building until the sim adds a "was a site" flag (BUG-0275, S4).
- **How to change it:** the towers' numbers are in `game/data/factions/*/buildings.json` (`attack`, `detector`); for the rules
  (a)-(c) write it in the inbox ("towers should be revealed when they shoot from high ground").

### The balance tables are now pinned cell by cell to the shared fight harness, and one stale time is fixed (data track, D8, 2026-10-09)

- **What happened:** the data track's balance report test no longer carries its own copy of the fight scene; it calls the sim's
  shared harness, so any rule change that moves a fight's numbers now fails a test that names the page, the pair, the seat and
  the column. Both faction pages' "Balance baseline" tables were re-printed from it.
- **What changed on the pages (no game data changed):**

  | Page | Row | Field | Old → new | Why |
  | --- | --- | --- | --- | --- |
  | malazan.md | Wickan Lancer v Desert Archer, seat 1 | Time to last death | 21.5 s → 22.0 s | the chase rule fixed last session (BUG-0149) changes this one fight by 11 ticks; same winner, survivors and cost |
  | both pages | every group row | **new column "Winner hp left"** | (none) → the surviving winners' summed hit points | the brief asked to pin hit points and the pages had no such column |

  The siege tables are byte-identical. No name or description changed; nothing numeric in the game changed.
- **Also:** the test that checks the design doc's Age II sentence now reads the bullet from the file's own lines, so a
  contradiction written after a dash on the same line is caught too (BUG-0260 fixed; nine wrong wordings all fail).
- **Rough edge:** when a table's header differs, the test reports only the header and not the stale cells beneath it (BUG-0290,
  S4; the next data session, a hardening one, fixes it).
- **Still wanted from you:** the balance answer (the D6 entry below). The data track's next session is its first clean-up
  (D-H1), then D9 pins the towers' new numbers to the faction pages.

### Fog of war is on `main`, and the sim's clean-up closed nine old bugs: the fog is part of the saved fingerprint, a chaser no longer gives up a reachable enemy, broken data can't ship, and the fight harness is shared (sim track, M4-H1, 2026-10-08/09)

- **What happened:** the sim's clean-up session, on top of the fog. **The fog now lands on `main`** with it (the view fixed its
  one old test row first thing, BUG-0219). The clean-up itself:
  - *The fog's "sees now" map is part of the game's fingerprint* (BUG-0215, the decision I announced in the entry below): a saved
    game (M6) will store it, and two games with the same fingerprint now provably play the same future. Cost: about a twentieth
    of a millisecond per tick on the biggest map.
  - *A unit that gave up chasing something it can't reach now fights a reachable enemy it spots next* (BUG-0149). Before, a
    soldier stalled under a cliff-top archer that then saw a raider behind a short wall gave the raider up too and stood idle in
    sight of it. Now a switch to a **new** target starts a fresh chase; only a switch **back** to the one it just left keeps the
    "I'm not getting anywhere" count (so two targets taking turns can't keep it running forever).
  - *Bad faction data is refused at load* when something can never be had (BUG-0134): a building that needs an upgrade researched
    only inside itself, a unit trained at a building nobody can build. The error names the faction and everything it locks.
  - *Wrong-type values in data files say what the field wants* ("expected a whole number", "a list", ...) instead of a programmer's
    type name (BUG-0113).
  - *Hundreds of units pushed out of a building site onto a tiny plateau never share a spot* (BUG-0133).
  - The seed-21 test replay from your M3 playtest is re-recorded on the new fingerprint and checked tick for tick (11,541
    ticks, BUG-0211); a test that misread the PC slowing down as a cost regression now uses medians (BUG-0158); four fog nits
    (BUG-0216); and the **counter-triangle fight scene is one shared harness** (`CounterTriangleScene`) the data track's balance
    report will use next, so the two can never drift apart again (BUG-0230).
- **What you'll notice in the game:** only the chase change: a soldier that failed to reach one enemy walks round a wall to fight
  the next one instead of standing there. Everything else is under the hood.
- **Producer decisions, revisit any time:**
  - *The chase rule above* (a new target = a fresh chase; back to the previous one = the count continues). Alternative: reset the
    count whenever the new target is nearer than the old best gap.
  - *Fast A-click spam to new points (BUG-0157) stays as it is for now.* The builder built and measured the fix ("an attack-move to
    a new point keeps a chase whose target is in sight"): better on average at every army size (about 94 % of a single order's
    damage against 86-92 %), but single clicks still swing 10 points either way, so I deferred it to the next sim clean-up with a
    fairer bound (the average over click rates). Same-point spam is lossless and clicks 200 ms apart lose at most 4 % today.
  - *One counter-triangle fight changed by half a second* because of the chase rule (Wickan Lancers v Desert Archers, seat 1: 22.0 s
    instead of 21.5 s, the same winner and survivors). The Malazan page still shows the old time; the data track updates it
    next (BUG-0243) and pins every number so the next drift fails a test.
  - *Size: ~230 rule lines, ~900 builder test lines, ~470 QA test lines, ~90 doc lines; no fix round.*
- **Rough edges:** **BUG-0241 (S3, next sim clean-up):** a soldier whose scans keep taking two or three *unreachable* cliff-top
  enemies in turn can shuttle along the foot of the cliff indefinitely (it counts walking toward each new one as progress); it
  was there before this session too, and needs enemies on a cliff that keep changing what they target. **BUG-0157** above.
  **BUG-0242 (S4):** one data error message says "a number" where it should say "an object".
- **To change it:** the chase rules are engine constants (`CombatConstants`) and the switch rule is in `CombatSystem.Engage`:
  by inbox note; the fight harness's scene (army size, spacing, map) is `Scenario/CounterTriangleScene.cs`.

### The view's clean-up: a sound test no longer fails when the PC is busy, corpse discs beside ramps sit on their own ground, and pressing Cancel twice sends one cancel (view track, M4-VH1, 2026-10-08/09)

- **What happened:** the view's clean-up session, after fixing the one old test row that blocked the fog (BUG-0219, three lines:
  the test's raiders are now in sight before they are attacked). Then: the **sound test** that failed whenever another program
  used the CPU now waits on the real clock, with new rows that run the game at 8x to prove the old wait was wrong (BUG-0220);
  **corpse discs** on low ground right beside a ramp's side wall no longer float up to half a metre (at most ~0.2 m now, BUG-0226);
  a thrown stone drawn by a test that only looks every few ticks can no longer borrow another stone's start point (BUG-0222);
  **every test that orders an attack now stages the target in sight** (a sweep of all view tests, so the fog can't silently drop
  their orders); the M3 playtest script now checks the rallied laborer chops the *rally* forest, not any tree (BUG-0148); a
  **second press of Cancel** on a building site before the next tick sends nothing (BUG-0126); and the "Build and export" notes
  now say the export preset and script don't exist yet (they come with M6).
- **What you'll notice in the game:** a corpse disc beside a ramp's wall sits on the ground instead of hovering; pressing Cancel
  twice fast on a site cancels once (before, the second press was harmlessly ignored by the rules anyway). Nothing else visible.
- **Producer decisions, revisit any time:**
  - *Two minimap timing tests take the best of three batches* instead of one, with the same limits: a real slowdown is slow in every
    batch, a busy PC slows one. Same spirit as the studio's "a speed test counts only when run alone" rule.
  - *A tiny term in the corpse-disc rule is borrowed from the QA test that measures it* (BUG-0250): harmless (under a centimetre),
    to be replaced by a plain constant at the next view clean-up.
  - *Size: ~85 code lines, ~190 scene-test lines, ~330 QA test lines, ~30 doc lines; no fix round.*
- **Rough edges:** **BUG-0251 (S3, next view clean-up):** one test scene (`EconomyViewTest`) sometimes crashes *while Godot shuts
  down, after printing PASS*, when the PC is busy (about 1 run in 15; never when quiet; not caused by this session's change as far
  as QA can tell). Nothing in the game; the merge check reruns it once. **BUG-0250 (S4):** the term above; a test-only edge in the
  stone drawing; a guard that would outlive a "restart match" button that doesn't exist yet.
- **To change it:** nothing player-facing; by inbox note.

### Every building's sight is on both faction pages and pinned to the data (data track, D7, 2026-10-08/09)

- **What happened:** both faction pages' Buildings tables gained a **Sight** column: the Watchtower / Lookout Tower **24 m**, every
  other building **12 m** (the fog's default for a building with no sight of its own). Tests pin it both ways: change a page cell
  or a data value alone and a test names the building and the field. The design doc's vision paragraph now states the 12 m default
  and the towers' 24 m. Also closed from the D6 report: a sentence contradicting the Age II rule appended to the design doc's
  bullet now fails the test (BUG-0230 item 1), and the Whirlwind page's siege row sits in its own table like the Malazan page's
  (item 3). On the way the data track fixed the one test row that was red on the fog-era tree (BUG-0240: its copy of the siege
  scene had no spotter, so the fog dropped every attack; one line).
- **Change table: nothing numeric changed; no name or description changed; no data file changed; the replay fingerprint did not
  move.** The only new text is in the design docs.
- **Rough edges (next data task, D8):** the one stale time on the Malazan page (BUG-0243, see the sim entry above); the balance
  report's fight scene is still a copy of the sim's (D8 switches it to the shared harness and pins every printed number);
  **BUG-0260 (S4):** a contradiction written on the *same line* after a " - " still passes the Age II wording test (contrived).
- **To change it:** `sight` per building in `game/data/factions/<faction>/buildings.json` (any building may carry one), the 12 m
  default `buildingSight` in `game/data/common/rules.json`; the pages follow (the tests say which side disagrees).

### Fog of war is in the rules: each side sees only what its own units and buildings see, high ground sees down but low ground can't see up, and a shot from a hill briefly gives the shooter away (sim track, M4-3a, 2026-10-08)

- **Status update (integration, same evening): held on its branch one session.** I accepted this work, but when the conductor
  merged the view's new work into it and ran the whole test suite, one old view test (written before fog existed) ordered a
  soldier to attack three raiders that nobody on its side could see, and the fog now refuses that order, as designed
  (BUG-0219). It is a three-line change to that test, the view's first item next session; then the fog lands on `main`.
  Nothing in the fog itself is wrong. Lesson kept: the merge check now runs the whole test suite, not only the scene tests.
  **Update 2026-10-09 (session 2144): landed on `main`** with the sim's clean-up (the M4-H1 entry near the top); BUG-0219 fixed by
  the view; BUG-0215 / 0211 / 0216 below all fixed.
- **What was built:** the first half of fog of war, in the rules only (nothing is drawn yet: the darkened map is the view's
  next feature after its clean-up). Every player has a map of what it has **never seen** (black later), has **seen before**
  (darkened later) and **sees now**, refreshed five times a second from every one of its units and buildings (a building
  under construction looks around too). Each unit sees a circle of its sight range (10-18 m by type); a building sees **12 m**
  and the **Watch Tower / Lookout Tower 24 m**. **High ground:** a unit sees everything on its own level and below, but
  nothing on a higher plateau unless it is within 4 m of it (you see the lip of the cliff you stand under); a unit on a ramp
  counts as standing on the lower level. **Fighting uses it:** a unit only picks, chases or fires back at enemies its side can
  see, and an attack order on something nobody on your side can see is dropped; an attacker hitting from higher ground is
  **revealed to the victim's side for 2 seconds** after every hit, so the low side can shoot back or run. Everything the view
  will need (the per-player map, a change counter, "can this player see that unit") is ready and read-only.
- **What you'll see in the game:** nothing new on screen yet (the map still draws everything until the fog shader lands), but
  the behaviour has changed: units at the foot of a plateau ignore archers on top until those archers shoot, then they have
  two seconds to fire back; a lone unit sent to attack something far off that none of your units or buildings can see just
  stays put (the order is dropped); scouting matters once the shader hides what you can't see.
- **Producer decisions, revisit any time:**
  - *A building with no sight of its own sees 12 m* (the design doc only gives the towers' 24 m). The number is
    `buildingSight` in `game/data/common/rules.json`.
  - *A building under construction sees too* (so you can watch your site being attacked). Alternative: only finished buildings.
  - *The reveal is keyed on where the attacker fired from* (a catapult that shoots from a hill and walks down is still revealed
    where it is). Alternative: where it stands when the hit lands.
  - *Every player's fog refreshes on the same tick, every 4 ticks* (builder's choice; it could be spread across ticks later).
  - *One-level fights play exactly as before:* a unit always counts what its own sight circle covers right now, besides its
    side's map, so nothing changed on flat ground (the counter-triangle results are byte-identical).
  - *Changed after QA (BUG-0215):* I had asked for the "sees now" map to be rebuilt from scratch rather than saved; QA proved
    that between two refreshes it depends on where units *were*, which a save file wouldn't know. The sim's clean-up session
    next makes it part of the saved state (a few bytes per 64 map cells). Alternative: only allow saving right after a refresh.
  - *Size: ~760 rule lines (budget ~800-1,000 for a new system), ~1,450 dev test lines, ~650 QA test lines, ~115 doc lines; one
    fix round (a memory bound re-baselined with the fog's 2.4 MB written down; the targeting check reordered so big fights on
    hills cost less than before the fog).*
- **Rough edges:** **BUG-0215 (S3, above; fixed next session).** **BUG-0211 (S3):** one test replay recorded before the fog can
  no longer prove its first 19 ticks (the fog is now part of the fingerprint); it is re-recorded next session. **BUG-0216
  (S4):** a 64 m sight on a tiny test map misses a few cells (nothing shipped comes near 64 m); a Catapult told to hold position
  no longer shoots things between 18 m (its sight) and 26 m (its reach) unless something else of yours sees them (the rule
  working as designed; the docs said otherwise and will be corrected); once the shader exists, a unit may start shooting a
  target the screen still hides for up to a fifth of a second (the screen refreshes every 4 ticks, combat looks every tick).
- **To change it:** `buildingSight` in `game/data/common/rules.json`; `sight` per unit and per building in
  `game/data/factions/<faction>/units.json` / `buildings.json` (any building may carry one); the 4 m lip, the 2 s reveal and
  the 4-tick refresh are rule constants (`VisionConstants`), by inbox note.

### You can see the arrows, bolts and catapult stones now, and the right-click attack from last session is on `main` (view track, M4-V3 with M4-V2 landed, 2026-10-08)

- **What was built:** the shots the sim has been flying since M4-2b are drawn: an aimed shot (bolt, arrow, magic bolt) is a
  short **light streak** in a pale shade of its owner's colour flying at chest height along its path; a **catapult stone or
  sharper** is a small dark **stone on an arc** (its peak a quarter of the throw's length, between 1 and 6 m). Where a shot
  lands: a **yellow flash** on a hit (0.2 s), a **dust puff** on a miss (0.3 s), an **orange burst** for a stone or sharper
  (0.4 s, sized by its splash). Also fixed: `main`'s one red test scene (BUG-0210: the view QA proved the "missing" walker
  had been shot dead by arrows, so the pre-combat scene now runs with combat off like the five before it), and the corpse
  discs that were half-buried on ramps now sit on the highest ground under their rim (BUG-0190). And the accepted
  **right-click attack** from the M4-V2 entry below is on `main` with this.
- **Try it (two minutes):** `& $env:GODOT --path game`, box-select the grey army, press **A** and click the orange army. Pale
  streaks fly from the crossbowmen at the back, stones arc from the catapults and burst orange where they land, yellow
  flashes pop on units being hit. Right-click one orange soldier: the red ring and "Pursuing" from M4-V2.
- **Producer decisions, revisit any time:**
  - *Placeholder looks until the M6 art pass:* streak 1.2 m long at 1.2 m up, stone radius 0.28 m, mark colours and the arc
    curve are constants in `game/scripts/ProjectileViews.cs` and `ProjectileTracker` / `ImpactMarks` (in `sim/Rts.Sim/ViewApi/`).
  - *Marks live in game time* (8x speed shortens them) and at most 512 are kept, the oldest replaced.
  - *A stone that lands "slides" its last 0.6 m along the ground* and vanishes just short of its burst (BUG-0222, S4): barely
    visible at normal zoom; a note for the M6 projectile pass.
  - *The studio's attack-order test scenes now stage their targets within sight* (a consequence of the fog rule, not a visual
    change: an attack on an unseen enemy is dropped).
  - *Size: ~305 game-code lines, ~457 view-helper lines, ~1,100 dev test lines, ~1,070 QA test lines, ~105 doc lines; two fix
    rounds (a mark could be drawn fully transparent at 8x on a slow frame; corpse discs floated 4 m beside a cliff, then
    2 m beside a ramp's side wall: both fixed).*
- **Rough edges (next view clean-up):** **BUG-0226 (S4):** a corpse disc on low ground right beside a ramp's side wall, near
  the ramp's foot, can still float up to half a metre; two of the attack-order test scene's rows check a little less than
  their text says. **BUG-0222 (S4):** the slide above; two technical edges not reachable in play. **BUG-0220 (S3):** an old
  sound test fails when the PC is busy with something else (it passes 8 / 8 when quiet); nothing in the game. **Update
  2026-10-09: all three fixed in M4-VH1 (the entry near the top); the slide stays an M6 note.**
- **To change it:** the constants above; the `--no-combat` scene list is in docs/03; the rest by inbox note.

### The first balance report: how wide each counter wins, a proposed target for you to decide, and the Sapper's self-splash measured (data track, D6, 2026-10-08)

- **What happened:** the data track measured the eight counter-triangle fights from the sim's tests (equal cost, both seats,
  flat ground, no upgrades or abilities) and wrote the table into both faction pages ("Balance baseline"). **How much of its
  cost the winner keeps:** Heavy Infantry v Horse Raider **88-94 %**, Raider v Wickan Lancer **95-100 %** (Line beats Shock);
  Wickan Lancer v Desert Archer 70-80 %, Horse Raider v Crossbowman 69-77 % (Shock beats Ranged); Crossbowman v Priest
  67-87 %, Desert Archer v Cadre Mage 72-89 % (Ranged beats casters); a Catapult kills a Tent in 9 % of the time the same cost
  of Heavy Infantry needs, a Battering Ram a Billet in 5 %. **The Sapper's self-splash (BUG-0182 item 2):** 4 Sappers behind
  4 Heavy Infantry against 8 Horse Raiders throw 15 sharpers, wound their own side for 41 hit points in total (9 of them the
  thrower itself), kill nobody of their own, and the riders are wiped out while all four Sappers die to them anyway. A 2 m
  minimum range makes it worse (61 points); a 1 m splash removes it but the fight goes worse (fewer survivors). Four Sappers
  alone lose to the riders in every variant.
- **Your call (non-blocking; nothing changes until you answer):**
  1. **The target band.** The data track proposes: *the winner keeps 40-65 % of its cost, in both seats, seats within 15
     points.* Below ~40 % one upgrade level can flip the fight; above ~65 % the loser trades away almost nothing, so the counter
     is decided at build time instead of in the fight. **Every pair is above the band today**; Line v Shock (88-100 %) is the
     outlier. Casters fight without their abilities until M4-4, so those rows should be re-measured then before tuning.
  2. **Line v Shock.** If you want it narrowed, say roughly how much (for example "aim for 60 %"); the data track then proposes
     exact numbers (a Horse Raider or Wickan Lancer hp / attack nudge, or a Heavy Infantry cost nudge) in a table here before
     shipping. Or "leave balance until the fog-on sandbox" and it waits.
  3. **The Sapper.** Recommendation: **keep it** (the self-splash is small next to what the riders do; "Fragile; wants an
     escort" is the intended trade-off). Alternatives: a 2 m minimum range or a 1 m splash.
- **Change table: nothing numeric changed; no name or description changed; no data file changed; the replay fingerprint did
  not move.** Also fixed: BUG-0200 (the design doc's Age II clause is now pinned by the dev's own test; a misleading test
  message reworded).
- **Rough edges (BUG-0230, S4, next data task):** a contradicting sentence appended after the Age II clause would still pass
  the test; the balance table's fight harness is a copy of the sim test's (the sim makes it shared next session); the
  Whirlwind page's siege row uses the wrong column. **Update 2026-10-09: the first and third fixed in D7, the sim's shared
  harness exists (M4-H1); D8 switches the report to it and corrects one time that moved (BUG-0243, the M4-H1 entry).**
- **To change it:** an inbox line ("balance band 40-65 ok, narrow Line v Shock to about 60 %", "Sapper: keep"), then the data
  track's next task is the tweak with a table here; the measured scene is `Scenario/CounterTriangleTests`.

### Process note: the 3-hour session lock declared a live session dead, again (studio, 2026-10-08)

- The 14:35 session's builds and QA ran long under three tracks' load (the sim build alone took 1 h 52 min), so at 18:14 its
  lock was over three hours old and the next session took over, as the routine says. The old session noticed, stopped
  cleanly and left a note; nothing was lost (this session resumed every track from the committed work and finished it). It is
  the second time (the first was 2026-10-07-2014).
- **Suggestion for your routine file (not mine to change):** have a session refresh the lock's time at each step, or widen the
  window to about 5 hours; and have a resume check for running studio processes before taking over. The old conductor's note
  is an uncommitted file in your checkout (`studio/sessions/2026-10-08-1435-incident.md`); its content is in this session's
  log, so you can delete it.

### Arrows, bolts and catapult stones fly now: every unit of both factions fights, shots can miss, splash hurts your own side, and the counter triangle is proven in tests (sim track, M4-2b, 2026-10-08)

- **What was built:** the second half of combat. A ranged unit (Crossbowman, Desert Archer, Cadre Mage, Priest) or a
  siege unit (Catapult, Sapper) now **fires a projectile** at the end of its wind-up instead of hitting at once: a bolt
  flies at 25 m/s, a catapult stone or a Sapper's sharper at 12 m/s, and the damage lands where and when the shot
  arrives. **Aimed shots can miss:** a bolt hits only if its target is still within about 0.3 m of where the shot was
  going; a galloping Horse Raider crossing the line of fire dodges every shot from 6 m out. **Lobs always explode** where
  they were aimed, and **splash** hurts everything in the radius: full damage inside the inner 40 %, falling to half at
  the edge; a Catapult's stone or a Sapper's sharper also hurts **your own units** at half damage (never your own
  buildings). The Catapult **can't fire at anything closer than 6 m**: told to attack a unit that close, it waits; on
  its own it picks a farther target instead. With this, **all 14 shipped units fight**; before, only melee units did.
  Eight **counter-triangle scenario tests** now pin the design doc's rock-paper-scissors with equal-cost groups: Heavy
  Infantry beat Horse Raiders, Raiders beat Wickan Lancers, Lancers beat Desert Archers, Horse Raiders beat Crossbowmen,
  Crossbowmen beat Priests, Desert Archers beat Cadre Mages, a Catapult kills a Tent 11x faster than the same cost of
  Heavy Infantry, a Battering Ram a Billet 18x faster than Raiders. Also fixed: a unit hammering a building now turns on
  the enemy soldier killing it (BUG-0156, the rough edge from last session).
- **What you'll see:** `& $env:GODOT --path game`, box-select the grey army, press **A**, click the orange army. The
  crossbowmen at the back now stop short and start killing from 15 m while the infantry closes; orange units fall before
  any grey unit reaches them. **The shots themselves are invisible until the next view task (M4-V3)**: you see hp bars
  shrink and white flashes on units nobody is touching. The F12 overlay's K / L counts climb from both sides.
- **The one decision to review: shots at walking foot units are "led".** The design doc said a shot flies to where the
  target *was* when fired and hits if the target is still within 0.3 m of that point, and that "slow units almost never
  dodge". With those numbers as written, a walking Heavy Infantry dodged **every** shot from more than 5 m (0 of 100 at
  8 m): in 15 m of flight it walks 1.8 m, six times the tolerance. No projectile speed or tolerance separates a walking
  infantryman at 15 m from a galloping Horse Raider at 5 m, so some rule had to depend on the target's speed. The rule
  now: a shot at a unit moving **no faster than 5 m/s** (every foot unit; cavalry is 6.2-6.6) is aimed at where the
  unit *will* be when the shot lands and kept on it in flight, so walkers are hit 100 % at every range (slow units
  "never" dodge by walking rather than "almost never"); cavalry is shot where it is and dodges as the doc says (a
  crossing Horse Raider: 33 % hit at 3 m, 0 % from 6 m). The 5 m/s is a number in `game/data/common/projectiles.json`
  (`leadSpeed`). *Alternatives:* a tolerance that grows with flight time (walkers would still dodge sometimes), or no
  lead at all and accept that archers can't hit anything that moves (a very different, dodge-heavy game). Say the word
  and the data track changes the number or the sim drops the rule.
- **Producer decisions, revisit any time:**
  - *Projectiles fly straight at constant speed*; the view will draw any arc.
  - *A miss lands harmlessly* (no ground splash from a missed bolt). A **lob** (stone, sharper) always explodes.
  - *A Catapult told to attack a unit inside its 6 m minimum range waits where it stands* (no stepping back; kiting is a
    later slice). On its own it never picks such a target and ignores one hitting it from inside 6 m.
  - *Splash hits enemy buildings* (as structure damage), *never your own buildings*; *your own units only from attacks
    flagged friendly fire* (Catapult, Sapper), the shooter itself included; a friendly-fire kill counts as both a kill and
    a loss for you.
  - *The hit tolerance (0.3 m) is per projectile type in data*, so bolts and arrows can differ later.
  - *The Battering Ram stays a melee siege unit* (no projectile).
  - *Size: ~800 rule lines (budget ~800 for a new system), ~1,600 dev test lines, ~1,500 QA test lines, ~175 doc lines;
    one fix round (the lead rule and three smaller bugs).*
- **Rough edges:** **BUG-0182 item 2 (S4, a data note for the next data task):** a Sapper fighting a melee unit at arm's
  length throws its sharper at its own feet and hurts itself with every shot; in test brawls heavy with Sappers and
  Catapults, 23-28 % of all deaths were friendly fire. The Malazan page calls the Sapper "fragile; wants an escort", so
  this may be the intended trade-off; the data track reports the numbers next and you decide (a minimum range, a smaller
  splash, or leave it). **BUG-0184 (S4):** a unit walking at exactly 5.0 m/s is led on 93 % of its steps (float rounding;
  no shipped unit walks at exactly 5), and a re-aimed bolt can jump 1.8 m in one tick instead of 1.25 (a note for the
  view's projectile drawing). **A balance note:** Line beats Shock by a very wide margin (14-15 of 16 Heavy Infantry
  survive a same-cost Horse Raider charge); the data track's balance report is next. **Update 1814: BUG-0184's rounding edge
  is fixed (M4-3a); the Sapper numbers and the margins are in the D6 entry near the top.**
- **To change it:** speeds, the hit tolerance and the lead speed in `game/data/common/projectiles.json`; each unit's
  splash radius, friendly-fire flag, minimum range and projectile in `game/data/factions/<faction>/units.json`; the splash
  falloff (40 % / 50 %) and the friendly-fire 50 % are rule constants (`CombatConstants`), the rest by inbox note.

### Right-click an enemy to attack it: the attack order is in the window, with a red ring and a "Pursuing" label (view track, M4-V2, 2026-10-08)

- **Status update (integration, same morning): held one session.** I accepted this work, but when the conductor merged
  the sim's projectiles onto `main`, one of the studio's older test scenes (2,000 units, written before combat) went red
  on `main` itself, with no view code involved: now that archers shoot, one of 40 walkers is gone 3 s after a minimap
  order (BUG-0210). The branch is kept; the fix is the same one-line "combat off" switch five other old scenes got in
  M4-V1, and it lands first thing in the view's next session, after which this entry's "Try it" works from `main`
  (today it works from the branch `studio/2026-10-08-0913-view`). **Update 1814: landed on `main` with M4-V3 (the entry near
  the top); the "Try it" below works from `main`; BUG-0190 (both rough edges) fixed.**
- **What was built:** the window half of the attack order from last session. With units selected, a **right-click on an
  enemy unit or building** sends every selected unit to attack *that* target (they chase it anywhere and ignore everything
  else until it dies); **A then a click on an enemy** does the same, A then a click on the ground is still attack-move;
  **Shift** queues attack orders behind other orders. A **red ring** flashes round the clicked target for half a second.
  The selection panel reads **"Pursuing"** while a unit chases its ordered target and "Attacking" once it swings. If one
  of your own units or a tree stands in front of the enemy under the cursor, the click means what it did before (a move
  or a gather): what you see is what you click. Workers obey the order too. The minimap's right-click stays a plain move
  until fog of war makes its dots trustworthy (M4-3). The F12 overlay shows the selected unit's target.
- **Also fixed (BUG-0160):** the F12 overlay's text no longer runs under the K / L counter (four short lines now); a unit
  that is hit before its first frame on screen now flashes once; **corpse discs are readable by team** (the owner's colour
  on a darker rim: grey-blue Malazan, gold Whirlwind); two developer-overlay labels no longer fall back to C# text.
- **Try it (two minutes):** `& $env:GODOT --path game`, box-select a few grey soldiers, **right-click an orange soldier**:
  a red ring, the panel says "Pursuing", they walk over and kill that one, then stand idle. Press **A** and click the
  orange Town Hall: they attack the building. Hold **Shift** and right-click two different enemies: they take the second
  after the first dies. Right-click the ground: a plain walk as before.
- **Producer decisions, revisit any time:**
  - *The ring is a plain red torus for 0.5 s, no cursor change* (cursor art comes with the M6 art pass). Alternative: a
    persistent marker on the target while the order lasts; one inbox line.
  - *"Pursuing" is the label's word* (builder's pick, in `ui.json`). Alternatives: "Chasing", "Hunting", "Attacking".
  - *A right-click on an enemy with a building selected still sets the building's rally point* (the M3 rule).
  - *The minimap's Attack half waits for fog* (M4-3). Alternative: attack on an enemy dot now.
  - *Size: ~300 game-code lines, ~220 view-helper lines, ~1,290 dev test lines, ~1,000 QA test lines, ~100 doc lines; one
    new QA tool (`tools/qa/scene-loop.ps1`, runs every headless test scene).*
- **Rough edges (BUG-0190, S4, next view task):** a corpse disc that falls on the edge of a ramp shows as a half-moon
  (the other half is under the slope); and a technical edge in the pick that nothing in the game can reach today.
- **To change it:** the label in `game/data/common/ui.json` (`states.ordered_attack`); the ring's size, colour and time
  (`TargetRing.UnitScale` / `BuildingScale`, `TargetMark.DefaultSeconds` in `sim/Rts.Sim/ViewApi/`); the corpse colours
  `CombatViews.CorpseShade` / `CorpseRimShade`; the rest by inbox note.

### The faction pages now say what each unit may attack, and the design doc's Age II wording matches the rules exactly (data track, D5, 2026-10-08)

- **What happened:** the data track's first M4 task. Both faction pages' unit tables got a **Targets** column ("all" for
  every unit, "buildings" for the Battering Ram), tests pin it to the data both ways (change the ram's rule in the data
  or on the page alone and a test names the unit and the field; a unit note saying "attacks buildings only" must agree
  with the data too). The design doc's "Ages" paragraph now reads **"Requires finished buildings in two different slots
  (any two of Infantry Hall, Ranged Hall, Shock Hall, Forge): two production halls, or one hall and the Forge"** and
  "level II Forge upgrades" (was "level-2" and a looser sentence), and the test that reads that paragraph moved with it.
  A text check that was case-sensitive and skipped the faction upgrades' names is fixed (BUG-0155). QA tried 21 wrong
  edits one at a time; every one failed a test naming the unit or tech, except one clause QA then pinned itself.
- **Change table: nothing numeric changed; no name or description changed; no data file changed; the replay fingerprint
  did not move.** The only player-visible text touched is in the design docs, not the game.
- **Rough edges (BUG-0200, S4, next data task):** the docs/02 clause "two production halls, or one hall and the Forge"
  was not checked by the dev's test (QA's row covers it now); and a test message for an unneeded explicit `"targets":
  "all"` in a data file is misleading. Note: the Shadow page's "Edur Ram: attacks buildings only" has no test until Shadow
  data exists (M8). **Update 1814: both fixed in D6 (the entry near the top).**
- **To change it:** the pages `docs/factions/malazan.md` / `whirlwind.md` and the data together (the tests say which side
  disagrees), or an inbox note.

### M3 is done: the whole base game (economy, buildings, production, Age II, the HUD) is signed off (all three tracks, 2026-10-08)

- **What M3 is:** everything from mines and trees to Age II: workers gather and return, buildings are placed and built
  by several workers and repaired, queues train units with rally points and a population cap, Age II and the Forge
  upgrades are researched and enforced, both factions are fully in data with their text, and the HUD (resource bar,
  selection panel, command card, build menus, placement ghost, queue strip, rally flags) runs it all in the window. Two
  days of sessions over three tracks; the last criterion was proven by a scripted run of your own playtest script.
- **I signed it off myself** (your autopilot setting `stop_at_milestone_end: no`). Conditions held: all eight criteria
  verified, every M3 system covered by unit, fuzz and determinism tests, every track's clean-up session done, and no
  serious open bug once BUG-0146 (the wedged laborers, below) was fixed and verified this session. The leftovers are
  small and listed in the retro (docs/05): a loader corner nobody ships (BUG-0134), the crowd cost of the laborer fix
  (BUG-0151, below), cosmetic view nits, a docs wording drift the data track tidies next.
- **Your playtest is still wanted, as feedback:** the ten-minute script in the M3-V3 entry below. Write "M3 playable ok"
  in the inbox, or what felt wrong; a complaint becomes a task ahead of roadmap work.
- **What's next (M4):** projectiles and splash so archers, mages, catapults and sappers fight (sim), telling a unit to
  attack a specific enemy in the window (view), then fog of war, abilities and stealth.

### Laborers no longer get stuck short of a tree, and you can tell a unit to attack a specific enemy; the Battering Ram now only hits buildings (sim track, M4-2a, 2026-10-08)

- **What was fixed first (BUG-0146, the S2 that held M3):** laborers sent to a tree with only one open side used to form a
  column where only the front one could reach, and when it left, the next one "arrived" against the queue behind it and
  stood there, 1.7 m short, chopping nothing for the rest of the match. The cause was in the walking rules, not the
  economy: a worker walking to a node stopped up to a metre short of its spot, and stopped early against *any* standing
  workmate, even one behind it. Now a worker walks right up to its spot, stops early only for a workmate who is nearer
  the spot than itself, and each open side of a tree or mine has **three standing spots** (middle, left, right), so
  three laborers chop one tree side by side with a fourth queued behind them. Checked on your seed-21 game (the one
  that found it: 343 ticks of waiting at worst, was 14,575; wood 1,360 instead of 100) and on 160 hand-built pockets of
  every shape.
- **What was built:** the **"attack this unit / building" order** in the rules (the window's right-click on an enemy is
  the view's next task). A unit told to attack a target chases it as far as it goes (no leash), ignores every other
  enemy while the target lives, and stands idle where it is when the target dies or proves unreachable. Shift queues it
  behind other orders. Workers obey it (they still never fight on their own). Spamming the order no longer cancels the
  swing (a found-and-fixed bug would have made click-happy players deal zero damage). Attack-moving to a **new** point
  while fighting re-picks the best target there, so you can pull a unit off a building onto the soldier killing it.
  **The Battering Ram attacks buildings only**, through a new data field ("what this attack may target"), as the
  Whirlwind page says. Replays now record whether combat was on (format 4) and the new order.
- **What you'll see:** the laborers at a forest edge now stand three abreast on an open side and the wood counter keeps
  rising. Nothing new to click yet for the attack order (next view task); in the rules it works and is fuzz-tested.
- **Producer decisions, revisit any time:**
  - *An ordered attacker has no leash but still gives up an unreachable target after about 2 s without gaining*, then
    stands idle where it is (not walking home). Alternative: chase forever, or walk home.
  - *A worker obeys an explicit Attack.* Alternative: workers ignore it (purely peaceful laborers).
  - *Archers, mages, catapults and sappers drop an Attack order until next session's projectiles land* (they cannot
    fight yet; the order is dropped, not queued).
  - *The new "targets" field defaults to "all"*; only the ram says "buildings".
  - *I accepted a slower crowded-gathering benchmark (BUG-0151):* a stress scene of 100 laborers on one tree and one mine
    costs 0.32 ms a tick instead of 0.23 (its budget is 1 ms). The code is not slower per worker; the fix simply puts
    39 % more laborers to work in a denser crowd, and they deliver 14-17 % more. The brief's "at most 10 % slower" bound
    was a guard against waste, which this is not. Alternative: order a cheaper crowd (four variants were tried; each
    broke a wedge case or saved little). Revisit with the crowd-cost work after M4.
  - *Size: ~560 rule lines (budget 1,500), ~1,100 dev test lines, ~1,500 QA test lines, ~170 doc lines; two fix rounds
    (three S2 bugs found and fixed in-session: a chaser bouncing between two targets forever, the swing-cancel on spam,
    the attack-move that could not redirect).*
- **Rough edges:** **BUG-0156 (S3, next sim task if cheap):** a unit hammering a building ignores the enemy soldier
  killing it unless you give it an order (an attack-move onto the soldier now works). **Update 0913: fixed in M4-2b (the
  entry at the top): it turns on the soldier between swings while that soldier is in sight.** **BUG-0157 (S3):** very fast
  A-click spam to *different* points (every 50-150 ms) still costs a brawl 10-26 % of its damage (same-point spam costs
  nothing; clicks 250 ms apart cost nothing). **BUG-0151 (S3)** above. **A balance note for you:** crowded gathering is
  now about 15 % more productive at 20 laborers on few nodes (1-10 laborers unchanged); the data track's balance pass
  can tune rates if you want the old income curve.
- **To change it:** the ram's rule is `"targets": "buildings"` on its attack in `game/data/factions/whirlwind/units.json`
  (any unit can take `units` / `buildings` / `all`); the three standing spots and their spacing are
  `EconomyConstants.StandPointsPerCell` / `StandPointSpacing`; the leash / give-up rules by inbox note.

### You can see the fighting now: hp bars, hit flashes, bodies and rubble, and a kill counter (view track, M4-V1, 2026-10-08)

- **What was built:** the view half of combat. A unit that has taken damage shows a small **hp bar** above it (green,
  yellow, red by how much is left; only hurt units show one, like buildings). A unit **flashes white** for a moment when
  it is hit. A dead unit vanishes and leaves a **flat dark disc** where it fell for 10 s of game time; a destroyed
  building leaves a **low grey slab** over its footprint for 20 s. The resource bar reads **"K n / L n"** (your kills and
  losses) under the population, and F12 shows both players'. The selection panel's hit points are live. Also fixed:
  five of the studio's older test scenes that broke when units started fighting now run with a developer-only
  "combat off" switch (`--no-combat`, never a game option) without changing what they check.
- **Please watch a fight (two minutes):** `& $env:GODOT --path game`, box-select the grey army, press **A** and click on
  the orange army far east. Watch bars appear and shrink, white flashes on hits, discs where units fall, the K / L
  counter climbing top right; click a fighting unit and read its hit points dropping in the panel. When the slate
  Town Hall or a Tent falls (send the whole army at it with A), a grey slab stays where it stood for 20 s.
- **Producer decisions, revisit any time:**
  - *Corpses are flat discs in the owner's colour at 35 %, rubble a grey slab*: placeholders until the M6 art pass (death
    animations, real corpse models). QA found the discs read near-black for both teams (BUG-0160 item 3); the next view
    task brightens them. Alternative: no corpses until M6.
  - *A corpse lasts 10 s and rubble 20 s of game time* (so 8x speed shortens them); at most 2,000 markers, the oldest
    replaced.
  - *The flash is 0.15 s and restarts on every hit*, so a unit under a hail of blows stays lit.
  - *Hp bars grow with zoom past 30 m* like the cargo cubes, so they stay readable zoomed out.
  - *Size: ~450 game-code lines, ~280 view-helper lines, ~1,050 dev test lines, ~670 QA test lines, ~85 doc lines.*
- **Rough edges (BUG-0160, S4, next view task):** the F12 overlay's second line runs through the K / L label; a unit
  that is hit between being trained and its first frame shows a bar but never flashes; the corpse discs are too dark to
  tell teams apart; two developer-overlay labels fall back to C# text if the text file is missing. **Update 0913: all
  four fixed in M4-V2 (the entry near the top).**
- **To change it:** the labels in `game/data/common/ui.json` (`hud.kills`, `hud.losses`); the flash length
  `HitFlash.DefaultSeconds`, lifetimes `DeathMarkers.UnitLifetimeTicks` / `BuildingLifetimeTicks`, the marker cap
  `DeathMarkers.DefaultCapacity`, the bar colours `UnitHpBars.Full` / `Half` / `Empty` (all in
  `sim/Rts.Sim/ViewApi/`); the disc / slab look in `game/scripts/CombatViews.cs`; the rest by inbox note.

### Units fight now: attack-move, chasing, fighting back, hit points and death are in the rules (sim track, M4-1, 2026-10-07/08)

- **What was built:** the first slice of combat, melee only. A unit told to **attack-move** (press A, click a point) walks
  there and looks around every fifth of a second: when it sees an enemy it goes for it, stands and swings (a short
  wind-up, then a hit, then a cooldown), and when nothing is left in sight it walks on to where you sent it. An **idle**
  unit that sees an enemy, or is hit by one, fights back and chases it, but no farther than its own sight range from
  where it stood; then it walks home and stands idle again (so a lone scout can't drag your whole army across the map).
  A unit **holding position** fights whatever comes into reach and never moves. A unit on a plain **move** ignores
  enemies. **Workers never fight on their own** (not while idle, not when hit; only if you attack-move them), so your
  laborers keep mining when an enemy walks past and don't wander off to pick fights. Who to attack: whoever is attacking
  me first, then enemy soldiers, then other units, then buildings; nearest first. **Damage** is the design doc's
  formula: attack x the type-vs-armor table x bonuses, minus armor, never below 1; magic ignores armor; Forge upgrades
  count (a Heavy Infantry with Melee Weapons hits for 8 instead of 7). A unit at 0 hp **dies** on the spot (it vanishes;
  the body is the view's next task) and its population frees up; a building at 0 hp is destroyed (its queue refunded,
  its squares freed by the no-walled-off-ground rule). Two equal units that swing together both die (no "who was first
  in memory" advantage). Each player's kills and losses are counted.
- **What you'll see:** `& $env:GODOT --path game`, box-select the grey army, press **A** and click on the orange army far
  east: they march, stop at the enemy lines, turn to face them and, after a moment, orange and grey units start
  vanishing one by one until a side is gone (no hit animation, hp bar or body yet: those are the view's next task, as
  is the panel's live hp, which still reads "max / max"). The selection panel reads "Attacking" for a unit in a fight.
  Right-click instead of A and they walk through without fighting (and get hit without fighting back until they stop).
  Workers at a mine ignore an enemy soldier standing next to them. Ranged units, casters, the Catapult and the Sapper
  don't shoot yet (projectiles are next): they only get hit.
- **Producer decisions, revisit any time** (each a one-line change):
  - *Retaliation leash = the unit's own sight range from where it stood*, then it walks home without looking for more.
    Alternative: a fixed 15 m, or chase until the attacker dies.
  - *A swing lands if the target is still within reach + 0.5 m when the wind-up ends*; farther, the swing is lost.
    Alternative: always lands (homing), or never once out of reach.
  - *Workers never fight on their own* (taken after a test showed idle laborers walking 26 m to kill the neighbour's
    laborers). Alternative: workers fight back when hit (StarCraft-like).
  - *A chase that makes no progress for 2 s is given up* (the target is on a cliff you can't climb, behind a wall, or as
    fast as you), unless a friend is already fighting it (then you're just queued behind your own front rank). After
    three give-ups a unit only fights what comes into reach, until its next order or its next landed hit.
  - *The Battering Ram attacks units too for now*; the design doc says buildings only. That needs a new data field
    ("what this attack may target"), which changes the data fingerprint, so it lands with the next sim task.
  - *A test switch turns combat off* for the studio's older movement and economy tests (two-player scenes whose armies
    would now fight); real matches always fight. Not a game option.
  - *The one-player 2,500-unit stress budget went from 4.5 to 4.6 ms a tick* (combat's bookkeeping costs 0.08 ms per
    tick there; a 500 v 500 brawl costs 3.1 ms, within the 4 ms design budget).
  - *Size: ~1,250 rule lines (budget 1,500 for a clear design, ~800 for a new system: over, because two fix rounds
    added the give-up memory and its brawl exception), ~2,000 dev test lines, ~1,900 QA test lines, ~230 doc lines.*
- **Rough edges:** **BUG-0146 (S2; found by the view's scripted playtest, not by combat):**
  laborers sent to a tree that is open on one side can stand 1.7 m short of it, "gathering" nothing for the rest of the
  match; it holds the M3 sign-off. **Update 2026-10-08: fixed (M4-2a entry above); M3 signed off.** **BUG-0139 (S3):** the
  ram attacks units (above). **Update 2026-10-08: fixed, the ram is buildings-only (M4-2a).** **BUG-0149 (S3, next
  sim clean-up):** a unit that gave up chasing something unreachable, and then spots a reachable enemy behind a short
  wall, may give that one up too and stand idle in sight of it. **BUG-0144 (S3):** while buildings are being placed every
  two seconds, chasers can wait up to 1.7 s for a path. **BUG-0142 (S4):** in a duel between two idle equal units already
  in reach, the one whose turn to look comes first wins by one hit; the placement ghost says "Units in the way" for a
  worker that is fighting inside the footprint while the order itself goes through. Not in yet (planned slices): the
  explicit "attack this unit" order (M4-2a, next), projectiles / misses / splash / friendly fire (M4-2b), fog (M4-3),
  abilities (M4-4), stealth (M4-5). **Update 2026-10-08: the explicit order is in (M4-2a entry above); the panel's live
  hp, hp bars and bodies are in (M4-V1 entry above).**
- **To change it:** attack, armor, hp, range, cooldown and wind-up per unit in `game/data/factions/<faction>/units.json`,
  the type-vs-armor table in `game/data/common/damage_table.json`; the leash, the grace, the worker rule and the give-up
  timing by inbox note (they are engine constants in `CombatConstants`, not data: say if you'd rather have them in a file).

### M3's last criterion is proven by a scripted playtest through the real HUD; right-clicks beside trees are fixed; locked buttons say "Locked" (view track, M3-V4, 2026-10-07/08)

- **What happened:** a headless test now plays **your ten-minute script** (the M3-V3 entry below) through the real
  window at 8x speed, by injected clicks and keys only: box-select the five workers, right-click the mine, click the
  Town Hall, queue three Laborers and cancel one, rally on a forest, build a Barracks, a Depot, an Armory and a Billet
  with one worker, research Age II (greyed "Locked" first), Melee Weapons, place the Engineers' Yard only after Age II,
  train a Sapper and a Heavy Infantry, and read the green "+1" on the Heavy Infantry's attack. Every step is checked
  against the rules and prints the game time it finished at; it passes on two map seeds in about 10 minutes of game time
  each (budget 13), twenty runs out of twenty, and again on top of the new combat. **M3 is 8 / 8** on that; I have not
  signed it off yet because the run itself found a real bug (BUG-0146, below).
- **What you'll notice in the window:** (1) a **right-click on open ground just north of a tree or mine is a walk again**,
  and a click on the drawn canopy or the mine's block still gathers it (BUG-0125; 48,000 test rays, 0 wrong). (2) A
  building you haven't unlocked (the Wickan Corral before a Barracks, the three V-menu buildings before Age II) shows
  a **greyed button reading "Locked"** and, if you press it anyway, a red ghost reading "Locked" (was "Needs more", which
  sounded like money). (3) Age II's button reads **"In a queue"** while it is queued and **"Researched"** afterwards even
  if you lose a hall (was "Locked"). (4) "Attacking" is the panel's state text for a fighting unit.
- **Producer decisions, revisit any time:**
  - *A locked building's button stays pressable* (greyed, with the reason), so you can raise its ghost and see what it
    needs. Alternative: disable it like a train button.
  - *The pick tests a slightly larger round shape than the drawn 6- / 7-sided tree*, so about 1 % of clicks on the sliver
    between a facet and the circle name the tree (at most 8 cm, a pixel at far zoom). Alternative: the exact polygon,
    at a cost not worth it before the M6 art pass (BUG-0148 item 1, S4).
  - *Accepted with BUG-0147 open:* on the merged `main`, five of the studio's older M2 test scenes fail because the two
    start armies now fight each other (their expectations are about armies that never fight). The release gate (build,
    tests, boot check) is green; the scenes get the sim's combat-off switch in the view's next task. Alternative: hold
    the view, which would hold combat too (the window needs the view's "Attacking" text to boot).
  - *Size: ~80 game-code lines, ~120 view-helper lines, ~1,150 dev scene lines, ~660 QA test lines, ~100 doc lines.*
- **Rough edges:** **BUG-0146 (S2, sim; fixed first next session):** on seed 21 four laborers sent to a tree that is
  open only to the south stand in a column 1.7 m short of it, "gathering" nothing for 12 minutes; wood income from them
  is zero. You'd see it as workers standing still by a forest edge. The sim's next task fixes the arrival rule.
  **Update 2026-10-08: fixed (M4-2a entry at the top).** **BUG-0147 (S3, view's next task):** the five scenes above.
  **Update 2026-10-08: fixed (M4-V1 entry at the top: the scenes run with the developer-only combat-off switch).** **BUG-0148 items 1 and 3 (S4):** the sliver above; the
  script checks that a rallied laborer chops *some* tree, not the rally forest specifically.
- **To change it:** the words in `game/data/common/ui.json` (`placement.requires`, `research.already_queued`,
  `research.already_researched`, `states.attacking`); the drawn tree / mine sizes in `game/scripts/PropsView.cs` (the
  pick follows them); the script's steps in `game/tests/M3PlayableTest.cs`; the rest by inbox note.

### The shared upgrades' text is final: Age II's description rewritten for both factions, the six Forge upgrades kept (data track, D4, 2026-10-07/08)

- **What happened:** the data track's end-of-M3 clean-up. The seven techs both factions share (Age II and the six Forge
  upgrades) now have their text pinned to the design doc by tests, with one change: Age II's description named
  Malazan-only buildings ("the Caster Hall, Siege Works and Watch Tower") although the Whirlwind player reads the same
  text. The test failure messages from D3 now say which tech, which field, and both values (BUG-0132). The replay
  fingerprint moved once for the text (every checkpoint identical).
- **Change table (nothing numeric changed; no name changed):**

  | What | Field | Old → New | Why |
  | --- | --- | --- | --- |
  | Age II | description | "Advance to Age II. Unlocks the Caster Hall, Siege Works and Watch Tower, your unique unit, level-2 Forge upgrades and your faction upgrade." → **"Unlocks your caster, siege and tower buildings, unique unit, level II and faction upgrades. Needs two kinds of building: infantry, ranged, mounted or upgrade."** | one text serves both factions (no faction's building names); names the requirement in words, as every other description does |
  | Melee Weapons / II, Ranged Weapons / II, Armor / II | description | unchanged (quoted in the M3-5 entry below) | they already read well; now pinned to docs/02 |

- **Your call (taste):** "Needs two kinds of building: infantry, ranged, mounted or upgrade" uses "upgrade" for the Forge
  kind (the Armory / Smithy), because the rule forbids naming any one faction's building. If you'd rather read "Needs two
  of: an infantry hall, a ranged hall, a mounted hall or a forge" (design-doc kind names), or drop the "Needs" sentence
  (the card's tooltip already lists it), one inbox line; the data track applies it next time it runs.
- **Rough edges:** BUG-0155 (S4): one of the dev's text checks is case-sensitive and skips faction upgrade names; QA's own
  check covers the gap. The design doc's "Ages" paragraph says "level-2" where the data says "level II" and "two Age I
  production buildings or a Forge" where the exact rule is "any two of the four": the data track tidies that wording
  with its next task (its tests read that paragraph). **Update 0913: both done in D5 (the entry near the top).**
- **To change it:** `game/data/common/techs.json` (`description` of `age_ii` and the six upgrades), or an inbox note.

### Process note: a session resumed while its predecessor was still alive (studio, 2026-10-07/08)

- Session 2014 stalled; its lock was 3 hours old, so the 23:15 session took over and finished its work. Six minutes
  later the old session woke up and committed one report file to the view branch before stopping for good (nothing
  reached `main`; QA re-ran the report, nothing was lost). **Suggestion for the studio skill (your file, not mine):** a
  lock's age alone shouldn't declare a session dead; long sessions should refresh the lock, and a resume should check
  for running studio processes first. Also from this session: two tracks filed a bug with the same number (BUG-0145);
  the sim's became BUG-0149, and from now on each session's tracks get disjoint id blocks.

### The base game is whole and on `main`: the HUD landed, six view bugs are fixed, and your ten-minute M3 playtest is wanted (view track, M3-V3b, 2026-10-07)

- **What happened:** the HUD from the entry below (selection panel, production card, queue strip, rally points,
  population) is on `main` now. Its test setups were written before Age II had requirements, so they queued Age II at a
  bare Town Hall; they now build two halls first, and a new check proves the card's greyed buttons agree with the game's
  rules in every one of 450 frames through every phase (no halls, one hall, two of the same kind plus a site, two
  different halls, Age II queued and then a hall destroyed, rebuilt). Every test scene passes both with the shipped data
  and with the data track's building locks. `main`'s headless check is green again.
- **The view's clean-up, in plain words (what you'll notice):**
  1. A **greyed button looks greyed**: its name and key dim to half strength, with the red reason line at full strength
     (before, only the small reason line told it from a live button). A selected building being repaired no longer
     makes the game allocate memory every tick (BUG-0123).
  2. **Construction sites are a dusty mauve**, clearly different from the finished slate or orange buildings, and the
     gold / wood **cube a worker carries stays visible when you zoom out** (BUG-0107).
  3. **"Quartermaster's Depot" wraps at the space**, not mid-word; a flurry of **Shift-clicks on one spot sends one build
     order per worker**, not a copy per click; the **ghost keeps up with a panning camera**; the ghost's reason text is
     three times bigger and a deeper red (BUG-0122).
  4. **Clicking a building hidden behind a hill picks the hill**, not the building; and a **right-click on a tree's
     canopy means that tree** even when the ground under the cursor is behind it (before, the ground point decided).
  5. The benchmark's march test holds a per-seed bound (BUG-0104); stale docs figures refreshed (BUG-0105).
- **Please play it (ten minutes), this is M3's last criterion.** The script is in the entry below (M3-V3), unchanged,
  with two differences now that the data track's building requirements are live: the **Wickan Corral** button is red
  "Needs more" until a Legion Barracks is finished, and the three **V-menu buildings** (Cadre Tower, Engineers' Yard,
  Watchtower) are red until Age II. Write **"M3 playable ok"** in the inbox, or what felt wrong. If you don't, the studio
  proves the same script with a scripted headless run next session and ticks the criterion itself.
- **Producer decisions, revisit any time:**
  - *Accepted with one new rough edge open (BUG-0125, below) rather than hold the HUD a second session*: holding would
    have kept `main`'s check red and the playtest blocked. Alternative: strip the new tree-canopy pick before merging.
  - *Greyed = name and key at 50 %, reason line at 100 %.* Alternative: dim everything.
  - *Sites are mauve* (a hue far from both factions' colours) until the M6 art pass. Alternative: stripes or scaffolding.
  - *Size: ~290 game-code lines, ~100 view-helper lines, ~1,000 dev test lines, ~470 QA test lines, ~75 doc lines.*
- **Rough edges:** **BUG-0125 (S3, fixed in the next view task):** the new canopy pick treats a tree as a full-height
  column over its square, so a right-click on open ground up to about 2.5 m "behind" (north of) a tree or mine now sends
  workers to gather that tree instead of walking (339 of 3,000 test clicks beside trees; 0 before). Harmless but
  annoying beside forests; the fix is to test the drawn cone rather than the square. **BUG-0126 (S4):** a locked
  building's ghost says "Needs more" (sounds like money; should say "Locked") and its menu button isn't greyed; Age II's
  button says "Locked" while it is already in the queue or researched if a hall was lost (the game is right, the
  wording is unhelpful); items 1-2 fixed with BUG-0125, the rest at the view's next clean-up. **Update 2315: BUG-0125
  and BUG-0126 items 1-2 are fixed (the M3-V4 entry at the top); items 3-6 stay for the view's next clean-up.**
- **To change it:** the dim strength is `CommandCard.DimAlpha`, the site colour `BuildingViews.SiteColor`, the reason
  text size `BuildGhost.ReasonPixelPerZoom`; the words in `game/data/common/ui.json`; the rest by inbox note.

### Buildings and units never cross a cliff when set down, refused build orders are free, impossible requirements are caught at load, and the data track's techs and building locks landed (sim + data tracks, M3-H2 and D3, 2026-10-07)

- **What was built (clean-up, no new features):**
  1. **A newly trained unit appears beside its building, never on another plateau.** The game now knows the map's
     plateaus (each connected patch of same-height ground; a ramp belongs to the ground at its foot). A unit spawning
     from a building whose whole plateau is packed **waits** instead of appearing 30 m away across a cliff, and units
     pushed out of a new building's footprint spread over their own plateau, one per square before any square takes a
     second (BUG-0097, BUG-0095). Twenty Town Halls all waiting for room cost 0.09 ms a tick (was 1.7-1.9).
  2. **A build order refused because it would wall ground off costs nothing the second time:** the answer is kept
     while the map is unchanged, so 100 workers told to build at one bad spot cost 0.4 ms instead of 33 (BUG-0096).
  3. **Impossible requirements are refused when the data loads** (BUG-0100): a Malazan unit needing a Whirlwind
     building, a shared upgrade needing one faction's building, or an "any two of" rule nobody can ever satisfy (say,
     Age II needing halls that themselves need Age II). An upgrade that applies to no unit is an error too.
  4. **The data track's D3 landed** with the test fixes that held it (BUG-0112): the sim's own stress tests had assumed
     no building was ever locked; they now run on a copy of the data with the locks cleared (they test geometry, not
     locks), and every one of their per-seed counts came out identical to before. Nothing weakened.
- **What you'll see:** the building locks, in the window (the entry above): the Wickan Corral needs a Legion Barracks;
  the Cadre Tower, Engineers' Yard and Watchtower need Age II (same for the Whirlwind: Horse Lines need a Raider Camp;
  Shrine, Ram Yard, Lookout Tower need Age II). The replay fingerprint moved only for the data; every unit's path is
  byte-identical.
- **D3's change table for your review (data track; nothing numeric changed):**

  | What | Field | Old → New | Why |
  | --- | --- | --- | --- |
  | Wickan Corral (Shock Hall) | needs | nothing → Legion Barracks | the faction page's Requires column; design doc "Buildings": the Shock Hall needs the Infantry Hall |
  | Horse Lines (Shock Hall) | needs | nothing → Raider Camp | same |
  | Cadre Tower, Engineers' Yard, Watchtower | needs | nothing → Age II | design doc "Ages": Age II unlocks the Caster Hall, Siege Works and Watch Tower |
  | Shrine of the Whirlwind, Ram Yard, Lookout Tower | needs | nothing → Age II | same |
  | Moranth Supply | description | "Munitions bought from the Moranth: the Sapper's Cusser cooldown drops from 45 to 30 s and the Catapult gains 4 m of range. Needs Age II." → **"Crates of Moranth munitions, paid for in Imperial coin: the Sapper's Cusser cooldown drops from 45 to 30 s and the Catapult gets +4 m range. Needs Age II."** | faction voice; still names both numbers and the requirement (tests pin that) |
  | Dryjhna's Prophecy | description | "The prophecy of the Apocalypse: Zealots gain 20 HP and the Priest's Sandstorm cooldown drops from 45 to 30 s. Needs Age II." → **"The Apocalypse foretold is at hand: Zealots gain +20 HP and the Priest of the Whirlwind's Sandstorm cooldown drops from 45 to 30 s. Needs Age II."** | same |
  | Faction pages | new "Techs" table each | — | the pages are the design source; tests pin the table to the data |

  The other twelve buildings keep no requirement, as the pages say. No name changed. BUG-0111 (page pins missed a false
  "+N pop" claim) and the buildings half of BUG-0090 are fixed by D3's tests.
- **Producer decisions, revisit any time:**
  - *A spawn on a packed plateau waits* (the Age of Empires behaviour). Alternative: let it appear at the foot of the
    plateau's ramp.
  - *Pushed-out units share a square only when there are more of them than squares on the plateau.* Alternative: let
    the extras wait inside the footprint until a square frees (they'd be invisible inside the building meanwhile).
  - *Impossible requirements are errors, not warnings*: a content author finds out at once; the shipped data has none.
  - *Ownership refinement for next session's D4:* the shared techs file (`common/techs.json`) belongs to the sim track,
    but D4 edits only its names and descriptions, as a named exception, and the sim task that session doesn't touch it.
  - *Size: ~500 rule lines, ~950 dev test lines, ~650 QA test lines, ~60 doc lines (budget 1,500).*
- **Rough edges:** BUG-0134 (S3, next sim clean-up): one impossible-requirement shape still loads clean: a building
  that needs an upgrade researched only at that same building (an Armory needing Melee Weapons). Nothing shipped does
  it; a content author would hit it the first time they try. BUG-0133 (S4): if more than 24 units are pushed out onto
  every single square of a tiny plateau, some share an exact point until they jostle apart (needs hundreds of units in
  one footprint; the docs sentence will be corrected). Not done this session for budget: BUG-0113 (a technical error
  message for a wrong-type value in a data file), BUG-0094 (a test helper's groves can wall cells in).
- **To change it:** the locks are the `requires` lists in `game/data/factions/<faction>/buildings.json`; the two
  descriptions in `game/data/factions/<faction>/techs.json` (or an inbox note: the data track applies it next session);
  the wait / spread rules by inbox note.

### You can run a whole base in the window now, up to Age II: a selection panel, train and research buttons, a queue with progress, rally points and the population count. Please play it (view track, M3-V3, 2026-10-07)

- **Update 1715: landed on `main`** (M3-V3b, the entry at the top): the playtest below works from `main` now, with the
  building locks live. BUG-0123 and BUG-0122 (rough edges below) are fixed.
- **Status update (integration, same evening): held one session.** I accepted this work, but when the conductor
  merged it on top of the sim's new locks, one of its own tests failed: the test queues Age II at a Town Hall that has
  no halls, which the sim now (rightly) refuses (BUG-0124). The branch is kept, the fix is a test setup change, and it
  lands first thing next session. Until then `main`'s window shows the M3-V2 state and logs an error at start (the
  sim's new "Locked" reason has no text yet on `main`), so the playtest below works after the next session, or today
  from the branch `studio/2026-10-07-1415-view` if you like.
- **What was built:** the last piece of M3's HUD. **(1) A selection panel** bottom centre (right of the minimap): one
  selected unit shows a coloured placeholder portrait with its initial, its name, HP, Attack, Armor, Range, Speed from
  the data (a researched upgrade shows beside the number as a green "+1"; **the bonus is shown only, combat applies it
  in M4**) and what it is doing ("Idle", "Gathering", "Building" ...); several units show a grid of up to 24 portraits
  (a "+N" for the rest) with the active Tab subgroup outlined, and a click on a portrait selects that unit alone; a
  building shows its name and hit points. **(2) A production card:** click one of your finished buildings and the
  command card lists what it trains and researches on the grid keys (a Barracks: Heavy Infantry on Q; the Town Hall:
  Laborer on Q, Age II on W; the Armory: the six Forge upgrades then Moranth Supply), each with its name, key and cost,
  and the description plus "Needs ..." in a tooltip. A button is **greyed with the reason** ("Can't afford", "Queue
  full", "In a queue", "Researched", "Locked") exactly when the rules would refuse it. **(3) A queue strip** above the
  card: up to five squares (a unit's colour and initial, or a tech's name), the first with a yellow progress bar; click
  a square to cancel it and get the full cost back. When Age II completes, "Age II" flashes in the resource bar.
  **(4) Rally points:** with a building selected, right-click the ground (or the minimap) to plant a small yellow flag
  with a line from the building: new units walk there; a rally on a gold mine or a tree makes new workers gather it;
  right-click the building itself to clear it. **(5) "Pop 5 / 10"** in the resource bar, red when the cap is full.
  **(6)** The three M3-V2 rough edges are fixed: a right-click on the top face of a building now means that building
  (BUG-0108), a placement click builds exactly where you clicked (BUG-0109), and a broken `ui.json` reports an error
  instead of blank labels (BUG-0110).
- **Please play it: this is M3's last criterion ("build a full Malazan base and reach Age II"), about ten minutes.**
  `& $env:GODOT --path game`, maximize. Box-select the five grey workers by the slate Town Hall, right-click the nearest
  gold mine: they gather. Click the Town Hall: the panel shows its name and HP; the card shows **Laborer (Q)** and
  **Age II (W)** greyed "Locked" (you need two different halls first). Press Q three times: three squares appear in the
  strip, the first with a progress bar; click the third square to cancel it (the 50 gold comes back). Right-click a
  forest with the Town Hall still selected: a yellow flag; the new Laborers walk there and chop. Select some workers,
  press **B** then **E** (Legion Barracks, 150 wood), click on green; then **B**, **A** (Armory, 100 / 100) somewhere
  else; later **B**, **Q** (Billet, 50 wood) to raise the cap from 10 to 18. When the Barracks and the Armory are both
  finished, click the Town Hall: **Age II is live** (400 gold / 200 wood, 60 s). Press W, watch the bar fill; at the end
  "Age II" flashes top right. Now click the Armory: Melee Weapons (100 / 50) is live, Melee Weapons II stays "Locked"
  until Melee Weapons is researched, and Moranth Supply is live after Age II. Click the Barracks: Heavy Infantry on Q;
  after Melee Weapons, select one and the panel reads its attack with a green "+1". Press **V**, **W** (Engineers'
  Yard): the Sapper button in it is "Locked" until Age II. Write **"M3 playable ok"** in the inbox, or what felt wrong
  (that becomes the view track's next task ahead of roadmap work). Things to look for: does the card read well at your
  resolution, is the flag visible enough, does the greyed button look greyed enough (it doesn't, see the rough edges).
- **Producer decisions, revisit any time:**
  - *Shared upgrades are listed before the faction's own* on a Forge's card (Armor, Armor II, Melee I / II, Ranged I /
    II, then Moranth Supply). Alternative: the faction upgrade first.
  - *A greyed button shows the reason instead of the cost* (the cost is still in the tooltip). Alternative: both lines.
  - *At the population cap the train buttons stay live*: an item queues, pays, and waits for room (the rules have no
    "pop full" refusal; the red "Pop" is the cue). Alternative: grey them with "Pop full"; one inbox line.
  - *Layout:* the panel sits between the minimap and the card; the resource bar moved 142 px left to make room for the
    pop count. Unit hit points read "max / max" until combat exists (M4).
  - *A right-click on any building's box means that building*: on your own damaged one, Repair; on your own site, join;
    on an enemy or an undamaged building, a walk to its centre (so units gather round it). A right-click on a tree or
    mine still goes by the ground point (nodes are low; fine in play).
  - *Size: ~1,350 game-code lines (panel 380, card +160, selection +160, queue strip 180, rally marker 120, resource bar
    +150, text loader +100), ~175 view-helper lines, ~1,000 dev test lines, ~1,270 QA test lines, ~120 doc lines; over
    the 1,500 budget on game code by the panel; accepted, it is plain layout code.*
- **Rough edges:** BUG-0123 (S3, fixed in the view's clean-up session, next): while a selected building is being
  repaired, the panel rebuilds its hit-point text every tick (about 52 bytes a tick: harmless in play, but against the
  studio's "no per-frame allocation" rule), and a greyed button keeps its bright name and cost, so only the small red
  reason line tells it from a live one. BUG-0122 (S4, same session): "Quartermaster's Depot" wraps mid-word, the ghost
  trails a panning camera by a frame, the reason text is small. Also: the Age II buildings (Cadre Tower, Engineers'
  Yard, Watchtower) and the Wickan Corral are **not locked yet** in the window: their "needs ..." values are the data
  track's D3, verified but held one session (see the data row in Now and the D3 note below).
- **To change it:** every word in `game/data/common/ui.json` (`train`, `research`, `states`, `hud` sections); panel and
  strip sizes are constants at the top of `game/scripts/SelectionPanel.cs` / `ProductionQueueStrip.cs`; the flash
  length `ResourceBar.AgeFlashSeconds`; the rest by inbox note.

### Requirements are real now: the Sapper needs Age II, Age II needs two different halls, Melee Weapons II needs Melee Weapons and Age II (sim track, M3-6, 2026-10-07)

- **What was built:** the "unlocks" half of M3's Age II criterion. Every "needs ..." in the data is now enforced: a
  unit can't be trained, a building can't be placed and a tech can't be researched until its requirements are met. A
  requirement is either a tech you have researched or **one of your own finished buildings of that kind** (a site under
  construction doesn't count; an enemy's doesn't). Age II has a special rule from the design doc, written in the data:
  **any two of** Infantry Hall, Ranged Hall, Shock Hall and Forge must be finished (two Barracks count once). The check
  happens **when you give the order**, not again when it completes: if your only Barracks is destroyed while a unit it
  unlocked is training, the unit still finishes (the Age of Empires rule). Four loader rough edges were fixed in the same
  pass: a duplicate key in any data file is an error (BUG-0008), every faction must fill all seven unit slots and all ten
  building slots exactly once (BUG-0010), an empty "applies to" list in an upgrade is an error instead of "everyone"
  (BUG-0098), and a requirement loop or a tech named like a building is an error (BUG-0099).
- **What you'll see:** in the window, once the production card above lands (held one session, BUG-0124): Age II
  greyed "Locked" until two different halls stand; the Sapper and Zealot "Locked" until Age II; Melee / Ranged / Armor level II "Locked" until level I and Age II;
  Moranth Supply until Age II. The placement ghost also goes red with "Needs more" for a building you haven't unlocked,
  but **no building is locked in the window yet**: the values ("Wickan Corral needs a Legion Barracks", "Cadre Tower,
  Engineers' Yard, Watchtower need Age II") are the data track's D3, held one session. Numbers: 5,000 refused orders in
  one tick cost 0.7-0.9 ms; a refusal is cheaper than an acceptance; the fingerprint of the replay did not move.
- **Producer decisions, revisit any time:**
  - *Checked at order time only* (above). Alternative: cancel queued items when their unlock is lost (StarCraft-like).
  - *A building requirement means a finished building you own.* Alternative: count a site, or an ally's (no allies yet).
  - *"Any two of" is a data rule* (`requiresAnyOf` with a count and a list of building kinds), not code, so a future
    faction or Age III can use it. The list names building **kinds** (slots) so the one shared Age II entry works for
    every faction.
  - *Every faction fills every slot exactly once* (seven units, ten buildings): a content rule, now enforced at load. It
    is what the design doc's templates say; it also means the Teblor / Tiste / Shadow factions (M7-M9) can't skip a slot
    without a rules change. Alternative: allow empty slots.
  - *The ghost's "needs" check runs before the map checks*, so the reason you see for a locked building is always
    "Needs more", even over a tree. And a researched Age II whose halls were later lost reads "Locked" rather than
    "Researched" if asked (a corner nobody will see: Age II can't be researched twice anyway).
  - *Size: ~520 code lines (budget 1,500), ~1,230 dev test lines, ~1,300 QA test lines, ~75 doc lines.*
- **Rough edges:** BUG-0100 (S3, sim clean-up next session): a data author could write a requirement that can never be
  met (a Malazan unit needing a Whirlwind building; a shared upgrade needing one faction's building; three halls needing
  Age II while Age II needs two halls) and the game loads it silently and locks the content for the whole match. The
  shipped data has none; the fix is a load-time check. BUG-0112 (S3, see the data note below): the data track's building
  requirements make 25 of the sim's own test setups fail (the tests assumed nothing is locked), which is why D3 is held.
  BUG-0113 (S4): a wrong-type value in a data file gets a technical error message; a size test now times a failing load.
  **Update 1715: BUG-0100 and BUG-0112 are fixed and D3 landed (the M3-H2 / D3 entry near the top): the buildings are
  locked in the window now. BUG-0113 stays for the next sim clean-up.**
- **To change it:** the requirement lists are the `requires` fields in `game/data/factions/<faction>/units.json`,
  `buildings.json` and the techs files; the Age II rule is `requiresAnyOf` on `age_ii` in `game/data/common/techs.json`
  (change `count` to 1 or 3, or the list); the order-time rule and the slot rule by inbox note.

### The techs text and the building requirements are written and checked, but wait one session to land (data track, D3, 2026-10-07)

- **Update 1715: landed** through the sim's M3-H2; the full change table is in the M3-H2 / D3 entry near the top. The two
  descriptions below are the shipped ones; an inbox note changes them in D4.
- **What happened:** the data track finished D3 (the faction upgrades' descriptions rewritten, the "needs ..." values
  on eight buildings, a Techs table on each faction page, tests that pin all of it to the pages, BUG-0111 and the
  buildings part of BUG-0090 fixed). QA and I verified it. It is **held on its branch** because the eight new
  building requirements make 25 of the sim track's own test setups fail (they were written when nothing was locked:
  BUG-0112). `main` is always green, so the sim's next session merges the branch with the test fixes, and the full
  change table for your review appears here then. The two new descriptions, so you can object early:
  - Moranth Supply: "Crates of Moranth munitions, paid for in Imperial coin: the Sapper's Cusser cooldown drops from 45 to 30 s and the Catapult gets +4 m range. Needs Age II."
  - Dryjhna's Prophecy: "The Apocalypse foretold is at hand: Zealots gain +20 HP and the Priest of the Whirlwind's Sandstorm cooldown drops from 45 to 30 s. Needs Age II."
- **To change it:** an inbox note; it is applied when the branch lands.

### Age II and the Forge upgrades exist in the rules: a Town Hall researches Age II, a Forge researches weapon and armor upgrades, and each player's upgrades are tracked (sim track, M3-5, 2026-10-07)

- **What was built:** techs. The game now has a data file of **technologies**: Age II (400 gold / 200 wood, 60 s, at the
  Town Hall) and six Forge upgrades (Melee Weapons I / II, Ranged Weapons I / II, Armor I / II, at the Armory / Smithy),
  shared by both factions, plus one upgrade per faction (Malazan **Moranth Supply**, Whirlwind **Dryjhna's Prophecy**,
  200 / 150, 45 s, at the Forge). A tech is researched **through the same queue a building trains units in** (one timer
  per building, like Age of Empires): you pay when you queue it, it takes no population, cancelling refunds it in full
  (so does the building being destroyed), and a tech can be in only one queue at a time and researched only once. When it
  finishes, the player "has" it: Age II makes the player Age 2, and the upgrades add to the right units' stats: Melee
  Weapons +1 attack per level to every unit that fights in melee (workers too), Ranged Weapons +1 attack per level to
  every unit that shoots (and towers, from M4), Armor +1 per level to every non-siege unit; Moranth Supply shortens the
  Sapper's Cusser cooldown 45 s → 30 s and adds 4 m to the Catapult's range; Dryjhna's Prophecy gives Zealots +20 HP and
  shortens the Priest's Sandstorm 45 s → 30 s. **Combat doesn't apply the bonuses yet** (there is no combat until M4);
  they are a query the fighting will read.
- **What you'll see:** nothing in the window yet: the research buttons are the view track's next task (M3-V3: click a
  Town Hall, press the Age II button, watch the queue). What is not in yet either: **nothing is locked**. The data says
  "Melee Weapons II needs Melee Weapons and Age II" and "the Sapper needs Age II", and the game checks those names
  exist, but it doesn't enforce them until the next sim task (M3-6). Today: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 50 --workers 5 --forests 12 --mines 8 --ticks 600`
  ends each player's line with `age 1`. **Update 1415: both are in. The research buttons are in the window (M3-V3
  entry at the top) and the locks are real (M3-6 entry at the top).**
- **The new player-facing text, quoted** (placeholder wording by the sim builder; the data track polishes it next
  session and you review that pass):
  - Age II: "Advance to Age II. Unlocks the Caster Hall, Siege Works and Watch Tower, your unique unit, level-2 Forge upgrades and your faction upgrade."
  - Melee Weapons: "+1 attack for melee units." · Melee Weapons II: "Another +1 attack for melee units (+2 in all). Needs Melee Weapons and Age II."
  - Ranged Weapons: "+1 attack for pierce units and towers." · Ranged Weapons II: "Another +1 attack for pierce units and towers (+2 in all). Needs Ranged Weapons and Age II."
  - Armor: "+1 armor for all non-siege units." · Armor II: "Another +1 armor for all non-siege units (+2 in all). Needs Armor and Age II."
  - Moranth Supply: "Munitions bought from the Moranth: the Sapper's Cusser cooldown drops from 45 to 30 s and the Catapult gains 4 m of range. Needs Age II."
  - Dryjhna's Prophecy: "The prophecy of the Apocalypse: Zealots gain 20 HP and the Priest's Sandstorm cooldown drops from 45 to 30 s. Needs Age II."
- **Numbers (all copied from the design doc's Tech table, not tuned):**

  | Tech | Gold / wood | Time | Effect |
  | --- | --- | --- | --- |
  | Age II | 400 / 200 | 60 s | Age 2 |
  | Melee Weapons I / II | 100 / 50 · 175 / 100 | 30 s · 40 s | +1 / +2 attack, melee units |
  | Ranged Weapons I / II | 100 / 50 · 175 / 100 | 30 s · 40 s | +1 / +2 attack, pierce units (and towers, M4) |
  | Armor I / II | 125 / 50 · 200 / 125 | 35 s · 45 s | +1 / +2 armor, non-siege units |
  | Moranth Supply (Malazan) | 200 / 150 | 45 s | Sapper Cusser cooldown -15 s, Catapult range +4 m |
  | Dryjhna's Prophecy (Whirlwind) | 200 / 150 | 45 s | Zealot +20 HP, Priest Sandstorm cooldown -15 s |

- **Producer decisions, revisit any time** (the design doc left these open; each is a one-line data or rules change):
  - *The Forge table's "+1 / +2" are totals*, so level II adds one more point (not two). Alternative: level II adds 2 (+3 in all).
  - *"Melee units" means every unit whose attack is melee, so **workers get Melee Weapons** too* (as in Age of Empires). Alternative: exclude the worker slot.
  - *"Non-siege" means "not in the siege slot", so **the Sapper (a unique unit with a siege-type attack) gets Armor***. Alternative: go by attack type and leave the Sapper out.
  - *Shared techs live in one common file, the faction upgrade in each faction's file*; a tech names the building **kind** that researches it ("forge"), so one entry serves both factions' Forges.
  - *Research runs in the building's one queue, takes no population, is refunded in full on cancel* (AoE rules).
  - *A tech waits behind a unit that is stuck on the population cap* (the queue is in order; QA noted it, and the rule stands: a tech at the head is never blocked by pop). Alternative: let techs skip ahead of a pop-blocked unit.
  - *The same Cancel removes a unit or a tech from a queue* (no separate command).
  - *Size: ~900 code lines (budget 1,500), ~2,300 dev test lines, ~1,300 QA test lines, ~95 doc lines.*
- **Rough edges:** BUG-0098 (S3): if someone edits a tech's "applies to these units" list to be *empty*, the game
  reads it as "every unit of both factions" instead of refusing the file; the shipped data has no such list; fixed in
  the next sim task (M3-6), which rewrites that part of the loader anyway. BUG-0099 (S4): a requirement loop
  ("A needs B, B needs A") or a tech named like a building loads without complaint today; both become errors in M3-6.
  Also noted for M4: "Ranged Weapons ... and towers" has no tower path yet (towers have no attack until M4).
  **Update 1415: BUG-0098 and the loop / name items of BUG-0099 are fixed (M3-6); the "an upgrade that reaches no unit
  loads" item stays an S4 note. Update 1715: that last item is fixed too (M3-H2): such an upgrade is now a load error.**
- **To change it:** numbers and text in `game/data/common/techs.json` (shared) and
  `game/data/factions/<faction>/techs.json` (the faction upgrade); which units an upgrade reaches is the `appliesTo`
  part of each effect (attack type, unit tags, unit ids, siege yes / no); the rules above by inbox note. A data edit
  also regenerates the replay fingerprint (the studio does that).

### You can build a base in the window: a command card with grid hotkeys, B / V build menus, a green / red placement ghost, click a site to cancel it, right-click to repair (view track, M3-V2, 2026-10-07)

- **What was built:** the second half of M3's HUD. **(1) A command card** bottom right: a 5 x 3 grid of buttons on the
  keys Q W E R T / A S D F G / Z X C V B. With soldiers selected the middle row shows **Attack (A), Stop (S), Hold (H)
  and Move (M, new: a plain move that ignores enemies)**; with workers selected also **Build (B)** and **Build
  Advanced (V)** bottom right. Pressing a button does exactly what its key does. **(2) Build menus:** B lists the six
  Age I buildings (Billet, Quartermaster's Depot, Legion Barracks, Crossbow Range, Wickan Corral, Armory for the
  Malazans) on Q W E R T A, V the three Age II ones (Cadre Tower, Engineers' Yard, Watchtower); each shows its name,
  key and gold / wood cost, with the description in a tooltip; Esc or a right-click closes. **(3) The placement
  ghost:** picking a building shows a see-through box the size of its footprint under the cursor, **green** where the
  rules allow it and **red** where they don't, with the reason in words above it ("Blocked", "Would wall ground off",
  "Can't afford", "Units in the way" ...). A left click on green sends every selected worker to build it (the first
  places the site, the rest join); hold Shift to place several in a row (the workers build them in order); a click on
  red does nothing. **(4) Click a building** to select it (your own only; a box-select never grabs a building): a green
  outline shows round its footprint, and if it is a construction site the card shows **Cancel (B)**, which refunds the
  unbuilt share. **(5) Right-click with workers selected** on your own damaged building sends them to **repair** it,
  on your own construction site to **join** building it, on a tree or mine to gather (as before), anywhere else a move.
- **Try it (five minutes):** `& $env:GODOT --path game`. Box-select the five grey workers west of centre: the card
  appears bottom right with Build (B). Press **B**: six buildings on Q W E R T A. Press **Q** (Billet) and move the
  mouse: a green box follows it on open ground, red with "Blocked" over the Town Hall or a tree. "Can't afford" shows
  once a building costs more than you have (you start with 200 / 200: place an Armory at 100 / 100, then try a Wickan
  Corral at 75 / 150 and it is red for wood). Click on green: the workers walk over, a slate site rises with a yellow
  bar, and about 20 s later it is a Billet. Click the site while it builds: a green outline, the card shows **Cancel**;
  press **B** to cancel and get the unbuilt share back. Press **V** to see the Age II menu (every entry is still
  allowed today, since nothing is locked until the next sim task). Press **M**, click somewhere: a plain move. Press
  F12: the label reads "sel building" while a building is selected.
- **Producer decisions, revisit any time:**
  - *The card's words come from a small view-only text file* (`game/data/common/ui.json`: "Move", "Stop", "Hold",
    "Build", "Cancel", the refusal reasons, and which building kinds each menu lists), so no label lives in code and
    the pre-release rename pass stays data-only. If the file misses a word the game logs an error and runs without
    the card rather than showing a blank button.
  - *Layout (builder's choice, accepted):* unit commands on the middle row (A S D F = Attack Stop Hold Move), Build
    advanced / basic on V / B, a site's Cancel on B, the top row left empty for abilities (M4). Hotkey hints read your
    actual key bindings, so a rebind relabels the buttons.
  - *The ghost centres the footprint on the cursor's square* (within half a square for even sizes) and stops at the
    map edge rather than hanging off it.
  - *The first placement is immediate, later Shift placements are queued behind it*, so the workers build the sites
    in the order you placed them. Alternative: every placement immediate (the workers would split).
  - *A click on red does nothing and plays no sound* (the reason text is the feedback).
  - *Size: ~1,250 game-code lines (card, ghost, selection, text loader, outline), ~160 view-helper lines, ~930 dev test
    lines, ~720 QA test lines; over the 1,500 budget on game code by the card's button plumbing; accepted.*
- **Rough edges (S3, all three fixed in the next view task, M3-V3, which touches the same code). Update 1415: BUG-0108
  / 0109 / 0110 are fixed (M3-V3 entry at the top); BUG-0122 stays for the view's clean-up session.**
  - BUG-0108: a right-click on the **top face** of a building's box is read as a click on the ground *behind* it (the
    camera looks down at 55°, so the far half of a 3 m-tall box's top lands about 2 m behind the footprint). On a
    damaged Town Hall, 39 of 81 top pixels would send workers to walk behind it instead of repairing. You can't see
    it yet: nothing damages a building before M4 combat, and sites are joined by clicking the ghost or the ground
    inside the footprint. Fix: pick the box first, then the ground.
  - BUG-0109: a placement click builds where the ghost was **drawn last frame**, not exactly under the click; at 60 fps
    that is where you saw the box, so in play it looks right, but a very fast flick-and-click can land one square off.
  - BUG-0110: a hand-broken `ui.json` whose top level is not an object loads as blank labels without an error (the
    shipped file is fine).
  - BUG-0122 (S4, view clean-up session): "Quartermaster's Depot" wraps mid-word on its button; 25 Shift-clicks on one
    spot send 75 build orders; the ghost trails a panning camera by one frame; the reason text is small (about 9 px at
    30 m zoom) and the red reads orange over grass. **Update 1715: fixed (M3-V3b entry at the top).**
- **To change it:** words and menu lists in `game/data/common/ui.json`; key bindings in `game/project.godot`
  (`card_0` ... `card_14`, `order_move`, `build_basic`, `build_advanced`); button size and the ghost's colours are
  constants in `game/scripts/CommandCard.cs` / `BuildGhost.cs` until the M6 art pass; the rest by inbox note.

### Buildings train units now: five-deep queues, rally points, population and a cap, full refunds (sim track, M3-4, 2026-10-07)

- **What was built:** the rules for training. A finished building takes orders for the units it trains (the
  Legion Barracks trains Heavy Infantry, the Garrison Keep trains Laborers, and so on, from each unit's "trained
  at" in the data): up to **five** in its queue, each **paid when you queue it** and **refunded in full** if you
  cancel it (or the building is destroyed). The first item trains for its train time (a 14 s Heavy Infantry
  takes exactly 280 ticks) and the unit appears on the nearest free square next to the building, then walks to
  the building's **rally point** if one is set; a worker rallied onto a gold mine or a tree starts gathering it
  by itself (the Age of Empires rule). **Population:** every unit counts its pop (half-steps allowed: a Lancer
  is 2, a Laborer 1), a Town Hall gives 10, a House 8, the hard cap is 100 (all from the data). An item only
  *starts* training when there is room; if the cap is full the queue waits, and it resumes the moment a House
  finishes. Nothing ever dies from losing a House: training just pauses. A unit with an Age II requirement
  (the Sapper, the Zealot) can't be trained until Age II exists (next sim task). Two spawned units never share
  a square; if there is no free square the unit waits inside until one opens. **Update 1131: Age II exists (the
  M3-5 entry at the top); the Sapper and Zealot stay locked until the next sim task (M3-6) makes "needs Age II"
  a real check instead of "has any requirement". Update 1415: done (M3-6 entry at the top): they unlock the tick
  after Age II completes.**
- **What you'll see:** nothing in the window yet: the production card (click a Barracks, pick a unit, see the
  queue and progress) and the rally marker are the view track's M3-V3, two view sessions away. Today the
  command-line tool shows the pop count: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 50 --workers 5 --forests 12 --mines 8 --ticks 600`
  ends each player's line with `pop 21/10` (the 50 debug units are spawned past the cap and still count,
  which is the rule for dev spawns).
- **Numbers:** 500 marching units + 20 Town Halls training non-stop + 50 gathering workers cost 0.40 ms a
  tick (budget 1.3, hard budget 4). QA's hostile run (8 seeds x 3,000 ticks of random train / cancel / rally /
  build / cancel / destroy orders on two players) balanced gold and wood to the exact coin every tick, and two
  identical runs stayed identical every tick.
- **Producer decisions, revisit any time:**
  - *A House finished by builders lets a waiting queue resume one tick (50 ms) later than one placed by a
    dev command.* Training runs before construction each tick, as the design doc's tick order says. The
    alternative (construction first) was not worth a reorder for one tick.
  - *The population numbers are recomputed from the units and queues, not stored in the replay fingerprint*
    (like a unit's speed, they follow from other stored facts). The fuzz tests recount them every tick.
  - *A rally order from the sim's side names the building by its map square*, so the rally target keeps its
    exact position.
  - *The queue holds five items* (a rule, not a stat: a code constant). The alternative, a data field per
    building, can come later if a design wants a longer queue somewhere.
  - *Size: ~950 code lines (budget 1,500), ~1,250 dev test lines, ~1,000 QA test lines, ~95 doc lines.*
- **Rough edges (S3, next sim clean-up session after three more feature sessions):** BUG-0097: if every
  square of a building's plateau is taken (by hundreds of units, a stress case), a new unit is set down on
  *another* plateau of the same height, up to 30 m away, instead of waiting; the same holds for units pushed
  out of a new building's footprint (that part predates this session). Fix: bound the search by the
  building's own connected plateau. BUG-0095 (from M3-H1) is half fixed: the cost is gone (9 / 35 ms → 0.08 /
  0.19 ms), the "leftovers stack on one point" part remains. **Update 1715: both fixed (M3-H2 entry near the top): a
  spawn on a packed plateau waits, and pushed-out units spread over their own plateau.**
- **To change it:** unit costs, pop, train times and "trained at" in `game/data/factions/<faction>/units.json`;
  pop provided in `buildings.json`; the hard cap in `game/data/common/rules.json` (`popCap`); the rally-on-a-mine
  rule, the queue length and the tick order by inbox note.

### The economy is on screen: a Town Hall and five workers at the start, a resource bar, right-click to gather, and buildings and workers show what they are doing (view track, M3-V1, 2026-10-07)

- **What was built:** the first half of M3's HUD. **(1)** The default match now starts each player with a
  finished Town Hall beside their army (on the nearest open 4 x 4 spot with a clear ring round it, on the
  army's own side of the map) and **five workers** next to it, standing on the side facing the nearest gold
  mine so the first trip is short. `-- --workers 12` changes the number (0-200); `-- --no-bases` gives the old
  armies-only match for tests. **(2)** A **resource bar** top right reads "Gold 200  Wood 200" (the names come
  from each faction's data file, not from code) and updates the frame a total changes. **(3) Right-click on a
  tree or a mine** with workers selected sends them gathering (the Command sound plays); any soldiers in the
  same selection just walk there; Shift queues it after the current order; right-click on plain ground is a
  move as before, and the minimap's right-click stays a move. **(4) Buildings** are drawn: a box in the
  player's colour on its footprint, 3 m tall; a construction site is a lighter slate box that **rises with its
  progress** under a yellow bar; a damaged building shows a green hit-point bar. **(5) Workers** are tinted
  green while gathering, amber while waiting with a load, blue while building, and carry a small gold or brown
  cube above their head while loaded. F12's second line adds "workers gathering N, returning N, building N".
- **Try it (three minutes):** `& $env:GODOT --path game`. Both armies now have a slate (Malazan) or orange
  (Whirlwind) Town Hall box beside them with five small workers. Box-select the five grey workers west of
  centre, right-click the nearest gold mine (dark block with a gold top): they turn green at the mine, carry a
  gold cube home, and the bar's gold ticks up by 10 per trip (about every 30 s per worker at first). Right-click
  a forest: the same with wood and brown cubes. Box-select workers *and* soldiers together and right-click a
  mine: the workers gather, the soldiers walk there. Press F12 to see the worker counts. Nothing can be
  *built* yet: that is the next view task (build menu and ghost preview).
- **Producer decisions, revisit any time:**
  - *Where the Town Hall goes* (nearest clear 4 x 4 spot beside the army, never between the two armies) and
    *where the workers stand* (mine-facing side) are debug placements until real start locations exist (M6).
  - *Criterion wording:* my brief said "all five workers reach Gathering within 60 ticks (3 s)" of the click;
    that only holds for a node within 12 m. On seed 1 the mine is 10 m away (tick 23) and the nearest tree
    12.7 m (tick 123). The gather order itself is instant; the walk is the sim's walking speed. I reworded the
    rule to "within 60 ticks of arriving" and closed the finding (BUG-0106) as not a defect. Alternative:
    place the Town Hall nearer a forest; not worth it before real start locations.
  - *A right-click on a node with no worker selected is a plain move* (soldiers don't gather).
  - *Both the site's rising box (the builder's idea) and the dev-only `--no-bases` flag were accepted.*
  - *Size: ~580 game-code lines, ~360 view-helper lines, ~600 dev test lines, ~1,100 QA test lines.*
- **Rough edges (BUG-0107, S4, next view clean-up session):** a Malazan construction site at 100 % is the same
  dark slate hue as a finished Malazan building (the bar is what tells them apart); the wood cube is only 3-4
  px tall when zoomed all the way out at 720p; placing or cancelling a building redraws the minimap's resource
  layer and the trees for no reason (cheap, but every build will trigger it from M3-V2). **Update 1715: fixed
  (M3-V3b entry at the top): mauve sites, zoom-scaled cargo cubes, the minimap redraws only on a felled tree; the
  trees' relist on a building change is the one leftover (BUG-0126 item 3, S4).**
- **To change it:** worker count default is `startingWorkers` in `game/data/common/rules.json`; the resource
  names in `game/data/factions/<faction>/faction.json`; colours and sizes are placeholder constants in
  `game/scripts/BuildingViews.cs` / `UnitViews.cs` until the M6 art pass; the rest by inbox note.

### M2 is done: the game window is signed off, and its clean-up fixed the benchmark, the start positions, the quit leak and the minimap dots (view track, M2-H2 + sign-off, 2026-10-07)

- **What M2 is:** everything you can see and touch today: a 3D terraced map with forests and gold mines,
  the camera, 200 placeholder soldiers that walk and turn smoothly, selection (click, box, Shift,
  double-click, Ctrl, groups 1-9, Tab), every order key (right-click, A, S, H, Shift-queue), the minimap
  with click-to-jump and right-click orders, the F12 developer overlay, select / command sounds, the
  `--screenshot` and `--bench` flags, and a measured 60 FPS with 10x headroom. Nine view sessions over
  three days, two of them clean-up sessions; 18 headless test scenes.
- **This session's clean-up (eight items), in plain words:**
  1. The benchmark's "order across the map" step **really marches across now** (about 109 m, to a spot
     85 % of the way to the far edge, or the opposite corner if that spot is a cliff or a forest) instead
     of 15 m to the enemy's doorstep (BUG-0101), and its `fps` is frames divided by seconds (BUG-0102).
     The 60 s numbers barely moved: 0.73 ms a frame at 100 units per player, 2.62 ms at 1,000 per player
     with the west army marching through the east one. QA checked the target on 200 map seeds.
  2. **Both armies start in a clearing**: no soldier next to a tree, a mine, a cliff edge or the map
     border, and each army on one height level (BUG-0085; seeds 1-40 checked at 100 and 1,000 per side).
  3. **Sounds stop when the game quits**, so the "instances leaked" warning is gone (10 of 10 runs clean;
     BUG-0087). A test row that could fail under heavy PC load now waits on the right clock (BUG-0088).
  4. **Minimap dots are a 2 x 2 coloured centre inside a one-cell rim**, so a lone scout reads in its
     player's colour (BUG-0069, my default; QA read the screen pixels of lone dots at the border, on a
     ramp and on a cliff lip).
  5. The F12 overlay's text line is rebuilt only when a number changes (a quiet frame allocates nothing,
     BUG-0083); cliff edges read crimson instead of olive, arrows no longer dip into steep ramps, the one
     compiler warning is gone (BUG-0084).
  6. Smaller: a felled tree re-uploads only the trees, not the mines (BUG-0086); `--bench` is capped at an
     hour and prints with a dot decimal on every PC (BUG-0103); four doc / test nits (BUG-0070).
  7. Two notes for the M6 release build are in the docs: keep the test scenes out of the shipped zip,
     and load the data files in a way that works from the packed game.
- **I signed M2 off myself** (your autopilot setting `stop_at_milestone_end: no`). Conditions held: all
  ten criteria verified, every M2 system covered by unit, fuzz and determinism tests (the sounds have no
  game-state side, so "determinism" doesn't apply there), no serious open bugs, clean-up session done.
  **Your playtest is still wanted, as feedback:** `& $env:GODOT --path game`, maximize the window,
  box-select the grey army west of centre (it stands in a clearing now), right-click far across the map
  past the orange army and watch the march through a ramp: smooth turning, no per-tick snapping. Click
  the minimap corners, scroll the wheel from 20 m to 60 m, press A and click, Shift + right-click three
  points, H, S. Zoom out (`-- --zoom 60`) and look at the minimap dots. Listen: a short blip on select, a
  rising two-note on an order. Then `& $env:GODOT --path game -- --bench 60 --vsync off` and read the
  `bench:` line (expect avg under 1 ms, fps over 1,000 on this PC). Write "M2 playable ok" in the inbox,
  or what felt wrong; a complaint becomes the view track's next task ahead of roadmap work.
- **Producer decisions, revisit any time:**
  - *Signed off on measurements, with your playtest as feedback rather than a gate* (the alternative,
    holding the view track until you play, would idle it; flip the autopilot setting to `yes` if you'd
    rather gate).
  - *The 2 x 2 dot* over the two alternatives (screen-pixel dots, or a rim only round a crowd's outside);
    both remain yours to pick by inbox note.
  - *The benchmark's pass mark is "the army centre moves at least 20 m in 10 s"* (seed 1 moves 22 m; the
    march runs through the idle enemy army, which halves its pace after a few seconds). QA found that
    seed 21 moves 19.1 m (BUG-0104), so the bound is fitted to seed 1, not the map family; the benchmark
    runs seeds 1 / 6 / 31 by default, so nothing fails today.
  - *At 1,000 units per side the start block may wrap round a mine or a forest strip* (every soldier is
    still clear of them); that is what the clearing rule allows.
  - *Size: ~230 game-code lines, ~330 view-helper lines, ~450 dev test lines, ~660 QA test lines.*
- **Rough edges (next view clean-up session, after four feature sessions):** BUG-0104 (S3) above; BUG-0105
  (S4): the minimap refresh test now runs at 92-94 % of its 0.3 ms limit (it could fail under load; the
  2 x 2 dot cost the margin) and two doc figures are stale. **Update 1715: both fixed (M3-V3b entry at the top).**
- **What's next (view track, M3):** a resource bar, right-click a tree or mine to gather, worker and
  construction feedback, then the build menu with a ghost preview, then the production queue UI.

### Sim clean-up: no patch of ground can ever be walled off, refused build orders are cheap, eight bugs closed (sim track, M3-H1, 2026-10-07)

- **What was built (clean-up, no new features):**
  1. **A cancelled or destroyed building can't leave a hole nobody can reach.** When a building goes
     (cancelled now, destroyed in M4 combat) and its squares touch open ground, they open as before. If
     other buildings or trees surround them, the squares stay blocked (a "pocket") until something next to
     them opens, and then the whole chain opens at once. So the "worker walks to the wall for ever" case
     (BUG-0093, the last trace of BUG-0078) is gone, and "every open square reaches every other square" is
     now true at every moment of a match. QA ran 30,000 random build / cancel / destroy / fell steps on six
     maps against an independent "can everyone reach everyone" checker: no violation, and two identical
     runs stayed identical every tick.
  2. **A refused build order is cheap.** "Can't afford it", "a unit is in the way" and "the building list
     is full" are checked before the expensive "would this wall ground off" test: 100 refused orders in one
     tick went from 22 ms to 0.07 ms (BUG-0091).
  3. **Units set down outside a new building never share a square** (the search keeps going ring by ring:
     400 soldiers stacked where a Town Hall goes land on 400 different squares in 2.5 ms); the push-out
     finds nearby units through the fast lookup grid (0.3 ms instead of 7 ms with 400 units around); the
     view will never draw a pushed unit sliding through the building; a worker holding position can build
     on its own spot; a hand-edited repair factor too small to do anything is refused (BUG-0092).
  4. Small ones: an empty resources file, or one without a tree or a mine type, is refused (BUG-0076); the
     command-line tool refuses a bad `--record` file name before the run instead of after (BUG-0072); a
     one-in-a-million "is this a wall?" answer no longer depends on which unit asked first (BUG-0071); a
     docs sentence (BUG-0079).
- **What you'll see:** nothing new in the window; these are safety and speed fixes under the hood that the
  M3 build menu (view track, next sessions) will rely on. Numbers: 500 marchers + 50 builders + a tree
  felled every tick still cost 1.19 ms a tick (budget 4); the replay fingerprint did not change (no unit
  moved differently).
- **Producer decisions, revisit any time:**
  - *Blocked pockets, rather than "an unreachable tree is not a target".* The alternative (teach workers to
    check reachability per target) fixes one symptom; the pocket rule keeps the whole-map promise that
    combat and the AI can rely on later.
  - *BUG-0080 stays as a documented limit.* If a player (or the M5 AI) places a building on **every single
    tick**, armies ordered more recently stand still while that lasts (a building every 2 ticks: only the 4
    oldest groups walk; every 4 ticks with 8 groups: all walk, the youngest after 3 ticks). The preferred
    fix (keep following the old route maps and rely on the per-step wall check) was not done: units pressed
    against a new building would give up after one second instead of waiting for the new map, and the
    current behaviour is pinned by tests on both tracks. The exact bound is in the docs with a test that
    measures it. Revisit when the AI's building rate is known (M5).
  - *Saved games (M6) will store the route maps themselves*, not only "which destinations were cached", so
    loading a save continues the game exactly as it would have run (BUG-0081). Alternative: refresh every
    map at save time in both runs, at the cost of a few metres of difference per unit. Owner may revisit
    at M6.
  - *The forest generator's whole-map double-check runs only in debug builds now* (the local check never
    disagreed with it across 246 maps); saves about 2 s of setup on the biggest maps in release.
  - *A `--record` run now empties an existing file of that name at start*, so a run that dies leaves an
    empty file where the old replay was. Accepted: don't record over a replay you want to keep.
  - *Size: ~290 code lines, ~540 dev test lines, ~1,100 QA test lines, ~120 doc lines (budget 1,500).*
- **Rough edges (S3, next sim clean-up session after four feature sessions):** BUG-0095: a building placed
  on a small plateau with fewer free squares than units standing in its footprint makes the search walk
  the whole map for each leftover unit (9 ms on the normal map, 35 ms on a 256 map) and the leftovers end
  up stacked on one point; a player would have to construct it. BUG-0096: a build order refused *because it
  would wall ground off* still pays the full check (100 such orders in one tick: 33 ms); the ghost preview
  will refuse those before an order is ever sent, so in play it needs a scripted flood of bad orders.
  BUG-0094 (S4): a test helper can build groves that wall cells in; test-only. **Update 1715: BUG-0095 and BUG-0096
  fixed (M3-H2 entry near the top); BUG-0094 stays for the next sim clean-up.**
- **To change it:** the pocket rule, the BUG-0080 call and the save-file decision by inbox note.

### You can place buildings and workers build them, in the rules (sim track, M3-3, 2026-10-06)

- **What was built:** the sim half of "building placement, construction with several builders, repair".
  A worker ordered to **build** a building at a spot pays the cost at once, a construction site appears
  (it is a wall to walkers from that moment), the worker walks to its edge and works; more workers
  ordered onto the same site join it. The design doc's build-time rule is exact: with n builders a
  building takes `t x 3 / (n + 2)` of its one-builder time, so a 20-second Billet takes 20 s alone,
  15 s with two, 10 s with four, 6 s with eight. A site's hit points grow with its progress. **Cancel**
  a site and the unbuilt share of the cost comes back (rounded down); **repair** a damaged building and
  workers restore it at half the build rate for a quarter of its cost, scaled by the damage, stopping
  when you can't pay. A building at 0 hit points disappears (combat will use that in M4). The game also
  answers "may this go here?" for the ghost preview the view will draw: not off the map, not on a cliff,
  ramp, tree, mine or other building, not on another height level, **not sealing off any ground**, no
  enemy or holding unit inside, affordable, and room in the building list. Your own idle units standing
  in the footprint are set down on the nearest free spot outside it (the RTS norm).
- **What you'll see:** nothing in the window yet. The build menu and the ghost preview are the view
  track's M3 work (after the M2 clean-up session). Today it runs in tests only: the QA suite places
  thousands of buildings on generated maps against an independent "did this wall anything off" checker,
  and every verdict agreed.
- **Numbers:** 500 marching units plus 50 builders on 10 sites cost 0.98 ms a tick (budget 4); a tree
  felled every tick on top: 1.17 ms. One placement check costs 0.0013 ms on average, 0.05 ms at worst on
  the default map.
- **Producer decisions, revisit any time:**
  - *A building may never wall ground off.* Age of Empires lets you wall; this game's design doc rules
    it out so no unit or worker can ever be stranded. The check keeps whatever regions already exist
    (so it still works after a pocket appears, see the rough edge). Alternative: allow walling and teach
    workers to ignore unreachable trees.
  - *Player 1 is Malazan, player 2 Whirlwind* (player p plays faction p) until the M6 lobby lets you pick.
  - *Idle own units in the footprint are pushed out; holding units block the placement* (H means "stay").
  - *Repair stops the moment you have 0 of any resource the building costs*, rather than running up a debt.
  - *Push-out ring 1 is the eight-neighbour ring; after eight rings the nearest passable cell.*
  - *The "+ 2" and "x 3" of the build-time formula are code constants*, since they are the rule itself,
    not a balance number; the repair factors (0.5 / 0.25) are in `game/data/common/rules.json`.
  - *Size: ~785 code lines (budget 1,500), ~990 dev test lines (budget ~1,000), ~1,800 QA test lines.*
- **Rough edges (S3, the sim clean-up session is next):**
  - BUG-0093: if you build a site, surround it with three Billets and then cancel the site, its cells
    reopen as a pocket nobody can reach; a worker sent to a tree touching only that pocket would walk to
    the wall for ever (the old BUG-0078 symptom, now needing a deliberate sequence of player orders).
    Fix planned next session: reopened cells that reach nothing stay blocked until a neighbour opens.
  - BUG-0091: a *refused* build order at a spot far round a wall runs the full "does this seal ground"
    check before the cheap checks (can't afford, unit in the way), so 100 refused orders in one tick cost
    22 ms. The fix is to run the cheap checks first; one session.
  - BUG-0092 (S4): a hand-edited repair factor below one 100,000th loads but does nothing; a worker on
    Hold can't build on its own spot; past eight full rings pushed units share a cell; push-out is slow
    with 400 units around a Town Hall (7 ms once).
  - **Update 0800: BUG-0093, BUG-0091 and BUG-0092 are fixed (see the M3-H1 entry at the top); two residuals
    stay (BUG-0095 / 0096, S3, both perf corner cases, next sim clean-up session).**
- **To change it:** repair factors in `game/data/common/rules.json` (`repair.rateFactor`, `costFactor`);
  the never-seal rule, push-out and the "player p plays faction p" rule by inbox note.

### The 60 FPS check is in: a built-in benchmark, smooth turning, and your playtest is the last word (view track, M2-7, 2026-10-06)

- **What was built:** the studio's half of M2's last criterion ("the owner moves an army of 100 units
  around a generated map at 60 FPS"). The game got a benchmark flag: `& $env:GODOT --path game -- --bench 60`
  plays a scripted loop for 60 s (box-select the army, order it, click the four minimap corners, zoom to
  20 m and 60 m, A + click, three Shift-queued moves, H, S, repeating every 10 s), prints one line of frame
  times and quits. `--vsync off` lets it measure the raw frame cost. Units now **turn smoothly** between
  ticks instead of snapping to a new facing 20 times a second. Four screenshots were taken and looked at
  (overview, a ramp crossing with nine units on the ramp, the minimap corner, the F12 overlay).
- **The numbers on this PC** (windowed, HUD and sound on, default map with forests and mines, 100 units
  per side, 60 s): one frame costs **0.72 ms on average and 1.31 ms at the 99th percentile** with vsync
  off, against the 16.7 ms a 60 FPS frame allows (about 5 % of the budget); with vsync on it holds the
  monitor's rate exactly (59.5 fps on a 60 Hz display, 118.5 on 120 Hz). 1,000 units per side zoomed
  out: 2.3 ms a frame. A test scene pins "under 16.7 ms average, under 33 ms worst 1 %" so a regression
  fails the suite.
- **Please play it (five minutes), this is the owner's half of the criterion:** `& $env:GODOT --path game`,
  maximize the window, box-select the grey army west of centre, right-click far across the map past the
  orange army and watch the march through a ramp: the turning should be smooth, no per-tick snapping.
  Click the minimap corners, scroll the wheel from 20 m to 60 m, press A and click, Shift + right-click
  three points, H, S. The FPS in the top-left label should sit at your monitor's refresh rate with no
  hitches. Then `& $env:GODOT --path game -- --bench 60` and read the `bench:` line. Write "M2 playable
  ok" (or what felt wrong) in the inbox; I will sign M2 off after its clean-up session either way unless
  you object.
- **Producer decisions, revisit any time:**
  - *I ticked the criterion on the measured numbers* even though the benchmark's "order across the map"
    step only sends the army about 15 m (BUG-0101, below): the frame budget has 10x headroom and a long
    march adds only sim time that is already measured elsewhere. The benchmark will be fixed to march
    properly in the next view session so it measures what you will do.
  - *The first 30 frames after the armies appear are not timed* (the first draw of new unit shapes is a
    one-time 85 ms hitch, load time rather than play).
  - *Benchmark steps go through the real selection and order code*, not shortcuts, so a bug in a key
    handler shows up in the numbers.
- **Rough edges (view clean-up session, next):** BUG-0101 (S3): the benchmark's "order across" step
  targets the enemy's start spot 15 m away, so the army barely moves. BUG-0102 (S3): the `fps` figure on
  the benchmark line reads low on short runs (it averages the engine's once-a-second counter, which
  includes the loading second; 60 s runs are off by 1-2 %). BUG-0103 (S4): with vsync on the frame times
  print as perfectly even (the engine smooths them), two log lines use the PC's decimal comma, an absurd
  `--bench 1e308` never ends. Also seen: on the default seed the grey army's start block straddles a cliff
  edge (goes with BUG-0085's "inside a forest"). **Update 0800: BUG-0101 / 0102 / 0103 fixed and the start
  block stands in a clearing (BUG-0085); see the M2 sign-off entry at the top. The benchmark now marches the
  army about 109 m across the map and its `fps` is frames divided by seconds.**
- **To change it:** the step list and timings are the table at the top of `sim/Rts.Sim/ViewApi/BenchScript.cs`;
  the warm-up count `BenchRunner.WarmUpFrames`; the rest by inbox note.

### The faction pages are now the design source for every unit and building number (data track, D2, 2026-10-06)

- **What was built:** the second data task. (1) Both faction pages (`docs/factions/malazan.md`,
  `whirlwind.md`) got full building tables: hit points, armor, cost, build time, footprint, what the
  building provides, and what it requires, next to the names and ids they already had. (2) Tests now pin
  every unit's numbers to its faction page's Units table (hit points, armor, attack, cooldown, range,
  speed, sight, cost, population, train time, where it trains), typed from the page, so changing a number
  in the data or on the page alone fails a test. (3) Tests also read the pages themselves and compare.
- **What changed in the data: nothing.** All 14 units already matched their pages, so no name, number or
  description moved, and no replay fingerprint was regenerated. There is nothing to approve this time; the
  table of changes is empty by design.
- **The new page columns** (one row example, Malazan Billet): HP 500 · Armor 3 · Cost 0 / 50 · Build 20 s ·
  Footprint 2×2 · Provides "+8 pop" · Requires "—". The Watch Towers read "Attack 10 pierce / 2 s, range 18;
  sight 24; detector 16 m" straight from the design doc; those tower numbers are not in the data yet (M4).
- **Producer decisions, revisit any time:** melee range is written as `0.5` in the data (edge to edge) and
  as "melee" on the pages; the pin treats them as equal. Radius and wind-up (which the pages don't give)
  are pinned as shipped (my M1-2 values).
- **Rough edges:** BUG-0111 (S4): a few page edits slip past the tests (a false "+N pop" claim on a
  building that gives none, the Forge / tower "Provides" wording, a decimal-comma PC formatting a range);
  fixed in the next data task that touches those tests. **Update 1415: fixed in D3 (held one session, see the
  D3 note near the top).**
- **To change it:** edit the page and the data together (the tests tell you which side disagrees), or
  write the change in the inbox and the data track does both.

### Every building of both factions is in the data now: names, numbers and descriptions for you to check (data track, D1, 2026-10-06)

- **What was built:** the first data-track task. Each faction's building file grew from one entry
  (the Town Hall) to all ten. Names and ids come from the faction pages, every number from the design
  doc's building table (identical for both factions, as the doc says), and each building got a
  one-sentence description in its faction's voice. The Town Hall entries did not change. Tests pin
  the roster to the doc (change a number in the doc without the data, or the other way round, and a
  test fails), and the replay fingerprint was regenerated once with every movement checkpoint
  byte-identical (proof that no unit stat moved).
- **What you'll see:** nothing yet: nobody can place a building until M3-3 (sim) and the build menu
  (view). The data is loaded at every start, so a typo would already fail the boot gate.
- **The change table** (every row is a new entry; "old" was "absent"; one row covers both factions
  because the numbers are the same by design):

  | Slot | Malazan (id) | Whirlwind (id) | HP / armor | Gold / wood | Time | Size | Does |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | House | Billet (`malazan_billet`) | Tent (`whirlwind_tent`) | 500 / 3 | 0 / 50 | 20 s | 2 x 2 | +8 population |
  | Camp | Quartermaster's Depot (`malazan_depot`) | Supply Cache (`whirlwind_supply_cache`) | 600 / 3 | 0 / 75 | 25 s | 2 x 2 | drop-off for gold and wood |
  | Infantry Hall | Legion Barracks (`malazan_barracks`) | Raider Camp (`whirlwind_raider_camp`) | 1200 / 4 | 0 / 150 | 40 s | 3 x 3 | trains the line unit (and the Zealot) |
  | Ranged Hall | Crossbow Range (`malazan_crossbow_range`) | Archer Camp (`whirlwind_archer_camp`) | 1200 / 4 | 0 / 150 | 40 s | 3 x 3 | trains the ranged unit |
  | Shock Hall | Wickan Corral (`malazan_wickan_corral`) | Horse Lines (`whirlwind_horse_lines`) | 1200 / 4 | 75 / 150 | 45 s | 3 x 3 | trains cavalry; needs the Infantry Hall |
  | Forge | Armory (`malazan_armory`) | Smithy (`whirlwind_smithy`) | 1000 / 4 | 100 / 100 | 40 s | 3 x 3 | upgrades (and the faction upgrade) |
  | Caster Hall | Cadre Tower (`malazan_cadre_tower`) | Shrine of the Whirlwind (`whirlwind_shrine`) | 1200 / 4 | 150 / 150 | 50 s | 3 x 3 | trains the caster; Age II |
  | Siege Works | Engineers' Yard (`malazan_engineers_yard`) | Ram Yard (`whirlwind_ram_yard`) | 1400 / 4 | 150 / 200 | 55 s | 3 x 3 | trains siege (and the Sapper); Age II |
  | Watch Tower | Watchtower (`malazan_watchtower`) | Lookout Tower (`whirlwind_lookout_tower`) | 800 / 5 | 50 / 125 | 35 s | 2 x 2 | Age II; its attack, sight and detection are not in the data yet |

  Why these numbers: they are the design doc's table (docs/02 "Buildings"), copied, not tuned.
- **The new player-facing text, quoted** (Malazan, then Whirlwind):
  - Billet: "Bunks, a roof and a cookpot for another squad, raising your population cap by 8."
  - Quartermaster's Depot: "A forward store where Laborers drop off gold and wood, so they spend less time walking and more time working."
  - Legion Barracks: "Trains Heavy Infantry, the shield wall every Malazan legion is built around."
  - Crossbow Range: "Trains the Crossbowman, who does the killing from behind the shield wall."
  - Wickan Corral: "Trains the Wickan Lancer, fast horsemen for flanks and raids; needs a Legion Barracks."
  - Armory: "Researches weapon and armor upgrades for the legions, and Moranth Supply once you reach Age II."
  - Cadre Tower: "Trains the Cadre Mage, the sorcerer attached to every Malazan army; needs Age II."
  - Engineers' Yard: "Trains the Catapult and the Sapper, who blow up the enemy and occasionally themselves; needs Age II."
  - Watchtower: "A manned tower that watches the approaches, spots hidden enemies and shoots at intruders; needs Age II."
  - Tent: "Shelter from the sun for more of the faithful, raising your population cap by 8."
  - Supply Cache: "A hidden store in the sand where Camp Followers drop off gold and wood far from the Holy Camp."
  - Raider Camp: "Trains the Raider and, from Age II, the Zealot: the endless blades of the Apocalypse."
  - Archer Camp: "Trains the Desert Archer, quick on foot and quicker with the bow."
  - Horse Lines: "Trains the Horse Raider to strike enemy worker lines and vanish into the desert; needs a Raider Camp."
  - Smithy: "Researches weapon and armor upgrades for the faithful, and Dryjhna's Prophecy once you reach Age II."
  - Shrine of the Whirlwind: "Trains the Priest of the Whirlwind, who carries the goddess's storm into battle; needs Age II."
  - Ram Yard: "Builds the Battering Ram, which breaks enemy buildings under cover of the swarm; needs Age II."
  - Lookout Tower: "A tall lookout that watches the sands, spots hidden enemies and shoots at intruders; needs Age II."
- **Producer decisions, revisit any time:**
  - *Requirements ("needs Age II", "needs a Legion Barracks") live only in the text for now.* The data
    format has no "requires" field yet; it comes with the Age II task (M3-5 / M3-6), and the text will
    be checked against it then (BUG-0090, S4). Same for the towers' attack and detection (M4). **Update
    1415: the field exists (M3-5), the game enforces it (M3-6), and D3 sets the values from the faction
    pages with a test that every "needs ..." sentence has one (held one session; the towers part of
    BUG-0090 stays for M4).**
  - *The faction upgrade is described as researched at the Forge* (the design doc lists it there);
    where research happens is settled when techs get their data format.
  - *One test rule was relaxed:* a QA test that pinned "an empty Malazan building list leaves exactly
    one building" could not survive ten Whirlwind buildings; it now pins "exactly Whirlwind's ten".
- **Rough edges:** BUG-0090 (S4) above; also a stale code comment saying only the Town Hall ships
  (next sim change in that folder fixes it). **Update 2114: the comment is fixed (M3-3); the stats
  tables you see above are now also on the faction pages (D2 entry at the top).**
- **To change it:** write the tweak in the inbox ("call the Billet a Barracks Annex", "Tent costs 40
  wood", "shorter Smithy text") and the data track does it next session (it regenerates the replay
  fingerprint). Or edit `game/data/factions/<faction>/buildings.json` yourself and run
  `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~Content` to see what the tests pin.

### The game makes a sound when you select and when you give an order (view track, M2-6, 2026-10-06)

- **What was built:** the audio plumbing, with two placeholder sounds generated by the game itself at
  start-up (no sound files, nothing downloaded): a short high **blip** when a click, a box, a
  double-click or a group recall changes your selection to something non-empty, and a rising
  **two-note confirm** when an order goes out (right-click, Shift + right-click, S, H, A + click, a
  minimap right-click). A box over 100 units is one blip; 50 clicks in one frame are one confirm; the
  same sound never repeats within 50 ms, so click spam doesn't buzz. `-- --mute` silences everything
  (for tests and for you). Later sounds (build complete, attack, alert) are one line each in the
  same table. This completes the M2 criterion "Placeholder audio for select and command" (9 of 10).
- **Try it, and please listen:** `& $env:GODOT --path game`. Box-select the blue army (blip),
  right-click across the map (two-note), press Tab (nothing), press 1 twice (one blip, the second
  press jumps the camera and is silent). Nobody in the studio could listen in an unattended session:
  QA checked the waveforms (right pitch, no clicks, silent edges) but **your ears are the last check**.
  If the blip is too sharp or the volume wrong, say so in the inbox; the volume is one constant
  (`Sfx.SfxVolumeDb`, -6 dB) until the M6 settings screen.
- **Producer decisions, revisit any time:**
  - *Generated tones instead of sound files*, so no download was needed (agents can't download); real
    SFX come with M6.
  - *Pressing a group digit when that group is already selected plays nothing* (that press is the
    camera double-tap). Alternative: a blip on every recall.
  - *Sounds play in headless test runs too* (Godot's dummy audio driver takes them silently), so the
    boot gate didn't need a special case.
- **Rough edges (M2 hardening session, after M2-7):** BUG-0087 (S3): a sound still playing when the
  game quits leaves a harmless "instances leaked" warning in the log about 2 runs in 5 (the players
  are never stopped at quit). BUG-0088 (S4, found by me, not caused by this task): one existing test
  scene's double-tap row can fail when the PC is heavily loaded, because it waits on game time while
  the double-tap window uses the wall clock. **Update 0800: both fixed (M2-H2): sounds stop when the game
  quits (10 of 10 runs clean), and the test row waits on the wall clock.**
- **To change it:** pitches and lengths are the note table at the top of `game/scripts/Sfx.cs`;
  volume `Sfx.SfxVolumeDb`; the 50 ms gap `Sfx.MinGapMs`; the rest by inbox note.

### A tree falling no longer stalls the army, and a building dropped in a column's path no longer makes it give up (sim track, M3-2b, 2026-10-06)

- **What was built:** the pathfinding now tells two kinds of map change apart. When ground **opens**
  (a tree is cut down, a building removed), every army's route map stays valid: nothing it points at
  has become a wall, it just might miss a new shortcut. Walkers keep walking it while the game
  refreshes those maps in the background at the usual two per tick (missing maps first, then the
  oldest stale ones). Before, every tree fall threw away every route map at once, so with workers
  chopping steadily most groups stood still until the chopping stopped (BUG-0073). When ground
  **closes** (a building is placed, a mine spawned), every map is thrown away as before, and every
  walker's "am I making progress?" memory is reset, so the longer way round the new wall counts as a
  fresh start instead of "stuck" (BUG-0077: half of a 16-unit column used to give up behind a dropped
  building; now 16 of 16 arrive). A unit standing on a freshly opened square (a tree cut from under
  it) waits a tick or two for its map to refresh instead of giving up. Also: the rules file now
  refuses a tree type bigger than one square (BUG-0074), and the route-map builder got twice as fast
  (0.7 → 0.37 ms per map) after QA's perf check failed the first round.
- **What you'll see:** nothing new on screen; it's a change in how the army behaves while the economy
  works. Once workers chop in the window (M3 HUD) and you can place buildings (M3-3), armies will
  march through a forest being cut without pausing, and a building dropped in front of a column makes
  it walk round, not stop. The numbers: 32 groups marching while a tree falls every single tick: longest
  pause 0 ticks (was 90+ of 100 ticks for 30 of 32 groups), average tick 0.40 ms (was 1.76).
- **Producer decisions, revisit any time:**
  - *Opening changes refresh lazily; closing changes still invalidate everything at once.* Simplest
    rule that is always safe. Its limit: a building placed on every single tick would starve all but
    two groups while it lasts (BUG-0080, S3; nothing in the game places that fast; revisit with M3-3).
  - *Kept my 0.5 ms target rather than relaxing it* when the first round came in at 0.81 ms; the fix
    made the map builder itself faster. Cost: the builder's inner loop is now written out eight times
    (one block per direction, ~150 lines, with the reason in a comment) because the tests run Debug
    builds where the compiler inlines nothing. Readability against 40 % of that budget; I accepted it.
    Alternative: fold it back and accept ~0.56 ms.
  - *Trees bigger than one square are refused* rather than teaching the forest generator about them
    (no such tree is planned).
  - *Save/load note for M6 (BUG-0081, S3):* a stale-but-valid route map now depends on the map as it
    was when built, so "save the keys, rebuild at load" would change the game by a few metres per unit
    on load. The docs now say so; the decision (save the stale maps, or refresh all at save) waits for M6.
  - *Size: ~540 code lines (budget 800), ~600 lines of dev tests (budget 600), ~1,400 of QA tests.*
- **Rough edges:** BUG-0080 and BUG-0081 above (S3, sim hardening in two sessions); the felling perf
  test has ~20 % headroom and fails when the whole suite runs at once (passes every time alone).
  **Update 0800: BUG-0081 is decided (the save file will store the route maps, see the M3-H1 entry at the
  top); BUG-0080 stays as a documented limit with its exact measured bound (the fix was judged riskier
  than the limit; details in that entry).**
- **To change it:** the build cap is `MovementConstants.MaxFieldBuildsPerTick` (2); the rest by inbox note.

### Workers gather gold and wood and carry it home by themselves (sim track, M3-2, 2026-10-06)

- **What was built:** the first thing the economy *does*. A worker ordered onto a gold mine or a
  tree walks to it, stands at its edge, fills up (10 gold in about 14 seconds, 10 wood in about 17,
  the rates from the rules file), carries the load to your nearest drop-off building, adds it to
  your gold or wood total, and walks back for more, with no further orders. When a tree is used
  up, the worker moves to the nearest tree within 20 m; when a mine runs dry it does the same with
  mines, and if there is nothing within 20 m it stops and keeps what it carries. Workers only ever
  cut trees they can stand next to, so a forest is eaten from the outside in and can never leave a
  hole nobody can reach (that closes BUG-0075 from last session). Crowded mines work: 20 workers
  on one mine all deliver (they spread round its eight sides and re-try a freer side every second);
  QA ran 80 on one mine and income kept rising. To have somewhere to deliver, the game now knows
  about **buildings**: the rules file gets a Town Hall for each faction (Malazan "Garrison Keep",
  Whirlwind "Holy Camp": 4 x 4 cells, 2,400 hp, 275 gold / 275 wood, 90 s, +10 pop, drop-off), a
  building store, and a developer command that places one (players can't build yet: that's M3-3).
  Buildings are walls to walkers. Your gold and wood totals start at 200 / 200 (rules file) and are
  part of the replay fingerprint, as is everything a worker carries.
- **What you'll see:** nothing in the window yet: there is no worker key and no resource bar (the
  view track's M3 work: resource bar first, then build menus). The headless tool shows it today:
  `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 50 --workers 10 --forests 12 --mines 8 --ticks 2000`
  prints a Town Hall + 10 workers per player gathering for 100 seconds and ends with
  `player 0 gold 250 wood 300` (from 200 / 200); run it twice and every fingerprint line matches.
- **Numbers:** one worker alone earns 60 gold per 2 minutes (walking included); five on one mine
  earn exactly 5x; 500 marching units plus 50 gathering workers cost 0.92 ms a tick (budget 4);
  200 workers gathering alone 0.26 ms.
- **Producer decisions, revisit any time:**
  - *Only the Town Hall is in the data for now*; the other nine building types per faction are the
    data track's first task (next session), so M3-3 (placing and constructing buildings) has them.
  - *A worker reaches a mine or drop-off from 1.25 m away* and *re-tries every second (20 ticks)*
    when it arrives behind others. Both are engine constants, not balance numbers.
  - *Drop-off = the nearest own drop-off building in a straight line*, chosen when the trip starts
    (the design doc's rule). Walking distance would be truer round cliffs; revisit with M3-3.
  - *Cargo rules:* a Move / Stop / Hold keeps what the worker carries (a later gather of the same
    kind continues from it); ordering a worker carrying wood onto gold **throws the wood away**
    (Age of Empires II's rule, the simplest). Alternative: deliver the old load first, then switch.
  - *A worker that fills up with no drop-off anywhere stops with the load*; building a drop-off
    later does not restart it (a new gather order does). Noted by QA as a "resume" you may want.
  - *A building placed on top of a unit is refused* (the dev command drops it); M3-3 will push
    units out instead, like most RTSs.
  - *Size: about 1,340 code lines against 1,500, tests 1,270 against 1,000.* Accepted.
- **Rough edges (S3, none reachable by a player before M3-3):**
  - BUG-0077: drop a building (dev command) straight across a marching column's path and half the
    column gives up instead of walking round (the "am I making progress" mark survives the detour).
    Fix is part of the next sim task (M3-2b), together with BUG-0073 (continuous tree-felling stalling
    walkers; measured this session at the real chopping rate: longest pause 1.3 s, so it is a comfort
    fix, not an emergency).
  - BUG-0078: if buildings wall off a patch of ground next to a tree, the tree still counts as
    "reachable" and a worker sent to it walks to the wall for ever. M3-3's placement rule (no building
    may wall ground off) makes it impossible; the docs sentence "unreachable targets can't happen"
    is stale until then. **Update 2114: done; BUG-0078 is closed by M3-3's placement rule (see the M3-3
    entry at the top); a residual pocket after a Cancel is BUG-0093.**
  - BUG-0079 (S4): a missing `cost` in a building file reports three errors instead of one;
    `--workers 0` without resources runs though the docs say it needs them.
  - **Update 1744: BUG-0073 and BUG-0077 are fixed (see the M3-2b entry at the top); the other nine
    buildings per faction landed (the D1 entry). Update 0800: BUG-0079 closed (M3-H1).**
- **To change it:** rates, carry and the 20 m search are in `game/data/common/rules.json`
  (`gatherRate`, `workerCarry`, `nodeSearchRadius`); the Town Hall numbers in
  `game/data/factions/<faction>/buildings.json`; reach / retry / cargo rules by inbox note.

### Forests and gold mines are on the map now, and the army walks round them (view track, M2-3b, 2026-10-06)

- **What was built:** the game window's map now has **12 forests and 8 gold mines** (placeholder
  looks: a tree is a dark-green cone on a brown trunk, 3.5 m tall, each turned a little differently;
  a mine is a dark slate block covering its 4 x 4 m with a gold block on top). The minimap paints
  forests dark green and mines gold under the unit dots, and the ground under a felled tree shows
  its true colour again (nothing fells trees in the window yet; it's wired so a tree disappears
  within a fifth of a second when the economy does). The F12 overlay shows resource cells green
  instead of red. This completes M2's "terrain mesh; trees as MultiMesh" criterion.
- **Try it:** `& $env:GODOT --path game`: forests on the plateaus, mines dotted round, the minimap
  shows both. Box-select the blue army and right-click the far side of a forest: they route round
  its edge. `-- --forests 3 --mines 2` for a sparse map, `-- --forests 0 --mines 0` for the old bare
  one, `-- --forests 64 --mines 64` for the densest the generator allows (about 1,700 trees).
  `-- --units 1000 --zoom 60` with the default forests still runs at ~400 fps with vsync off.
- **Producer decisions, revisit any time:**
  - *12 forests and 8 mines on the 128 x 128 map by default* (the view's launch defaults; the
    sim's own default stays 0 so old tests and replays keep their maps). Tell me if you want
    denser or sparser.
  - *Rocks and biome ground colours move to the M6 art pass* (rocks are decoration with no
    gameplay footprint; biome colours come with the real materials). The M2 criterion is ticked
    for the terrain mesh and the trees / mines; the note in the roadmap says so.
  - *A felled tree vanishes* (the list is compacted) rather than shrinking to nothing: the drawn
    count always equals the live count.
  - *Mines turn only in quarter turns, trees any of 256 angles*, so a block never pokes out of its
    footprint.
  - *Size: about 800 code lines (budget 800), tests 780 (budget 600).* Accepted.
- **Rough edges (view hardening session, after M2-6 / M2-7):**
  - BUG-0085 (S3): on the default seed, **your starting army stands partly inside a forest** (17 of
    100 soldiers touch a tree; 9 of 40 start blocks over seeds 1-20 do). Nobody is stuck, it just
    looks wrong and splits the army. My call: the view's debug start layout will skip cells next to
    trees (real start locations with clear bases come in M6). Alternative: the map generator keeps
    forests away from the start blocks (sim track).
  - BUG-0086 (S4): each relist re-uploads both prop buffers even if only one type changed; the
    minimap refresh has ~15 % headroom to its 0.3 ms limit.
  - **Update 0800: BUG-0085 and BUG-0086 fixed (M2-H2): both armies now start in a clearing (no tree, mine
    or cliff edge beside any soldier, the whole army on one level), and a felled tree re-uploads only the
    trees. The minimap refresh margin is now 6-8 % (BUG-0105, S4, a watch item).**
- **To change it:** defaults are `LaunchOptions.DefaultForests` / `DefaultMines` (view code, or an
  inbox note); colours and sizes are constants in `game/scripts/PropsView.cs` and
  `sim/Rts.Sim/ViewApi/MinimapRaster.cs` (placeholders until M6).

### The data track starts next session (studio, 2026-10-06)

- **What changed:** your note of 2026-10-06 is processed. STATE has a Data block in the Now table
  and a data feature queue; `studio/handoff.md` has a "## Data track" section with the first task.
  The only schema on `main` the data track can fill today is `buildings.json` (the unit rosters
  already hold all 7 slots per faction; techs, abilities and AI build orders need schemas from
  M3-5 / M4 / M5). So its first task is **the nine missing building types per faction** (names and
  ids from the faction pages, numbers from the design doc's building table, a one-line description
  each), with the change table and quoted text for you under For your review when it lands.
- **One thing to know:** a data change alters the data fingerprint, so the checked-in golden replay
  gets regenerated with it; the brief requires every movement checkpoint to stay byte-identical
  (proof that no unit stat moved). **Update 1744: landed; see the D1 entry at the top.**

### Gold mines and trees exist in the rules: the first piece of the economy (sim track, M3-1, 2026-10-06)

- **What was built:** the game now knows about resource nodes. A **tree** holds 100 wood and
  covers one 2 m square; a **gold mine** holds 2,500 gold and covers a 2 x 2 block. Both are solid:
  units walk round them. When a tree is cut down to nothing (nothing cuts yet; that's the next sim
  task, workers), its square opens up and every pathfinding arrow map is recomputed, so armies walk
  straight through the gap. A map can be generated with forests (connected clumps of 12-40 trees)
  and mines (at least 24 m apart), and the generator makes sure no patch of ground is ever walled
  in by them. All of this is part of the replay fingerprint, so a replay where a tree falls one tick
  later is a different replay. The replay file format grew (version 3; old files are refused with a
  clear code), and the checked-in golden replay was regenerated once with the proof that every
  unit's path stayed byte-identical.
- **What you'll see:** nothing in the window yet: the game window still builds its map without
  resources, and no tree or mine is drawn. The next view task draws them (placeholder cone trees
  and dark blocks) and turns forests on in the game's map, so in one or two sessions you'll see
  forests and mines on the map and the army routing round them. **Update 1503: landed (see the
  M2-3b entry above); workers gather now too (M3-2 entry), and BUG-0075 is fixed by the gather rule.**
- **Try it now (command line):** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200 --ticks 600 --forests 12 --mines 8`
  twice: the header ends with `forests 12 trees N mines 8` and the fingerprint lines are identical
  both times. Press F12 in the game window later (see the overlay entry) to see resource cells red
  once the view turns forests on.
- **Producer decisions, revisit any time:**
  - *A mine is 2 x 2 cells (4 x 4 m), a tree 1 x 1.* The docs didn't say; 2 x 2 gives eight sides for
    workers to stand on. Alternative: 3 x 3 for a grander mine.
  - *A worked-out mine leaves open ground, like a felled tree* (no rubble blocking the spot).
  - *Maps have no forests or mines unless asked* (the generator's defaults are 0). This kept every
    M1 test and the golden replay's trajectories identical. The game window will ask for them in
    the next view task; the numbers (how many forests, how many mines) are a design call I'll make
    then (something like 12 forests and 8 mines on the 128 x 128 map). Tell me if you have a
    preference, or a preference for where mines go (near start positions comes with M3-3 / M6).
  - *Every mine holds the "start mine" amount (2,500)* until start locations exist and expansion
    mines (2,000) can be told apart.
  - *Room for 4,096 nodes per map* (a setting, not a limit of the design).
  - *Size: about 2,200 lines against a 1,000-1,300 budget*, mostly tests. Accepted: the new part is
    how resources interact with pathfinding and the fingerprint, and the tests are what prove it.
- **Rough edges (S3, none visible before workers exist):**
  - BUG-0073: when trees fall *continuously* (one per tick, QA's stress), every arrow map is
    invalidated every tick and most groups stand still until the felling stops. In play, with workers
    chopping, a tree falls every few seconds, which would mean short stalls. I've made this a design
    question for the next sim task (felled trees only *open* ground, so an old arrow map is still safe
    to follow; the fix is to keep using it and refresh it in the background).
  - BUG-0075: cutting a tree in the *middle* of a forest first leaves an open square nobody can
    reach, and a click on it makes the unit give up. Workers will only ever cut trees they can stand
    next to, so this can't happen in play once that rule is written down; the next sim task does that.
  - BUG-0074: the forest generator assumes trees are 1 x 1; if someone edits the data to make trees
    2 x 2, forests can wall ground in. Next sim clean-up session. **Update 1744: BUG-0073, BUG-0074
    and BUG-0075 are all fixed (M3-2 and M3-2b entries above).**
  - BUG-0076 (S4): small notes (a setup-time number in the docs, fixed; a redundant check that costs
    2 s on a huge 1024 x 1024 map; an empty resources file loads without complaint). **Update 0800:
    BUG-0076 fixed (M3-H1): an empty or one-kind resources file is refused, and the 2 s check runs only
    in debug builds.**
- **To change it:** `game/data/common/resources.json` (node types, footprints, names);
  `game/data/common/rules.json` (`treeWood`, `startMines.gold`); forest sizes and mine spacing are
  generator settings (`MapGenParams`), by inbox note.

### Press F12: the developer overlay shows the pathfinding under the hood (view track, M2-5, 2026-10-06)

- **What was built:** a toggle (F12) that draws the game's inner workings on the map: every ground
  cell as a faint square (red where units can't go, dark red on cliff edges, orange on ramps); yellow
  arrows on the ground showing which way the selected units' pathfinding map points, in a 40 x 40-cell
  window round the camera, with a cyan disc on the destination; a bar graph of the last 120 ticks'
  cost with the 4 ms budget line (bars go red over budget); and a second line in the top-left label
  with the live unit count, how many are moving, how many arrow maps are cached, and the average and
  worst tick time. Off by default; `-- --debug-overlay` starts with it on (so `--screenshot` can capture
  it). While off it costs nothing and nothing is built.
- **Try it:** `& $env:GODOT --path game`. Box-select the blue army, right-click a spot on the far
  plateau, press **F12**: the arrows converge on the ramp (orange) and then run to the cyan disc. Pan the
  camera: the arrow window follows. Press F12 again to hide it. `& $env:GODOT --path game -- --units 1000 --zoom 60 --debug-overlay`
  shows the graph with 2,000 units ticking (bars well under the line).
- **Producer decisions, revisit any time:**
  - *F12*, rebindable (`debug_overlay` in `game/project.godot`); the docs' key list left it free.
  - *Arrows for one destination only*: the lowest-numbered selected unit's. Arrows for every selected
    group would clutter; the window and a Tab subgroup pick the one you mean.
  - *A fixed 40 x 40-cell window round the camera*, not the exact visible area: at the farthest zoom you
    see more ground than the window covers. The visible-trapezoid version is deferred.
  - *The numbers (average, worst) are on the label, not drawn inside the graph.* The dev's call;
    accepted.
  - *Per-unit state labels and the dev console are deferred* (they are in the docs' wish list, not in
    the M2 criterion); labels may come with the M2-7 polish if cheap.
  - *Size: about 2,100 lines against an 800-1,100 budget*, mostly tests (an independent checker that
    recomputes the arrows after every refresh and compares them to what's drawn).
- **Rough edges (view hardening session, after M2-6 / M2-7):** BUG-0083 (S3): with the overlay on,
  the label's text line allocates a little memory every frame while the docs claim zero (the overlay's
  own drawing is zero; the fix is a line in the docs or a cached string). BUG-0084 (S4): cliff edges
  read olive, not dark red, on the green plateau; arrow tips dip a few cm into steep ramps; one compiler
  warning in a test scene; one dev test measures the wrong case. Also seen: with more than 128
  destinations active at once under the 2-maps-per-tick build limit, a new order's arrows can take a
  while to appear (BUG-0025, sim, known). **Update 1503: resource cells (trees, mines) show green in
  the overlay instead of red, since they open up when used. Update 0800: BUG-0083 and BUG-0084 fixed
  (M2-H2): the label is rebuilt only when a number changes, cliff edges read crimson, arrow tips clear the
  ramps.**
- **To change it:** key binding in `game/project.godot`; colours are constants in
  `sim/Rts.Sim/ViewApi/NavOverlayBuilder.cs` and `game/scripts/FlowArrowsView.cs` (dev-only, not
  player-facing); window size `FlowArrowLayout.DefaultWindow`; the rest by inbox note.

### M1 is done: the whole game simulation works without graphics, and I signed it off (sim track, M1-9 + sign-off, 2026-10-06)

- **What M1 is:** everything that makes the game *run*, with no picture yet: generated terraced maps
  with cliffs and ramps; armies of hundreds of units that walk, funnel through chokes, pack up at the
  click point, give up when truly stuck, and make way for each other; Stop / Hold / Attack-move and
  Shift-queues; replays that prove the game plays out identically every time; the 500-unit speed
  target (0.62 ms per tick against a 4 ms budget); and a command-line tool that runs a match with no
  window. Fourteen sessions over four days, three of them clean-up sessions.
- **This session's clean-up (nine items), in plain words:**
  1. A unit told to **hold position now blocks your own soldiers too** (nobody squeezes past a holder
     in a one-cell gap; before, 10 of 30 did). It already blocked the enemy.
  2. **Bad orders are refused at the door** instead of being accepted and corrupting the replay file
     later (only hand-built orders could do this; the game's own keys never produced one).
  3. **Enemy walls of 5+ units now hold** (a line of five foot soldiers across a 4 m choke used to be
     leaked through by your own units shoving each other). Walls of up to 32 units count.
  4. The two-army speed regression is paid back: 2,500 units of two armies fighting over one spot
     cost 10.6 ms per tick, now 6.8; one tight 2,500 blob 4.5 → 4.3 ms, and that number is now a
     test that fails if it regresses.
  5. Found while measuring: a unit that gave up while being pushed off a crowded goal could stop a
     metre away and still "own" the spot, so later arrivals packed around a stray. Fixed (BUG-0058).
  6. Command-line tool: a bad `--record` path is reported before the run, not after a long run; a
     replay too short to check says so; two cosmetic messages fixed.
  7. The docs now list every known limit of the movement code in one place (docs/03 "Known limits
     (M1)").
- **Try it (two minutes):** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 500 --ticks 1500`
  twice: the 15 fingerprint lines are identical and the timing line is under 1 ms per tick. Then
  `& $env:GODOT --path game`: box-select part of the blue army and press **H**; select the rest and
  right-click through the holders: nobody gets past them. Send the whole army across the map and
  watch it stream through a ramp.
- **Producer decisions, revisit any time:**
  - *Signed M1 off myself* (your autopilot setting `stop_at_milestone_end: no`). Conditions held: all
    eight criteria verified, QA coverage complete for every M1 system, no serious open bugs, clean-up
    session done. If you'd rather playtest before a milestone closes, flip that setting to `yes`.
  - *Two limits accepted instead of fixed:* (a) a unit's exact path can depend on the order its
    neighbours were created (BUG-0046); replays are exact anyway (same game, same result), it only
    matters for a hypothetical "re-create the army in a different order" case. The fix was built and
    measured but it reshuffles every crowd's outcome and broke three test bounds; not worth it now.
    (b) 4.7% of units give up when 500 units go to 500 random spots (my target was 3%); every rule
    that got lower also made jams never end. Both revisit with the "crowd cost" pathfinding work after
    M4. Alternative: spend a session re-fitting the test bounds and landing (a) now.
  - *The 64-goal stress test allows one bad map to give up 76 of 128* (it's the map that once
    livelocked; typical maps give up 28-30, and that typical number is now guarded too).
- **Rough edges (all small, next sim clean-up session in four sessions):** BUG-0071 (S3): in one
  constructed geometry the "is this a wall?" shortcut can answer differently depending on which unit
  asks first; deterministic, never seen in random play, no replay risk. BUG-0072 (S4): a `--record`
  file name with illegal characters is still caught only after the run. **Update 0800: both fixed (M3-H1).**
- **For you, one line in your file:** `CLAUDE.md` says "Current milestone: M1". Suggested:
  "Current milestones: M3 (sim track), M2 (view track)", plus the CLI line
  `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200   # headless hashes + timings`.
- **What's next:** M3, the economy: gold mines and trees, workers gathering, building placement and
  construction, production queues, Age II, and the full Malazan and Whirlwind rosters in data.
  **Update 1255: gold mines and trees landed (see the M3-1 entry at the top).**

### View clean-up: the minimap right-click cancels A, lone dots are visible, five bugs closed (view track, M2-H1, 2026-10-06)

- **What was built (clean-up, no new features):**
  1. Right-clicking the **minimap** while A is armed now cancels A and orders nothing, the same as a
     right-click on the 3D map (BUG-0068 fixed).
  2. A double-tap on a group digit at exactly the 300 ms limit always counts the same way; if every
     selected unit dies while A is armed, the next click selects normally instead of being swallowed
     (BUG-0067 fixed).
  3. A height lookup that could crash for a unit billions of metres off the map can't any more
     (BUG-0052 fixed; unreachable in play, but gone).
  4. **Minimap dots have an outline**: each unit's dot is its cell in the player colour inside a
     one-cell dark rim (or a light rim around the dark Malazan colour), so a lone scout no longer
     hides against a ramp tick or a cliff lip (BUG-0064 fixed). Costs 0.13 ms per refresh at 2,000
     units (was 0.06; budget 0.25).
  5. Test gaps closed: the facing test now catches a mirrored turn; the mesh test now catches
     missing cliff walls (BUG-0053 fixed).
- **Try it:** `& $env:GODOT --path game`. Select a few units, press A, right-click the minimap: the
  `A` disappears from the top-left label and nobody moves. Zoom out (`-- --zoom 60`) and look at the
  minimap: every unit is a small outlined square.
- **Producer decisions, revisit any time:**
  - *The rim colour switches by brightness:* dark rim around light colours (orange Whirlwind), light
    rim around dark ones (slate-blue Malazan). The dev's call; I accepted it, but see the rough edge.
  - *The refresh-cost target is the 0.25 ms limit, not the old 0.06 ms.* Doubling a tenth of a
    millisecond five times a second is nothing.
  - *No fix for maps wider than 220 cells* (none exist): a note in the docs says what to do when they do.
- **Rough edge, a taste call for you (BUG-0069, S3):** at the minimap's size a lone dot is a 5 x 5
  pixel square with only 1-4 pixels of player colour in the middle, so **a lone Whirlwind unit reads
  as a black dot and a lone Malazan unit as a near-white one**, close to the white camera outline.
  Two light-coloured factions' scouts would look alike, and gaps inside an army show as thin rim
  lines through it. QA's screenshots are in the conductor's scratch folder for this session
  (`qa100-minimap.png`, `qa990-minimap.png`, 4x crops; not in the repo). Default plan: a 2 x 2-cell
  coloured centre inside the rim (more colour, same visibility) in the next view clean-up session
  (after M2-5, M2-6, M2-7). Alternatives: draw dots in screen pixels (a fixed 3-4 px colour square
  with a 1 px outline, also future-proof for big maps), or rim only around the outside of a crowd.
  Say which in the inbox if you have a preference. BUG-0070 (S4): four doc/test nits, same session.
  **Update 0800: done (M2-H2): the dot is now a 2 x 2 coloured centre in a one-cell rim (the default plan);
  QA read the screen pixels of lone dots at the border, on a ramp and on a cliff lip and every one shows
  3-4 pixels of the exact player colour. BUG-0070 fixed too. The other two styles remain yours to pick.**
- **To change it:** rim colours are `MinimapRaster.DarkRim` / `LightRim` (engine constants); the
  double-tap window is `ControlGroups.DoubleTapSeconds`; the rest by inbox note.

### The order keys are in: A, S, H, Shift-queue, double-click, control groups and Tab (view track, M2-3, 2026-10-06)

- **What was built:** the keyboard half of giving orders. **A** then click: attack-move (for now
  it just walks there; fighting on the way comes in M4). **S**: stop. **H**: hold position (your
  own walkers route round holders instead of nudging them). **Shift + right-click** (or Shift + S /
  H): queue the order behind the current ones, up to 8 per unit. **Double-click** a unit, or **Ctrl +
  click** it: select every unit of that type on screen (Shift adds them). **Ctrl + 1-9** saves the
  selection as a group, **Shift + 1-9** adds to it, **1-9** recalls it, and tapping the digit twice
  quickly jumps the camera to the group. **Tab** steps through the unit types in a mixed selection
  (shown in the top-left label as `sub <type> 1/3`; the command card will use it in M3). Esc
  cancels an armed A. Every key is its own rebindable action in `game/project.godot`.
- **Try it:** `& $env:GODOT --path game`. Box-select the blue army, press H, then select a few
  others and right-click through them: the holders don't budge. Select a group, press A, click far
  away: they walk (the label shows an `A` while you're aiming). Shift + right-click four spots:
  they visit them in order. Ctrl + 1, click empty ground, press 1: they're back; press 1 again at
  once: the camera jumps to them. Double-click one cavalry unit: all cavalry on screen are selected.
- **Producer decisions, revisit any time:** A with nothing selected does nothing; while A is armed,
  a click off the map does nothing and keeps aiming; Ctrl + digit with nothing selected keeps the
  old group (no accidental wipes); recalling an empty group changes nothing; Ctrl + Shift + digit
  counts as assign; the double-tap window is 0.3 s; Tab order is by unit type id; a unit told to
  hold shows no "holding" marker because any queued order ends the hold (so a marker would lie).
- **Rough edges (view hardening session, next view session):** BUG-0068 (S3): right-clicking the
  *minimap* while A is armed sends a plain move and leaves A armed, where a right-click on the
  3D map cancels A as intended. BUG-0067 (S4): a double-tap at exactly 300 ms is a coin flip; if
  every selected unit dies while A is armed, the next click is swallowed (can't happen before M4).
  Also visible now: send a tight group through Shift-queued points and some units give up a leg
  when they bump into their own packed comrades (BUG-0028, the crowd-cost follow-up after M4).
  **Update 0905: BUG-0068 and BUG-0067 fixed (see the view clean-up entry above).**
- **To change it:** key bindings are in `game/project.godot` (one action per key, e.g.
  `order_hold`); the double-tap window is `ControlGroups.DoubleTapSeconds`; the rest by inbox note.

### The game can be run from the command line with no window, and the last M1 criterion is met (sim track, M1-8, 2026-10-06)

- **What was built:** a small tool, `tools/Rts.Cli`, that runs a match headless: it spawns an army
  (every unit type, `--units` of them), marches it across the map from one edge to the far side,
  and prints a fingerprint of the game state every 5 seconds of game time plus how long each tick
  took (average, 99th percentile, worst). It can record the run as a replay file and play a replay
  back, checking every fingerprint; the exit code says what happened (0 fine, 1 bad arguments or
  files, 2 a replay that doesn't match). This is the "runs a scenario headless and prints hashes
  and timings" criterion, the last of M1's eight. The same task gave the view a read-only window
  into the pathfinding arrow maps, so the M2-5 debug overlay can draw them.
- **Try it:** `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200 --ticks 1500`. Run
  it twice: the 15 fingerprint lines are identical (that's determinism you can see). Add
  `--record x.replay` then `dotnet run --project tools/Rts.Cli -- play x.replay`: "ok: 15
  checkpoints matched". `--units 2500` shows the tick cost at scale (about 4.6 ms on this PC).
  Update 1255: `--forests 12 --mines 8` adds resources to the map (see the M3-1 entry). Update 1503:
  `--workers 10` adds a Town Hall and 10 gathering workers per player and prints each player's gold
  and wood at the end (see the M3-2 entry).
- **Producer decisions, revisit any time:** the march goes to the walkable cell farthest (by
  walking distance) from the middle of the player's own map edge, so armies cross the whole map
  (with `--players 2` the two armies cross each other); `--units` is the total across players and
  is capped at 100,000 (the start blocks hold about 14,600 on the default map).
- **Rough edges (M1 hardening session):** BUG-0057 (S4): an unwritable `--record` path is only
  reported after the run finishes; a replay recorded with fewer ticks than the checkpoint interval
  "passes" with nothing compared; a cosmetic `: :` in one error message. (The docs mismatch about
  timing with `--record` is already fixed.) **Update 0905: BUG-0057 fixed; one leftover (illegal
  characters in the file name) is BUG-0072, S4.**
- **For you, one line in your file:** `CLAUDE.md`'s Commands block has no CLI line yet (agents
  don't edit it). Suggested: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200   # headless hashes + timings`.
- **What's next for M1:** one clean-up session (nine small bugs, listed in the sim debt backlog),
  then I sign M1 off and write its retro; M3 (economy and buildings) starts after. **Update 0905: done,
  see the M1 entry at the top.**

### Stop, hold position, attack-move and Shift-queued orders exist in the rules; the M1 speed target is proven (sim track, M1-7, 2026-10-05)

- **What was built:** three new orders a unit understands besides "walk there": **Stop** (drop
  everything and stand), **Hold position** (stand and refuse to be pushed aside by your own
  walkers; it will fight from there in M4), and **Attack-move** (walk there; from M4 it will stop
  to fight anything met on the way). Plus **Shift-queuing**: up to 8 orders lined up per unit, run
  one after the other; a queued Stop or Hold ends the line; a unit that gives up on one leg still
  tries the next. Replays now record the queue flag (format 2; the old format is refused with a
  clear code; the checked-in golden replay was regenerated and every fingerprint stayed identical,
  which proves no movement rule changed). Also the M1 roadmap criterion "500 moving units under
  4 ms per tick" now has a named, enforced test.
- **What you'll see:** nothing yet; there are no keys for it. The next view task (M2-3) wires A /
  S / H / Shift. Then: select a group, press H, and walkers from your own army route round them
  instead of nudging them off their spot; Shift-right-click four points and the group walks them
  in order. Update M2-3: landed; see the entry above.
- **Numbers:** 500 units marching cost 0.67 ms per tick on this PC (budget 4 ms, so 6x headroom);
  2,500 units each with a full queue of 8: 3.0 ms.
- **Producer decisions, revisit any time:**
  - *A held unit still lets its own army squeeze past it in a one-cell gap* (BUG-0055, S3): 10 of 30
    friendly walkers got past a big unit holding a 2 m corridor; enemies never do. This is the
    "your own standing units are soft" rule from M1-5, applied to holders as the brief said. I
    think a unit told to hold a choke should block its own side too (that's what H means), and I've
    scheduled that change for the M1 clean-up session, as long as it doesn't hurt the crowd numbers.
    Alternative: keep holders soft and document it. **Update 0905: done, holders block everyone; the
    crowd numbers didn't move.**
  - *Hold, then Shift-queue a move = walk off at once.* A queued order given to a holding unit
    starts immediately (it's standing idle), and starting an order releases the hold. This is how
    most RTSs behave; the HUD must not show "holding" once an order is queued after it (noted for M2-3).
  - *Queue of 8 per unit; a 9th is dropped silently.* The view can warn if it wants to.
  - *One tick over budget on size* (about 1,510 changed lines vs 1,500). Accepted.
- **Rough edges (M1 clean-up session):** BUG-0055 above; BUG-0054 (S3): a hand-built order with a
  flag bit the rules don't know is accepted and then makes the replay file unwritable (no factory
  produces one, and the game doesn't save replays until M6); BUG-0056 (S4): three nits. **Update
  0905: all three fixed.**
- **To change it:** the queue length is `OrderConstants.QueueCapacity` (an engine constant, not
  data); the hold / stop behaviour is a rules decision: write it in the inbox.

### The minimap is in: see the whole map, jump the camera, send troops with a right-click (view track, M2-4, 2026-10-05)

- **What was built:** a 220-pixel map in the bottom-left corner: the three height levels in their
  tints, ramps as small orange ticks, cliff lips dark, the unreachable border darkened; every unit a
  dot in its side's colour (refreshed 5 times a second); and a white outline showing exactly what
  the camera sees (wider at the top, since the camera looks across the ground at an angle). It is
  the first piece of the HUD (`Hud` layer), so later panels have a home.
- **Try it:** `& $env:GODOT --path game`. Left-click anywhere on the minimap (or hold and drag) and
  the camera jumps there. Box-select your army, then right-click a spot on the minimap: they walk
  there, same as right-clicking the ground. Hovering the minimap never scrolls the camera. `-- --no-hud`
  starts without it (clean screenshots). `-- --units 1000 --zoom 60` still runs at 120 fps.
- **Producer decisions, revisit any time:**
  - *Bottom-left, 220 px, square; a non-square map gets letterboxed* (dark bars) instead of
    stretched, so distances read the same both ways. Alternative: stretch to fill.
  - *Same placeholder colours as the 3D terrain,* so what you see on the minimap matches the ground.
    Real biome colours come with the art pass (M6).
  - *Dots are one map cell (2 m) and show the current tick, not the smooth in-between position*;
    at 5 refreshes a second nobody can tell. **Update 0905: dots are now the cell plus a one-cell rim
    (see the view clean-up entry).**
  - *The mouse wheel does nothing over the minimap* (minimap zoom may come later).
- **Rough edges (view hardening session, after M2-3):** BUG-0064 (S4): a lone Whirlwind dot is
  nearly the same orange as a ramp tick, and a lone Malazan dot on a cliff lip is nearly the cliff
  colour, so single scouts can hide in plain sight; likely fix is an outline or a 2x2 dot. On maps
  wider than 220 cells (none exist in the game yet) about a quarter of cells would never get a
  pixel. No fog, resource markers, pings or Alt-click yet (M3/M4). Update M2-3: a Shift + right-click
  on the minimap now queues the move, like on the 3D map. **Update 0905: BUG-0064 fixed (outline);
  the follow-up on the outline's colour is BUG-0069.**
- **To change it:** size and corner are in `game/scenes/Match.tscn` (the `Minimap` node's
  offsets); dot colours follow each faction's `primaryColor` in `game/data/factions/<faction>/`.

### You can now select your soldiers and send them somewhere (view track, M2-2, 2026-10-05)

- **What was built:** when the game opens, 200 placeholder units stand on the map: your army in
  Malazan slate-blue to the west of the centre, the Whirlwind army in orange to the east. Each unit
  is a capsule sized by its body radius from the data (foot soldiers small, cavalry bigger, siege
  biggest). Left-click a unit, or drag a box over several; hold Shift to add or remove; click empty
  ground to clear. Selected units get a green ring. Right-click anywhere on the map and they walk
  there, smoothly at the frame rate (the game ticks 20 times a second; the picture blends between
  ticks). Enemy units can't be selected. The top-left label now ends with `sel N`.
- **Try it:** `& $env:GODOT --path game`. Drag a box over the blue army, then right-click the ramp
  or the plateau behind it: the crowd funnels through the ramp (the M1-5 behaviour, now visible).
  `-- --units 1000` starts 2,000 units (120 fps on this PC, vsync-capped); `-- --zoom 60` starts
  zoomed out.
- **Producer decisions, revisit any time:**
  - *Team colour = faction colour.* Player 0 plays Malazan, player 1 Whirlwind, until M6's match
    setup gives each player a colour of their own. Alternative: a per-player palette now; skipped
    because there is no setup screen to pick it in yet.
  - *Clicks have a 12-pixel minimum radius* so far-away units stay clickable; *Shift + click on
    empty ground keeps the selection* (as in most RTSs); *a click on a cliff face sends units to the
    plateau above it* (the wall belongs to the higher ground).
  - *Start positions are a debug layout*: two half-disc blocks either side of the map's centre, so
    both armies fit the opening camera. Real start locations come with the maps in M6.
  - *Over the size budget* (2,085 lines vs 1,500; about 900 of them game code, the rest tests).
    Accepted because the code is plain and the tests are what make the picking exact (the click
    point lands within 1 mm of the drawn ground).
- **Rough edges:** units turn to face where they walk but don't animate (capsules). With 2,000
  units selected, three right-clicks inside one tick overflow the order queue and the third is
  dropped with a warning (by design; the queue holds 4,096 orders). Two nits for the view hardening
  session: BUG-0052 (S3, a height lookup would crash for a unit billions of metres off the map,
  which can't happen) and BUG-0053 (S4, a test that can't catch a facing sign flip, a stale remark,
  one doc number). Update M2-4: the minimap's right-click uses the same order path. **Update 0905:
  BUG-0052 and BUG-0053 fixed.**
- **To change it:** unit sizes are the `radius` values in `game/data/factions/<faction>/units.json`;
  team colours are each faction's `primaryColor` in `game/data/factions/<faction>/`. Both are data
  edits (plus a golden-replay regen, which the studio does).

### Crowds route round each other, parked units make way, enemy walls hold better (sim track, M1-4d-3 hardening, 2026-10-05)

- **What was built** (the M1 hardening session: five debt items in one batch):
  1. Walkers steer round other groups' standing clusters and enemy clumps instead of pressing in
     and giving up (the shorter way round; ties go right).
  2. Waiting isn't being stuck: a unit behind traffic that is still moving (anyone's, within 2 m),
     or behind a unit waiting for its path, keeps its patience (behind a path-wait it loses patience
     four times slower rather than never, so a jam always ends).
  3. A unit pushed off its cluster walks back to its spot once the pushing stops, once per order,
     so a corridor can't bounce it back and forth forever.
  4. A pair parked in a one-cell corridor is pushed along by a walker stuck behind it (parked lines
     of up to 3 yield together; a cluster's spot still holds).
  5. Smaller fixes: an enemy standing on your click point is a wall, not a friend (BUG-0037); a
     walker beside an enemy no longer slides into its own standing comrade (BUG-0038); a short
     re-click inside the same 2 m square moves an arrived unit (BUG-0030); a hand-edited replay can't
     declare more than 24 hours (BUG-0040 part 1); the movement tests now run on many maps instead
     of one (BUG-0034/0039).
- **What you'll see:** send one group through another's parked cluster and most now get through:
  111 of 200 walkers cross a mixed cluster (was 2); two groups swapping rooms through a one-cell
  corridor all arrive; the parked pair steps along. Groups sent to four points 6 m apart: 51% arrive
  (was 36%; on the fair comparison 241 → 256 of 500).
- **Producer decisions, revisit any time:**
  - *Accepted below my targets again* (four points 51% / 34% vs 60% / 50%; 64 small groups 19% give
    up vs 10%; walkers crossing their own army's settled cluster 22 of 200 vs 150). Every row beats
    the old code on the same setup, and the cause is the same structural one: the shared path map
    doesn't know where units stand, so four clusters merge into one 12 m mass that walkers from
    the far side can't get round before their 1-second patience runs out. The real fix is a "crowd
    cost" in the path maps; I'm not scheduling it inside M1 and will propose it once the M4 combat
    sandbox shows whether it matters in play.
  - *Found and fixed in-session* (QA): one map in 160 where 94 units walked forever (S1); the
    walk-back pushing the walker it had just let through back off its goal (S2); a re-click
    mid-route making a walker give up (S2).
  - *BUG-0045 lowered from S2 to S3, my call, you may object.* QA found that a line of **five or
    more** standing enemies plugging a passage (for example 5 foot soldiers across a 4 m choke) can
    still be leaked through: your own units shove a given-up comrade into the enemies, and it later
    walks out the far side. This was already true before this session; the M1-5 entry below said
    "nobody gets through", which held for the 3-unit plugs tested then. Plugs of up to 4 units now
    hold, including diagonal ones and ones that form and dissolve mid-push. I didn't block the merge
    on it: nothing you can see depends on it before M4 (units pressed into enemies will be fighting,
    not standing), and it predates this work. It's scheduled for the M1 end-of-milestone hardening
    session. Alternative: reject the whole session over it, losing nine verified fixes. **Update
    0905: fixed; plugs of up to 32 hold.**
  - *Perf:* 500 moving units cost 0.17 ms per tick (the M1 budget is 4 ms). At 2,500 units, one tight
    cluster is at 4.5-4.6 ms vs my 4.5 ms slice target (3-5% over the old code the same day), and
    two-player clusters got 2x slower (BUG-0044, S3: the new plug check runs for every touching
    enemy). All beyond M1's supported scale; hardening session. **Update 0905: paid back (4.3 and
    6.8 ms).**
  - *The old crowd tests measured the wrong thing:* a slot-numbering slip sent both players to
    every point, so they measured enemies contesting a spot, not crowds. Re-set to one player per
    point, with the old code re-measured on the same setup for a fair comparison.
  - *Every map's trajectories changed once* (golden replay regenerated, reason in the commit).
- **Rough edges (all S3/S4, M1 end-of-milestone hardening):** BUG-0044 two-player perf at 2,500;
  BUG-0045 plugs of 5+; BUG-0046 a walker's exact path can depend on the order its neighbours were
  spawned in (replays are unaffected: same seed + same orders still match tick for tick); BUG-0049
  one 64-goal map gives up 75 of 128 (test bound 48); BUG-0050 random-goal give-ups 4.7% vs the 3%
  target (the price of the livelock fix); BUG-0047 test-guard nits. BUG-0028/0032 (crowd targets)
  stay open as the "crowd cost" follow-up. **Update 0905: BUG-0044 / 0045 / 0047 / 0049 fixed;
  BUG-0046 and BUG-0050 accepted as known limits (see the M1 entry at the top).**
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CrowdRoutingTests" --logger "console;verbosity=detailed"`.

### The game window shows a 3D map you can fly over (view track, M2-1, 2026-10-05)

- **What was built:** the first picture. `& $env:GODOT --path game` now opens a window with a
  generated map in 3D: flat plateaus at three heights (tan lowland, green upland, pale stone top),
  dark vertical cliff walls, orange sloped ramps with walls at their sides, a sun with shadows and a
  sky. The simulation ticks underneath at 20 ticks per second; a small white label in the top-left
  shows the tick number, game speed, how long the last tick took, and the frame rate. No units are
  drawn yet (next view task). Update M2-2: units are drawn now, see the entry above.
- **Try it:** arrow keys pan; push the mouse against a window edge to pan; hold the middle mouse
  button and drag to grab the ground; mouse wheel zooms between 20 m and 60 m above the ground.
  The camera can't leave the map. `& $env:GODOT --path game -- --seed 6` shows a map with all three
  heights in view at the start; `-- --speed 4` runs the sim at 4x.
- **Screenshot tool (for the studio and for you):** `& $env:GODOT --path game -- --screenshot
  C:\temp\shot.png --screenshot-after 2` saves a picture after 2 seconds and quits. Every later
  visual task is checked with it, so the studio no longer needs the optional Godot MCP install.
- **Producer decisions, revisit any time:**
  - *Placeholder colours* (three level tints, ramp orange, cliff brown) are hard-coded in the mesh
    builder for now; biome colours and real materials come with the art pass (M6). Change them by
    an inbox note until then.
  - *Camera numbers from the design doc as written:* 55° pitch, no rotation, 20-60 m zoom, 4 m per
    wheel notch, pan speed equal to the zoom height per second. Alternative: a rotating camera; the
    doc rules it out for readability.
  - *The pure camera/clock/mesh logic lives in `sim/Rts.Sim/ViewApi/`* so it is unit-tested
    without Godot. It reads the sim and never writes it (QA proved this: 100 mesh builds between
    ticks leave the game state hash untouched).
- **Rough edges:** ramps look a little steeper at their two ends than in the middle (about 31° vs
  22°), because heights are stored at cell centres; cosmetic, no fix planned unless it bothers you.
  Three tiny input nits (BUG-0041: a `--seed` with no value swallows the next flag; the start-up log
  prints the largest possible seed as -1; a clock corner case no code path reaches) go to the view
  hardening session. Update M2-2: BUG-0041 fixed. The test scene `game/tests/CameraClampTest.tscn`
  compiles into the game for now; it is excluded when the release build exists (M6).

### Replays and a determinism proof that outlives the session (sim track, M1-6, 2026-10-05)

- **What was built:** a replay file format and player. A replay stores the map seed and settings,
  every command the game accepted (with the tick it was given), and a fingerprint of the game state
  every 5 seconds. Playing it back re-runs the match from the seed and checks every fingerprint;
  the first mismatch stops it and names the tick. One "golden" replay is checked in: 200 units of
  every kind marching across the seed-1 map, 75 seconds of game time, 15 fingerprints, a 15 KB text
  file you can read in a diff.
- **What it means for you:** from now on, any change to movement, tick order, random numbers or
  unit stats that changes how a match plays out fails the test suite until a developer regenerates
  the golden on purpose and explains why in the commit. This is the guard that keeps future replays
  and saved games exact, and it's the backbone of the M6 replay viewer. Update M1-4d-3: the guard
  did its job; the golden was regenerated once, with the reason in the commit. Update M1-7: the
  file format grew a column (format 2) and the fingerprints stayed byte-identical. Update M1-8: you
  can record and play replays yourself from the command line (see the M1-8 entry). Update M1-9: the
  golden stayed byte-identical through the whole clean-up batch. Update 1255: format 3 (resources);
  the fingerprints changed because the fingerprint now covers trees and mines, but every unit's path
  was proven identical before and after.
- **Also fixed (BUG-0014):** two particular seeds (0 and the largest possible number) used to give
  the same map. The seed is now scrambled before use, so every seed is its own map. Side effect:
  **every map changed once.** Seed 1 today is not the seed 1 of yesterday. Nothing you have seen is
  lost (nothing was on screen yet).
- **Producer decisions, revisit any time:**
  - *A replay is tied to the exact game data it was recorded with*, including display text. Change
    one number in `game/data/` and old replays refuse to play (with a clear code, never a silent
    desync). Alternative: hash only balance-relevant fields; rejected for now because a wrong guess
    there means a desync that looks like a bug.
  - *Fingerprints every 100 ticks (5 s)* by default; file size grows ~17 bytes per checkpoint, so a
    30-minute match is under 1 MB even with thousands of orders.
  - *Six movement tests keep running on their old maps* through a helper that undoes the seed
    scramble, because their pass/fail bounds were tuned to one map. QA showed those bounds fail on
    most other maps (BUG-0039, S3, pre-existing): the next sim session, a hardening one, re-measures
    them on many maps. Update M1-4d-3: done; the helper is gone from those tests and the bounds hold
    on every swept map (one 64-goal map still breaks a bound, BUG-0049). Update M1-9: BUG-0049
    re-bounded on 80 maps.
- **Rough edges:** a hand-edited replay can declare a tick count of two billion and make playback
  spin for hours (BUG-0040, S4; a limit is a few lines, hardening session). Update M1-4d-3: the
  24-hour limit is in. When the AI arrives (M5) the recording point inside the tick needs a decision
  (also BUG-0040, still open for that part).

### Two tracks started: the view track begins M2 while the sim track finishes M1 (2026-10-05, session 1446 plan)

- **What changed for you:** from this session the game window stops being an empty scene (see the
  M2-1 entry above: it landed). Units, selection and right-click orders are the next view task (M2-2).
  Update 1609: M2-2 landed too. Update 2330: the minimap (M2-4) landed. Update 0655: the order keys
  (M2-3) landed; M1's criteria are all met. Update 0905: M1 signed off; sim starts M3. Update 1255:
  resources (M3-1) and the F12 overlay (M2-5) landed.
- **Producer decisions, revisit any time:**
  - *Where the view's tests live.* Dev tests in `sim/Rts.Sim.Tests/ViewApi/`, QA tests in
    `sim/Rts.Sim.Tests/QA/ViewApi/`, Godot-side test scenes in `game/tests/`; `ViewApi/` may hold
    pure helpers with no sim reference (fixed-step clock, terrain geometry, camera limits) so that
    logic is unit-tested without Godot. Alternative: put those helpers in `game/scripts/`, where
    nothing can test them. Now also written into the ownership table in docs/07.
  - *The sim track keeps its public setup API additive* (no new required fields on `SimConfig`, no
    signature changes to `Simulation` or the data loader), so the view compiles after both merge.
    Held this session and the next. Update 2330: held again (M1-7 kept the 3-argument `Move`).
    Update 0655: held (M1-8 added one read-only method and nothing else to the sim). Update 0905:
    held (one unused public constant renamed; `Enqueue` now throws on malformed commands, which the
    view never produces). Update 1255: held (`SimConfig.ResourceCapacity` optional; a new required
    data file `resources.json` that the game already ships).
  - *First view slice order:* scene + terrain + camera + screenshot flag before unit views, because
    the screenshot flag is how the studio (and you) verify every later visual task.
  - *Bug numbering with two QA inspectors:* both filed a BUG-0039 this session. From now on the
    brief gives each track its own starting number (view = sim + 10). The view's bug is BUG-0041.
- **Docs drift for you to fix (one line, your file):** `CLAUDE.md` still says "Current milestone:
  M1" and "don't write gameplay code ahead of the roadmap". With two tracks that line is per
  track (sim M3 now, view M2). The agents don't edit `CLAUDE.md`; suggested text: "Current
  milestones: M3 (sim track), M2 (view track)". Update 0655: plus the CLI command line (M1-8 entry).
- **Cap note:** `autopilot.md` says 8 sessions per day; the old dashboard said 10. The studio
  follows autopilot (8).

### The M1 headline works: an army crosses the map and climbs a ramp (M1-5, 2026-10-05)

- **What was built:** the proof test for M1's main promise. 200 soldiers of every kind (foot,
  cavalry, siege) start near the west edge of a generated map and are sent to the far side of a
  plateau, so they must funnel through a 6 m ramp. On every map tried (58 maps, plus 20 with a
  climb of two ramps, plus armies of 500 and 1,000) everyone arrives, nobody gives up, nobody ends
  up in a cliff, and running the same march twice gives the identical result tick for tick. The
  march takes 72-127 seconds of game time, under half the allowed limit.
- **What you'll see:** select your whole army and right-click across the map: the group streams
  through the ramp as a crowd, the ones pressed against the ramp's walls wait their turn instead
  of quitting, and the whole army arrives and packs up around the click point. Before this
  session, a few units (1-9 per march) got pinned at the corners of ramps and gaps between
  plateaus, stopped, and stayed behind. Update M2-2: you can do exactly this in the game window now.
- **Producer decisions, revisit any time:**
  - *Waiting in a moving queue doesn't count as stuck.* A unit only starts its 1-second give-up
    timer when nobody just ahead of it is making headway either. Trade-off: a jammed crowd (for
    example behind an enemy blocking a pass) takes a little longer to give up, about 10-14 seconds
    for 60 units instead of 9-10.
  - *Enemy units holding their ground are solid.* QA found that a walker squeezed between two
    standing enemies could slip through a 0.2 m slit (an old bug, made worse by the new patience).
    Now a line of enemies plugging a ramp holds: nobody gets through, everyone behind gives up.
    Your *own* standing units stay "soft" (a walker can still slip between two of them, and can
    shove idle ones aside), because making them solid too cut crowd arrivals in the tests.
    **Correction (1609):** "nobody gets through" held for the 3-unit plugs tested then. Plugs of 5
    or more enemies can still be leaked through by your own units shoving each other (BUG-0045,
    S3, pre-existing); plugs of up to 4 hold since M1-4d-3. See that entry. **Update 0905: plugs of
    up to 32 hold; your own *holding* units are solid too.**
- **Rough edges (S3, next hardening session):** if an enemy is standing exactly on the spot you
  clicked, your unit treats it as a friend and stops touching it, a hand's width inside it
  (BUG-0037); and a unit squeezed between an enemy and its own standing comrade can press into the
  comrade by up to 14 cm for a tick (BUG-0038). Neither lets anyone through a plug. Also, with
  500-1,000 units marching, a few pairs end up closer than they should (a known limit of the
  back-off rule). Update M1-4d-3: BUG-0037 and BUG-0038 fixed.
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ScenarioTests" --logger "console;verbosity=detailed"` prints each seed's path length, ticks taken and the limit. Update M1-6: the same march is now the golden replay (`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`). Update M1-8: `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 200` runs the same kind of march from the command line.

### Idle units step aside for walkers (M1-4d-2, 2026-10-05)

- **What you'll see:** soldiers walking through your own idle troops push them out of the way
  instead of stopping. A clustered group bends to let walkers by but doesn't split apart. A lone
  unit parked in a narrow pass steps aside once a friendly walker has been stuck behind it for
  0.5 s. Enemy units and units that are already walking are never pushed. A unit squeezed between
  a standing unit and a cliff can walk away again (BUG-0031 fixed). Update M1-7: a unit told to
  hold position is never pushed either.
- **Producer calls, revisit any time:**
  - Accepted although my own crowd targets were missed: groups sent to 4 nearby points now arrive
    35-44% of the time (was about 16%), against the 60-80% I asked for. The causes are structural
    (pathfinding ignores units; units waiting for a path and enemies act as walls) and were
    outside this task. QA filed them as S2 (BUG-0032/0033); I set them to S3 and scheduled a
    follow-up, M1-4d-3 crowd routing. Update 2026-10-05-1234: that follow-up now waits for the M1
    hardening session (your rule: S3 work goes there), and the cross-map scenario test (M1-5) went
    first because one army walking to one spot doesn't need it (confirmed: it passed without it).
    Update 1446: the hardening session is the next sim session. Update 1609: it landed (see the
    M1-4d-3 entry above); BUG-0033 fixed, BUG-0032 stays open for the crowd-cost follow-up.
  - Units standing on their own click point hold it. So a parked *group* in a 1-cell corridor still
    blocks its own army until you move it (BUG-0033 remainder). Update 1609: a parked pair now
    yields; a cluster's spot still holds.
- **Rough edge:** walkers crossing a cluster that mixes both players' units mostly give up (2 of
  200 in a test, 28 before shoving). M1-4d-3 targets this. Update 1609: 111 of 200 now.
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "PastAFriendlyUnitParkedThereByAMove|WalkersCrossingASettledBlob" --logger "console;verbosity=detailed"`.

### Two studio sessions overlapped (incident note, 2026-10-05)

- At about 12:14 a second session (id 2026-10-05-1214) overwrote `studio/.session.lock` in your
  checkout while session 1013 was still running; your three studio-skill commits (724234a,
  432fafe, c089405) went to main meanwhile. The studio worktree was untouched and session 1013
  finished normally. If that second session is still chaining, two conductors may be running.
  Your "Downloads stay with the owner" inbox note was processed at the 1234 PLAN (reflected in
  the M6 queue line). Session 1234 ran on the studio-session skill as loaded at its start; your
  later two-track (sim + view) change to the skill applies from the next session. Update 1446:
  this session is the first on the two-track skill; no second conductor has shown up since.
  Update 1609: a power outage at about 16:40 interrupted both builders; both were resumed from
  their transcripts with work intact, no second conductor. Update 2330: a quiet session. Update
  0655: quiet again. Update 0905: one near miss, no harm: the view builder's file I/O used a
  relative path and briefly rewrote two files in *your* checkout (`game/scripts/UnitViews.cs`,
  `game/tests/UnitViewsTest.cs`); restored byte-identical, your `git status` shows only your own
  `.claude/settings.json` change. Builders are reminded to use absolute paths. Update 1255: quiet
  (Remote Control was refused as "unavailable in unattended sessions"; no effect on the work).

### Units cluster around the click point, and give up when stuck (M1-4d-1, 2026-10-05)

- **What you'll see:** select 30 soldiers and right-click a spot. They walk there and pack into a
  tight cluster centered on the click, shoulder to shoulder, rather than lining up in a formation.
  Two units meeting in a narrow pass both step to their right and slip past each other.
  Spam-clicking the same spot doesn't make them restart or stutter.
- **Giving up:** a unit that makes no headway for 1 second (wedged behind a crowd, or blocked by
  standing units) stops and stands idle instead of jittering in place forever. Update M1-5: waiting
  behind a groupmate who is still moving no longer counts as "no headway". Update M1-4d-3: nor does
  waiting behind anyone's moving traffic within 2 m.
- **Producer decisions:**
  - Clusters instead of formations for now: StarCraft-style clumping, where Age of Empires uses
    line or box formations. Formations can come back in M2 with group commands.
  - 1 second before giving up. Shorter feels snappier but units stop short more often; longer
    means more pushing and shoving before they quit.
  - Clicking the spot a unit is already heading to counts as the same order.
- **Rough edges until the next session (shoving):** units that are standing still never step
  aside yet. So two groups sent to spots close together bump into each other's clusters and many
  stop short: in a stress test, 84% of 500 units sent to 4 nearby points gave up 8-16 m early
  (BUG-0028). A unit squeezed between a standing unit and a cliff can't walk away (BUG-0031).
  Both should go away once idle units make room for moving ones. (Update: shoving landed in
  M1-4d-2; BUG-0031 fixed, BUG-0028 partly, see the entry above.)
- **Also open:** a short nudge order (under about 2.8 m, inside the same 2 m map cell) to a unit
  that has already arrived is ignored (BUG-0030). It's in the debt backlog. Update M1-4d-3: fixed.

### Pathfinding: shared arrow maps with a speed limit (M1-4b and M1-4c, 2026-10-04)

- **What it is:** when you send units somewhere, the game computes one map of arrows for that
  destination: every 2 m square points along the shortest route there. Every unit headed to that
  spot follows the same map, so moving 200 units costs about the same as moving one. The game
  remembers recent destinations (32 to 128, depending on how many units the game allows), so
  re-ordering to a recent spot is instant.
- **Speed limit (Producer decision):** at most 2 new arrow maps per tick, which is 40 per second,
  oldest orders first. Each takes about 0.7 ms to build, and the limit keeps every tick inside its
  time budget so the game never hitches.
- **What you'll see:** nothing in normal play. Only in a big burst: order 32 separate groups to 32
  different spots at the same instant and the last group starts walking about 0.8 s after the first.
  Update M1-8: the M2-5 debug overlay will be able to draw these arrows on the ground. Update 1255:
  it does; press F12 (see the overlay entry at the top).
- **Why it's saved with the game (Producer decision):** which arrow maps are remembered decides
  which units wait a tick, so that memory is saved and checked like the rest of the game state.
  That keeps replays and saved games exact.
- **Rough edges (debt backlog):** with more separate destinations active at once than the game
  remembers (only in very large games with many small groups), some older groups can stall while
  newer ones walk (BUG-0025). In same-instant order bursts, one side of the map gets served
  0.1-0.2 s sooner on average (BUG-0026, cosmetic). Update M1-7: maps larger than 256 x 256 are
  now documented as unsupported until arrow maps can be built in slices (BUG-0023); the design
  map is 128 x 128.

### Terraced maps: plateaus, cliffs and ramps (M1-3 and M1-4a, 2026-10-03)

- **What you'll see:** maps have up to three height levels (ground, 4 m and 8 m up), each a flat
  plateau. Plateau edges are cliffs no unit can climb. The only way up is a ramp, 6 m wide and 8 m
  long with a gentle slope, entered only at its top or bottom because its sides are walled. Ramps
  are natural chokepoints, wide enough for about 7 foot soldiers or 4 horsemen side by side, so a
  few defenders can hold one. In M4 the high-ground rule raises the stakes: units below can't see
  up onto a plateau. Update M2-1: you can now see these in the game window.
- **Cliff edges (Producer decision):** the outer 2 m strip of each plateau counts as cliff, so
  units on high ground stand about one step back from the visible edge. The alternative (walls
  between squares) would let them stand right at the lip but makes pathfinding more complex.
- **Safety nets:** the map's outer ring is impassable, and any patch of ground no ramp reaches is
  sealed off, so a unit can never be stranded. The generator re-rolls a layout that comes out too
  blocked (under half walkable) or missing a height level, up to 8 times (Producer decision), then
  takes the best one, so map generation can't hang. A normal map takes about 4 ms to make.
- Also added: a fast "who's near me" lookup grid that movement and later combat use. Nothing to
  review there.

### First real game data (M1-2, 2026-10-03)

- **What it is:** the Malazan and Whirlwind rosters (7 units each, stats from the faction pages),
  the damage-type table and the economy rules now live as JSON in `game/data/`. The game checks
  every file when it starts and lists every mistake at once.
- **Values the docs didn't specify (Producer decision, first pass):**
  - **Body size:** 0.4 m radius for foot soldiers, 0.7 m for cavalry, 0.9 m for siege. This is the
    one you'll notice most: it decides how tightly crowds pack and how many units fit through a
    ramp at once. Update M2-2: you can see the three sizes on screen now.
  - Per-unit attack wind-up times, faction color palettes, and a placeholder for "requires Age II".
- **To change them:** edit the numbers in `game/data/factions/<faction>/units.json`, or ask the
  studio. No code changes are needed. Update M1-6: a data edit also means regenerating the golden
  replay (the studio does this; it's one command). Update 1255: `common/resources.json` joins the
  data (tree and gold-mine types).

### Optional: Godot MCP server (M0)

- A tool that lets Claude launch the game and take screenshots more easily. Not needed: since
  M2-1 the game has its own `--screenshot` flag, which covers the studio's need.

## Requests for the sim track

What the view track needs from the sim and can't add itself (the Producer plans these for the sim
track right after S1/S2 bugs).

1. ~~`Stop`, `HoldPosition`, `AttackMove` command kinds and shift-queued orders~~ → **done in
   M1-7** (session 2330).
2. ~~Public read access to a cached flow field's directions, for the M2-5 debug overlay~~ → **done
   in M1-8** (session 0655): `FlowFieldCache.PeekCached(targetCell)`; read `DirectionAt` / `CostAt`,
   don't keep it across ticks. M2-5 used it (session 1255).
3. ~~`DataError.ToString()` shouldn't print `: :` for empty fields~~ → **done in M1-9** (session 0905).
4. ~~Low priority: a previous-tick facing (`PrevFacing`) so unit views can blend turns~~ → **done
   in M3-2** (session 1503): `UnitStore.PrevFacing`, set with `PrevPosition`, not hashed. Use it in
   M2-7.
5. Noted, not requested: Shift-queued legs through a packed friendly group give up (BUG-0028);
   the M2-3 test scene works round it with spread-out units. Crowd-cost follow-up after M4.
6. Noted (M2-5, session 1255): with 64+ live goals under the 2-builds-per-tick cap about 90 % of
   selected goals had no cached field in QA's churn; that is BUG-0025 (sim debt). No depletion
   events for resource nodes: the view polls `World.Resources.Alive` / `Generation` (fine for M2).
7. Available since M3-2 (session 1503) for the M3 HUD: `World.Gold` / `World.Wood` (per-player
   spans), `World.Buildings` spans, `UnitStore.Cargo` / `CargoKind` / `GatherNode` and the states
   `Gathering` / `Returning` for worker feedback. Nothing requested yet.
8. Data track requests (D1, session 1744; carried in BUG-0090): (a) a building `requires` field
   (a building id and / or Age II: Shock Hall ← Infantry Hall; Caster Hall, Siege Works, Watch Tower ←
   Age II) with validation, planned with M3-5 / M3-6; (b) Watch Tower `attack` / `sight` / `detector`
   fields (docs/02: 10 pierce / 2 s, range 18, sight 24, detector 16 m), planned with M4 combat / fog;
   (c) where the faction upgrade is researched (Forge per docs/02) when `techs.json` gets its schema
   (M3-5). Until then the descriptions say "needs Age II" in text only.
9. ~~Sim-owned nit from D1: `sim/Rts.Sim/Data/BuildingSlot.cs:4` still says "M3-2 ships the Town Hall
   only"~~ → **done in M3-3** (session 2114).
10. Available since M3-3 (session 2114) for the view's build ghost and worker feedback:
    `World.CanPlace(player, typeId, anchorCell, out PlacementError)` (call once per frame on the main
    thread between ticks: it writes flow-field scratch, BUG-0092 note), `Command.Build` / `Cancel` /
    `Repair`, `Buildings.UnderConstruction` / `Work` / `WorkNeeded(type)` / `Hp` for a site progress bar,
    `UnitStore.BuildTarget` and `UnitState.Building`. Group build = one `Build` per selected worker (the
    first places, the rest join the same anchor). Nothing requested yet.
11. ~~Noted at the M2 sign-off (session 0800) for the view's M3 HUD: `World.Pop` / `PopCap` per player and a
    building's production queue + progress come with M3-4~~ → **available since M3-4** (session 0925) for the
    view's M3-V3 production card: `World.HalfPop` / `HalfPopCap` (per-player spans, half-pop units: show
    `n / 2`, ".5" for odd), `World.CanTrain(player, slot, type, out TrainError)` (why a button is greyed),
    `Buildings.QueueCount` / `QueueTypeAt(slot, i)` / `Progress` / `TrainTicks(type)` / `HasRally` /
    `RallyPosition` / `ReservedHalfPop(slot)`, `GameData.UnitsTrainedAt(buildingType)` (the card's buttons,
    sorted by id), `Command.Train(player, point, type)` / `CancelTrain(player, point, index)` /
    `SetRally(player, buildingCell, target)` / `ClearRally(player, point)`. Since M3-H1 a pushed unit's
    `PrevPosition` equals its `Position`, and `NavFlags.Pocket` (32) cells are `Blocked`.
12. Noted (M3-V1, session 0925, BUG-0107 item 3): the view keys its minimap resource layer and props relist on
    `NavGrid.Version`, which building changes bump too. A resource-only version (or a `ResourceStore` change
    counter) would let the view redraw only on fells; low priority, the view can also diff `Resources.Count`.
    Not requested yet.
13. Available since M3-5 (session 1131) for the view's M3-V3 production card: `World.CanResearch(player, slot, tech,
    out ResearchError)`, `World.HasTech` / `Age` / `TechBonus(player, unitType, TechStat)`, `GameData.Techs` /
    `TechsResearchableAt(buildingType)` (sorted ids), `Buildings.QueueIsTechAt(slot, i)` / `ItemTicks(slot, i)` (a
    queue item is a unit or a tech; `QueueTypeAt` returns the tech id when `QueueIsTechAt`), `Command.Research(player,
    point, tech)`; `CancelTrain` cancels techs too. ~~Requested of the view by the sim (M3-6): the view's `ui.json`
    must carry `placement.requires` / `train.requires` / `research.requires` texts before M3-6's `Requires` members
    merge~~ → **done in M3-V3** (session 1415; `UiText.ForwardKey` tolerates a key with no member yet).
14. Data track requests (D3): none; building `requires` and faction techs schemas are on `main`. The common techs' text
    (`common/techs.json`) waits for D4 because M3-6 edited that file (`age_ii.requiresAnyOf`).
15. ~~**Data track, from D3 (session 1415, BUG-0112), first item of the sim's M3-H2:** the sim-owned
    `Stress/ConstructionFuzzStressTests` (15 rows), `NeverSealTests` (8) and `RequirementFuzzTests` seeds 2-3 assume no
    shipped building has a `requires`; with D3's `buildings.json` they fail~~ → **done in M3-H2** (session 1715):
    `TestSim.DataWithoutBuildingRequires` / `ConfigWithoutBuildingRequires` for the geometry oracles, the requirement
    fuzz rewrites both factions; D3 merged, golden regenerated once (`data-hash` 702859B867AAC412). Rule kept: fixtures
    that read data values are named in the sim brief whenever a data task changes those values.
17. Noted for the data track's D4 (session 1715): `common/techs.json` is the sim's file; D4 edits its `displayName` /
    `description` strings only, as a named exception, and the sim's M4-1 does not touch it that session. If the data
    track needs a non-text change there, it comes here as a request.
16. Noted by the view (M3-V3 QA): `CanTrain` has no "pop cap" reason, so a train button stays live at the cap and the
    item waits inside the queue (the M3-4 rule). If the owner wants greyed buttons at the cap, the sim adds a
    `TrainError.PopFull` ordered after `CannotAfford` (one line + tests) and the view keys `train.pop_full`. Not requested.
18. ~~**From the view's M3-V4 QA (session 2315), the sim's next task's first item: BUG-0146 (S2).** Gatherers of a tree
    open on one side stand `Gathering` 1.7 m out of reach for the rest of the match~~ → **done in M4-2a** (session
    2026-10-08-0313): `MovementSystem.QueuesBehind` / `StandArrival2`, three stand points per cell edge; the seed-21 row
    un-skipped (the hash substituted in code), `M3PlayableTest` seed 21 PASS. The seed-6 corridor oscillation did not
    reproduce (a builder walking out against two gatherers, not the arrival rule); refile with a replay if seen.
19. Available since M4-1 (session 2315) for the view's combat views: `UnitStore.Hp` (and `UnitDef.Hp` for the max),
    `UnitState.Attacking`, `UnitStore.Target` / `TargetIsBuilding` / `WindupTicks` (a swing is starting when it is set),
    `World.Deaths` (one tick's `DeathEvent`s: victim handle, building or not, type, owner, killer's owner, position; read
    between ticks, emptied at the next tick's start), `World.Kills` / `Losses` per player, `SimConfig.Combat` (the test
    switch, for the M2 scenes of BUG-0147). Nothing requested yet; the `Attack(target)` command comes with M4-2a.
20. Available since M4-2a (session 2026-10-08-0313) for the view's M4-V2: `Command.Attack(player, unit, target,
    isBuilding[, queued])` (kind 16; dropped by the sim for a dead / own / forbidden target or a unit that cannot fight
    yet), `CombatMode.Ordered` (`UnitStore.Mode`), `UnitStore.QueuedTarget(entry)` for a queued Attack's handle (its
    `QueuePosition` is **not** a point on an Attack entry: a waypoint overlay must skip it, BUG-0153 item 1),
    `AttackDef.Targets`. Replay format 4 carries `combat`, so `ReplayPlayer.Run(replay, data)` needs no override (the
    `combat:` parameter stays for format 3 files recorded off). Used by M4-V2 (session 2026-10-08-0913). ~~Noted for
    M4-2b: the view's M4-V3 projectile visuals need a projectile store with positions and a "landed" tick~~ → **done in
    M4-2b**, see 21.
21. Available since M4-2b (session 2026-10-08-0913) for the view's M4-V3 projectile visuals: `World.Projectiles`
    (`ProjectileStore`: `Capacity`, `Count`; read-only spans per slot `Alive`, `Position`, `PrevPosition` (the launch
    point on the firing tick), `Target` (the impact point; a led shot's moves a little each tick as it is re-aimed),
    `ProjectileTypeId` (`GameData.Projectiles`: `Key`, `Kind` aimed / lob, `SpeedPerTick`), `Owner`) and `World.Impacts`
    (`ProjectileImpact`: `Position`, `ProjectileTypeId`, `Owner`, `Hit`; this tick's landings in slot order, emptied at
    the next tick's start like `World.Deaths`: read from `SimRunner.Ticked`). The sim flies straight at constant speed;
    the view draws any arc. BUG-0184 note: a re-led bolt can step up to 1.8 m in one tick (nominal 1.25); interpolate
    `PrevPosition` → `Position`, never extrapolate. Docs/03 "Implementation (M4-2b)", "For the view (M4-V3)". Nothing
    requested yet. Data track: no request (D5 needed no schema; towers wait for M4-3, abilities for M4-4).
22. Available with M4-3a (session 2026-10-08-1814; **on `main` since the 2026-10-08-2144 integration**) for the view's M4-V4: `World.Fog` (`FogStore`, read-only): `Visibility(player)`
    (`ReadOnlySpan<byte>`, row-major `y * Width + x`, 0 unexplored / 1 explored / 2 visible, changes only on update ticks
    `tick % 4 == 1`), `Version(player)` (+1 per update: re-upload the texture when it moves), `IsVisible` / `IsExplored(player,
    cell)`, `CanSeeUnit(player, slot)` (own, cell visible, or revealed by a high-ground hit), `CanSeeBuilding(player, slot)` (any
    footprint cell visible). Combat also counts a unit's own sight at this tick, so for up to 4 ticks a unit may fire at what
    `CanSeeUnit` still hides (BUG-0216 item 3). Docs/03 "For the view (M4-V4)". The last-known-buildings ghost list comes with
    M4-3b. **Data track requests:** (a) ~~BUG-0230 item 2: make `Scenario/CounterTriangleTests`' `Fight` / `TimeToKill` public~~
    → **done in M4-H1** (session 2026-10-08-2144), see 23; (b) the towers' `sight` schema is on `main`: D7 pinned it;
    `attack` / `detector` still wait for M4-3b.
23. Available since M4-H1 (session 2026-10-08-2144) for the data track's D8: `Rts.Sim.Tests.Scenario.CounterTriangleScene`
    (public `Fight(keyA, keyB, seatA)` → `(Side A, Side B, int Ticks)`, `TimeToKill(attacker, n, building, max)`, `Side`,
    `CountFor`, `SameCostCount`, every scene constant named: `Budget` 1200, `MapSize` 64, `Seed` 7, `BlockWidth` 5, `Spacing` 1.8,
    `FrontOffset` 12, `SiegeMapSize` 48). For the view's M4-V4 nothing new beyond 22; M4-3b (next sim task) adds `Fog.Ghosts(player)`
    and `PlacementError.Unexplored` (the view's `ui.json` key `placement.unexplored`). Known limit for M4-V4 (BUG-0216 item 3, docs/03
    "Known limits"): combat may fire at a unit `CanSeeUnit` still hides for up to 4 ticks; draw a shot only from a visible cell.
24. Available since M4-3b (session 2026-10-09-0125) for the view's M4-V5: `World.Fog.Ghosts(player)` (`ReadOnlySpan<BuildingGhost>`
    indexed by building slot: `Generation` (0 = empty; with the slot, the handle to pass to `Command.Attack(..., isBuilding: true)`),
    `TypeId`, `Cell` (anchor), `Owner`, `Known`), `Fog.GhostCount(player)`; changes only on update ticks. Draw a ghost for an entry
    whose building the player doesn't see now (`CanSeeBuilding` false, or the slot's live generation differs). No site flag yet
    (BUG-0275 item 1). `Buildings.TowerTarget` (read-only span: the unit a tower aims at, for a turret view later);
    `PlacementError.Unexplored` (= 10; the view's `placement.unexplored` text is on `main` with it). `ProjectileStore` shots fired
    by a tower carry `AttackerIsBuilding` internally; the view's spans are unchanged. **For the data track (D9):** the
    `buildings.json` `attack` / `detector` schema is on `main`.
25. ~~From the view's M4-V4 QA (session 2026-10-09-0125): BUG-0280 (S3), the build ghost reveals hidden enemies~~ → **done in
    M4-4a** (session 2026-10-09-0724): `CanPlace` skips enemy units `Fog.CanSeeUnit` hides; the Build is dropped when it applies
    onto one (Producer default, docs/01 row). Item 24 used by M4-V5 (ghosts drawn, criterion 5 complete).
26. Available since M4-4a (session 2026-10-09-0724) for the view's M4-V6, all on `World` (docs/03 "For the view (M4-V6)"):
    `Data.Units[type].Abilities` (ability ids) and `Data.Abilities[id]` (`DisplayName`, `Description`, `Range`, `Radius`,
    `CastTicks`, `CooldownTicks`); per unit slot `Units.AbilityReadyTick[slot * DataLimits.MaxUnitAbilities + k]` (cooldown left =
    `max(0, ready - TickNumber)`), `Units.State == UnitState.Casting` (6; `ui.json` `states.casting` "Casting" is on `main` with
    it) with `Units.CastAbility` / `CastTicks` / `CastPoint` (`CastAbility >= 0` while `Moving` = walking into range);
    `Units.Statuses` (`Count[slot]`, entries at `slot * StatusStore.PerUnit + k`: `StatusId` → `Data.Statuses[id]` text / `Kind`,
    `Magnitude`, `TicksRemaining`, `SourcePlayer`); `World.AbilityEvents` (cast starts / resolves of the last tick, like
    `Deaths`). `Command.UseAbility(player, unit, abilityIndex, point, queued)`: the view decides "only the nearest selected caster
    casts" (docs/02). **For the data track (D10):** `abilities.json` / `statuses.json` accept what docs/03 "For the data track"
    lists (`targetGround` + `damage` / `applyStatus` only; the other kinds load as "not supported yet" until M4-4b).
27. ~~**From the view's M4-V5 QA (session 2026-10-09-0724): BUG-0311 (S3, sim).** An Attack on a gone building's ghost ends when
    `VisionSystem.UnitSeesFootprint` says the unit sees the rectangle's nearest point (15.7-15.96 m at sight 16), but the fog
    marks cells by their centres, so the entry (and the ghost) stays 19-26 s and a fresh Attack is refused by `MayAttack`~~ →
    **done in M4-H2** (session 2026-10-10-0624): `UnitSeesFootprint` is the fog's own `SeesFootprint`, so the order ends at
    the update the ghost goes (1 tick apart on both `QaGhostViewTest` seeds). The scene's `KNOWN BUG-0311` line no longer
    prints and it can run `--strict`; its mean-walk check is stale now: **BUG-0390, see 29.**
28. Available since M4-4b-2 (session 2026-10-10-0215) for the view's M4-V6c (docs/03 "For the view (M4-V6c)"): `World.Zones`
    (`ZoneStore`: `Capacity`, `Count`, `BlockerCount`; per slot `Alive`, `Owner`, `Center` (m), `AbilityId` (→
    `Data.Abilities[id].DisplayName` / `Radius`), `TicksRemaining` (1 on its last tick), `Radius(slot)`, `BlocksVision(slot)`),
    `Units.Statuses.BlindOf(slot)` (-1 none), `StatusSystem.SightOf(world, slot)`. Draw an enemy's blocking zone only where the
    player may see it (`Fog.Visibility` holds the hidden cells as explored; `Fog.CanSeeUnit` already hides the units in it).
    **For the data track (D10c):** the `createZone` effect and the `blind` status kind are on `main` (docs/03 "For the data
    track (M4-4b-2)"). The D10b text proposals (Telas Fire / Cusser / Slowed `description`) would have been requests to the
    sim; the Producer resolved them instead: the data track edits those `description` fields itself in D10c (a named
    exception, the sim off every data file that session) and regenerates the golden once.
29. **From the sim's M4-H2 QA (session 2026-10-10-0624), for the view track: BUG-0390 (S3, view file).** `QaGhostViewTest`
    seed 6's "walked at least 10 m on average" check (`Check(after < before - 10f, ...)`, line 296) fails once BUG-0311 is
    fixed: the order ends as soon as the nearest attacker's fog shows the ground (13.8 m out), so the four attackers' mean
    distance drops 3.8 m, not 10 (on the base the order outlived the ghost by 500 ticks and the check passed by accident).
    Replace it with: the unit that ended the order (the selected unit nearest the footprint when the ghost went) came within
    its sight of the footprint *and* walked at least 10 m toward it (per-unit start distances), and drop the `--strict` gate
    around the BUG-0311 check so `|ordersEnded - ghostGone| > UpdateInterval` always fails. **Conductor-dispatched on the
    view branch at the 0624 integration** (the merged loop is the gate); else the view's first item next session. Also
    available since M4-H2 for the view: `Fog.CanSeeBuilding` / `Fog.Ghosts` count a tower revealed by its own high-ground
    hit as seen (BUG-0270), so a tower that fires down at you is drawn for 2 s per hit without any view change.
30. Available since M4-4b-3a (session 2026-10-10-1015) for the view's M4-V6d (docs/03 "For the view (M4-V6d)"):
    `StatusKind.Frenzied` (= 3; a new marker arm, the default arm until it has a colour); `AbilityDef.Kind ==
    AbilityKind.SelfAura` (the card needs no target click: the button casts, sending any on-map point, the caster's position
    is the natural one; `Range` is 0, draw the radius around the caster; `Units.CastPoint` and both `AbilityEvent.Point`s
    are the caster's position); `Data.Units[type].Passives` (data, for a tooltip later). No shipped `selfAura` ability
    exists: a headless scene uses a test-only one (`SelfAuraTests.AddTo` is the sim-test pattern). Note for the view
    (BUG-0422 item 2, documented): a unit trained the same tick a Martyrdom fires is missed by it. **For the data track
    (D11):** the `passives` schema (`whenBelowHp` / `onDeath`), the `frenzied` kind and the Zealot's two passives are on
    `main` (docs/03 "For the data track (M4-4b-3a)" has the exact JSON shapes and what is refused); `Frenzied` is still in
    `PendingStatuses` (its description reports until D11 pins it).
31. Available since M4-4b-3b (session 2026-10-10-1314) for the view's M4-V7 slice 2 (docs/03 "For the view (M4-V7)"):
    `AbilityKind.Summon` (= 2; the same no-target card as `SelfAura`: the button casts, any on-map point; `Range` 0, `Radius`
    may be 0), `Units.IsSummoned(slot)` / `Units.ExpiresAtTick[slot]` (lifetime left = `ExpiresAtTick - TickNumber`),
    `DeathEvent.Expired` (a vanish: killer -1, no kill / loss, no corpse), **`World.MaxHp(slot)` / `MaxHpOf(player, type)` for
    the hp bars and the first-sight flash (BUG-0450, S3: `ViewApi.UnitHpBars` / `CombatViews.SyncBars` / `UnitViews._maxHp`
    still use `UnitDef.Hp`; the skipped `QA/ViewApi/UnitHpBarsMaxHpQaTests.AProphecyZealotAt70Of83_ShowsABar` row is the
    regression check; first item of the next view task)**. **From the view's M4-V6d QA for the sim (BUG-0461, S3):** a cast's
    walk-in hemmed in by own `Attacking` units gives up after 20 stuck ticks and drops the cast silently (a 0.17 m limit cycle
    on flat ground meanwhile); triaged to the end-of-M4 sim hardening with a direction (sim debt backlog). **For the data
    track:** the `summon` / `spawn` schema is on `main` (docs/03 "For the data track (M4-4b-3b)"); no shipped summon before
    Shadow (M8). Used by M4-V7 slice 2 (session 2026-10-10-1954): BUG-0450 fixed through `UnitViews.FillMaxHp` + the span-only
    `UnitHpBars.Collect` overload (a `World` can't be passed into `ViewApi`: the source guard).
32. Available since M4-5 (session 2026-10-10-1954) for the view's **stealth visuals, now an M8 task** (docs/03 "For the view
    (M4-V8)"): `World.IsStealthed(slot)` (own stealthed units translucent), `World.IsDetectedBy(player, slot)` (a detected-enemy
    marker), `Units.Statuses.HasStealth(slot)` (a Revealed stealth carrier), `UnitDef.Detector` / `BuildingDef.Detector` (rings;
    finished buildings only detect). `Fog.CanSeeUnit` already hides an undetected stealthed enemy, so nothing is needed before M8.
    **For the data track (D12):** the unit `detector` schema (`units[k].detector`, m, above 0, at most 64) and the `stealthed` /
    `revealed` statuses (numberless texts, no magnitude) are on `main` with this integration (docs/03 "For the data track (M4-5)");
    both texts sit in `AbilityContentTests.PendingStatuses` until D12 pins them against docs/02 "Status effects". Nothing requested
    of the sim by the view's M4-V7 slice 2. (D12, accepted in session 2026-10-10-2334 and held on its branch, lands at the next
    session's data merge: both casters `detector 10`, the texts pinned.)
33. **From the view's M4-VH3 (session 2026-10-10-2334), for the sim track, low priority (M6 export):** a text-taking `DataLoader`
    overload (load `GameData` from already-read JSON strings or a `Func<string, string>` reader keyed by the relative path) so the
    view can load `game/data/` and `ui.json` from a `.pck` instead of `ProjectSettings.GlobalizePath("res://data")`; the directory
    overload stays for tests and the CLI. Plan it with the first M6 export task or fold it into a sim hardening session.
34. ~~**Available since M4-H3 (session 2026-10-10-2334) for the view's M5-V1:** `AbilityEvent.Dropped`~~ → **used by M5-V1** (session
    2026-10-11-0314: `ViewApi/LostCastCues`, the cue, `SfxEvent.Refused`). `Fog.Ghosts` entries carry `Site` (the remembered building
    was a construction site when last seen; not hashed): still undrawn (the view's foundation item).
35. **Available since M5-1 (session 2026-10-11-0314).** For the **data track (D14):** the `ai.json` schema is on `main`, required for
    every faction: docs/03 "For the data track (M5-1)" has the exact shape and every refusal (difficulties `easy` / `normal` / `hard`
    each with all eight fields; `buildOrder` 1-256 steps, `at` exactly one of `time` / `workers` / `pop`, `do` `build` / `train` /
    `research` / `expand`, `count` 1-200 and none on `research`, the same at + do + id twice an error; one composition per age with
    the faction's own unit ids and weights above 0; `attack.minArmyPop` / `minArmyCost`; `retreat.hpFraction` / `lossFraction`).
    **Not in `ContentHash` until M5-2**: a D14 edit moves no golden. For the **view track:** `Rts.Sim.Ai.PlayerView` exists (the AI
    seat for M5-V2 comes with M5-2's `AiPlayer`); nothing requested. ~~**From the data's D13 QA for the view: BUG-0560**~~ → fixed in
    M6-V1 (session 2026-10-11-0634). Noted by the sim for M5-2: `World.CanPlace` counts hidden enemy buildings, so the AI needs a
    fog-safe placement check before it places anything → `PlayerView.CanPlace` (M5-2).
36. **Available since M5-2 (session 2026-10-11-0634).** For the **view track (M6-V2 / M5-V2):** `SimConfig.Seats`
    (`ImmutableArray<PlayerSeat>`; `PlayerSeat(Faction, SeatController.Human | Ai, AiDifficulty)`; empty = today's defaults;
    `Validate` refuses a count other than `PlayerCount`, an unknown faction, an AI seat whose faction has no `ai.json`),
    `SimConfig.SeatOf(p)` / `HasAiSeat`, `World.FactionOf(p)` reads the seat; `GameData.Factions[f].DisplayName` for the dropdown;
    `AiDifficulty` has no display name yet (M5-4 / `ui.json`: the view may key `setup.difficulty_easy` etc. itself). A replay recorded
    with an AI seat is format 5 and plays back with every seat human. `ReplayRecorder` logs the AI's commands like a human's. For the
    **data track:** `rules.json` `ai` (sim-owned, `common/`) holds the generic knobs; `ai.json` is outside `ContentHash` until M5-3.
    **From the data's D14 for the sim (M5-3 / M5-4):** (a) the composition weights are pop percentages on the pages while
    `ProductionManager` compares unit **counts** per weight: weigh by the unit's pop (`UnitDef` pop) or say the weights are counts;
    (b) no schema field for the sketches' expansion condition (army ≥ 12 pop and no enemy near the base for 60 s), the Whirlwind
    4:30 first raid, a rally point / watchtower at the choke: the military / scout managers decide, then the schema if needed;
    (c) `expand` waits for both the step's time and `expandAfter` (documented; keep or drop the step's time); (d) the build order's
    houses past the cap vs the economy manager's house rule (a balance check, data D15). **From the sim's M5-2 QA for M5-3:**
    BUG-0574 (S3) in the economy manager's marks; the AI starves of wood on a generated map until it scouts. → **(a) done in M5-3**
    (session 2026-10-12-1930: production weighs by half-pop); (b) still open (M5-4); (c) documented; (d) measured by D15 (keep).
37. **From the view's M6-V2 (session 2026-10-12-1930), for the sim track (M5-4a, planned):** docs/02 "Game flow" 4: `World.IsDefeated(p)`
    (no live building incl. sites, and the player had one once; never-built players never defeated), `World.DefeatEvents` (player,
    tick) like `AbilityEvents`, `Simulation.MatchOver` (at most one undefeated player among those that ever built); hashed; the AI of a
    defeated seat stops. Until then the view's `ViewApi/MatchOver` watch prints the dev line. → **Not built in M5-4a** (the PLAN's
    cutoff: only under 700 dev lines; the start locations alone took 972) → **done in M5-4b** (session 2026-10-13-0916): `World.IsDefeated(p)`
    / `EverBuilt(p)` / `DefeatEvents` (one-tick span of `DefeatEvent { Player, Tick }`), `Simulation.MatchOver` (latched), the door
    drops a defeated player's commands where they apply, `PlacementError.Defeated`; docs/03 "Implementation (M5-4b)" → "For the view
    (M6-V4)". The view's M6-V4 keys off them next session. ~~**For the view's M6-V3 (planned):** `World.StartLocations`~~ → **landed in M5-4a**
    (session 2026-10-13-0146): `World.StartLocations` / `NavGrid.StartLocations` (read-only span of `StartLocation { Cell, Level }`,
    player order, empty on hand-built maps, not hashed); `StartBase.Plan` uses them itself, so the view's M6-V3 needed no call change.

38. **From the view's M6-V2 / M6-V5 (written at the 1616 ACCEPT; referenced since 1930 but never listed), for the sim track:** docs/02
    "Game flow" 2 / 4: a **team** per seat (`PlayerSeat.Team`, 0 = none), the defeat rule and `Simulation.MatchOver` per team (the
    last *team* standing wins; allies' units and buildings are not enemies for combat, vision is shared or not: the Producer decides
    at the brief). Needed before the setup's team column; the slot count itself (2-4) needs no sim change beyond four fair start
    locations (`MapGenParams.StartLocations`).
39. **From the view's M6-V4 (session 2026-10-13-1616), for the sim track (after M5-4c slice 2):** per-player, read-only spans like
    `Kills` / `Losses` for docs/02 "Game flow" 5: (a) gold and wood **gathered** (delivered to a drop-off) over the match; (b) units
    trained (finished from a queue; dev spawns apart); (c) kills and losses split into units and buildings; (d) buildings built
    (finished, sites apart) and buildings lost; (e) the tick the player reached Age II (-1 before); (f) an army-value sample every N s
    (the summed cost of the player's live non-worker units; N in `rules.json`), kept for the whole match, for the graph. Hashed or not
    as the sim decides, documented. The view's `MatchStats.Columns` takes each as one getter + one `ui.json` label.
    **Note (0946 PLAN):** (e) becomes "the tick of the first key tech" once the Ages are dropped (owner direction 2026-10-14); build
    it as a per-tech researched-tick span, not an Age field.
40. ~~**From the 0946 PLAN (owner playtest note 9), for the view track:**~~ → **done** (session 2026-10-14-0946: the sim's M5-4e shipped `World.LiveUnits(p)` / `ProductionBuildings(p)`; the view's M6-V7 re-staged the three match-over scenes; `AbilityViewTest` left: BUG-0711). the sim's M5-4e changes the defeat rule this session (no live
    unit and no finished unit-producing building; houses / camps / Forge / towers / sites don't count; a never-built player still never
    defeated). `World.IsDefeated` / `EverBuilt` / `DefeatEvents` / `MatchOver` keep their names; `PlacementError.Defeated` unchanged. New
    read-only: `World.LiveUnits(p)` and `World.ProductionBuildings(p)` (counts). The view's defeat scenes are re-staged by M6-V7 the same
    session so they hold under both rules.
41. ~~**For the sim track (owner playtest note 8, the view's next task):**~~ → **done by the view alone** (session 2026-10-15-2325, M6-V8:
    `ViewApi/SeenResources.Amount` / `UpdateAmounts` / `InSight`, view-owned, read-only, hash twin equal over 2,005 ticks; the sim added nothing).
    (kept for the record:) the selection panel needs a node's remaining amount as the
    *player* may know it: `World.Resources.Remaining[slot]` while the node is visible; for an explored-but-unseen node the fog's memory
    keeps presence only (`FogStore.NodeAt`), not the amount. The view will show "last seen: N" from its own per-node cache (view state),
    unless the sim prefers to add a remembered amount to `NodeMemory` (hashed or not, the sim's call). Not blocking; the view can start
    with "visible only".
42. **For the sim track (M6 criterion 3's save half; from the 1654 ACCEPT):** a **save format** and `Simulation` save / load entry
    points: the whole sim state (every store, the fog bits, the flow-field cache or its rebuild, the RNG streams, the tick, the
    seats) **plus the state outside the hash that decides the next tick**: `World.SurroundClaim` / `SurroundHolder` (BUG-0733),
    the AI's `Blackboard` whole (M5-4b's defense marks, M5-4c's casts, M5-4f's `DefenderGeneration` / `DefendAll` / `DefendAnswer` /
    `WaveMinHalfPop` / `WaveMinCost`), the `BuildingGhost.Site` memory. A round-trip test compares those arrays directly, not only
    the hash (docs/03 "Save/load and replays"). Text (a version / data mismatch) stays in `ui.json`. Not blocking the view: its
    criterion-3 half that needs no sim change is replay playback (the public `Rts.Sim.Replays` API), planned first. Planned for
    the sim after M5-4f lands and before M5-4d, or with M5-H3 if the Blackboard layout is still moving.

## Feature queue: sim track (feature sessions, in order)

1. ~~M3-6 `requires` resolution and gating~~ → **done** (session 1415). ~~M3-H2 end-of-M3 sim hardening + D3 landing~~ →
   **done** (session 1715; BUG-0113 / 0094 left for the next hardening).
2. ~~M4-1 combat slice 1~~ → **done** (session 2315: melee acquisition, chase, leash, holders, damage formula, death;
   BUG-0139 / 0144 / 0149 / 0142 left).
3. ~~BUG-0146 (S2) first, then M4-2a~~ → **done** (session 2026-10-08-0313: the wedge fix, `Command.Attack` kind 16,
   `attack.targets`, replay format 4; BUG-0151 / 0156 / 0157 S3 and BUG-0153 S4 left).
4. ~~M4-2b projectiles / splash / friendly fire / min range + the counter-triangle rows + BUG-0156~~ → **done** (session
   2026-10-08-0913; 1 fix round: the lead rule BUG-0183, BUG-0180 / 0181 / 0182 item 1; BUG-0182 item 2 and BUG-0184 S4
   left; golden `data-hash` moved twice, `k` lines identical).
5. ~~M4-3a fog of war + the high-ground rule + vision-gated targeting + the reveal + buildings' `sight` + the view surface~~ →
   **accepted** (session 2026-10-08-1814, resumed 1435; 1 fix round: BUG-0214 re-baseline, BUG-0217 gate cost; BUG-0184 item 1
   folded in; golden regenerated once), held one session for BUG-0219, **on `main` since the 2026-10-08-2144 integration**.
6. ~~M4-H1, the sim's hardening session~~ → **done** (session 2026-10-08-2144, 0 fix rounds: BUG-0215 / 0211 / 0216 / 0230 item 2 /
   0149 / 0134 / 0133 / 0158 / 0113 item 1 / 0153 items 3-4; BUG-0241 S3, BUG-0242 S4 filed; BUG-0157 deferred by decision).
7. ~~M4-3b towers / the last-known list / explored placement~~ → **done** (session 2026-10-09-0125, 1 fix round: BUG-0276 two
   Perf rows' setup, BUG-0274 five view scenes fixed on the view branch; BUG-0270 / 0273 S3, BUG-0271 / 0272 / 0275 S4 left;
   golden `data-hash` moved once, `k` lines identical; lands together with the view's M4-V4).
8. ~~BUG-0280 first (Requests 25), then M4-4 slice 1~~ → **done as M4-4a** (session 2026-10-09-0724, 2 fix rounds: BUG-0300 /
   0303 S2 (the `ui.json` line, the replay whitelist), BUG-0301 S3 (the pulse clock), BUG-0302 a + d; BUG-0302 b + c S4 left;
   golden `data-hash` moved once, `k` lines identical).
9. ~~M4-4b-1 the `abilityCooldown` tech effect + the DoT whole-seconds rule + the Cusser (`damage` `buildings` / `friendlyFire`)~~ →
   **done** (session 2026-10-09-1155, 0 fix rounds, half-size under the usage cap; BUG-0330 S3 pre-existing filed; golden
   `data-hash` moved once, `k` lines identical, 41842085985611BF in `SameGameDataHashes`).
10. ~~M4-4b-2 zones / Blinded / Sandstorm~~ → **done** (session 2026-10-10-0215, 0 fix rounds; BUG-0360 / 0361 S3 and BUG-0363
    S4 filed; golden `data-hash` moved once, `k` lines identical, 5896D3E7C9FD36AD in `SameGameDataHashes`; ~1,780 lines).
11. ~~M4-H2, the sim's hardening~~ → **done** (session 2026-10-10-0624, 0 fix rounds: BUG-0360 / 0361 / 0363 items 1-2 / 0330 /
    0311 / 0241 / 0157 / 0270 / 0242 fixed, the docs/02 "allied" wording; BUG-0390 S3 (a view file) and BUG-0391 S4 filed; not
    reached: BUG-0302 b + c, 0275 1-2 (item 1 built and backed out: it moves the seed-21 hash), 0271 / 0272, 0144, 0142 1-2, 0113
    item 2, 0094; golden untouched; ~1,430 lines). Counter 0 / 4.
12. ~~M4-4b-3a the `frenzied` status kind, unit passives (`whenBelowHp`, `onDeath`) with the Zealot's Frenzy of the Apocalypse +
    Martyrdom, the `selfAura` kind, the memory re-baseline itemised~~ → **done** (session 2026-10-10-1015, 0 fix rounds, QA
    PASS_WITH_ISSUES: BUG-0420 / 0421 S3, BUG-0422 S4 filed; golden `data-hash` moved once, `k` lines identical, D22C849A175F9ED5
    in `SameGameDataHashes`; memory bound 231,920,000 with every byte since M4-4a itemised; ~2,600 lines incl. QA). Counter 1 / 4.
13. ~~M4-4b-3b BUG-0421 (hp techs raise hit points) + BUG-0420 (the exact line) + summons (`summon` kind, `spawn` effect,
    temporary units with `ExpiresAtTick`, the expiry through the death path)~~ → **done** (session 2026-10-10-1314, 0 fix
    rounds, QA PASS_WITH_ISSUES: BUG-0450 S3 (view code: hp bars on the type's hp), BUG-0451 S3 (pre-existing: the
    `EmptyTick(2500)` gate has no headroom), BUG-0452 S4 filed; `targetUnit` / `autocast` **deferred** by Producer decision
    (docs/01) until a faction's data needs them; golden untouched (`ContentHash` extended only for spawn effects); ~2,990 lines
    incl. QA; criteria 6 and 8 ticked). Counter 2 / 4.
14. ~~M4-5 stealth / detection (criterion 7)~~ → **done** (session 2026-10-10-1954, 0 fix rounds, QA full PASS_WITH_ISSUES: BUG-0480 /
    0481 S3, BUG-0482 S4 filed, none reachable with shipped data; BUG-0452 items 1-3 folded in and fixed; the `stealthed` / `revealed`
    kinds, `rules.json` `stealth`, a unit `detector`, `StealthSystem` at the end of phase 5 with a derived per-slot per-player mask,
    `World.HiddenFrom` on `UnitSeesUnit` / `BuildingSeesUnit` / `Fog.CanSeeUnit` / the scan shortcut, the Revealed rule at delivery /
    resolve; test-only sources; golden `data-hash` moved once, `k` lines identical, 889D6BEA3A49F08E in `SameGameDataHashes`; memory
    +9,144 itemised, 29,560 B margin; ~2,600 lines incl. QA; criterion 7 ticked, **M4 10 / 10**). Counter 3 / 4.
15. ~~M4-H3, the end-of-M4 sim hardening~~ → **done** (session 2026-10-10-2334, 0 fix rounds, QA full PASS_WITH_ISSUES: BUG-0461's cast
    half (`CastWalkInBlockerRange` 1.5 m / `CastWalkInPatienceSeconds` 3 s, `OwnFighterAhead`, `AbilityEvent.Dropped`), 0480 / 0481 /
    0482, 0451 (re-baselined 550 µs; BUG-0510 filed), 0401 (`ReachableOnFoot`), 0391, 0275 item 1 (`Site`, unhashed), 0094, 0302 c;
    0271 / 0272 / 0142 items 1-2 wontfix; BUG-0511 / 0512 S3, BUG-0513 S4 filed; not reached: BUG-0144, 0113 item 2; golden untouched;
    ~1,770 lines incl. QA). Counter 0 / 4. **M4 signed off at this ACCEPT.**
16. ~~M5-1, `PlayerView` + the `ai.json` schema~~ → **done** (session 2026-10-11-0314, 0 fix rounds, QA full PASS_WITH_ISSUES:
    `Rts.Sim.Ai.PlayerView` + `SeenUnit` / `SeenBuilding` / `SeenResource`, `FogStore.NextSeenUnit`, the oracle fuzz 3 seeds x 2,000
    ticks x 4 players, the source-scan + values-only guards, the hash twin, the perf row; `AiDef` + `DataLoader.Ai` + both minimal
    `ai.json`; `ai.json` outside `ContentHash` by decision; golden untouched; BUG-0540 S3 (the node-liveness leak: M5-2), BUG-0542 S3 →
    S4 (the perf layout, ruled), BUG-0541 S4 filed; ~3,200 lines incl. QA). Counter 1 / 4. **Criterion 1 ticks with BUG-0540's fix.**
17. ~~M5-2, the AI's first decisions~~ → **done** (session 2026-10-11-0634, 1 fix round, QA FAIL → PASS_WITH_ISSUES: BUG-0540's node
    memory (`FogStore.NodeAt`, unhashed), `SimConfig.Seats` + `PlayerSeat` + replay format 5 (golden header only), `AiPlayer` /
    `Blackboard` / `BuildOrderExecutor` / `EconomyManager` / `ProductionManager` / `ICommandSink` / `PendingOrder`, `PlayerView.CanPlace`
    / `CanPlaceAnywhere` / `CanTrain` / `CanResearch`, `rules.json` `ai` + `AiRulesDef`; BUG-0570 / 0571 S2 and 0572 S3 fixed in the
    round; BUG-0574 S3, BUG-0573 S4 filed; `ai.json` into `ContentHash` **moved to M5-3** (Producer decision); ~3,200 lines incl. QA
    against ~1,500: item 5 not sliced, accepted). Counter 2 / 4. **Criterion 1 ticked.**
18. ~~M5-3, the AI fights and scouts~~ → **done** (session 2026-10-12-1930, 2 fix rounds, QA FAIL → FAIL → FAIL on the carried BUG-0592
    alone, which the Producer re-triaged S2 → S3: `ScoutManager` (the ring, the mirror, the 56 m map grid), `MilitaryManager` (rally by
    Moves, waves at the thresholds × `waveSizeMultiplier`, retreat, `waveCooldown`, `MaxTries` 3), the enemy memory, the rebalance,
    BUG-0574, pop weights, seven `rules.json` `ai` knobs, `ai.json` + `rules.json` `ai` in `ContentHash` (golden `data-hash` →
    8409DB7517579FEF over two regens, `k` lines equal), BUG-0590 (pre-existing S2: `CombatSystem.TooNear`) + the four page cells
    (BUG-0597 S1), BUG-0591 / 0593 / 0595 / 0596 fixed in the rounds; BUG-0598 S3, BUG-0594 S4 filed; ~3,800 insertions incl. ~1,600
    of QA and fixes against ~1,500). Counter 3 / 4. **Criteria 2 and 3 advanced.**
19. ~~M5-4a, start locations~~ → **done** (session 2026-10-13-0146, 0 fix rounds, QA full PASS_WITH_ISSUES: `MapGenParams`' five `Start*`
    fields (2 locations, clear radius 8 cells, edge margin 10, mines within 18 m, forest within 24 m; 0 = none), `MapGenerator.FindStarts`
    (site disc of open ground, exact point mirror, quarter turns for 3-4, no RNG, redraw through `MaxAttempts`, the flat fallback carries
    them), `Heightmap` / `NavGrid` / `World.StartLocations` (not hashed), `ResourcePlacer` start mines round-robin + one start forest off
    the exit side, `StartBase.Plan` centred on the location (hand-built maps unchanged, 166 rows byte-identical), `AiScenes.Generated` on
    them, replay **format 6** (golden header only, `k` lines unchanged, `data-hash` unchanged), BUG-0592 fixed (known wood 386 / 2 / 2,
    first in 902 / 720 / 472 on seeds 1-3, asserted 1,200 / 2,000; AI v AI 468-902, asserted 3,000); **Requests 37 and BUG-0598 not
    built** (the PLAN's 700-line cutoff; 972 dev lines); BUG-0620 / 0621 S3, BUG-0622 S4 filed; ~2,000 lines incl. QA). Counter 4 / 4.
    **M6 criterion 7's generator half landed.**
20. ~~M5-H1, the sim's hardening~~ → **done** (session 2026-10-13-0527, 0 fix rounds, QA full PASS_WITH_ISSUES: BUG-0598 (`WaveBusyTick`,
    an idle wave ends after `waveCooldown`), BUG-0640 (root cause: the builder's one-tick Idle blink; `PlayerView` reads it as Building,
    `EconomyManager.Sites` resumes a site nobody builds after `StalledThinks` 3 thinks: Malazan 36,000 11 / 11, cap 98), BUG-0620
    (`Validate`'s border rule + a flat-map centre), BUG-0621 (`FindStarts` same level + half the smaller side apart: 0 / 100 mismatched
    pairs on every size, closest 129 / 206 / 350 m), BUG-0511 (`OwnFighterAhead` corridor + level), BUG-0512 (`ChaseCell`,
    `ReachableOnFoot` for a stopped chaser: 500 units 0 give-ups), BUG-0594 / 0622 items 2 + 4 / 0573 item 1 / 0541 item 2 / 0513 /
    0542's gated row; BUG-0651 S3, BUG-0650 S4 filed; golden untouched; 563 dev lines + 501 QA). Counter 0 / 4.
21. ~~M5-4b slice 1, the defeat rule + the AI's defense~~ → **done** (session 2026-10-13-0916, 1 fix round, QA full FAIL → PASS:
    `World.Defeat.cs` (`IsDefeated` / `EverBuilt` / `DefeatEvents`, phase 11b, `MatchOver` latched after BUG-0672), the door drop at
    apply, the AI of a defeated seat silent, `PlacementError.Defeated` + `ui.json` `placement.defeated`, hash bits 34-36;
    `Ai/DefenseManager` (kept hp per building slot, intruders within `defendRadius`, the point's second-trigger rule BUG-0673, `Move`
    home then `AttackMove` BUG-0670, workers flee to a drop-off away from the point and are released `defendFor` after their flee
    BUG-0671), `rules.json` `ai` `defendRadius` / `defendFor` / `workerFleeRadius` in `ContentHash` (golden header →
    72EF431E70C49C39, `k` lines equal); BUG-0670 / 0671 S2 + 0672 / 0673 S3 fixed in the round; BUG-0674 S3 (view file), BUG-0675 S4
    filed; ≈ 1,197 dev lines + ~1,100 QA). Counter 1 / 4. **Criteria 3 and 5 advanced; Requests 37 done.**
22. ~~M5-4c slice 1, the ability manager~~ → **done** (session 2026-10-13-1616, 0 fix rounds, QA full PASS: `Ai/AbilityManager` last
    in the think, the group-in-reach rule with `ai.castMinTargets` 2, the building target for the Cusser, the friendly-fire guard,
    judged by the cooldown with `MaxTries` 3; `PlayerView.TryGetAbilityReady` / `TryGetCast`; 8 Hard seats × 16 casters 0.4 ms / 0 B;
    the golden header moved once; BUG-0700 fixed; BUG-0701 S3 / BUG-0702 S4 left; **the knobs deferred under the 700-line hard stop**;
    criterion 2 ticked).
23. ~~M5-4e, the new win condition~~ → **done** (session 2026-10-14-0946, 1 fix round, QA full FAIL → PASS: out = has built + no live
    unit + no finished unit-producing building (`PlayerLedger.LiveUnits` / `ProductionBuildings`, kept at `UnitStore.TrySpawn` / `Free`
    and `AddFinished`, derived, not hashed; `World.LiveUnits(p)` / `ProductionBuildings(p)`), phase 11b as before, the golden unmoved;
    `TestSim.KeepIn` for scenes that build before any unit; BUG-0705 S2 + BUG-0706 S3 fixed in the round; BUG-0703 S3 (the collapse
    rule, Producer decision (c), M5-H2) and BUG-0704 S4 left; ≈ 772 dev lines + QA's 428). Counter 3 / 4. **Criterion 5 advanced.**
24. ~~Pathing slice 1 "surround a building" (M5-P1)~~ → **done** (session 2026-10-15-2325, 1 fix round, QA full FAIL → PASS: `Combat/SurroundRule`
    (a ring of standing spots round a live building, 2 per footprint cell a face + 4 corners, claimed nearest-first in a fixed order,
    `World.SurroundClaim` / `SurroundHolder` checked both ways on read, **not hashed: M6 save state, BUG-0733 wontfix by Producer decision**),
    `CombatSystem` hooks (`ChaseCell` / `Chase` / `QueuedAtRing` / no planting on the way / the claim follows a striker / no give-up while
    queued or walking to a reachable claimed spot in every mode, BUG-0731), `CombatConstants.SurroundSpotsPerCell` 2; 20 line units striking
    within 10 s **17-20 / 20 (was 9-11)**; 0 B a tick; the golden untouched; BUG-0730 / 0731 fixed in the round, BUG-0732 S4 left (M5-H2),
    **BUG-0734 S2 = the data pages' siege cells, re-printed on the merged tree at the integration**; 274 dev + 353 test + 74 docs lines, QA +1,000).
    Counter 4 / 4. **Then, in this order (inbox requests before roadmap work):** (0) **M5-H2 hardening** (BUG-0703 collapse rule first, BUG-0732,
    BUG-0745's replay probe, BUG-0701, the S4 list; brief in `studio/handoff.md`);
    (a) **M5-4f proportional defense + `gatherRate` gold 0.85 / wood 0.75 with the seed-21 re-record** (one header move, the session after D19)
    (the design in For your review 2026-10-14: `ai.defendRatio` / `defendMinValue` / `recallHallHp`, home army first, part of the wave
    above its start threshold, the whole wave for a dying hall) + **D15 C** (the executor skips a house step at the pop cap) + BUG-0702
    item 1 (the densest cluster) · QA full, ~900 lines; (b) ~~pathing slice 1~~ done (above); (c) **pathing slice 2 "flank through your own line"** (a melee walker blocked by own standing ranged units routes round
    them; BUG-0028 / 0032 / 0046 family; the House row's far-corner jam, 17-18 / 20); (d) **M5-4c slice 2**, the knobs (`gatherBonus` with an exact hashed carry, `focusFire`,
    `pullBackWounded`) → criterion 4; (e) **Requests 39 (+ 38)**, the stats counters (39 e as a per-tech tick) and teams; (f) **D14 b**,
    the strategies schema (`ai.json` `strategies[]`, age-agnostic: a mix switches on a tech or a time; the pick by the AI's seeded RNG
    at start, Easy the first) with minimal entries, the data track fills the openings after; (g) **M5-4d** expansion / rebuilding /
    periodic re-scouting / high ground before attacking up / **hunting the last enemy units** when no enemy building is known
    (criterion 3); (h) **D14 c** counter-composition (a decayed per-type enemy memory, the counter table in `rules.json` `ai`, a strength
    per difficulty); (i) **M5-5** the 20-minute winner test (criterion 5); (j) M5-6 the playable criterion with the view's menu match.
    Teams (Requests 38) when M6's 2-4 slots need them. **After M5:** D14 d (squads: home guard, streamed reinforcements, harass) if
    the owner wants it after playing a-c; the **Ages → tech tree** design task (docs first, owner review, then data + a small sim
    clean-up); a second start forest per base if D18 shows the generator short of trees (`MapGenParams`, next sim hardening).
25. ~~M5-H2 sim hardening~~ → **done** (session 2026-10-16-0847, 1 fix round, QA full FAIL → PASS: the collapse rule (`World.Collapse` in phase
    11b, `DeathEvent.Collapsed`, neither a kill nor a loss, `CombatSystem.DropDeadReferences` after), owner-layered surround claims +
    the reachable-spot filter (BUG-0732), the flow-step-alone slide against a wall (BUG-0745's sim half, `WallPinTests`), the fog's
    near-blocker mask + `ZoneStore.BlockerVersion` + an 8-storm `AiPerfTests` row (BUG-0701), the no-stacking rule with retries
    (BUG-0702 items 2-3, BUG-0760), `EconomyManager.MaxJoins` (BUG-0650), BUG-0675 c, BUG-0704 item 4, BUG-0651 closed as not
    reproducing; QA's `CollapseRuleQaTests` (8 rows) + `SimHardeningM5H2QaTests`; the golden untouched; memory bound → 232,306,000
    (+167,984 B itemised); BUG-0761 S2 (data-owned, fixed by D-H5) / 0762 / 0763 found and fixed; BUG-0764 S3 filed by the Producer;
    not reached: CLAUDE.md's milestone line, BUG-0144, BUG-0113 item 2; +247 / −30 production, +1,964 / −138 whole diff). **Counter
    0 / 4.** **Then** item 24's list from (a): M5-4f + D15 C + BUG-0702 item 1 + `gatherRate` with the seed-21 re-record (and the
    CLAUDE.md line first), pathing slice 2, M5-4c slice 2, Requests 39 (+ 38), D14 b, M5-4d, D14 c, M5-5, M5-6.
26. **M5-4f** proportional defense + D15 C + BUG-0702 item 1 + `gatherRate` 0.85 / 0.75 with the seed-21 re-record + the CLAUDE.md line →
    **built, held on `studio/2026-10-16-1654-sim` (8ac7b43) for its QA round** (session 2026-10-16-1654: the dev stopped BLOCKED on one
    red QA stress row, `GatherPocketStressTests` `(MineOneSide, 40)` at the quicker gathering = **BUG-0790** (S3); QA full never ran, so no
    merge; the Producer read the diff (`DefenseManager.Size` / `Pick` / `Recall` with the in-place heap, `Blackboard` +12 B × slots,
    `MilitaryManager`'s kept start threshold, `AiPlayer.IntruderWorth` / `HallSlot`, `BuildOrderExecutor`'s D15 C, `AbilityManager`'s
    nearest-enemy seed, `EconomyManager`'s per-think no-node cache, the three knobs validated + hashed, `rules.json`, docs 01 / 02 / 03,
    `ProportionalDefenseTests` 8 rows) and ran the task's slice 407 / 407; **BUG-0791** (S4) filed for the executor's per-think site
    search; +386 / −47 production, +372 tests, +101 docs). **Counter 1 / 4.** **Then** the round (QA full + the BUG-0790 ruling), the
    merge with the whole scene loop, and the list above from pathing slice 2, with **Requests 42** (the save format) after Requests 39.
4. M6 (far ahead): agents can't download. **Owner note 2026-10-08: the art direction is grounded / realistic
   (Quaternius Universal Base Characters + Modular Outfits + Universal Animation Library 1 / 2, Mixamo packs for gaps;
   KayKit a fallback only; docs/04 and docs/01 updated by the owner), and the packs are already in `asset-sources/`
   (characters, animations, ambientCG textures, Stylized Nature MegaKit, Poly Haven trees / rocks, Kenney UI, Sonniss
   audio); Blender 5.2 installed. Still to download before M6 (docs/04): fonts (Cinzel, EB Garamond), music, the Godot
   4.7.2 .NET export templates. The Producer lists those three under "Waiting on you" when M5 starts. Do not pull the
   look test or real-art work forward: M6 stays where it is.**

## Feature queue: view track (feature sessions, in order)

1. ~~M3-V3b re-land (BUG-0124, S2) + the view's end-of-M3 hardening~~ → **done** (session 1715; BUG-0126 items 3-6
   left for the next view hardening).
2. ~~M3-V4, the M3 "Playable" proof + BUG-0125 + BUG-0126 items 1-2~~ → **done** (session 2315; `M3PlayableTest.tscn`
   ticks the criterion; BUG-0147 / 0148 left).
3. ~~M4-V1 (BUG-0147 + the first combat views)~~ → **done** (session 2026-10-08-0313; BUG-0160 S4 left).
4. ~~M4-V2 the Attack-target order in the HUD + BUG-0160~~ → **accepted** (session 2026-10-08-0913, 0 fix rounds; BUG-0190
   S4 left) but **held on `origin/studio/2026-10-08-0913-view`** (ESCALATE at integration: BUG-0210, `main` red on
   `MinimapTest` since M4-2b).
5. ~~BUG-0210 first, then M4-V3 projectile visuals + BUG-0190~~ → **done** (session 2026-10-08-1814, resumed 1435; 2 fix
   rounds: BUG-0221 / 0223 / 0224 corpse-disc and mark fixes, BUG-0218 `AttackStage`, BUG-0212 / 0213 `--no-combat`; the held
   M4-V2 lands with it; BUG-0220 S3, BUG-0222 / 0226 S4 left).
6. ~~BUG-0219 (S2) first, then M4-VH1, the view's hardening session~~ → **done** (session 2026-10-08-2144, 0 fix rounds:
   BUG-0219 / 0220 / 0222 / 0226 / 0148 item 3 / 0126 item 4, the unseen-target sweep, docs/03 "Build and export"; BUG-0251 S3,
   BUG-0250 S4 filed; BUG-0148 item 1, BUG-0126 items 3 / 5 / 6 left).
7. ~~M4-V4 the fog on screen~~ → **done** (session 2026-10-09-0125, 0 view fix rounds + 1 conductor-dispatched round for the
   sim's BUG-0274 (five scenes re-staged on explored ground, no `Check` relaxed); BUG-0280 S3 filed against the sim, BUG-0281 S4
   left; ghosts shipped as a hook only).
8. ~~M4-V5 ghosts drawn, right-click a ghost, the "Unexplored" row, BUG-0273~~ → **done** (session 2026-10-09-0724, 0 fix
   rounds; criterion 5 complete; BUG-0310 S3 (view) and BUG-0311 S3 (sim) filed; BUG-0281 item 3 not reached).
9. ~~M4-V6a the card's ability row, targeting rings, one cast per click from the nearest ready caster, the cast bar~~ → **done**
   (session 2026-10-09-1155, 1 fix round: BUG-0341 S2 the `order_queue` string lookup; `ViewApi.AbilityCaster`,
   `AbilityViews`, `AbilityViewTest.tscn`, QA's `QaV6aTest`; BUG-0340 / 0342 S4 left; item 5 not reached).
10. ~~M4-V6b status markers, the resolve flash, the Cusser card, BUG-0342 / 0310 / 0340~~ → **done** (session 2026-10-10-0215,
    0 fix rounds; `ViewApi.StatusMarkers` / `ResolveFlashes`, `AbilityViews` markers + flash + the new bar, Shift stays
    armed, `FogView.CollectGhosts` footprint rule, QA's `QaV6bTest`; BUG-0370 S3, BUG-0371 S4 left; ~1,115 lines).
11. ~~M4-VH2, the view's hardening~~ → **done** (session 2026-10-10-0624, 0 fix rounds: BUG-0370 / 0371 / 0251 fixed, BUG-0281
    items 1 + 3, BUG-0250 items 1 + 3, BUG-0126 item 3; `ViewApi.SentCasts` / `SeenResources`, `MinimapRaster.DrawnEnemyDotAt`,
    `game/tests/SceneExit.cs` in all 44 scripts; BUG-0400 S4 filed; not reached: BUG-0281 item 2 (M6), 0250 item 2, 0148 item 1,
    0126 items 5 / 6, export hygiene; ~1,585 lines). Counter 0 / 4.
12. ~~M4-V6c zone discs where the player may see them (`ViewApi.ZoneDiscs` + hash twin), the Blinded marker colour,
    `AbilityViewTest` rows + QA's `QaV6cTest`~~ → **done** (session 2026-10-10-1015, 0 fix rounds, QA PASS_WITH_ISSUES: BUG-0430
    S3 (a disc across a cliff), BUG-0431 S4 filed; BUG-0390 had landed at the 0624 integration; ~1,760 lines incl. QA; loop 40 / 40).
    Counter 1 / 4.
13. ~~M4-V6d + M4-V7 slice 1: the Frenzied marker (`FrenzyColor`), the `selfAura` card (`AbilityCaster.Lead`, `SelfCaster` /
    `SelfAbilityOrder`, the hover ring), `M4PlayableTest.tscn` (every step of the brief landed)~~ → **done** (session
    2026-10-10-1314, 2 fix rounds: BUG-0460 / 0463 S2 (the scene's own timing flakes: fresh units by handle, rubble by any
    footprint cell, the swing window by tick, a second Cadre Mage) fixed; QA PASS_WITH_ISSUES: BUG-0461 S3 (sim), BUG-0462 S4;
    26 / 26 seed-runs + the Producer's 2 PASS; ~2,270 lines incl. QA; criterion 10 ticked). Counter 2 / 4.
14. ~~M4-V7 slice 2: BUG-0450 (hp bars + the first-sight flash on `World.MaxHp` per slot via `UnitViews.FillMaxHp`, the span-only
    `UnitHpBars.Collect`, `HitFlash.UpdateWithSlotMax`), the `AbilityKind.Summon` card arm (`AbilityCaster.IsNoTarget`, no radius
    line / ring at 0, `DeathMarkers.Collect` skips `Expired`), the sandbox's enemy-storm step 9, BUG-0462 item 5~~ → **done** (session
    2026-10-10-1954, 0 fix rounds, QA PASS_WITH_ISSUES: BUG-0490 S4; 14 / 14 seed-runs, loop 41 / 41; ~1,435 lines incl. QA). Counter 3 / 4.
    **M4-V8 stealth visuals moved to M8** (Producer decision at this ACCEPT: the roadmap lists "stealth visuals for owner and enemies"
    under M8 and no unit can be stealthed before Shadow; docs/03 "For the view (M4-V8)" is its spec; Requests 32).
15. ~~M4-VH3, the end-of-M4 view hardening~~ → **done** (session 2026-10-10-2334, 0 fix rounds, QA PASS_WITH_ISSUES: BUG-0430 + 0462
    item 1 (`ViewApi/GroundPatch` + `GroundDisc` + `ground_disc.gdshader`: discs and rings on the terrain cells across a cliff), 0431
    (teal own rim), 0490 (`SlotMaxHp`, the measured tick range), 0400 items 2-3, 0126 item 5, 0250 item 2; wontfix 0462 item 3, 0126
    item 6, 0148 item 1; BUG-0520 / 0521 S4 filed; export hygiene not reached (M6); loop 42 / 42 with QA's `QaVh3Test`; ~2,050 lines
    incl. QA). Counter 0 / 4. **M4 signed off at this ACCEPT.**
16. ~~M5-V1, the lost-cast cue + the flat rings + export hygiene~~ → **done** (session 2026-10-11-0314, 0 fix rounds, QA PASS_WITH_ISSUES:
    `ViewApi/LostCastCues`, `GroundDiscPool`, cast rings and resolve flashes as pooled `GroundDisc`s with the shader `scale` (BUG-0520),
    `GroundDisc.Place`'s clamped-hole compare (BUG-0521 item 1), `SyncCues` + `ui.json` `hud.cast_lost` + `SfxEvent.Refused`,
    `game/export_presets.cfg` (tests out, data in; pack-only verified), `AbilityViewTest` rows + QA's `QaM5V1Test`; loop 43 / 43;
    BUG-0550 S4 filed; ~1,780 lines incl. QA). Counter 1 / 4.
17. ~~BUG-0560 first, then M6-V1, the shell's first slice~~ → **done** (session 2026-10-11-0634, 0 fix rounds, QA PASS_WITH_ISSUES: the
    shell in `Main.cs` + `MainMenu` / `SkirmishSetup` / `PauseMenu` / `ShellUi`, `LaunchOptions.HasMatchFlag` + `--map-size` /
    `--skirmish`, `ui.json` `menu` / `setup` / `pause`, the remembered site as a foundation + the `siteShare` picker overload,
    `ShellTest.tscn` + QA's `QaShellTest.tscn`, the second smoke pass; BUG-0560 fixed; BUG-0580 S3, BUG-0581 S4 filed; ~1,900 lines
    incl. QA; loop 45 / 45). Counter 2 / 4. **M6 criterion 1 advanced.**
18. ~~M6-V2 + M5-V2, the lobby's seats and the sandbox AI~~ → **done** (session 2026-10-12-1930, 0 fix rounds, QA PASS_WITH_ISSUES:
    the setup's live faction lists + the AI's difficulty from `ui.json` `setup.difficulty_*`, `MatchArgs` → `LaunchOptions.Parse`,
    `--faction <slot> <id>` / `--ai <d>`, `SimRunner.BuildSeats` (explicit seats always, hash-equal to none), `Match.PickSeats`,
    `ViewApi/MatchOver` + the "Match over:" dev line, Requests 37 filed, BUG-0580 fixed (`_Notification` paused / unpaused), BUG-0581
    items 1 + 4, `AiSeatTest.tscn` + QA's `QaM6V2Test.tscn` / `MatchOverQaTests` / `ExplicitSeatsQaTests`; BUG-0600 / 0601 / 0603 / 0604
    S3, BUG-0602 S4 filed; ~1,880 lines incl. QA against ~1,000; loop green). Counter 3 / 4. **M6 criterion 1 advanced.**
19. ~~M6-V3, the menu's match as the design says + per-player colours~~ → **done** (session 2026-10-13-0146, 1 fix round, QA FAIL →
    PASS_WITH_ISSUES: `SkirmishSetup.MatchArgs` `--units 0` + a colour list per slot (8 `ui.json` `setup.colour_*` entries with swatches),
    `LaunchOptions` `--colour <slot> <n>` + `--skirmish` defaults (`--units 0`, slot 2 `DefaultAi` Normal: BUG-0630 S2 fixed in the
    round), `Match.PlayerColours` → `PlayerRgb` (the one colour source; `UnitViews` per-player materials), `StartFocus` on the local
    hall, `Minimap._Notification` (BUG-0600), `UiText.ReadColours` (equal RGBs refused) + `IsSizeFormat` (BUG-0602 item 1), four view
    tests on `PlayerRgb`, `AiSeatTest`'s gather probe (PENDING on the branch, gathering on the merged tree), QA's `QaM6V3Test`; BUG-0632
    S3, BUG-0631 S4 filed; 555 dev lines + QA; loop 48 / 48). Counter 4 / 4. **M6 criterion 1 advanced (colour).**
20. ~~M6-VH1, the view's hardening~~ → **done** (session 2026-10-13-0527, 0 fix rounds, QA standard PASS_WITH_ISSUES: the start-location
    probes are Checks (BUG-0604), `ui.json` Crimson / Jade retuned + a whole-table `ShellTest` row (BUG-0632), colours unique per match
    (`SkirmishSetup.PickColour`, `LaunchOptions.FirstFreeColour`; BUG-0631), `M4PlayableTest` step 7 reads the cell before the blast
    (BUG-0603) + seed 6's framing (BUG-0660), `GroundDiscPool.Reclaim` (BUG-0550 item 1), `SlotMaxHp.Capacity` (BUG-0521 item 3), QA's
    `QaVh1Test`; BUG-0661 S2 (pre-existing: four scenes red since M5-4a, two cleared by this integration), BUG-0662 S3, BUG-0663 S4
    filed; 265 dev lines + 358 QA). Counter 0 / 4.
21. ~~BUG-0661 first, BUG-0662, then M6-V6 the settings screen~~ → **done** (session 2026-10-13-0916, 0 fix rounds, QA standard
    PASS_WITH_ISSUES: `game/tests/BenchMarch.cs` (the march measured over the units the across order sent, half a free march;
    BUG-0661 fixed, the root cause the off-screen start workers), `GroundDiscPool.Reclaim(key)` lower-stale-first else highest
    (BUG-0662), `GameSettings` / `SettingsApplier` / `SettingsScreen` / `SettingsText`, `Main` boot order + `CommitSettings`,
    `RtsCamera.EdgePanSpeed` / `PanVector`, `Sfx` on the `Sfx` bus, `default_bus_layout.tres`, `ui.json` `settings` (21 keys) +
    `menu.settings` / `pause.settings`, `SettingsTest.tscn`, `ShellTest` rows, QA's `QaM6V6Test.tscn`, QA's QaH1Test oracle fix
    (BUG-0682), ProductionHudTest's click (BUG-0683); BUG-0680 / 0681 S3, BUG-0684 S4 filed; +1,310 dev lines + QA). Counter 1 / 4.
    **M6 criterion 4 advanced (all but keybinds).**
22. ~~BUG-0674 + BUG-0680 + BUG-0681, then M6-V4 the banner and the stats screen~~ → **done** (session 2026-10-13-1616, 0 fix rounds,
    QA standard PASS_WITH_ISSUES: the loop green again (51 / 51); `Match.Outcome` latched from `DefeatEvents`, `MatchOverBanner` through
    the pause path, `MatchStats` with today's five columns, the pause menu's Stats, `ui.json` `match_over`; Requests 39 written;
    BUG-0710 S4 left; criterion 2 advanced, ticked when Requests 39's columns show).
23. ~~M6-V7~~ → **done** (session 2026-10-14-0946, 0 fix rounds, QA standard PASS: `Main._Process` sets `Input.MouseMode` Confined /
    Visible from `ViewApi/CursorConfine.Wanted` (match running, unpaused, focused, the setting), `--no-confine`, never headless; `GameSettings.
    ConfineCursor` + `camera.confine_cursor` + the Settings check box (`ui.json` `settings.confine_cursor`); `CameraLimits.EdgeDirection`
    (the last pixel pans, outside = none); `ControlGroups` holds one building per group (`AssignBuilding` / `RecallBuilding` /
    `PruneBuildings` / `TryBuildingCentre`), `SelectionController.GroupKey`; BUG-0710 both items (`ResourceBar.Sync` under the banner,
    `ViewApi/MatchOver` + its tests deleted); `DefeatStaging` + the three match-over scenes staged for both rules; the dev line reads
    "is defeated"; `BuildingGroupsTest`, QA's `QaM6V7Test` + `M6V7QaTests`; loop 55 / 55 on the branch; ≈ 1,098 dev lines). Counter 3 / 4.
    **Criterion 4 advanced.** Left: BUG-0711 (`AbilityViewTest` under the new rule), the owner's real-cursor try.
24. ~~M6-V8, the resource-remaining click + keybinds~~ → **done** (session 2026-10-15-2325, 1 fix round, QA standard FAIL → PASS_WITH_ISSUES:
    `SelectionController.SelectResource` / `PickResource` / `SelectedResource` (a left click on a drawn node selects it alone, 0 commands),
    `SelectionPanel.ShowResource` ("2500 Gold left" live while a footprint cell is in sight, else "Last seen: N ... left" from
    `ViewApi/SeenResources.Amount`, updated once a tick by `FogOfWar.Resources`), `KeyMap` (31 actions: 4 orders, Tab, B / V, 9 digits, 15
    grid keys; `project.godot`'s keys as defaults; the clash rule; `Read` / `Write` of `settings.cfg` `[keys]` with `ResolveClashes`;
    `Apply` re-registers the `InputMap`), `SettingsScreen`'s General / Keybinds tabs (click a key, press the new one, a conflict refused with
    `keys.conflict`, "Default keys"), `GameSettings.Keys`, `CommandCard` hints follow the bound keys, `ui.json` `hud.resource_left` /
    `hud.last_seen` + 15 `keys.*` entries, `ResourceClickTest` / `SettingsTest` rows / `ShellTest`, QA's `QaM6V8Test` + `QaM6V8R1Test` +
    `QA/ViewApi/SeenAmountsQaTests`; BUG-0740 S2 / 0741 / 0742 S3 + BUG-0743 items 1-2 fixed in the round; BUG-0743 item 3 / BUG-0744 S4
    and BUG-0745 S3 (an `M4PlayableTest` load flake, maybe movement) left; +1,242 / -41 dev lines; loop 57 / 58 under load, 58 / 58 alone).
    Counter 4 / 4. **M6 criterion 4 ticked.** **Then:** (0) **M6-VH2 hardening** (BUG-0684, BUG-0744 1-2, BUG-0743 item 3, BUG-0745's scene half,
    BUG-0622 item 1; brief in `studio/handoff.md`); (a) ~~the resource click + keybinds~~ done; (b) **M6-V5's slot part**
    (2-4 slots; teams need Requests 38); (c) save / load + replay playback (criterion 3); (d) the look test (the owner's packs are in
    `asset-sources/`); (e) `tools/export.ps1` with Requests 33; (f) M5's view part: criterion 6's playable run against the AI; (g) the
    stats columns as Requests 39 lands.
25. ~~M6-VH2 view hardening~~ → **done** (session 2026-10-16-0847, 0 fix rounds, QA standard PASS_WITH_ISSUES: BUG-0684 items 1-5
    (per-process `user://` folders in `SettingsTest` / `ShellTest`, the screen's VSync under `--vsync` + `Picks` keeps the file's, the
    dead `ShowMenu()` branch, `GroundDiscPool.Reclaim`'s sorted stale list, `BenchRunner.FitArmy`), BUG-0744 items 1-2 (`KeyMap.Read`
    compares `OS.GetKeycodeString(key)` with the trimmed input; `IsPlainKey` round-trips), BUG-0743 item 3 (`FogOfWar.Runner` subscribes
    to `SimRunner.Ticked`), BUG-0745's scene half (`SimRunner.FixedFrameDelta`, `M4PlayableTest.PullBackSpot`), BUG-0622 item 1
    (`ViewApi/StartBase.Plan` edge wrap; `QA/StartBaseEdgeQaTests`); QA's `QaM6VH2Test` (hostile keys, every code, the disc-pool fuzz, 2x
    last-seen); BUG-0770 S4 filed; loop 59 / 59 on the branch; +1,015 / −71 whole diff). **Counter 0 / 4.** **Then:** (b) **M6-V5's slot
    part** (2-4 slots, free-for-all; the team column waits on Requests 38), (c) save / load + replay playback, (d) the look test, (e) export,
    (f) criterion 6's playable, (g) the stats columns.
26. ~~M6-V5 slot part: 2-4 player slots, free-for-all + BUG-0770~~ → **done** (session 2026-10-16-1654, 0 fix rounds, QA standard PASS, no
    bugs: `SkirmishSetup` four rows built once / 2-4 shown, Add player / Remove player (`ui.json` `setup.add_slot` / `remove_slot`), a
    difficulty list per AI slot, colours unique over the shown slots (`LaunchOptions.FirstFreeColour(ReadOnlySpan<bool>)`), `MatchArgs`
    with `--players` + per-slot `--ai`; `LaunchOptions` `--players 2|3|4`, slots 1-4 for `--faction` / `--colour`, `--ai <slot> <d>` beside
    `--ai <d>`, slot-by-slot collisions; `Match` `PlayerCount` / `StartLocations` from `--players`, `PickSeats` per slot, a base per
    player; `SimRunner.SlotAi[]` + `SeatsFor` (`BuildSeats` / `Player1Ai` kept); BUG-0770 (per-process files + a before / after byte
    compare in `ShellTest` / `AiSeatTest`); `ShellTest` +413, `FreeForAllTest.tscn`, QA's `QaM6V5Test.tscn`; docs/02 "Game flow" 2 +
    docs/03 "Implementation (M6-V5)"; `sim/` diff empty; +1,962 / −110 whole diff, 19 files; loop 61 / 61). **Counter 1 / 4.** **M6 criterion
    1 advanced (team left, Requests 38).** **Then:** (c1) **replay playback** (criterion 3's replay half, view-only; the Producer confirms
    at PLAN against (d)), (d) the look test, (c2) save / load when the sim's Requests 42 lands, (e) export (the owner's templates), (f)
    criterion 6's playable, (g) the stats columns (Requests 39), (h) teams (Requests 38). **M6-VH3** after four (1 / 4 now).

## Feature queue: data track (feature sessions, in order; owner reviews every landed task)

1. **Owner review tweaks** from the inbox (the D1 / D2 / M3-5 text entries under For your review) whenever present
   come first (QA light; golden `data-hash` regen on a data change).
2. ~~D3 techs content~~ → **landed** (session 1715, through the sim's M3-H2); the review table is under For your review.
3. ~~D4 shared techs text + BUG-0132 + golden regen~~ → **done** (session 2315; the review table is under For your
   review; BUG-0155 S4 left).
4. ~~STOP until a schema it needs is on `main`~~ → `attack.targets` landed (M4-2a, session 2026-10-08-0313).
5. ~~D5 `attack.targets` pins, docs/02 "Ages" wording + parser, BUG-0155~~ → **done** (session 2026-10-08-0913, 0 fix
   rounds; BUG-0200 S4 left; nothing numeric changed).
6. ~~D6 BUG-0200 + the counter-triangle balance report + the Sapper self-splash numbers~~ → **done** (session
   2026-10-08-1814, resumed 1435, 0 fix rounds; the proposal is under For your review; BUG-0230 S4 left; nothing numeric
   changed).
7. ~~D7 the Sight column + BUG-0230 items 1 and 3 + BUG-0090's tower item~~ → **done** (session 2026-10-08-2144, 0 fix rounds;
   BUG-0240 fixed on the way; BUG-0260 S4 left; nothing numeric changed).
8. ~~D8 the shared harness + every page cell pinned + BUG-0243 / 0260~~ → **done** (session 2026-10-09-0125, 0 fix rounds;
   a new "Winner hp left" column on both pages; BUG-0290 S4 left; nothing numeric changed). Data's counter is 4 / 4.
9. ~~D-H1, the first data hardening~~ → **done** (session 2026-10-09-0724, 0 fix rounds, QA PASS: BUG-0290 / 0090 fixed,
   `Content/PageTables.cs`, no number / text / `data-hash` change). Data's counter is 0 / 4.
10. ~~D9 the towers' Attack / Detector columns~~ → **dropped** (Producer decision, session 2026-10-09-1155 PLAN: the pages'
    Provides text carries both numbers and D-H1 pinned them both ways; owner may revisit).
11. ~~D10a every loaded ability's page row + every status name pinned both ways, the landed-allowance, the Duration column~~ →
    **done** (session 2026-10-09-1155, 0 fix rounds; BUG-0350 S3 + BUG-0351 S4 left; no number / text / `data-hash` change).
12. ~~D10b the Cusser row pinned fully (BUG-0350), BUG-0351, the text review~~ → **done** (session 2026-10-10-0215, 1 fix round
    for the cross-track BUG-0362; BUG-0380 S3 left; the Cusser Effect cell on malazan.md reworded; no number / `data-hash` change).
13. ~~D10c~~ → **done** (session 2026-10-10-0624, 0 fix rounds, QA PASS: Sandstorm and Blinded pinned by field both ways,
    `PendingAbilities` empty, BUG-0380 fixed, the three descriptions as proposed + Sandstorm's "Your own units are unaffected",
    the whirlwind.md row reworded, `blinded` kept with a proposal; golden `data-hash` 5896D3E7C9FD36AD → EA5CB5A0AFCEEDBE, every
    `k` line equal; BUG-0410 S4 left; ~840 lines). Counter 3 / 4.
14. ~~D-H2, the data hardening one session early~~ → **done** (session 2026-10-10-1015, 0 fix rounds, QA PASS_WITH_ISSUES: BUG-0440
    S4 filed; BUG-0410 fixed (`DescriptionProblems` by role), `Compare` takes the status list, `AgesRuleQaTests` on
    `PageTables.Heading`; tests only, no data / page / golden change; ~620 lines incl. QA). Counter 0 / 4.
15. ~~D11 the Zealot passives + the Frenzied row pinned both ways (`Content/PassiveContentTests`, `PendingPassives` Shield Wall /
    Charge), `Frenzied` out of `PendingStatuses`, the Frenzied and Blinded descriptions reworded, Martyrdom's "+50% attack speed"
    on the page, one golden regen~~ → **done** (session 2026-10-10-1314, 0 fix rounds, QA PASS_WITH_ISSUES: BUG-0470 S4; golden
    `data-hash` D22C849A175F9ED5 → 4383EBB9345FABF7, every `k` line equal, in `SameGameDataHashes`; ~650 lines incl. QA; the
    review table is under For your review) **but held at integration** (ESCALATE, conductor rule: the merged tree fails the
    sim's `DataContentHashTests.EverySpawnField_AndTheSummonKind_ChangeTheHash`, which pins the shipped hash as a literal;
    `origin/studio/2026-10-10-1314-data` @ 9978bd6 with `main` merged). Counter 1 / 4 once landed.
16. ~~D-H3 (one session early) + the D11 landing commit: BUG-0470 item 1 (a second same-trigger page sentence fails naming both),
    BUG-0440's four wordings (the colon, the hyphen, "dmg", the per-phrase linger)~~ → **done** (session 2026-10-10-1954, 0 fix
    rounds, QA light PASS_WITH_ISSUES: BUG-0500 S4; tests only, no data / page / golden change; D11 landed with it: the identical
    `DataContentHashTests.cs` hunk on the sim and data branches; ~330 lines incl. QA). Counter 0 / 4. No inbox tweaks had arrived.
17. ~~D12 the casters' `detector 10`, the page pins, Stealthed / Revealed out of `PendingStatuses`, one golden regen, BUG-0500, the
    BUG-0470 status line~~ → **done** (session 2026-10-10-2334, 1 fix round: BUG-0530 S2 (two sim-track tests asserted "no shipped
    detector"; fixed in 4 lines under a conductor-named exception) + BUG-0531 S4; golden `data-hash` 889D6BEA3A49F08E → C4C16BD39408471A,
    every `k` line equal, in `SameGameDataHashes`; ~500 lines incl. QA; the review table is under For your review) **but held at
    integration** (the conductor's rule: a code conflict with M4-H3 in `StealthLoaderTests.cs` / `StealthContentHashTests.cs`;
    `origin/studio/2026-10-10-2334-data` @ c0dd350). Counter 1 / 4.
18. ~~D13 = the D12 landing + the Line v Shock pass~~ → **done** (session 2026-10-11-0314, 0 fix rounds, QA PASS_WITH_ISSUES: D12 merged
    with the recorded resolution (blobs 92e6a333 / 2139d2dc, golden C4C16BD39408471A at the merge); the line units' `bonusVs.mounted`
    1.5 → **1.1** by the brief's fallback (the sweep 1.5-1.0 reaches no band cell; 1.0 fails `AbilityFuzzStressTests` seed 11), pins
    `DamageCalcTests` 11 / `UnitRosterQaTests` / QA's `LineMountedBonusParityQaTests`, both pages re-printed, docs/02 line 51, docs/01
    row; golden `data-hash` → **5760E83CB301B62E**, no `k` line moved, in `SameGameDataHashes`; BUG-0560 S3 (view test code) and
    BUG-0561 S4 filed; ~700 lines incl. QA). Counter 2 / 4. The owner's ×1.1 / ×1.5 / hp-pass choice is under For your review.
19. ~~Inbox answers first (D11 / D12 / D13), else D14, `ai.json` build orders for both factions~~ → **done as D14** (session 2026-10-11-0634,
    0 fix rounds, no QA inspection (the dev reported BLOCKED on six sim-track pins: BUG-0571, fixed on the sim branch), **accepted at
    integration** on the merged tree: both `ai.json` 1 → 26 steps, compositions by pop, `attack` 20 / 1,544 and 14 / 861, the pages'
    "AI build order (Normal)" tables + composition tables, `Content/AiContentTests` A-F, BUG-0561 fixed; no golden change; ~530 lines;
    the review table is under For your review). Counter 3 / 4. No inbox tweaks had arrived.
20. ~~Inbox answers first, else D15, D14's QA rows + the AI economy check~~ → **done as D15** (session 2026-10-12-1930, 0 fix rounds, QA
    PASS_WITH_ISSUES: `QA/Content/AiContentQaTests` (87 rows a-i, both factions; `minArmyCost` recomputed from `units.json`) + QA's
    `AiContentFileSideQaTests` (52), `AiHouseRuleReportTests` (3 report rows: the economy rule never fired, keep the order: proposal A,
    under For your review); BUG-0610 S4 filed; no data / page / golden change; 633 lines). Counter 4 / 4. No inbox tweaks had arrived.
21. ~~D-H4, the data hardening~~ → **done** (session 2026-10-13-0146, 0 fix rounds, QA light PASS_WITH_ISSUES: BUG-0610 fixed (the
    faction assert a documented precondition through `World.FactionOf`, the four `Blackboard` reads named, the numbers dated), the
    Raider v Wickan Lancer cells confirmed pinned, the house-rule report re-run on M5-3's AI (Malazan 30 min: 11 placed / 9 finished,
    cap 82 → **BUG-0640 S3 filed against the sim**; Whirlwind 7 / 7); BUG-0641 S4 filed; no data / page / golden change; ~60 lines).
    Counter 0 / 4. No inbox tweaks had arrived.
22. ~~D16, the difficulty blocks pinned~~ → **done** (session 2026-10-13-0527, 0 fix rounds, QA light PASS: a `## Difficulty` table on both
    pages, `Content/AiContentTests` J (both ways, 48 page-cell edits) + K (docs/02's stated numbers, 15 file mutations), QA's
    `AiDifficultyFileSideQaTests` (48), docs/02 Easy "doesn't use abilities" (Producer default, BUG-0541 item 3), BUG-0641 fixed; no data
    / golden change; ~320 lines + 57 QA; the review table is under For your review). Counter 1 / 4. No inbox tweaks had arrived.
    **Integration note (0916 PLAN):** session 0527 died at the data step, so D16 sat accepted on `studio/2026-10-13-0527-data` (16c6da7,
    `main` a8ebff7 merged in, diff = exactly the D16 files). **At the 0916 ACCEPT:** 16c6da7 had reached `origin/main` on its own
    before session 0916's base (dde71a4 descends from it; verified), so no landing step was needed; session 0916 ran no data task.
23. ~~Inbox answers first, else D17~~ → **done as D17** (session 2026-10-13-1616, 0 fix rounds, QA light PASS: docs/02's Normal bullet
    "doesn't focus fire or pull back wounded units" pinned by K + QA's `NormalMicroPhraseQaTests`; both pages' Defense lines on M5-4b's
    rule, the Whirlwind counter-raid kept as a later idea; `Content/AiSpendingReportTests` (wood is the limit, no Age II in 20 min on
    the flat scene); no data change; BUG-0720 S4 left, item 1 fixed by the Producer).
24. ~~D18, four reports and no number changed~~ → **done** (session 2026-10-14-0946, 0 fix rounds, QA light PASS: BUG-0720 item 2 on both
    pages; `AiSpendingReportTests.GeneratedNormal_*` (seeds 1 / 7 × both factions: wood still the limit, 9 of 16 marks), `TreesNearBasesReportTests`
    (seeds 1-7: 19-61 trees within 30 m of every start, nearest 16-20 m; the menu's match is seed 1), `BuildingKillTimeReportTests` (today 6 / 12
    band cells, set (C) 12 / 12), `LineShockSweepReportTests` (no hp / armor / cost set reaches the band); QA's `D18ReportPreconditionQaTests`;
    no data change, golden unmoved; BUG-0721 S4 left; the QA report file was not committed (standing rule 23)). Counter 3 / 4.
25. ~~D19, the owner's numbers applied~~ → **done** (session 2026-10-15-2325, 1 fix round (the dev's first report BLOCKED on 18 stale sim-track
    pins; a conductor-named exception let it move them: tests / comments only, none loosened, the Producer read every hunk), QA standard
    PASS_WITH_ISSUES: `bonusVs.mounted` ×1.1 → **×1.5** (Heavy Infantry, Raider); Easy `useAbilities` **true** both factions; the Structure
    column melee 0.4 → **0.7** / pierce 0.2 → **0.35** / magic 0.4 → **0.6** (siege 3.0); every building's armor halved rounded down (5 → 2,
    4 → 2, 3 → 1): 27 data lines, exactly the named ones; golden `data-hash` C6FC120A55AE4A0B → **AB811AD47F68D0AE**, all 15 `k` lines
    byte-identical, the hash in `SameGameDataHashes` with its reason; the kill-time report re-based (12 / 12 on the shipped data **before the
    sim's surround rule**: 20 HI 15.6 / 43.8 / 68.7 s, 20 Raiders 13.6 / 36.1 / 57.4 s); both pages' Line v Shock rows at ×1.5 (94 / 88 / 100 /
    95 %) and siege rows re-printed (Catapult on a Tent 47 %, Ram on a Billet 34 %: **stale again after the sim's M5-P1 lands: the merged-tree
    reprint at this integration re-prints them, BUG-0734**); QA's `QA/Content/D19QaTests` (10 rows: every unit × building × bonus 0-3 by hand
    = `DamageCalc`); BUG-0750 S4 left; ~430 dev lines + QA; the review table under For your review). Counter 4 / 4. **Then D-H5 hardening**
    (an inbox answer on D19 / the Shock pass / `goldShare` first, then BUG-0750, BUG-0721, the D18 reports re-run; brief in `studio/handoff.md`).
    (kept for the record:) **Not in D19** (moved at the 0946 ACCEPT):
    `rules.json` `gatherRate` gold 0.7 → 0.85, wood 0.6 → 0.75 (sim-owned and it changes the recorded seed-21 match: the sim's M5-4f session
    with the re-record); the Line v Shock hp / armor / cost (D18: no set reaches the band; a Shock pass is a proposal for the owner). Then the D14 b openings (2-3 per faction) once the schema is on `main`; the house notes after D15 C. **The
    Teblor's `ai.json` is not pulled ahead of M7** (Producer decision, 0916 PLAN: `game/data/factions/` has only `malazan` and
    `whirlwind`; a new faction folder needs a whole roster, appears in the setup's faction lists and moves the golden, and the Giant
    armor class / regeneration / multi-spawn schemas are M7 sim work). Then the Shock v Ranged pass if the owner asks for one, the
    M7-M9 rosters when their milestones open. **Rules for every data brief:** name every sim-track test that pins a value the task
    changes, and the final assertion both tracks agree on; a sim task written the same session against the same content writes its
    tests content-independent.
26. ~~D-H5 data hardening~~ → **done** (session 2026-10-16-0847, 1 fix round for the sim-found BUG-0761 (S2: the kill-time report's House
    column measured a collapse; `TestSim.KeepIn(sim, 1)` after the attackers + three guards), QA light PASS: BUG-0750 items 3 + 5 (the
    precondition test's lever edits off the shipped values, 12 / 12 mutants killed; the regen-reason convention in `bugs/README.md`),
    BUG-0721 items 1-3 (the siege ratio against both line rows, the spending conclusion cut at the idle seat's defeat, the menu's 8
    mines), the D18 reports re-run on D19 + the surround rule (both pages + docs/03: "wood binds at some marks only"; 20 Raiders 9.8 /
    19.5 / 35.8 s); **no data change, golden unmoved**; **BUG-0780 S3** (Catapults 1.7× / 1.6× the Raider per cost on a hall / Town Hall:
    the owner's call); +304 / −39 lines). **Counter 0 / 4.** **Then:** an inbox answer (D19's table, the Shock pass, `goldShare`,
    BUG-0780) first; else **STOP cheaply** until the strategies schema (D14 b) or D15 C (M5-4f) is on `main`.

## Debt backlog: sim track (hardening sessions only; counter **1 / 4** after M5-4f (held), session 2026-10-16-1654)

- **After M5-4f (held, 2026-10-16-1654), new sim debt:** **BUG-0790 (S3, Producer-filed):** `Stress/GatherPocketStressTests.Pocket_NoGathererWedgesOutOfReach_ConservationExact(MineOneSide, 40)`
  red at `gatherRate` 0.85 / 0.75 (seeds 2 / 8: a queued worker 68 / 84 ticks out of reach with nobody on the straight line, bound 40;
  green at 0.7 / 0.6): QA rules in M5-4f's round (a free spot in reach left standing = a sim crowding fault → the row skipped with the
  id and the fix in M5-H3's stand-point logic; a full face = the check's geometry → QA re-stages it and the bug closes). **BUG-0791
  (S4, Producer-filed):** `BuildOrderExecutor` repeats a failing site search every think once gold is affordable (the storms fuzz's
  measured think 0.47-0.62 → 1.57-1.64 ms, ungated; the gated rows inside their gates): a "no site until tick" mark per step, a
  search-count test; M5-H3. **BUG-0702 item 1** fixed on the held branch (closes when it lands).
- **After M5-H2 (2026-10-16-0847), open sim debt, most valuable first:** **BUG-0764 (S3, new, Producer-filed):** 17-20 Perf rows red on
  an idle machine on base and head alike (A/B equal three ways); re-baseline with dated reasons or gate relative to a calibration row
  (an owner fresh-boot run first, Waiting on you). **BUG-0144 (S3)** and **BUG-0113 item 2 (S4)** not reached for the fourth time: first
  items of M5-H3. **BUG-0702 item 1 (S4)** goes with M5-4f (the densest cluster). **BUG-0704 items 2-3 (S4)** (the "camps" wording; two
  re-staged rows weaker than their names). **CLAUDE.md's "Current milestone: M1" line** (one line, the Producer doesn't edit CLAUDE.md:
  the next sim dev does, or the owner). **Not bugs:** the House row's far-corner jam 17-18 / 20 (pathing slice 2); 500 attackers on one
  Town Hall +30 % a tick. **Closed in M5-H2:** ~~BUG-0703~~ (S3, the collapse), ~~BUG-0732~~ (S4), ~~BUG-0745~~ sim half (S3; the view's
  scene half the same session: closed), ~~BUG-0701~~ (S3), ~~BUG-0702 items 2-3~~, ~~BUG-0650~~ (S4), ~~BUG-0675 c~~ (S4), ~~BUG-0704 item
  4~~ + the `TestSim` nit, ~~BUG-0651~~ (S3, closed as no longer reproducing; the guard row runs), ~~BUG-0750 item 4~~ (the fuzz's off-seat
  share), and the session's own ~~BUG-0760~~ (S3) / ~~BUG-0762~~ (S3) / ~~BUG-0763~~ (S4); ~~BUG-0761~~ (S2, data-owned) by D-H5's round.
  Memory bound re-baselined 232,080,000 → **232,306,000** (head 232,225,352 B: +131,072 the surround layers, +36,864 one more layer,
  +24 the mask, +24; QA: justified, the same ~72 KB margin policy).
- **BUG-0734 (S2, M5-P1 QA; not debt: the merged-tree reprint at the 2325 integration, data track; fixed by b616086):** the faction pages' siege tables
  (`Content/CounterTriangleMarginsTests`, 8 rows) measure the line razing a Tent / Billet, and the surround rule makes the line faster
  (on the old data: Catapult on a Tent line 192.2 → 155.0 s; Ram on a Billet 254.5 → 191.8 s, the Ram 13.8 → 13.7 s); D19 re-printed
  the same cells the same session on the old movement. **Producer decision:** no sim change; the data track re-prints both pages' siege
  cells + prose and re-runs `BuildingKillTimeReportTests` on the merged tree before the push (standing rule 25 from now on). **BUG-0732
  (S4, M5-P1 QA; M5-H2):** (1) a sealed pocket's open-but-unreachable spots are claimed nearest-first (store-seam scenes only; a
  reachability filter on the pick); (2) `SurroundRule.HeldByOther` ignores the owner (a rival's claims read as held: 8 / 12 strike with 3
  seats). **BUG-0733 (S3, wontfix, Producer decision 2026-10-16):** the claims decide the next re-pick but are not in `StateHash`
  (hashing them = a hash-format change moving every golden checkpoint for no gameplay change); docs/03 "Implementation (pathing slice
  1)" + "Save/load and replays" make them M6 save state (both arrays whole, compared directly by the round-trip test). ~~BUG-0730~~ (S3:
  the fenced-hall row samples "busy" at think ticks, the limit unchanged), ~~BUG-0731~~ (S3: a building chase walking to its claimed
  spot keeps on while `ReachableOnFoot`, every mode) **fixed in M5-P1's round 1** (620ac47). **BUG-0745 (S3, view-filed, maybe
  movement; M5-H2 item 3):** `M4PlayableTest` seed 1 step 7: the Sapper stood still among the pulled-back army through 8 Moves (replay
  `m4playable-seed1-fail-tick2497.replay`): a bare-sim probe decides scene fault vs movement fault. **Second data point (the Producer's
  merged-tree loop at the 2325 ACCEPT):** seed 6 step 6 "a kill and a loss: not by tick 5500" under load, PASS alone (49.3 s). Not bugs (M5-P1 QA): the House row
  stays at 17-18 / 20 (the far-corner jam, slice 2); 500 attackers on one Town Hall cost +30 % a tick (1.94 → 2.50 ms quiet), 0 B.
- **BUG-0703 (S3, M5-4e QA; first item of M5-H2):** a defeated player's standing buildings stay active (its towers shoot and score
  kills, its Forge research runs on; nothing but the door, `CanPlace` and the AI think reads `IsDefeated`). **Producer decision (c), docs/01
  2026-10-14:** the remaining buildings collapse on the defeat tick (freed like razed ones, death events for the rubble, no kill
  credited); flip QA's `WinConditionQaTests.ADefeatedPlayersTower_KeepsShooting_Measured` and `...CommandsOnItsStandingBuildings_*(defeated)`.
  **BUG-0704 (S4, M5-4e QA):** item 1 fixed (M6-V7's dev line; docs/03's M6-V4 section reworded by the Producer at the ACCEPT); item 3 re-staged `DefeatRuleQaTests.EveryCommandKind_*` / `AbilityManagerQaTests.ADefeatedSeat_NeverCasts` weaker than their
  names (covered by `WinConditionQaTests`); item 4 `DefeatCountsFuzzTests` one-way; `TestSim.cs` `int worker =Rts...` missing space.
  ~~BUG-0705~~ (S2), ~~BUG-0706~~ (S3) **fixed in M5-4e's round 1** (`TestSim.KeepIn`; 14 rows re-staged with guards).

- **BUG-0701 (S3, M5-4c QA):** an AI think grows about 0.028 ms per live vision-blocking storm (`FogStore.NextSeenUnit` leaves its
  inlined cell test for `CanSeeUnit` against every zone while `Zones.BlockerCount > 0`; 0.93 ms at 24 storms, 2,400 units), and
  `AiPerfTests` loads data with Sandstorm's `blocksVision` off so the gate never sees it. Fix: test only units near a blocking zone (a
  per-cell "near a blocker" mask like `FogStore.Update`'s) and give `AiPerfTests` a shipped-data row with a fixed storm count (8);
  standing rule 21. **BUG-0702 (S4, M5-4c QA):** (1) two qualifying groups either side of a caster → no cast (the centroid is empty
  ground; seed the group from the enemy nearest the caster or the densest one: slice 2 if a few lines); (2) several casters stack a
  non-stacking ability on one point in one think (skip a point another caster was sent to this think for abilities whose statuses
  refresh); (3) a group exactly at `range + radius` is in the search ring but never castable (a docs/03 line: the reach is a search
  ring, the cast needs the group within the radius of the pulled-back point). ~~BUG-0700~~ (S3) **fixed in M5-4c slice 1**.
- **BUG-0675 item c (S4, M5-4b QA):** `defendRadius` 256 (the maximum) on a 128-cell map makes an AI that never attacks (every enemy
  it sees is a threat, the threat never ends, no wave starts): a docs/03 line beside the knobs ("a radius covering the map = an AI that
  only defends") or a tighter maximum if the data track asks; items a / b / d closed by the Producer's triage. ~~BUG-0670~~,
  ~~BUG-0671~~ (S2), ~~BUG-0672~~, ~~BUG-0673~~ (S3) **fixed in M5-4b's fix round** (session 2026-10-13-0916). Also from M5-4b's QA,
  not bugs: three raiders rotating at three spots re-send the army every other think (gated at that level); a seat the other's wave
  reaches before its own first wave defends instead of waving (`AiVersusAiTests` seed 2).
- **BUG-0651 (S3, M5-H1 QA):** at 2x the supported crowd (1,000 ordered attackers through the ramp detour) one unit gives up a reachable
  building: a stopped chaser whose chase cell's field isn't cached reads "unreachable" in `CombatSystem.ReachableOnFoot` (the `Moving`
  branch holds the count; the stopped branch should too); un-skip `SimHardeningH3QaTests.ThousandUnits_*`; fold into an M5-4b task
  that touches the chase code if a few lines. **BUG-0650 (S4, M5-H1 QA):** `EconomyManager.Sites` never gives up on a site no worker can
  reach (hand-spawned past the placement rules; 99 joins / 300 thinks, income 67 %): a per-site join count beside `SiteStill`, give up or
  back off after N; un-skip the QA row. `StalledThinks` 3 ruled a code guard (docs/01). The tower measurement (5 joins, 4 workers lost)
  waits for M5-4b's defense. **BUG-0622 item 1 (S4):** a hand-built start location by the west edge wraps the hall's anchor
  (`ViewApi/StartBase.Plan`, view-owned: the sim may take it by name); items 2-4 fixed. ~~BUG-0598~~, ~~BUG-0640~~, ~~BUG-0620~~,
  ~~BUG-0621~~, ~~BUG-0511~~, ~~BUG-0512~~ (S3), ~~BUG-0513~~, ~~BUG-0542~~, ~~BUG-0573~~, ~~BUG-0594~~, ~~BUG-0541~~ (S4, with the
  data's D16) **fixed in M5-H1** (session 2026-10-13-0527).
- ~~**BUG-0620 (S3, M5-4a QA):**~~ (kept for the record) `MapGenParams.Validate` accepts start params the border ring can't hold (`StartSiteRadius + 2` cells in), so
  `Generate` and a validated replay header throw (`InvalidOperationException`; 180 / 444 swept sets): `Validate` requires
  `max(lo, StartSiteRadius + 2) <= min(hiX, Height - 3 - StartSiteRadius)` by name; un-skip QA's two rows. **BUG-0621 (S3, M5-4a QA;
  Producer ruling in the file):** 15 / 100 seeds on 128 maps (seed 1 among them) start the bases on different levels, 70-90 m apart:
  a same-level rule and a least separation (half the smaller side) in `FindStarts`; the golden may move once. **BUG-0640 (S3, filed by
  the data QA from the D-H4 report, sim track):** on M5-3's AI two Malazan Billet sites are placed and never finished (cap 82 at 30 min,
  was 98); no resume path (`OwnBuildingSites` only skips), `EconomyManager.cs:60` turns the house rule off while any house site stands.
  **BUG-0622 (S4, M5-4a QA):** (1) a hand-built start location by the west edge wraps the hall's anchor to the east edge; (2) short
  resource counts favour player 0 (`--mines 1` → 1 / 0); (3) ~~docs "tick doubles"~~ fixed in docs/03 at the ACCEPT; (4) the dev's mirror
  row allows ±1 where the rule is exact. ~~BUG-0592~~ (S3: wood late on seeds 1-2) **fixed in M5-4a** (session 2026-10-13-0146: the start
  forest within 24 m; asserted 1,200 / 2,000).
- **BUG-0598 (S3, M5-3 QA re-check 2; not debt: folded into M5-4a):** a wave whose members all stand by a target none can strike
  (a hall ringed two cells deep by trees, combat on) stays on forever with no orders: each member hits `MaxTries` by the target, the
  "crowded off it" branch assumes friends fight it, none does, nothing ends the wave. Direction: end the wave (cooldown) when no member
  has been ordered or made progress for `waveCooldown` ticks, or keep the crowded branch only while some member is Attacking the
  target. Un-skip `QA/AiMilitaryQaTests.AWaveWhoseMembersAllStandByATargetNoneCanStrike_DoesNotStayForever`. **BUG-0592 (S3, filed S2,
  re-triaged at the 1930 ACCEPT; not debt: the start-locations task):** on generated seeds 1-2 the AI knows wood at ~2,400 ticks and
  gathers at ~3,800-4,400; the map has no start locations (the forest is 71-75 m out). Target after M5-4a: first wood by tick 2,000 on
  seeds 1-3. **BUG-0594 (S4, M5-3 QA):** (1) the rebalance switches the first `Gathering` worker in slot order and
  `EconomySystem.StartGather` drops its partial load of the other kind: pick an empty (or the lightest) load; (2) the economy's
  idle-pass scout skip has no test (its mutation survives); (3) ~~`waveCooldown` 0 undocumented~~ docs/03 says it turns the bound off.
- ~~BUG-0574~~ (S3: no mark while loaded or without a drop-off; `ClearSeenMarks` on sight; QA's row un-skipped), ~~BUG-0590~~ (S2,
  pre-existing since M4-2b: `CombatSystem.TooNear`, a negative gap is in reach of an attack without a minimum range), ~~BUG-0591 /
  0595 / 0596~~ (S3: the wave's units-only leave, `MaxTries` 3 with progress by a cell, a member leaving alive is not a loss),
  ~~BUG-0593~~ (S2: the 56 m map grid after the mirror), ~~BUG-0597~~ (S1: four page cells re-printed) **fixed in M5-3** (session
  2026-10-12-1930); ~~BUG-0541 item 1~~ and ~~BUG-0573 item 2~~ (a weight that rounds to 0f is a load error) fixed there too.
- **BUG-0574 (S3, M5-2 QA re-check; not debt: folded into M5-3, which rewrites the economy manager's marks):** a worker carrying a full
  load with no drop-off (the AI's last hall razed) is Idle after its Gather applies, so `EconomyManager.NoteDroppedGather` marks the live
  out-of-sight mine as gone; `Blackboard.NodeDropped` never clears, so the mine is lost to the AI for the match. Directions: don't judge
  a Gather while the worker carries a full load of that kind or has no drop-off; or judge "dropped at apply" (the unit's `GatherNode`
  default) rather than "Idle now"; and clear a mark when the node is next seen alive. Un-skip QA's
  `AiGatherMarkQaTests.ALiveMine_IsNotMarked_WhenALoadedWorkerHasNoDropOff`. **BUG-0573 (S4, M5-2 QA):** (1) `PlayerView.CanPlace`'s
  `StoreFull` and pocket answers read world-wide state (as the human's ghost does): a docs line or drop the store check; (2)
  `ProductionManager` divides by a weight BUG-0541 shows can load as 0f (goes with BUG-0541 item 1).
- ~~BUG-0540~~ (S3: per-player node memory in the fog, `FogStore.NodeAt` / `NodeMemory`, unhashed; the view answers a remembered node
  until its ground is seen; QA's row un-skipped) **fixed in M5-2** (session 2026-10-11-0634); ~~BUG-0570~~ (S2: dropped-Gather marks),
  ~~BUG-0571~~ (S2: content-independent AI tests), ~~BUG-0572~~ (S3: `CanPlaceAnywhere` before the site search) fixed in M5-2's fix round.
- **BUG-0542 (S4, filed S3, ruled at the 0314 ACCEPT):** `PlayerViewPerfTests` gates the banded layout (0.23 ms alone); interleaved,
  eight views answering nearly all 2,500 units cost 0.57 ms Debug / 0.38 ms Release (the answers themselves, ~20-30 ns a record). Per
  think, not per tick, so under 0.1 ms a tick amortised. Next sim hardening: a second gated row for the interleaved layout (1.0 ms units,
  1.5 ms whole read, Debug) and docs/03 "Cost" naming both. **BUG-0541 (S4, M5-1 QA):** (1) a composition weight of 1e-300 passes
  `Checker.Pos` and stores as 0f (refuse a weight that rounds to 0 as a float, or floor at 1e-6); (2) `PlayerViewArchitectureTests.Scan`
  skips code lines starting with `*` (strip comments properly); (3) docs/02 Easy "rarely uses abilities" vs the bool `useAbilities`
  (M5-4's ability manager: a rate, or the docs say "doesn't"); note: handle slot indices are a weak side channel (M5-2 design note).
- **BUG-0511 (S3, M4-H3 QA; first item of the next sim hardening):** `MovementSystem.OwnFighterAhead` accepts the whole forward
  half-plane (`Dot > 0`), so an own fighter 68-79° off the caster's line (across a cliff edge, 0.7-0.8 m edge to edge) holds the
  walk-in's stuck count for as long as it fights (at exactly 90° it drops). Direction: the blocker must be inside the caster's swept
  corridor (lateral offset ≤ the two radii plus a small slack) and on the caster's level or a legal step away; reword docs/01 / 03
  "its centre in front of the direction of travel" with it; un-skip QA's `AnOwnFighterBesideTheCaster_NotInItsWay_DoesNotHoldTheStuckCount`
  (0.3 / 0.6). Decided at the 2334 ACCEPT: a caster ringed by its own attackers in an endless fight waits for good (criterion 1).
- **BUG-0512 (S3, M4-H3 QA; second item):** 444 of 500 units give up an ordered Attack on a reachable building once they reach the
  crowd round it: movement's stuck stop leaves the unit Idle for one tick, a scan on that tick reads `ReachableOnFoot` false (not
  Moving), and the stall count held at 9 reaches 10. Direction: test the goal field's cost at the unit's cell whatever its state and
  keep the give-up only where the walk ended at the nearest ground (the goal cell's cost finite but the target out of reach), or treat
  a stuck stop beside friends attacking the same target like the BUG-0143 friend exception; un-skip QA's
  `FiveHundredUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp_ZeroBytes`. Not a regression (the base gave all 500 up at tick 39).
- **BUG-0513 (S4, M4-H3 QA):** `AbilityScenes.Had / TickOf(resolved: false)` count a Dropped event as a cast start (add `!e.Dropped`);
  `TowerGhostQaTests.ARangedOrderOnARememberedBuildingUpACliff_EndsWithin2Minutes(gone: false)` loosened with `||` (the Tent must take a
  hit with the target still held); the memory comment says +16,408 where +11.8 KB is measured. **BUG-0510 (S4, M4-H3 profiling; QA's
  scene):** `EmptyTick_WithManyUnits_IsCheap` spawns 2,244 of 2,500 units off the map into the corner bucket and they brawl along the
  diagonal, so acquisition is 75 % of the "idle" tick. Fix: a true idle row (units on the map, one owner or a non-fighter, spaced beyond
  sight) before M5 adds per-player AI work; whether `SpawnUnit` should refuse an off-map point is a sim-rules question (the replay
  format accepts it).
- **BUG-0461 (S3): the movement half only.** The 0.17 m two-tick limit cycle on flat ground beside planted own units (seed 1 tick 1644
  of a sandbox replay that no longer loads; BUG-0035 / 0037 family) is not reproduced in six hand-built layouts; documented in docs/03
  "Known limits". Needs the sandbox to save a replay of it again under the current data; since M4-H3 a caster in that spot would vibrate
  while it waits instead of giving up. ~~The cast half~~ fixed in M4-H3 (`CastWalkInBlockerRange` / `CastWalkInPatienceSeconds`,
  `OwnFighterAhead`, `AbilityEvent.Dropped`; `Abilities/CastWalkInTests`).
- ~~BUG-0480~~ (S3: `World.DetectNeighbors`, +11.8 KB), ~~BUG-0481~~ (S3: exactly one `revealed` when any `stealthed`), ~~BUG-0482~~
  (S4: a reveal replaces the oldest non-stealth entry), ~~BUG-0451~~ (S3: the 2,500 row's gate 550 µs with the trend itemised; the
  profile is BUG-0510), ~~BUG-0401~~ (S3: `CombatSystem.ReachableOnFoot`, the stall count held at `GiveUpScans - 1`;
  `Combat/OrderedChaseReachTests`), ~~BUG-0391~~, ~~BUG-0094~~ (`ResourceMaps.SpawnSealSafe`), ~~BUG-0302~~ (a, b, d fixed; c wontfix:
  the ability perf phases run twice, 20x under budget), ~~BUG-0275 item 1~~ (`BuildingGhost.Site`, view-only, **not hashed**: hashing
  it moved the seed-21 checkpoints; item 2, the `ExploreAllForTests` seam, kept as a marked seam), ~~BUG-0422 items 2-3~~ (closed as
  notes), ~~BUG-0271 / 0272~~ (wontfix, documented limits: the projectile store sized for units; one ghost per building slot),
  ~~BUG-0142 items 1-2~~ (wontfix, documented) all closed in **M4-H3** (session 2026-10-10-2334). Not reached: BUG-0144, BUG-0113
  item 2 (below). ~~BUG-0452~~ fixed in M4-5.
- ~~BUG-0421~~ (S3: spawn at the max incl. hp techs, `ProductionSystem.Researched` adds the max change to live units, floor 1)
  and ~~BUG-0420~~ (S3: the `whenBelowHp` line in whole millionths, a 64-bit integer compare; QA's row un-skipped, an 80 M-case
  oracle) fixed in **M4-4b-3b** (session 2026-10-10-1314). **BUG-0422 (S4):** item 1 (docs/03 mojibake) fixed by the Producer at
  the 1015 ACCEPT; items 2-3 are notes (Martyrdom misses a unit trained this tick, documented; the memory margin) to close here.
- **Memory bound (re-baselined in M4-4b-3a):** 231,908,912 of 231,920,000 bytes on the 1024 map (**11,088 B margin** after M4-H3's
  +11.8 KB measured: `World.DetectNeighbors`, `int[UnitCapacity]`; the row's comment says +16,408 + rounding, BUG-0513 item 3); before
  it M4-5's +9,144 (`StatusStore._stealth` 4,120, `World._detectedBy` 4,120, `DetectorBuildings` 1,048, counters) and M4-4b-3b's
  +33,832; every step since M4-4a fix round 1 is itemised in `FieldBuildFairnessQaTests`' comment. **The next per-slot array
  re-baselines** (itemised, in the brief: M5's AI state is the likely one).
- ~~BUG-0360~~ (S3: `FogStore.SeesUnitNearZones` + per-zone fog records hashed while live; `ZoneHides` by the point),
  ~~BUG-0361~~ (S3: the recorder guard), BUG-0363 items 1-2 (S4: `VisionSystem.BlockerNear`; a new storm hides at once; item 3
  a note), ~~BUG-0330~~ (S3: the death list units + buildings), ~~BUG-0311~~ (S3: `UnitSeesFootprint` = `fog.SeesFootprint`),
  ~~BUG-0241~~ (S3: `UnitStore.ChaseSwitches` / `ChaseChainBest`, give up at `GiveUpScans` switches unless a friend fights),
  ~~BUG-0157~~ (S3: `CombatSystem.ChasesInSight` for units on an attack-move leg; mean bound rows), ~~BUG-0270~~ (S3: the
  per-(building slot, player) reveal pair, hashed while in force), ~~BUG-0242~~ (S4: containers first in `ExpectedKind`), the
  docs/02 "allied" wording: all fixed in **M4-H2** (session 2026-10-10-0624, 0 fix rounds, QA PASS_WITH_ISSUES). Cross-track:
  BUG-0390 (S3, the view's `QaGhostViewTest` check, Requests 29).
- Known gap (M4-4b-2, documented in docs/03): an attack whose minimum range exceeds the blind reach (the Catapult) can't fire
  while Blinded. Revisit if the owner cares.
- ~~Producer decision (0724) for M4-4b: the DoT whole-seconds rule~~ → landed in M4-4b-1 (docs/01 row 2026-10-09).
- **Producer decision (0724) for M4-4b:** a `damageOverTime` `applyStatus` duration must be whole seconds, at least 1 s, at load
  (a DoT under a second deals nothing with the pulse clock); docs/01 row with the task.
- ~~BUG-0280~~ (S3) fixed in M4-4a (session 2026-10-09-0724: `CanPlace` skips hidden enemy units; the Build is dropped on apply).
- View item from BUG-0275 item 1 (M4-H3): `BuildingGhost.Site` exists now (view-only, unhashed); drawing a remembered site as a
  foundation is the view's (next view hardening; docs/03 "Implementation (M4-V5)").
- ~~BUG-0215~~ (S3: the visible bits packed and hashed; golden regenerated once), ~~BUG-0211~~ (S3: the seed-21 replay re-recorded,
  11,541 checkpoints checked), ~~BUG-0216~~ (S4: `w*w + h*h`, docs/03 known limits 1-4), ~~BUG-0230 item 2~~ (the public
  `Scenario/CounterTriangleScene`), ~~BUG-0149~~ (S3: `UnitStore.ChasePrev`), ~~BUG-0134~~ (S3: `CheckAnyOfReachable` honours
  `researchedAt` / `trainedAt`), ~~BUG-0133~~ (S4: van der Corput radii), ~~BUG-0158~~ (S4: warm-up + medians), ~~BUG-0113 item 1~~
  (S4: wrong-type messages), ~~BUG-0153 items 3-4~~ all fixed in M4-H1 (session 2026-10-08-2144). Not reached there: BUG-0144,
  BUG-0142 items 1-2, BUG-0113 item 2, BUG-0094 (below).
- ~~BUG-0214~~ (S2, the 1024-map memory bound re-baselined 228 → 230.5 MB with the fog itemised) and ~~BUG-0217~~ (S3, the
  vision check after the best-candidate compare) fixed in the M4-3a fix round; ~~BUG-0184 item 1~~ fixed in M4-3a (item 2 is
  a view note, handled by interpolation).
- ~~BUG-0146~~ (S2) and ~~BUG-0139~~ (S3) fixed in M4-2a (session 2026-10-08-0313), with ~~BUG-0150 / 0152 / 0154~~ (S2,
  filed and fixed in-session) and BUG-0153 items 1-2. **BUG-0144 (S3,** chasers' field waits under placement churn,
  `ChasersUnderPlacementChurn_LongestFieldWait_AtMostOneSecond` skipped; not reached in M4-H1 / H2 / H3): third item of the next
  sim hardening, or with M5's AI placement work if it touches the field cache (BUG-0080 is the same family).
- ~~BUG-0156~~ (S3) fixed in M4-2b (session 2026-10-08-0913: `CombatSystem.AttackerInScanRange`, the re-pick between
  swings while the last attacker is alive and within the scan radius; ~~BUG-0180~~ (S3, the first fix keyed on a stale
  `LastAttacker`), ~~BUG-0181~~ (S3, recorder vs a non-default projectile capacity) and ~~BUG-0183~~ (S2, the lead rule)
  filed and fixed in-session; BUG-0182 item 1 fixed (`DataLimits.MinProjectileSpeed`)).
- **BUG-0184 (S4, M4-2b lead nits; fold into M4-3 if a few lines):** `step^2 <= lead^2` fails at exactly `leadSpeed` in
  13 of 360 headings (float rounding; no shipped unit walks at exactly 5 m/s; flip the skipped
  `QA/ProjectileLeadQaTests.Lead_AStepOfExactlyTheLeadSpeed_IsLedInEveryHeading`); a re-led bolt can step 1.8 m in one
  tick (a view note, Requests 21). Also: `ProjectileImpact.Position`'s doc comment says "where the target was when the
  shot was fired" (it is the impact point, which a led shot moves).
- **BUG-0182 item 2 (S4, data note, not a sim fix):** Sappers splash themselves in melee; friendly fire is 23-28 % of
  deaths in shooter-heavy fuzz (`Stress/RangedSplashFuzzQaTests`). The data track reports it in D6.
- **BUG-0151 (S3, M4-2a, Producer re-triage):** the 200-worker gather row costs 0.32 ms (was 0.23; budget 1 ms) because
  the wedge fix puts 39 % more workers to work in 2x denser crowds at the nodes (per-walker cost unchanged, +24-30 % per
  resource delivered). Revisit with the crowd-cost work after the M4 sandbox (the same item as BUG-0028 / 0032 / 0046);
  the dev tried four cheaper variants, each broke a `GatherPocketStressTests` row or cut little.
- Note (M4-1): `CombatTerminationTests`' 3x walking-limit bound could be 2x (worst measured 1.23x). `CombatConstants`
  (scan interval, grace, give-up scans, max give-ups) are engine rules in code, not data; move them to `rules.json` if
  the owner wants to tune them.
- **BUG-0113 item 2 (S4)**: the 10k-unit load Perf row times a failing load (10,000 slot errors) since BUG-0010 (item 1,
  the CLR type names, fixed in M4-H1).
- M4 note: `TechBonus` has no tower path (towers have no attack until M4-1 / M4-3).
- ~~BUG-0112~~, ~~BUG-0100~~, ~~BUG-0097~~, ~~BUG-0095~~, ~~BUG-0096~~, ~~BUG-0099 item 2~~ fixed in M3-H2 (session
  1715); ~~BUG-0008~~, ~~BUG-0010~~, ~~BUG-0098~~, ~~BUG-0099 items 1 + 3~~ fixed in M3-6 (session 1415).
- **BUG-0080 (S3, known limit since M3-H1)** a closing change every p ticks lets only the 2p oldest goal
  groups walk while it lasts; the bound is in docs/03 "Known limits" and pinned by
  `SimHardeningTests.ClosingsEveryPeriodTicks_*`. Revisit (usable-stale closed fields with a stuck-tick
  exemption) once the M5 AI's placement rate is known.
- ~~BUG-0081~~ decided in M3-H1: the M6 save file stores the flow-field cache's contents (docs/03).
- ~~BUG-0078~~ fixed in M3-3; ~~BUG-0093~~ (pocket rule), ~~BUG-0091~~, ~~BUG-0092~~, ~~BUG-0079~~,
  ~~BUG-0076~~, ~~BUG-0071~~, ~~BUG-0072~~ fixed in M3-H1 (session 0800).
- ~~BUG-0090 sim part~~ (`BuildingSlot.cs` comment) fixed in M3-3; the data part stays in the data backlog.
- Note (M3-2b): `FlowField.Build` has the 8 directions written out (~150 lines) for Debug speed; fold
  back only if tests move to Release. The felling Perf row (`OneTreeFallsEveryTick_AverageTick_Perf`,
  < 0.5 ms absolute) has ~20 % headroom and fails under full-suite contention: rerun alone before
  filing; widen the scene or pin p50 if it ever fails alone.
- Note (QA, M3-2): a worker that fills up with no own drop-off ends its loop Idle with the load and a
  drop-off built later doesn't re-arm it; consistent with docs/03, a "resume" is a design option for
  the M3 HUD / M3-4.
- Note (QA, M3-H1): `--record` now truncates an existing file before the run (an aborted run leaves an
  empty file); `TightBlob2500` has 0.06-0.1 ms of headroom alone (4.49-4.54 of 4.6 ms, M4-H1 note in docs/03): a failure
  alone, fresh process, is real; under any other load it fails on base and head alike.
- Note (M4-H1): the full-state hash on a 1024 map costs 0.107 ms (2 players) / 0.42 ms (8) with the fog's two bit sets;
  M6's save stores the explored and visible bits and must not re-stamp at load (docs/03 "State and hashing").
- Note (QA, M3-4): `CommandDoorFuzzStressTests` covers kinds 0-15 since M3-H2 (session 1715; new kinds from M4 must be
  added). `HalfPop` / `HalfPopCap` are derived (not hashed): the fuzz recount is the guard.
- Note (M3-H2): plateau ids, the per-plateau spawn memos and the seal memo are derived scratch (not hashed;
  `StateHashTests.PlateauIds_AndTheSpawnAndSealMemos_AreDerived_AndNotHashed`); the seal memo keys on `NavGrid.Version`,
  never `BlockVersion`. `TightBlob2500` 4.27-4.39 ms alone against 4.5: M4-1's per-unit work must stay inside it.
- **BUG-0046 (S3, known limit)** wall clips in slot order; a slot-free sort exists in the M1-9
  report (hard walls first, farthest first, ties by position) and re-rolls three fitted bounds.
  Revisit with the crowd-cost work after M4. 4 QA rows skipped.
- **BUG-0050 (S3, known limit)** random-goal give-ups 4.7% (target 3%); alternatives in docs/03.
- **BUG-0028 / BUG-0032 (S3)** crowd targets (4 points 51% / 34%, 64 goals median 22-24% give up,
  same-owner crossing 22 / 200): needs a crowd cost in the flow fields; after the M4 sandbox.
- BUG-0040 (S4) part 2: in-tick AI enqueue vs the phase-14 checkpoint is a design note for M5.
- BUG-0025 (S3) evict the live field with the newest order (the M2-5 churn confirms it at 64 goals)
  + BUG-0026 (S4) rotate same-tick tie-break.
- BUG-0005 (S3) per-player command buckets (O(n^2) insertion sort under a flood); before M5.
- BUG-0023 (S3) single field build > tick budget on maps > 256: documented as unsupported.
- Note (QA, M3-6): `CanResearch` reports `Requires` ahead of `AlreadyResearched` (the brief's order); a researched
  Age II whose halls fall reads "Locked" if the view asks. The dev `SpawnBuilding` ignores requirements (documented). A
  queued `Build` is checked when it starts (phase 7), so it sees a phase-3 Age II of the same tick; direct commands see
  it the next tick (documented).
- Note (QA, M3-5): a tech queued behind a pop-blocked unit waits (the M3-4 pause rule; Producer kept it); the
  golden regen reason for M3-5 is in docs/03 (data-hash only: `techs.json` + buildings' `requires`).
- BUG-0002 (S4) `.sln` Release config maps RtsGame to Debug; with `tools/export.ps1` (M6).
- Perf (Debug, this machine, alone): 500 moving 0.62-0.63 ms, with 12 forests + 8 mines 0.82 ms; 500
  marching + 50 gathering 0.90-0.92 ms; felling + 50 builders + 500 marchers 1.19 ms (1,000: 1.42, 2,500:
  2.55); 100 refused Builds in a tick 0.04-0.09 ms (SealsGround 33 ms, BUG-0096); push-out of 16 in a blob
  of 400 0.29 ms, 400 stacked in a Keep 2.46 ms; 200 workers gathering alone 0.26 ms; flow field build
  128 x 128 0.37 ms (0.7 before M3-2b); 32 groups with a tree felled every tick 0.40 ms (1.76 before);
  full 4,096-slot resource hash 20 µs; 2,500 one-player tight blob 4.33-4.40 ms (enforced <= 4.5, ~3% headroom); two-player
  contested blob 6.84 ms (guard < 10.5); 2,500 to 4 points 3.08 ms (guard < 3.7); 1,000 walkers
  crossing a 1,500 blob 14.2 ms (report); CLI 2,500 march 4.6 ms. Setup: 128 map with 12 forests +
  8 mines +5 ms; 1024 map at the resource caps +2.2 s. Gather efficiency per worker per minute
  falls with crowding at shared nodes (10 workers 29.5 → 200 workers 13.4), not from field waits.
- Known limits: docs/03 "Known limits (M1), as of M1-9" is the list (flow fields ignore units;
  4.7% random-goal give-ups; the BUG-0048 map gives up 76 of 128; pack rule not guaranteed; partial
  spawn-order independence; clusters over 32 are not plugs; holders are walls only; maps > 256
  unsupported; perf measured in Debug). Plus: enqueue stamps `TickNumber + 1`; `SimInfo.Version`
  recorded but not checked; no depletion events (views poll); .NET 8 support ends 2026-11-10, move
  to the next LTS at M6.

## Debt backlog: view track (hardening sessions only; counter **1 / 4** after M6-V5, session 2026-10-16-1654)

- **After M6-V5 (2026-10-16-1654):** ~~BUG-0770~~ **fixed** (2e4313a: `ShellTest.Flow` / `SkirmishBoot` / `FreeForAll` and `AiSeatTest` on
  per-process `user://` files never written, each scene comparing the player's `settings.cfg` bytes before / after; QA: a rebound key's
  md5 unchanged over the windowed `ShellTest`, the 61-scene loop and the smoke). **Not a bug (QA note, for M6-VH3):** the `--colour`
  collision warning reads "Ignoring --colour: slot 2 on colour 2 ..." even when slot 2 had no `--colour` of its own (its default was
  taken by an earlier slot's pick; M6-VH1's wording). `--players 4 --units 12` arms players 0-1 only (documented). Open view debt
  otherwise unchanged (below).
- **After M6-VH2 (2026-10-16-0847), open view debt:** **BUG-0770 (S4, new, M6-VH2 QA):** `ShellTest.Flow` / `SkirmishBoot` and `AiSeatTest`
  boot `Main` without a `SettingsPath`, so they read the player's `settings.cfg` and a desk rebind can fail them (QA fixed its own five
  scenes; a per-process path as `SettingsTest` now does); fold into the next view task that touches `ShellTest`, else M6-VH3. **BUG-0744
  item 3** documented (one bad hand-edited key resets a row: docs/03 "The file"). **BUG-0550 item 2**, **BUG-0521 items 2-3** (unreachable),
  **BUG-0281 item 2** (the M6 fog-look pass), **BUG-0663** (S4) unchanged. Note: the seed-6 bench runs at zoom 60 since `BenchRunner.FitArmy`,
  so its numbers aren't comparable with older runs (documented); the old shared folders `settings-test/` and `shell-test-settings/` under
  `%APPDATA%/Godot/app_userdata/RtsGame/` are unused (safe to delete). **Closed in M6-VH2:** ~~BUG-0684~~ (S4, items 1-5), ~~BUG-0744 items
  1-2~~, ~~BUG-0743 item 3~~ (S4, closed), ~~BUG-0745~~ scene half (with the sim's half: closed, Producer), ~~BUG-0622 item 1~~ (S4, closed).
- **BUG-0744 (S4, M6-V8 QA round 1; M6-VH2):** `[keys]` leftovers the tab can't reach: (1) "Meta+K" / "Command+K" / "Cmd+K" / "Kp+K"
  read with 0 problems as K (Godot drops a prefix it doesn't treat as a mask): compare `OS.GetKeycodeString(key)` with the trimmed input;
  (2) `TrySet` accepts non-key codes inside `CodeMask` (don't round-trip): `IsPlainKey` requires the name to round-trip; (3) one bad
  hand-edited key can reset a whole row (15 problems on a grid rotation without moving A): documented in docs/03 "The file", a design
  choice. **BUG-0743 (S4, M6-V8 QA):** items 1-2 fixed in the round (the screen's Defaults keeps the keys; framed key buttons, one
  "Default keys" at the top of the tab); **item 3 open:** the last-seen amount is sampled once per rendered frame's tick, so at 2x or a
  hitch "Last seen" can be one gather older than the sight covered: `UpdateAmounts` from `SimRunner.Ticked` if a few lines, else wontfix
  (`SeenResources.Update` has the same cadence by design). **BUG-0745 (S3, M6-V8 QA round 1):** `M4PlayableTest` seed 1 step 7 failed once
  under load (the Sapper never left its cell through 8 right-clicks, 45.5 m from the Smithy; PASS alone in 45.7 s), **and seed 6 step 6
  once under load on the Producer's merged-tree loop** ("a kill and a loss: not by tick 5500"; PASS alone 49.3 s); the sim's M5-H2 probes
  the saved replay first; the scene half (pull the army off the Sapper before step 7; step 6's staging) if it says scene fault. ~~BUG-0740~~ (S2: `KeyMap.Read`
  checked each key against defaults of actions it hadn't read: tab-made swaps lost on the next launch; now `ResolveClashes` over the
  whole map), ~~BUG-0741~~ (S3: a group digit / Tab may never share a grid key), ~~BUG-0742~~ (S3: masked "Shift+Q" and `Key.Special`
  refused) **fixed in M6-V8's round 1** (acdaeef; QA's `QaM6V8R1Test`: 3,000 seeded tab walks, 4,799 action × key bindings, 500 fuzzed files).
- **BUG-0711 (S3, filed by the Producer at the 0946 ACCEPT; not debt: a conductor-dispatched view fix round before the view's merge, else
  the sim branch is held and the view's first item next session):** (1) `AbilityViewTest.ProphecyBarRow` places seat 1's Smithy before
  seat 1 has any unit, so under M5-4e seat 1 is out and its `Research` is dropped ("Zealot max 63"); fix: one Whirlwind unit for seat 1
  on a far cell three ticks before the Smithy. (2) `QA/ViewApi/M6V7QaTests.ViewApiDriven_*` (2 rows) places a **House** (named "Billet")
  for player 0 before any unit: player 0 out, the Barracks spawn dropped (`GatherMaps.Building` expected 2, got 1); fix: `TestSim.KeepIn`
  on both twins. Standing rule 24.
- ~~**BUG-0710 (S4, M6-V4 QA)**~~ **fixed in M6-V7** (session 2026-10-14-0946: `ResourceBar.Sync` from `Main.OnMatchEnded`, mutation-verified;
  `ViewApi/MatchOver` + `ViewApi/MatchOverTests` + `QA/ViewApi/MatchOverQaTests` deleted). (kept for the record:) (1) the HUD's "K / L" line under the banner is one
  tick behind the stats (the banner pauses inside the killing tick before the `Pausable` HUD's `_Process`): a `Sync()` from
  `Main.OnMatchEnded`, or let `ResourceBar` run `Always` for its text; (2) `sim/Rts.Sim/ViewApi/MatchOver.cs` is dead since M6-V4
  (`Match` reads `DefeatEvents`): delete it with `ViewApi/MatchOverTests` and QA's `QA/ViewApi/MatchOverQaTests` (QA agreed; the
  behaviour they pinned is the sim's rule, covered by the M5-4b tests and `MatchOverViewTest` / `QaM6V4Test`). ~~BUG-0674~~,
  ~~BUG-0680~~, ~~BUG-0681~~ (S3) **fixed in M6-V4** (session 2026-10-13-1616; BUG-0684 item 3 guarded).
- ~~**BUG-0674 (S3, M5-4b QA; not debt: the view's first item next session, red on `main`):**~~ (kept for the record) `AiSeatTest.MatchOverLine` cancels
  player 0's only building and rebuilds; under the defeat rule the cancel defeats player 0 and the rebuild is dropped ("round 2: 0
  buildings after the Build"). Fix: a second building of player 0 standing in that row, or round 2 expects the drop (one `Match over:`
  line); M6-V4 rewrites the row for the real banner anyway. **BUG-0680 (S3, M6-V6 QA; with it, red on `main`):** a 20-digit integer in
  `settings.cfg` makes `ConfigFile.Parse` log an engine ERROR (`String::to_int`); the pre-parse `LineShape` accepts any digit run: cap
  it (`-?\d{1,18}` for integers; longer only with a fraction or exponent); QA's two `QaM6V6Test` rows are the regression test and
  print FAIL until then (rule 19: they should print `KNOWN BUG-0680`; the fix turns them into Checks). **BUG-0681 (S3, M6-V6 QA):**
  the boot's `ApplyWindow` sets the stored size in Windowed, undoing Godot's `--resolution` / `--fullscreen` / `--maximized` /
  `--position`, so the bench runs at the stored size and depends on the quality preset; fix: skip the window part when an engine
  window flag is in `OS.GetCmdlineArgs()`, or let a dev / bench run ignore the file's window + quality; document in docs/03
  "Implementation (M6-V6)". **BUG-0684 (S4, M6-V6 QA):** (1) `SettingsTest` / `ShellTest` write fixed `user://` paths shared by every
  worktree and loop (a per-process folder as `QaM6V6Test` does); (2) the screen shows the file's VSync while `--vsync` overrides it;
  (3) `CommitSettings(stay: false)` with a live unpaused match calls `ShowMenu()` (no UI path reaches it; `else if (CurrentMatch ==
  null)`); (4) `GroundDiscPool.Reclaim` scans every node per call (O(nodes × keys) on a frame of all-new keys; tens of discs today);
  (5) the bench orders 48 of seed 6's 100 (the column is half on screen). ~~BUG-0661~~ (S2), ~~BUG-0662~~, ~~BUG-0682~~, ~~BUG-0683~~
  (S3) **fixed in M6-V6** (session 2026-10-13-0916).
- ~~**BUG-0661 (S2, M6-VH1 QA; not debt: the view's first item next session, red on `main`):**~~ (kept for the record) `QaH2Test` (seed 1: the army centre moves
  19.3 m in 10 s against a 20 m line; seed 6: 11.3 m against 15 m) and `QaM27Test` (19.3 m) fail since the M5-4a merge (`FogViewTest` and
  `QaV3Test` were red too and turned green with M5-H1's start rule). Test staging on the new start geometry: re-derive each threshold
  from the scene's intent (the start block, the unit's speed, a ramp or forest in the seed-6 way), the reason in the test; never loosen
  without one. **BUG-0662 (S3, M6-VH1 QA):** `GroundDiscPool.Reclaim` takes the first node whose key isn't taken yet this frame, so on a
  full pool a new low key cascades every stable key to another node and rebuilds its mesh (4 rebuilds for 1; BUG-0520's "never
  rebuilt" broken); fix: reclaim only from keys lower than the key being taken (callers take keys ascending), else grow; a Check that
  stable keys keep their nodes. **BUG-0663 (S4):** `--colour 1 2` alone warns "Ignoring --colour" (nothing was ignored: slot 2's default
  moved); a one-tick Gathering / Returning snapshot is not robust on Normal (18 % of ticks 300-500 have no worker standing): new scenes
  check "first gathering tick ≤ 400". ~~BUG-0604~~, ~~BUG-0632~~, ~~BUG-0603~~, ~~BUG-0660~~ (S3), ~~BUG-0631~~ (S4), BUG-0550 item 1,
  BUG-0521 item 3 **fixed in M6-VH1** (session 2026-10-13-0527).
- ~~**BUG-0632 (S3, M6-V3 QA re-check):**~~ (kept for the record) two table colours break `CombatViewTest`'s corpse legibility rules (BUG-0160 item 3): Crimson's
  corpse fill luminance 0.185 (< 0.2), Iron v Jade corpse fills 0.222 apart (< 0.25); Cobalt (0.210) and Cobalt v Violet are close.
  Producer decision: retune `ui.json` (names kept) + a `ShellTest` row over all 8 colours and 28 pairs; flip `QaM6V3Test.CorpseRulesOverTable`
  to Checks. **BUG-0631 (S4, M6-V3 QA):** ~~items 1-2~~ fixed in the round (equal RGBs refused; four tests on `PlayerRgb`); item 3 open:
  both slots may pick one colour → Producer decision: unique per match (the hardening). **BUG-0604 (S3):** behaviour fixed on the merged
  tree (the menu's AI gathers by tick 400; every hall sees a mine); stays open until `AiSeatTest` / `QaM6V3Test`'s probes are Checks (the
  hardening's first item). ~~BUG-0600~~ (S3: `Minimap._Notification`), ~~BUG-0601~~ (S3: per-player colours), ~~BUG-0602~~ (S4: `{{0}}`
  refused; item 2 = BUG-0630), ~~BUG-0630~~ (S2, filed and fixed in the round: `--skirmish` seats slot 2 AI Normal) **fixed in M6-V3**
  (session 2026-10-13-0146).
- **BUG-0604 (S3, M6-V2 QA; not debt: the view half is M6-V3, the sim half M5-4a):** from the menu the match is the dev sandbox
  (100 units a side in contact, `Pop 163 / 10`: the AI's workers die and its Trains wait), and with `--units 0` the start base sees no
  node on seeds 1 / 3 (the generator has no start locations). **BUG-0601 (S3, M6-V2 QA; M6-V3):** a mirror match draws both sides in
  the faction's `PrimaryColor` (`Match` `playerRgb`, `UnitViews` materials per faction): per-player colours. **BUG-0600 (S3, M6-V2 QA;
  M6-V3):** `Minimap._jumping` is the third press-state holder (a 362 m camera jump on a motion after a pause); drop it on
  `NotificationPaused` / `Unpaused`; flip `QaM6V2Test.MinimapHeldIntoPause`'s probe to a `Check`; sweep `game/scripts` for other
  press-to-release flags. **BUG-0603 (S3, M6-V2 QA; the view's hardening):** `M4PlayableTest` seed 6 step 7 flaked once under load (step 6
  ran 2,268 ticks into the enemy base, the camp fell to 338 hp, the blast razed it and the Sapper died with it: no sight on the rubble);
  pull the army back once the enemy army is gone, or check sight from a unit kept at the camp; replay
  `m4playable-seed6-fail-tick4094.replay`. **BUG-0602 (S4, M6-V2 QA):** (1) `setup.map_size_item` `"{{0}}"` passes `IsSizeFormat` (M6-V3);
  (2) `--skirmish` keeps slot 2 human while the menu's Start has an AI seat (M6-V3 makes `--skirmish` = the menu's Start).
- ~~BUG-0580~~ (S3: `RtsCamera` / `SelectionController` drop held buttons on `NotificationPaused` / `Unpaused`; the two probes are
  `Check`s) **fixed in M6-V2** (session 2026-10-12-1930); ~~BUG-0581 items 1 + 4~~ (`setup.map_size_item`, the CS8602) fixed there too;
  items 2-3 open (item 2 = the generator's start locations, M5-4a).
- **BUG-0580 (S3, M6-V1 QA; not debt: folded into M6-V2, which touches the pause code):** a mouse release while the tree is paused
  never reaches `RtsCamera` (`_dragging`) or `SelectionController` (`_pressing` / `_boxing`), so after Resume the camera follows the
  cursor (8.43 m on one motion) or the selection box stays and follows it. Fix: clear held-button state on `NotificationPaused` /
  `NotificationUnpaused` (or `Main.PauseMatch` tells both), or check `Input.IsMouseButtonPressed` on motion; turn the two
  `KNOWN BUG-0580 probe` prints in `QaShellTest.PauseBurstAndLeaks` into `Check`s. **BUG-0581 (S4, M6-V1 QA):** (1) the map size items
  "N × N" are a C# literal (`SkirmishSetup.Build`): a `setup.map_size_item` format in `ui.json` (M6-V2); (2) a 192 / 256 map keeps the
  128 layout (halls ~55 m apart, 8 mines, 12 forests) with a bigger margin: sim map generation, for the map slice (M6 criterion 7);
  (3) the dev could not set BUG-0560's status (process; the briefs' wording changed at the 0634 ACCEPT); (4) **added by the
  Producer at ACCEPT:** `game/tests/QaShellTest.cs(62,15)` CS8602, the one build warning on the merged tree (QA's own scene; a null
  check in M6-V2 keeps `main` at 0 warnings).
- ~~BUG-0560~~ (S3: step 5 done on the cast's resolve, `CastOnBlob` fails fast with the enemy army gone, step 8 asserts the Burning
  marker on staged Zealot A; 16 / 16 seed-runs under load) **fixed in M6-V1** (session 2026-10-11-0634).
- **BUG-0550 (S4, M5-V1 QA):** (1) `GroundDiscPool.Take` grows when the free stack is empty although nodes held by keys this frame
  won't take are only freed in `End` (16 keys → 16 other keys makes 32 nodes; once, bounded by the key count): reclaim stale nodes in
  `Take` / `Begin`, or correct docs/03's "only when a frame needs more than ever before"; (2) the lost-cast disc (body radius + 0.9 m)
  lies under the crowd that blocked the walk-in, so with `--no-hud` a red sliver is all that shows: the M6 art pass (a wider disc, a
  tint on the caster's mesh, or an above-ground mark). Notes: a cue on a caster that dies plays the sound and draws nothing; several
  drops within the 50 ms Sfx gap are one sound (documented).
- ~~BUG-0520~~ (S4: cast rings and resolve flashes as pooled `GroundDisc`s on the terrain cells, the flash grown through the shader
  `scale`) **fixed in M5-V1** (session 2026-10-11-0314). **BUG-0521 (S4, M4-VH3 QA):** ~~item 1~~ (`GroundDisc.Place` compares the
  clamped hole) fixed in M5-V1; items 2-3 open, none reachable: `SeenResources` drops an old remembered node unseen when only the
  reused slot's new footprint is seen (nodes spawn only at setup); `CombatViews.MaxHp ??=` keeps an earlier `Bind`'s array on a
  re-bind (`Match` binds once).
- ~~A remembered site as a foundation~~ (from the sim's BUG-0275 item 1, M4-H3) → **done in M6-V1** (session 2026-10-11-0634:
  `BuildingViews` draws it at `SiteMinHeight` in the darkened site colour; `BuildingPicker.PickGhostRay`'s `siteShare` overload picks it).
- ~~BUG-0430~~ (S3: `ViewApi/GroundPatch.Collect` + `GroundQuad`, `GroundDisc` on an `ImmediateMesh` of the terrain cells under the
  disc, `ground_disc.gdshader`; `zone_disc.gdshader` removed) and ~~BUG-0462 item 1~~ (the hover / targeting rings the same),
  ~~BUG-0431~~ (S4: `OwnZoneRimColor` teal (0.2, 0.8, 0.9); pixels `#75DDE8` vs `#DCAE55`), ~~BUG-0490~~ (S4: `SlotMaxHp` filled once a
  tick and shared; the measured range 2,869-3,081 / 3,692-4,493 in the comment and docs/03), ~~BUG-0400 items 2-3~~, ~~BUG-0126
  item 5~~, ~~BUG-0250 item 2~~ (the `Straddle` radius term kept with its reason), wontfix with reasons ~~BUG-0462 item 3~~ (marker
  rows at zoom 60: M6 art), ~~BUG-0126 item 6~~ (the resource bar's text rebuild: M6 HUD), ~~BUG-0148 item 1~~ (facet slivers: M6
  props): all closed in **M4-VH3** (session 2026-10-10-2334, 0 fix rounds, QA PASS_WITH_ISSUES; QA's `ViewHardening3QaTests` 13 rows
  + `QaVh3Test` in the loop). Not reached: export hygiene (below).
- ~~BUG-0450~~ (S3) **fixed in M4-V7 slice 2** (session 2026-10-10-1954). ~~BUG-0460~~ (S2) and ~~BUG-0463~~ (S2) fixed in the M4-V6d
  fix rounds (session 2026-10-10-1314). BUG-0462: items 1, 2, 4, 5 fixed, item 3 wontfix (M4-VH3): closed.
- Notes (M4-V6c QA, not bugs): a seen enemy disc is drawn whole over unexplored black (deliberate, listed for the M6 fog-look pass
  with BUG-0281 item 2); `FogView.Refresh` costs 1.8-4.3 ms a refresh with 16 blocking zones and ~1,960 units (pre-existing
  M4-4b-2 / BUG-0360 work: the per-zone records; measure again at the sandbox and file if it shows); `--units 1000` spawns
  ~1,960-1,990 units, not 2,000.
- ~~BUG-0390~~ (S3, M4-H2 sim QA; a view file, Requests 29) **fixed at the 0624 integration** (cc85fdf, conductor-dispatched):
  `QaGhostViewTest` seed 6's per-unit walk check; verified on `main` at the 1015 PLAN.
- ~~BUG-0400~~ (S4, M4-VH2 QA): item 1 closed by QA's `NewViewApiHelpers_VH2_*`; items 2 (`CommandCardTest.RestartMatchCancel`) and 3
  (`SeenResources` takes only the removal when just the old footprint is seen) fixed in M4-VH3.
- ~~BUG-0370~~ (S3: `AbilityCaster.HasQueuedCast` + `ViewApi.SentCasts` until the apply tick), ~~BUG-0371~~ (S4: `SlowColor`
  0.08 / 0.3 / 0.95; `ResolveFlashes` decides `Shown` and the age origin on the first frame), ~~BUG-0251~~ (S3:
  `game/tests/SceneExit.cs` in all 44 scripts; 30 / 30 under load), BUG-0281 items 1 + 3 (S4: `ViewApi.SeenResources`,
  `MinimapRaster.DrawnEnemyDotAt`), BUG-0250 items 1 + 3 (S4), BUG-0126 item 3 (S4): all fixed in **M4-VH2** (session
  2026-10-10-0624, 0 fix rounds, QA PASS_WITH_ISSUES).
- ~~BUG-0342 (S4)~~ and ~~BUG-0340 (S4)~~ fixed in M4-V6b (session 2026-10-10-0215: a thicker bar on a violet back with a
  minimum fill, Shift keeps the ability armed; prebuilt "+N" strings, cached minimap `StringName`s). The walking-casters
  remainder of BUG-0342 is BUG-0370.
- ~~BUG-0341 (S2, M4-V6a QA)~~ fixed in the session's fix round (a cached `StringName OrderQueue` in `AbilityViews`,
  `SelectionController`, `CommandCard`; `QaV6aTest` 0 B rows).
- ~~BUG-0310 (S3)~~ fixed in M4-V6b (session 2026-10-10-0215: `FogView.CollectGhosts` skips an entry whose remembered footprint
  has a visible cell, through `FogStore.SeesFootprint`; the QA row un-skipped, QA's footprint fuzz 2 seeds x 1,200 ticks).
- ~~BUG-0273~~ (S3) fixed in M4-V5 (session 2026-10-09-0724: the seed-21 replay re-recorded, 12,131 checkpoints checked; the
  sim's M4-4a hash whitelisted by BUG-0303).
- **BUG-0281 item 2 (S4, M4-V4 QA; items 1 and 3 fixed in M4-VH2):** new corpses / rubble appear darkened in explored fog
  although unseen (docs/02 says last-seen state); deferred to the M6 fog-look pass (a per-cell "last seen" copy), documented
  in docs/03 "Implementation (M4-VH2)".
- ~~BUG-0250 item 2~~ (S4): `TerrainHeight.Straddle` keeps its radius term with the reason in the comment (a flat 5 cm put discs
  0.26 m under ramp ground on 4 seeds); wontfix in M4-VH3. Items 1 and 3 fixed in M4-VH2.
- ~~BUG-0219~~ (S2: the three Raiders spotted; the fog merged), ~~BUG-0220~~ (S3: `WallClock.Wait`, `SfxTest` on the wall clock, 8x
  regression rows), ~~BUG-0226~~ (S4: three step bands in `MaxUnder`, the 0.25 m row flipped, the Billet / Tent rows run behind
  `Check`s, docs wording), ~~BUG-0222~~ (S4: the off-line restart, the M6 slide note, the ring order documented), ~~BUG-0190~~
  (S4, set `fixed`), ~~BUG-0148 item 3~~, ~~BUG-0126 item 4~~ all fixed in M4-VH1 (session 2026-10-08-2144). Not reached:
  BUG-0148 item 1, BUG-0126 items 3 / 5 / 6 (below).
- ~~BUG-0147~~ (S3) fixed in M4-V1 (session 2026-10-08-0313: the five M2 scenes pass `--no-combat`, launch arguments
  only, no `Check` or threshold changed; loop 28 headless + 5 Shot + Playable).
- ~~BUG-0160~~ (S4) fixed in M4-V2 (session 2026-10-08-0913: four F12 lines, the first-sight flash rule in `HitFlash`'s
  typed overload, corpse fill 85 % on a 30 % rim, empty fallbacks); the docs/03 "until format 4" line fixed by the
  Producer at that session's PLAN. Coverage gap noted in M4-1: the view hash twins run combat off; `CombatViewTest`'s
  twin now runs on combat (closed).
- ~~BUG-0125~~ fixed in M3-V4 (session 2315: `ResourcePicker.PickRay` tests `PropsView.Shape`, the drawn trunk + cone /
  block + gold block; 0 / 3,000 open-ground rays taken for a node). ~~BUG-0145~~ (the Playable re-task flake) fixed in
  the same session.
- ~~BUG-0148~~ (S4, M3-V4 nits): item 1 (facet-sliver picks, ~1 %, at most 8 cm) wontfix in M4-VH3 until the M6 art pass replaces
  the placeholder props; item 3 fixed in M4-VH1; items 2 and 4 in the M3-V4 fix round. Closed.
- ~~BUG-0126~~ (S4, M3-V3b nits): items 1-2 fixed in M3-V4, item 4 in M4-VH1, item 3 in M4-VH2, item 5 (the minimap refresh sum
  guarded at 0.5 ms, measured 0.275) in M4-VH3; item 6 (the resource bar's text rebuild on a total change, the M2-H2 rule) wontfix
  until the M6 HUD pass. Closed.
- ~~BUG-0123~~, ~~BUG-0104~~, ~~BUG-0105~~, ~~BUG-0107~~, ~~BUG-0122~~, ~~BUG-0124~~ fixed in M3-V3b (session 1715);
  ~~BUG-0108 / 0109 / 0110~~ fixed in M3-V3 (session 1415; `QaV2Test` rows are `Check`s now).
- ~~BUG-0106~~ set to `wontfix` at the 1131 ACCEPT (criterion reworded at 0925; file + README row updated by the
  Producer).
- ~~BUG-0069~~, ~~0070~~, ~~0083~~, ~~0084~~, ~~0085~~, ~~0086~~, ~~0087~~, ~~0088~~, ~~0101~~,
  ~~0102~~, ~~0103~~ fixed in M2-H2 (session 0800).
- Edge-pan hover suppression (`RtsCamera.EdgePanBlocker`) can't fire today: the minimap's 8 px margin
  keeps it out of the 8 px edge band. Harmless; revisit if the HUD layout changes.
- Export hygiene: ~~exclude `game/tests/` from the release build~~ → **done in M5-V1** (`game/export_presets.cfg` "Windows Desktop",
  `exclude_filter tests/*`, `include_filter data/*.json`; pack-only export verified: 67 files, none under `tests/`, all 17 data JSON;
  the test scripts still compile into `RtsGame.dll`, documented). Still open for M6's export task: load `game/data/` and `ui.json` in a
  `.pck`-safe way instead of `ProjectSettings.GlobalizePath("res://data")` (needs the text-taking `DataLoader` overload from the sim
  track, Requests 33), `tools/export.ps1` and its exported-exe smoke check; the export templates are the owner's download.
- `MinimapDotsShot --units 1000` silently drops its second lone unit (store full; documented).
- Cosmetic: ramp ends ~31° vs 22° mid-ramp; no wall skirts on the map border; 1024² mesh 904 MiB
  transient (outside supported sizes); `StartLayout` at radius 1.0 puts bodies exactly touching.
- Overlay deferred items (docs/03): per-unit state labels, arrows for more than one goal, a window
  that follows the visible trapezoid, the dev console.
- Note: headless scene runs print Godot warning stack traces for the expected "order dropped"
  warnings; not errors. Builders: absolute paths for all file I/O.
- Optional M0 item: Godot MCP server (owner install; not needed since `--screenshot`).

## Debt backlog: data track (counter **0 / 4** after D-H5, session 2026-10-16-0847)

- **After D-H5 (2026-10-16-0847):** **BUG-0780 (S3, dev-filed, the owner's call):** since the surround rule + D19, 2 Catapults do 1.7× / 1.6×
  the 20 Raiders' damage per cost on a hall / Town Hall (2.8× on a House; 2.3-3.9× the Heavy Infantry's everywhere): pinned as measured
  in `BuildingKillTimeReportTests`, not loosened; options under For your review (accept, or a Catapult pass measured first). **Closed:**
  ~~BUG-0750~~ (S4: items 1-2 the reprint, 3 + 5 D-H5, 4 the sim's M5-H2), ~~BUG-0721~~ (S4, all three items), ~~BUG-0761~~ (S2, the kill-time
  report's House column measured a collapse: `TestSim.KeepIn(sim, 1)` after the attackers + three guards that fail any fall not player
  0's kill; QA: 0 collapses in the 895 Content tests). Rule 24 for every measured report that stages a lone non-producing building: a
  keeper, placed after the attackers so the unit slots don't shift.
- **BUG-0750 (S4, D19 QA; D-H5):** (1) `docs/factions/andii.md:51` "115 vs a Town Hall per swing" is 118 at armor 2; (2) docs/03:1889
  "Shipped: ... armor 5" reads as today's value; (3) `QA/Content/D18ReportPreconditionQaTests`' Structure / ×1.5 lever edits now write the
  shipped values and can't fail (QA's `D19QaTests.BeforeD19Copy_*` covers the kill-time path); (4) `Stress/AbilityManagerFuzzStressTests:120`'s
  `UseAbilities` check can't fail with every shipped difficulty casting (the off-seat property rests on
  `Ai/AbilityManagerTests.ASeatWithUseAbilitiesFalse_*`, a data copy): give the fuzz a share of off-seat scenes (sim-owned test, a named
  exception); (5) the golden regen's reason is in the `SameGameDataHashes` comment, not the commit message (D10c's weren't either: write the
  convention down). **Process (D19):** the dev's first report was BLOCKED on 18 stale sim-track pins the brief had not all named
  (`DamageCalcTests`, `CombatTests`, `SplashTests`, `AbilitySystemTests`, `QA/CusserQaTests`, `DataValidationTests`, `Ai/AbilityManagerTests`,
  `Stress/AbilityManagerFuzzStressTests`); the conductor granted a named exception; every hunk is a pin or a comment, none loosened
  (Producer-read). Rule for the next data brief that changes a formula input: grep `sim/Rts.Sim.Tests` for the old literal first and list
  every hit. **BUG-0734 (S2, sim-filed, data-owned cells):** the merged-tree reprint at the 2325 integration (see the sim backlog).
- **BUG-0721 (S4, D18 QA; D-H5):** (1) `BuildingKillTimeReportTests` checks the siege ratio against the 20 Heavy Infantry row only (against
  the Raider row the margin is about half: 2.5-3.9x, still ok); (2) `AiSpendingReportTests.GeneratedNormal_*`'s conclusion counts the 15 /
  20-minute marks after the idle seat's defeat (741-840 s); (3) the generated rows use `AiScenes.Generated`'s 6 mines, the menu 8. Also from
  the report, not a bug: Crossbowmen sit on the 1-damage floor against halls under every lever (131 s / 263 s unchanged by (C)). **Process:**
  the QA report file `studio/qa/2026-10-14-0946-D18.md` was never committed (standing rule 23).
- ~~**BUG-0720 (S4, D17 QA)**~~ **fixed** (item 1 by the Producer at the 1616 ACCEPT; item 2 in D18, session 2026-10-14-0946, verified by QA).
  (kept for the record:) item 1 (docs/03 line 6185 quoted the old page line "Laborers flee to the Garrison Keep") **fixed by the
  Producer at the 1616 ACCEPT**; item 2 (both pages: "a Quartermaster's Depot more than 16 m from it" reads as "from the Keep"; the
  rule means from the threat: "whichever is nearest among those more than 16 m from the threat; else the nearest one") → D18. Notes
  from D17's QA (not bugs): the spending report reads `Simulation.AiOf` / `Board` / `Def` through `InternalsVisibleTo` (the house
  report's pattern, BUG-0641's precedent, accepted); the flat scene's 12 trees a base run dry by 20 min (re-measure on a generated map).
- ~~BUG-0641~~ (S4) **fixed in D16** (session 2026-10-13-0527: every internal named, `ai.ThinkTicks` / `ai.HouseType` read from the AI
  with a house-type precondition, the workers column documented). Notes from D16's QA (not bugs): J compares post-tick-conversion
  values (a sub-half-tick `thinkInterval` edit passes the page pin; the sim can't see it either); docs/02 never states outright that
  Normal doesn't micro (a Producer-default wording + a K row is a candidate small item). No open data bug.
- ~~**BUG-0641 (S4, D-H4 QA; D16):**~~ (kept for the record) the house report's comment leaves out `Simulation.AiOf`, `AiPlayer.Board`, `PendingOrder.*`,
  `PendingKind`; `thinkTicks` / `house` re-derived beside `AiPlayer` (read them from the AI or assert equal once); the "workers" column is
  a live count now. ~~BUG-0610~~ (S4) **fixed in D-H4** (session 2026-10-13-0146). **BUG-0640** (S3) is the sim's: filed from this
  track's report (two Malazan Billet sites never finished on M5-3's AI; the data's numbers for the sim's fix: 11 placed → 9 finished, cap
  82 at 30 min). **Process (D-H4 QA):** `taskkill /F /IM python.exe` killed two processes machine-wide → standing rule 16 in the handoff.
- **BUG-0610 (S4, D15 QA; D-H4):** `AiHouseRuleReportTests` asserts `ai.Faction == player` beyond "no exception"; it and
  `AiContentQaTests.E` read `AiPlayer.Board` internals (`StepPending`, `HousePending`, `StepDueTick`, `StepDone`, `ThinkTicks`,
  `HouseType`) that a sim rename breaks at compile time (the sim's M5-4a touches the `Blackboard` again: the conductor rebuilds after
  the merge); the class comment hard-codes measured numbers (date them, or move them to the session report).
- ~~D14 had no QA inspection~~ → **D15** (session 2026-10-12-1930) wrote the attack list as 87 + 52 permanent rows and measured the
  house-rule overlap (the economy rule never fired in 30 minutes; the cap never reached 100 on the flat scene: wood runs out at 18:00;
  the 12th Billet stays due at no cost; gold piles to 6,004): **keep the order as shipped** (Producer decision, For your review). Note
  for M5-4's difficulty pass: the unspent gold says the AI's spending is the next tuning target, not its housing.
- ~~BUG-0561~~ (S4: andii.md / shadow.md ×1.1 with a note, whirlwind.md's "nearest" clause, malazan.md's measure) **fixed in D14**
  (session 2026-10-11-0634). **Balance follow-up (owner's call, For your review D13):** the Line v Shock band needs hp / armor / cost,
  not the bonus; Shock v Ranged 69-80 % untouched.
- ~~BUG-0500~~ (S4: the three linger phrasings read right; QA's row un-skipped) and the ~~BUG-0470 status line~~ (fixed
  items 1-3) fixed in **D12** (session 2026-10-10-2334); ~~BUG-0530~~ (S2: the two sim-track stealth tests asserted the pre-D12 state;
  4 lines under a conductor-named exception) and ~~BUG-0531~~ (S4: docs/03 "For the data track (M4-5)" rewritten as shipped) fixed in
  D12's fix round 1. **Standing rule from BUG-0530:** a data brief names every sim-track test that pins a value the task changes and
  the final assertion both tracks agree on; the sim track's brief for the same session carries the same line.
- ~~BUG-0470~~ (S4: `Compare` fails a second same-trigger sentence naming both passives and quoting both sentences; the QA row
  un-skipped) and ~~BUG-0440~~ (S4: the colon, the hyphen, "dmg", the linger role per phrase; 15 rows, 11 failed first; QA's 56-case
  matrix) **fixed in D-H3** (session 2026-10-10-1954). D11 landed the same session (the identical `DataContentHashTests.cs` hunk).
- Known gaps (D-H2, documented in docs/03): distance words are one role (range vs radius, sight vs reach not told apart); "for
  N s" cannot tell an ability's own duration from an applied status's. Revisit if a shipped text needs it.
- ~~BUG-0410 (S4, D10c QA)~~ fixed in D-H2 (session 2026-10-10-1015: `DescriptionProblems` checks each number against the field its
  words name; the QA theory un-skipped, 4 / 4 mutants fail naming entry, number and role; QA's `DescriptionRoleQaTests` 49 / 49
  role mutants). ~~D10b note (`Compare` through `GameData.Statuses`)~~ and ~~the D-H1 note (`AgesRuleQaTests`' own heading lookup)~~
  closed in D-H2.
- ~~BUG-0380 (S3, D10b QA)~~ fixed in D10c (session 2026-10-10-0624: every page damage claim must be consumed by a data effect,
  exact-first; the QA row un-skipped).
- ~~BUG-0350 (S3)~~ and ~~BUG-0351 (S4)~~ fixed in D10b (session 2026-10-10-0215: the Cusser pinned by field; the Duration
  message; the review table in the report). ~~BUG-0362 (S2, cross-track, filed by the sim QA)~~ fixed in D10b's fix round.
- Data counter 0 / 4 after D-H2, 1 / 4 after D11. D9 dropped (Producer decision, session 2026-10-09-1155 PLAN). ~~Pending text
  (Producer proposal, owner may veto): `blinded.description` "can't target" → "can't attack anything more than 3 m away", with
  D11~~ → shipped in D11 (session 2026-10-10-1314).
- ~~BUG-0090 (S4)~~ **closed in D-H1** (session 2026-10-09-0724): the last item, the towers' shooting / detection text, is pinned
  both ways to `BuildingDef.Attack` / `Detector` by `BuildingContentTests` K / K2 / K3 (5 / 5 mutations caught). ~~BUG-0290
  (S4)~~ fixed in D-H1 (compare by column name, header + stale cells reported, "rows not checked" on a lost key column;
  QA's `PageCompareQaTests`). Data counter 0 / 4 after D-H1. Note: QA's `AgesRuleQaTests` keeps its own `### Ages` lookup
  (could use `PageTables.Heading`; a later QA pass).
- ~~BUG-0111 (S4)~~ fixed in D3 (landed in session 1715 through the sim's M3-H2). The buildings part of BUG-0090
  likewise; the towers part stays (M4).
- ~~BUG-0132 (S4)~~ fixed in D4 (session 2315): A-C messages read "tech field: page X vs data Y"; `RequiresText.Needs`
  reads "requires" / "after" too and a test asserts no shipped description uses them.
- ~~BUG-0155 (S4, D4)~~ fixed in D5 (session 2026-10-08-0913: H any-case, faction tech names covered). ~~Docs drift (D4
  QA note)~~: docs/02 "Ages" reads "level II" and the exact any-two-of rule since D5; `TechContentTests.G` parses it.
  Watch Tower's attack (docs/02 Buildings table) is not in the sim until M4-3.
- ~~BUG-0200 (S4, D5)~~ fixed in D6 (session 2026-10-08-1814: G pins the clause, C's message states the style rule).
- ~~D-H1~~ done (session 2026-10-09-0724); see the BUG-0090 / 0290 line above. Next data hardening after 4 feature tasks.
- ~~BUG-0243~~ (S3: the Malazan page's Lancer seat-1 row 21.5 → 22.0 s, 888 hp; every printed cell pinned), ~~BUG-0230 item 2~~
  (the shared `CounterTriangleScene` harness; the copy is gone), ~~BUG-0260~~ (G reads the Ages bullet from the file's lines;
  nine mutants fail) all fixed in D8 (session 2026-10-09-0125). ~~Items 1 and 3 of BUG-0230~~ fixed in D7.
- ~~BUG-0240~~ (S2) fixed in D7 (the spotter line); ~~BUG-0090's tower-sight item~~ closed in D7 (attack / detector text waits
  for M4-3b: D9).
- **Balance notes (measured in D6, on both pages; the owner's call is under For your review):** winner keeps 67-100 % of its
  cost in every pair (proposed band 40-65 %); Line v Shock 88-100 % is the outlier; casters re-measured after M4-4; the Sapper's
  self-splash is small (keep); crowd income +15 % at 20 workers (BUG-0146 fix) still to fold into the full balance pass.
- Note: a QA row (`AnEmptyBuildingList_Loads_...`) now pins "exactly Whirlwind's ten remain"; it moves
  again whenever a building is added or removed (intended: the roster is pinned).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-16 | [2026-10-16-1654](sessions/2026-10-16-1654.md) | sim M5-4f: the proportional defense (`DefenseManager.Size` / `Pick` / `Recall`: the intruders' worth from `GameData.Units`, the answer max(`defendRatio` × threat, `defendMinValue`), home army nearest the point first off an in-place min-heap, then wave members nearest the hall while the rest keep the wave's start threshold (`Blackboard.WaveMinHalfPop` / `WaveMinCost`), the whole wave when the threat outweighs everything or the hall is hit under `recallHallHp`; `DefenderGeneration` / `DefendAll` / `DefendAnswer` outside the hash), the three knobs validated + hashed, D15 C (a House step done unsent at the hard cap), BUG-0702 item 1 (the cast group seeded from the nearest enemy), `gatherRate` 0.85 / 0.75 + the seed-21 re-record (`Replays/seed21_playable.replay`, 7,682 ticks; `data-hash` C059AD620DE581F1), `EconomyManager`'s per-think no-node cache, CLAUDE.md's milestone line, docs 01 / 02 / 03; view M6-V5: 2-4 player slots (`SkirmishSetup` Add / Remove player, a difficulty list per AI slot, unique colours over the shown slots; `LaunchOptions` `--players`, slots 1-4, `--ai <slot> <d>`; `Match` / `SimRunner.SlotAi` + `SeatsFor`, a base per player on N start locations; BUG-0770; `ShellTest` +413, `FreeForAllTest.tscn`, QA's `QaM6V5Test.tscn`; docs/02 + docs/03 "Implementation (M6-V5)"); data STOP (planned) | **sim REJECT = held for its QA round** (the dev stopped BLOCKED: `GatherPocketStressTests` `(MineOneSide, 40)` red at the quicker gathering, **BUG-0790 S3**; QA full never ran; Producer: build 0 warnings, the slice 407 / 407, the red row reproduced, the diff read whole and sound; **BUG-0791 S4** filed; one round requested, then re-judged), **view ACCEPT** (0 rounds, QA PASS, no bugs; Producer: build 0 warnings, smoke PASS, the four new / touched scenes PASS alone; `sim/` diff empty → merges alone; **BUG-0770 closed**). **M5 2 / 9 (criterion 7 built, not landed); M6 1 / 10 (criterion 1 advanced: slots in, team left); counters sim 1 / 4, view 1 / 4, data 0 / 4; no open S1 / S2; Requests 42 (the save format) added; next: sim M5-4f's round, view replay playback (or the look test), data waits; bug ids next: sim 0820, view 0830, data 0840** |
| 2026-10-16 | [2026-10-16-0847](sessions/2026-10-16-0847.md) | **hardening on all three tracks.** sim M5-H2 (the collapse rule: `World.Collapse` on the defeat tick, `DeathEvent.Collapsed` neither a kill nor a loss, `DropDeadReferences` after; owner-layered `SurroundHolder` + `SurroundRule.Reachable` by cached fields (BUG-0732); `MovementSystem`'s flow-step-alone slide against a wall (BUG-0745's sim half, `WallPinTests`); `FogStore._nearBlocker` mask + `ZoneStore.BlockerVersion` + an 8-storm `AiPerfTests` row (BUG-0701: 1.1 → 0.27 ms); `AbilityManager` no-stacking rule + `ForgetEnded` / `NeverRan` retries (BUG-0702 items 2-3, BUG-0760); `EconomyManager.MaxJoins` (BUG-0650); BUG-0675 c, BUG-0704 item 4, BUG-0651 closed; docs/02 / 03 / 01); view M6-VH2 (per-process `user://` folders in `SettingsTest` / `ShellTest`, the VSync field under `--vsync`, `GroundDiscPool.Reclaim`'s sorted stale list, `BenchRunner.FitArmy`, `KeyMap.Read` name compare + `IsPlainKey` round-trip (BUG-0744 1-2), `FogOfWar.Runner` on `Ticked` (BUG-0743 item 3), `SimRunner.FixedFrameDelta` + `M4PlayableTest.PullBackSpot` (BUG-0745's scene half), `ViewApi/StartBase.Plan` edge wrap (BUG-0622 item 1); QA's `QaM6VH2Test`; docs/03); data D-H5 (BUG-0750 items 3 + 5, BUG-0721 items 1-3, the D18 reports re-run on D19 + the surround rule, BUG-0761's keeper + guards in the kill-time report; no data change, golden unmoved) | **all three ACCEPT** (sim 1 fix round, QA full FAIL → PASS: BUG-0760 S3 / 0762 S3 / 0763 S4 found and fixed, **BUG-0761 S2 data-owned → fixed by D-H5's round**; memory bound → 232,306,000 (justified); view 0 rounds, PASS_WITH_ISSUES: BUG-0770 S4; data 1 round, PASS). Producer: scratch merge sim 12131c8 + view cdb4858 + data 4420985 built 0 warnings, smoke PASS, the full non-Perf suite + the whole scene loop (session log); BUG-0764 S3 filed (Perf rows red on an idle machine on base and head alike). **17 bugs closed; M5 2 / 9, M6 1 / 10 unchanged; counters sim 0 / 4, view 0 / 4, data 0 / 4; no open S1 / S2; next: sim M5-4f (+ D15 C, BUG-0702 item 1, `gatherRate` + the seed-21 re-record), view M6-V5 slots, data an inbox answer or STOP; bug ids next: sim 0790, view 0800, data 0810** |
| 2026-10-15/16 | [2026-10-15-2325](sessions/2026-10-15-2325.md) | sim M5-P1 pathing slice 1 "surround a building" (`Combat/SurroundRule`: a ring of standing spots round a live building, 2 per footprint cell a face + 4 corners, the nearest free one claimed in a fixed order, `World.SurroundClaim` / `SurroundHolder` checked both ways on read and **not hashed** (BUG-0733 wontfix: M6 save state); `CombatSystem` `ChaseCell` / `Chase` / `QueuedAtRing` / no planting on the way / the claim follows a striker / no give-up while queued or walking to a reachable claimed spot in any mode (BUG-0731); `MatchOverTests` re-staged; docs/03 "Implementation (pathing slice 1)" + docs/01); view M6-V8 (`SelectionController.PickResource` / `SelectResource`, `SelectionPanel.ShowResource` "N Gold left" / "Last seen:", `ViewApi/SeenResources.Amount` / `UpdateAmounts` / `InSight`, `KeyMap` 31 actions + the clash rule + `[keys]` read / write with `ResolveClashes` + `Apply`, `SettingsScreen` General / Keybinds tabs, `GameSettings.Keys`, `CommandCard` hints follow the keys, `ui.json` +17 keys, `ResourceClickTest`, docs/02 Controls + docs/03 "Implementation (M6-V8)"); data D19 (×1.5 vs Mounted, Easy `useAbilities` true, Structure 0.7 / 0.35 / 0.6, every building's armor halved: 27 data lines; golden header → **AB811AD47F68D0AE**, `k` lines equal; the kill-time report re-based 12 / 12; both pages re-printed; 18 sim-track pins moved under a conductor exception) | **all three ACCEPT** (sim 1 fix round, QA full FAIL → PASS: BUG-0730 / 0731 S3 fixed, BUG-0733 S3 wontfix (Producer), BUG-0732 S4; **BUG-0734 S2 = the pages' siege cells, re-printed on the merged tree by a data fix-mode dev before the push (REQUIRED)**; view 1 round, FAIL → PASS_WITH_ISSUES: BUG-0740 S2 / 0741 / 0742 S3 fixed, BUG-0743 item 3 / 0744 S4, BUG-0745 S3 (`M4PlayableTest` load flake, maybe movement); data 1 round (the pins), PASS_WITH_ISSUES: BUG-0750 S4). Integration sim → view → data → the reprint. Producer's scratch merge: build 0 warnings, slices 2,564 / 2,574 (the 8 = BUG-0734 with the merged numbers: Catapult / Tent line 30.2 s 56 %, Ram / Billet 31.2 s 44 %), kill-time 12 / 12 (20 HI 11.8 / 23.1 / 45.0 s), smoke PASS, scene loop 57 / 58 under load (the 1 = `M4PlayableTest` seed 6 step 6, PASS alone: BUG-0745's class). **M5 2 / 9; M6 1 / 10 (criterion 4 ticked); counters sim 4 / 4, view 4 / 4, data 4 / 4 → hardening on all three next (M5-H2, M6-VH2, D-H5); Requests 41 closed; next bug ids sim 0760, view 0770, data 0780** |
| 2026-10-14 | [2026-10-14-0946](sessions/2026-10-14-0946.md) | sim M5-4e: the owner's win condition (`World.Defeat.cs` out = has built + `LiveUnits` 0 + `ProductionBuildings` 0; `PlayerLedger` counts at `UnitStore.TrySpawn` / `Free` and `AddFinished`, derived; `World.LiveUnits(p)` / `ProductionBuildings(p)`; `TestSim.KeepIn`; `MatchOverTests` (a)-(g), `DefeatCountsFuzzTests`, 14 scenes re-staged; docs/02 "Game flow" 4, docs/03 "Implementation (M5-4e)"); view M6-V7: `Main._Process` + `ViewApi/CursorConfine` (Confined while a match runs unpaused and focused, `--no-confine`, never headless), `GameSettings.ConfineCursor` + the Settings box, `CameraLimits.EdgeDirection` (the last pixel pans), `ControlGroups` with one building per group, `SelectionController.GroupKey`, BUG-0710 (`ResourceBar.Sync`, `ViewApi/MatchOver` deleted), `DefeatStaging` + the three match-over scenes for both rules, the dev line "is defeated", docs/02 Controls + docs/03 "Implementation (M6-V7)"; data D18: both pages' flee wording (BUG-0720), `AiSpendingReportTests.GeneratedNormal_*` (wood still the limit), `TreesNearBasesReportTests` (19-61 trees within 30 m of every start), `BuildingKillTimeReportTests` (set (C) meets 12 / 12), `LineShockSweepReportTests` (no set in the band) | **all three ACCEPT** (sim 1 fix round, QA full FAIL → PASS: BUG-0705 S2 + 0706 S3 fixed; BUG-0703 S3 (Producer decision (c): the loser's buildings collapse, M5-H2), BUG-0704 S4; view 0 rounds, PASS, **+ a conductor-dispatched round for BUG-0711** (`AbilityViewTest` + 2 `M6V7QaTests` rows staged under the old rule; else the sim branch held); data 0 rounds, PASS, BUG-0721 S4, no QA report file (rule 23)). Integration sim → view → data; golden unmoved. Producer's scratch merge: build 0 warnings, slices 2,367 / 2,369 (the 2 = BUG-0711), smoke PASS, loop 52 / 53 (the 1 = BUG-0711). **M5 2 / 9 (criterion 5 advanced); M6 criterion 4 advanced; counters sim 3 / 4, view 3 / 4, data 3 / 4; no open S1 / S2; next: sim pathing slice 1, view M6-V8 (resource click + keybinds), data D19 (the owner's numbers, one header move)** |
| 2026-10-13 | [2026-10-13-1616](sessions/2026-10-13-1616.md) | sim M5-4c slice 1: `Ai/AbilityManager` (last in the think; the enemy group within `range + radius`, its centroid pulled back to range, cast at ≥ `ai.castMinTargets` 2 enemies in the radius; the nearest seen enemy building for `HitsBuildings`; self / aura and summon at the caster; never with an own unit in a friendly-fire radius; judged by the cooldown, `MaxTries` 3 per point; BUG-0700's map clamp), `PlayerView.TryGetAbilityReady` / `TryGetCast`, six `Blackboard` arrays (26 B × slots), golden header → C6FC120A55AE4A0B; the knobs deferred (hard stop); view BUG-0674 / 0680 / 0681 + M6-V4: `Match.Outcome` / `Ended` / `DefeatedAt` from `DefeatEvents`, `MatchOverBanner` (22) through the pause path with `SimRunner` breaking the frame's ticks, `MatchStats` (23) with `Columns`, the pause menu's Stats, `ui.json` `match_over` 18 keys, Requests 39; data D17: docs/02's Normal phrase + K pin, both pages' Defense lines, `AiSpendingReportTests` | all three ACCEPT, 0 fix rounds (QA PASS / PASS_WITH_ISSUES / PASS: BUG-0700 fixed, BUG-0701 S3, BUG-0702 / 0710 / 0720 S4; the scene loop green on `main` again; M5 2 / 6) |
| 2026-10-13 | [2026-10-13-0916](sessions/2026-10-13-0916.md) | sim M5-4b: the defeat rule (`World.Defeat.cs`: `IsDefeated` / `EverBuilt` / `DefeatEvents`, phase 11b after combat, `Simulation.MatchOver` latched; the door drops a defeated player's commands where they apply, its AI silent, its units stay; `PlacementError.Defeated` + `ui.json` `placement.defeated`; hash bits 34-36) + the AI's defense (`Ai/DefenseManager` first in the think: kept hp per building slot, intruders within `defendRadius` of the hall, the point = intruders' centroid else the hit building, re-versioned on a fresh threat or the second far trigger in a row; the wave ends like a retreat, no wave / rally while it holds; `AttackMove` inside the base else `Move` home then `AttackMove`; workers within `workerFleeRadius` flee to the nearest drop-off away from the point, released `defendFor` after their flee, no gather at nodes by the point), `rules.json` `ai` `defendRadius` 48 / `defendFor` 20 / `workerFleeRadius` 16 (golden header → 72EF431E70C49C39, `k` equal), docs/03 "Implementation (M5-4b)" + docs/01; view M6-V6: BUG-0661 (`BenchMarch.cs`: the march over the units the order sent vs half a free march), BUG-0662 (`Reclaim(key)`), the settings screen (`GameSettings` / `SettingsApplier` / `SettingsScreen`, `user://settings.cfg`, three quality presets, Music / Sfx buses, `RtsCamera.EdgePanSpeed`, `--vsync` wins for the run), `ui.json` `settings`, `SettingsTest.tscn`, `ShellTest` rows, QA's `QaM6V6Test`, QaH1Test's oracle (BUG-0682), ProductionHudTest's click (BUG-0683), docs/03 "Implementation (M6-V6)" + docs/02 + docs/01; data: no task (D16 was already on `main`) | **sim ACCEPT** (1 fix round, QA full FAIL → PASS: BUG-0670 / 0671 S2 (the army never came home; a parked raider idled the workers for good: an AI-vs-AI deadlock) + 0672 / 0673 S3 fixed in the round; BUG-0674 S2 → **S3 by the Producer** (a stale view-test oracle, the product per spec), BUG-0675 S4), **view ACCEPT** (0 fix rounds, PASS_WITH_ISSUES: BUG-0680 / 0681 S3, BUG-0684 S4). Integration sim → view; the sim moves the golden's header once. Producer's scratch merge (docs/01 + coverage unions): build 0 warnings, smoke PASS, `SettingsTest` / `ShellTest` / `QaH2Test` / `QaM27Test` PASS, `AiSeatTest` FAIL (BUG-0674), `QaM6V6Test` FAIL (BUG-0680, on purpose). **M5 1 / 6 (criteria 2, 3, 5 advanced); M6 criteria 2, 4 advanced; no open S1 / S2; counters sim 1 / 4, view 1 / 4, data 1 / 4; next: sim M5-4c (abilities + difficulty knobs), view BUG-0674 + 0680 then M6-V4, data inbox answers or D17** |
| 2026-10-13 | [2026-10-13-0527](sessions/2026-10-13-0527.md) | sim M5-H1 hardening: BUG-0598 (`Blackboard.WaveBusyTick`: a wave idle past `waveCooldown` ends like a retreat), BUG-0640 (root cause the builder's one-tick Idle blink; `PlayerView.UnitAt` reads it as Building, `TryGetBuildTarget`, `EconomyManager.Sites` resumes a site nobody builds after `StalledThinks` 3 thinks: Malazan 36,000 11 / 11, cap 98), BUG-0620 (`Validate`'s `max(lo, rs + 2) <= min(W / 2 - 1 - rs, H - 3 - rs)` + a flat-map centre), BUG-0621 (`FindStarts` `SameLevel` + `Apart` half the smaller side: 0 / 100 mismatched pairs per size, closest 129 / 206 / 350 m), BUG-0511 (`OwnFighterAhead` corridor `CastCorridorSlack` 0.1 m + level), BUG-0512 (`CombatSystem.ChaseCell`, `ReachableOnFoot` for a stopped chaser: 500 units 0 give-ups), six S4 items, docs/01 + docs/03; view M6-VH1 hardening: the start-location probes → Checks (BUG-0604), `ui.json` Crimson `#B82633` / Jade `#2EA35A` + a whole-table `ShellTest` row (BUG-0632), colours unique per match (`SkirmishSetup.PickColour` / `GreyTakenColours`, `LaunchOptions.FirstFreeColour`; BUG-0631; docs/02), `M4PlayableTest` step 7 reads the cell before the blast + seed 6's framing (BUG-0603 / 0660), `GroundDiscPool.Reclaim` (BUG-0550 item 1), `SlotMaxHp.Capacity` (BUG-0521 item 3), QA's `QaVh1Test`, docs/03 "Implementation (M6-VH1)"; data D16: a `## Difficulty` table on both pages, `AiContentTests` J-K (both ways + docs/02's numbers), QA's `AiDifficultyFileSideQaTests`, docs/02 Easy "doesn't use abilities", BUG-0641 | **all three ACCEPT**, 0 fix rounds each (sim PASS_WITH_ISSUES: BUG-0651 S3, BUG-0650 S4; view PASS_WITH_ISSUES: **BUG-0661 S2 pre-existing** (four scenes red on `main` since M5-4a; two cleared by this integration), BUG-0662 S3, BUG-0663 S4; data PASS). Integration sim → view → data; golden untouched. Producer's scratch merge: build 0 warnings, `Content` 933 / 0, `FogViewTest` / `QaV3Test` / `AiSeatTest` / `QaM6V3Test` / `QaVh1Test` PASS, `QaH2Test` / `QaM27Test` FAIL (BUG-0661). **M5 1 / 6 (criterion 4's pins in); 18 bugs closed; counters sim 0 / 4, view 0 / 4, data 1 / 4; one open S2 (BUG-0661, view scenes); next: sim M5-4b slice 1 (Requests 37), view BUG-0661 then a feature, data inbox answers or D17** |
| 2026-10-13 | [2026-10-13-0146](sessions/2026-10-13-0146.md) | sim M5-4a: the generator's start locations (`MapGenParams` `StartLocations` 2 / `StartClearRadius` 8 / `StartEdgeMargin` 10 / `StartMineDistance` 18 m / `StartForestDistance` 24 m; `MapGenerator.FindStarts`: a site disc of open ground, exact point mirror, quarter turns for 3-4, no RNG, redraw through `MaxAttempts`; `Heightmap` / `NavGrid` / `World.StartLocations`, not hashed; `ResourcePlacer` start mines round-robin + a start forest off the exit side; `StartBase.Plan` centred on the location, hand-built maps byte-identical; `AiScenes.Generated` on them; replay format 6, golden header only; BUG-0592 fixed), docs/03 "Implementation (M5-4a)" + docs/01; view M6-V3: `--units 0` from the menu and `--skirmish` (slot 2 AI Normal), a colour per slot (`ui.json` `setup.colour_1..8`: Iron, Ochre, Crimson, Cobalt, Jade, Violet, Rose, Bone) + `--colour`, `Match.PlayerRgb` the one colour source (`UnitViews` per-player materials), `StartFocus`, `Minimap._Notification` (BUG-0600), `UiText.ReadColours` + `IsSizeFormat` (BUG-0602), `AiSeatTest` probe, QA's `QaM6V3Test`, docs/03 "Implementation (M6-V3)"; data D-H4: BUG-0610 (the report's reads named, the assert a precondition), the page cells confirmed, the house-rule report on M5-3's AI | **all three ACCEPT** (sim 0 fix rounds, PASS_WITH_ISSUES: BUG-0620 / 0621 S3, BUG-0622 S4; Requests 37 + BUG-0598 deferred by the PLAN's cutoff; view 1 fix round, FAIL → PASS_WITH_ISSUES: BUG-0630 S2 fixed in the round, BUG-0632 S3, BUG-0631 S4; data 0 rounds, PASS_WITH_ISSUES: BUG-0640 S3 against the sim, BUG-0641 S4). Integration sim → view → data; only the sim moves the golden (header). Producer's scratch merge: build 0 warnings, the slices 2,764 / 4 skipped / 0 failed (7 m 1 s), smoke PASS (both passes), `AiSeatTest` PASS (the menu's AI: 1 worker gathering / returning and 4 queue items at tick 400, no PENDING line) (the menu's AI gathers by tick 400). **M5 1 / 6 (criteria 2-3 advanced; M6 criterion 7's generator half landed); M6 criterion 1 advanced (colour); counters sim 4 / 4, view 4 / 4, data 0 / 4; no open S1 / S2; next: sim M5-H1 + view M6-VH1 (hardenings), data D16** |
| 2026-10-12/13 | [2026-10-12-1930](sessions/2026-10-12-1930.md) | sim M5-3: `ScoutManager` (the worker nearest the hall at `scoutAfter` 15 s; 8 compass points at `scoutRadius` 40 m, the hall's mirror, then a `scoutGrid` 56 m map grid nearest-unexplored first; released once a forest, a mine and an enemy building are known; a dead scout not replaced), `MilitaryManager` (rally by Moves `rallyDistance` 12 m toward the nearest known enemy building; waves at `attack.minArmyPop` / `minArmyCost` × `waveSizeMultiplier` on the known enemy building nearest the rally; Idle by a seen target → `Attack`; a razed target re-targets or the wave goes home; retreat at `retreat.lossFraction`; `waveCooldown` 60 s; `MaxTries` 3 per member with progress by a cell, a member leaving alive not a loss), the enemy memory (seen buildings + ghosts, the last enemy army's centroid), the rebalance, BUG-0574, pop weights, a 0f weight refused, seven `rules.json` `ai` knobs, every `ai.json` / `rules.json` `ai` field in `ContentHash` (golden `data-hash` 5760E83CB301B62E → 8409DB7517579FEF over two regens, `k` lines equal), BUG-0590 (`CombatSystem.TooNear`) + four page cells (BUG-0597), docs/03 "Implementation (M5-3)" + docs/01; view M6-V2 + M5-V2: live faction lists + the AI's difficulty from `ui.json` `setup.difficulty_*`, `SkirmishSetup.MatchArgs` → `LaunchOptions.Parse`, `--faction` / `--ai`, `SimRunner.BuildSeats` (explicit seats always), `Match.PickSeats`, `ViewApi/MatchOver` + the "Match over:" dev line, Requests 37, BUG-0580 (`_Notification`), BUG-0581 items 1 + 4, `AiSeatTest.tscn`, docs/03 "Implementation (M6-V2)"; data D15: `QA/Content/AiContentQaTests` (87 rows a-i, both factions, `minArmyCost` recomputed from `units.json`) + QA's `AiContentFileSideQaTests` (52), `AiHouseRuleReportTests` (3 report rows: the economy rule never fired; keep the order) | **all three ACCEPT** (sim 2 fix rounds, FAIL → FAIL → FAIL on the carried BUG-0592 alone, **re-triaged S2 → S3 by the Producer** (the criterion's timing was unmeasured; the root cause is the generator's missing start locations: the next sim task); BUG-0590 S2 / 0593 S2 / 0597 S1 / 0591 / 0595 / 0596 S3 fixed in the rounds; BUG-0598 S3, BUG-0594 S4 filed; view 0 rounds, PASS_WITH_ISSUES: BUG-0600 / 0601 / 0603 / 0604 S3, BUG-0602 S4; data 0 rounds, PASS_WITH_ISSUES: BUG-0610 S4). Integration sim → view → data; only the sim moves the golden; the Producer's scratch merge: build 0 warnings, the AI / content / ViewApi / replay slices 2,510 / 1 / 0, smoke PASS; one docs/03 conflict with its resolution in the log. **M5 1 / 6 (criteria 2 and 3 advanced); M6 criterion 1 advanced; counters sim 3 / 4, view 3 / 4, data 4 / 4; no open S1 / S2; next: sim M5-4a (start locations + `IsDefeated` + BUG-0598), view M6-V3 (the menu match as hall + workers, per-player colours, BUG-0600), data D-H4** |
| 2026-10-11 | [2026-10-11-0634](sessions/2026-10-11-0634.md) | sim M5-2: BUG-0540's node memory (`FogStore.NodeAt` / `NodeMemory`, unhashed; `PlayerView` answers a remembered node until its ground is seen), `SimConfig.Seats` + `PlayerSeat` / `SeatController` + replay format 5 (`seat` lines; golden header only), `AiPlayer` (thinks before phase 1 every `thinkInterval`, reads only its kept `PlayerView`, sends through `ICommandSink` = `Simulation.Enqueue`, state unhashed, its own `SimRng` stream), `Blackboard`, `BuildOrderExecutor` (concurrent due steps, one command per step per think, judged next think, retry marks), `EconomyManager` (gold share, house rule, dropped-node marks), `ProductionManager`, `PlayerView.CanPlace` / `CanPlaceAnywhere` / `CanTrain` / `CanResearch`, `rules.json` `ai` + `AiRulesDef`, docs/03 "Implementation (M5-2)" + docs/01; view BUG-0560 + M6-V1: the shell (`Main.cs`, `MainMenu` / `SkirmishSetup` / `PauseMenu` / `ShellUi`, `LaunchOptions.HasMatchFlag` + `--map-size` / `--skirmish`, `ui.json` `menu` / `setup` / `pause`), the remembered site as a foundation + the `siteShare` picker overload, `ShellTest` + QA's `QaShellTest`, the second smoke pass; data D14: both `ai.json` 1 → 26 steps, compositions by pop, `attack` 20 / 1,544 and 14 / 861, the pages' tables, `Content/AiContentTests`, BUG-0561 | **all three ACCEPT** (sim 1 fix round, FAIL → PASS_WITH_ISSUES: BUG-0570 / 0571 S2 + 0572 S3 fixed, BUG-0574 S3 + 0573 S4 filed; view 0 rounds, PASS_WITH_ISSUES: BUG-0580 S3, BUG-0581 S4; data 0 rounds, **no QA ran** (dev BLOCKED on six sim pins = BUG-0571), accepted at integration on the merged tree). Integration sim → view → data; only the sim moves the golden (header). **M5 1 / 6 (criterion 1 ticked, 2 advanced); M6 criterion 1 advanced; counters sim 2 / 4, view 2 / 4, data 3 / 4; no open S1 / S2; next: sim M5-3 (scout + military managers, `ai.json` into `ContentHash`, BUG-0574), view M6-V2 + M5-V2 (seats in the lobby, `--ai`, BUG-0580), data D15 (D14's QA rows)** |
| 2026-10-11 | [2026-10-11-0314](sessions/2026-10-11-0314.md) | sim M5-1: `Rts.Sim.Ai.PlayerView` (+ `SeenUnit` / `SeenBuilding` / `SeenResource`; own always, enemy units by `Fog.CanSeeUnit` incl. stealth, enemy buildings by `CanSeeBuilding` or the ghost rule, nodes on explored ground with `Remaining` only in sight, cells, queues; stateless, cursor enumeration, values only), `FogStore.NextSeenUnit`, the `ai.json` schema (`AiDef` / `AiDifficultyDef` / `AiStep` / `AiComposition`, `DataLoader.Ai`, `DataLimits` 256 / 200 / x10, both minimal files, outside `ContentHash`), the oracle fuzz 3 seeds x 2,000 ticks x 4 players + `PlayerViewOracle`, the source-scan and values-only guards, the hash twin, the perf row, memory +0 itemised, docs/03 "Implementation (M5-1)" + "For the data track (M5-1)"; view M5-V1: `ViewApi/LostCastCues`, `GroundDiscPool`, cast rings / resolve flashes as pooled `GroundDisc`s + shader `scale` (BUG-0520), `GroundDisc.Place` clamped-hole compare (BUG-0521 item 1), `SyncCues` + `ui.json` `hud.cast_lost` + `SfxEvent.Refused`, `game/export_presets.cfg`, `AbilityViewTest` rows + QA's `QaM5V1Test`; data D13: the D12 landing (merge with the recorded resolution, golden C4C16BD39408471A), the line units' `bonusVs.mounted` 1.5 → 1.1 (the sweep 1.5-1.0 reaches no band cell), pins + both pages + docs/02 line 51 + docs/01, golden `data-hash` → 5760E83CB301B62E | **all three ACCEPT**, 0 fix rounds each (sim PASS_WITH_ISSUES: BUG-0540 S3 (the node-liveness leak → M5-2; criterion 1 unticked until then), BUG-0542 S3 → S4 by ruling, BUG-0541 S4; view PASS_WITH_ISSUES: BUG-0550 S4; data PASS_WITH_ISSUES: BUG-0560 S3 (view test code), BUG-0561 S4). Integration sim → view → data; only the data branch moves the golden (twice, `k` lines equal). Producer's scratch merge of all three: build 0 warnings, the full non-Perf suite (session log). **M5 0 / 6 (criterion 1 advanced); counters sim 1 / 4, view 1 / 4, data 2 / 4; no open S1 / S2; next: sim M5-2 (BUG-0540 first), view BUG-0560 then M6-V1, data inbox answers or D14 `ai.json` build orders** |
| 2026-10-10/11 | [2026-10-10-2334](sessions/2026-10-10-2334.md) | sim M4-H3 hardening: BUG-0461's cast half (`SimConfig.CastWalkInBlockerRange` 1.5 m / `CastWalkInPatienceSeconds` 3 s, `MovementSystem.OwnFighterAhead`, the walk-in's own patience, `AbilityEvent.Dropped` via `AbilitySystem.Drop`), BUG-0480 (`World.DetectNeighbors`), BUG-0481 (exactly one `revealed` when any `stealthed`), BUG-0482 (`StatusSystem.RemoveEntry`), BUG-0451 (profiled; the 2,500 row's gate 550 µs; BUG-0510 filed), BUG-0401 (`CombatSystem.ReachableOnFoot`, the stall count held at 9), BUG-0391 / 0094 / 0302 c / 0275 item 1 (`BuildingGhost.Site`, unhashed), 0271 / 0272 / 0142 wontfix, docs/03 + docs/01; view M4-VH3 hardening: BUG-0430 + 0462 item 1 (`ViewApi/GroundPatch` + `GroundQuad`, `GroundDisc` on `ImmediateMesh`, `ground_disc.gdshader`), BUG-0431 (teal rim), BUG-0490 (`SlotMaxHp`, the measured range), BUG-0400 items 2-3 (`SeenResources`), BUG-0126 item 5, BUG-0250 item 2, three wontfix, QA's `QaVh3Test`; data D12: `detector 10` on both casters, page pins, Stealthed / Revealed pinned, golden `data-hash` → C4C16BD39408471A, BUG-0500 / 0470 / 0530 / 0531 | **all three ACCEPT; data held at integration** (sim 0 fix rounds, PASS_WITH_ISSUES: BUG-0511 / 0512 S3, BUG-0513 S4, merged; view 0, PASS_WITH_ISSUES: BUG-0520 / 0521 S4, merged; data 1 round, FAIL → PASS, **held on `origin/studio/2026-10-10-2334-data`**: a code conflict with M4-H3 in `StealthLoaderTests.cs` / `StealthContentHashTests.cs` the conductor may not hand-resolve). The Producer's resolution (D12's loader-test hunks, the sim's hash-test file, README union) verified on a scratch merge (build 0 warnings, 980 / 3 / 0 affected rows): the data track applies it first thing next session. **M4 signed off (10 / 10; retro in docs/05; the missing M3 retro written too); M5 Next; counters sim 0 / 4, view 0 / 4, data 1 / 4; no open S1 / S2; next: sim M5-1 `PlayerView` + `ai.json` schema, view M5-V1 the lost-cast cue, data the D12 rebase then the balance pass** |
| 2026-10-10 | [2026-10-10-1954](sessions/2026-10-10-1954.md) | sim M4-5: stealth / detection (`StatusKind.Stealthed` / `Revealed`, `common/statuses.json` `stealthed` / `revealed`, `rules.json` `stealth { proximity 3, revealSeconds 3 }`, `UnitDef.Detector`; `World.IsStealthed` / `IsDetectedBy` / `HiddenFrom` (`World.Stealth.cs`, a derived per-slot per-player mask), `StealthSystem.Run` at the end of phase 5 returning at once while `StatusStore.StealthedUnits` is 0, `OnStealthStarted`, `FogStore.SeesUnitCell`; the hook in `VisionSystem.UnitSeesUnit` / `BuildingSeesUnit`, `Fog.CanSeeUnit`, `CombatSystem.PickTarget`; `StealthSystem.OnAttackOrCast` in `HitUnit` / `HitBuilding` / `AbilitySystem.Run`; BUG-0452 items 1-3; golden `data-hash` → 889D6BEA3A49F08E); view M4-V7 slice 2: BUG-0450 (`UnitViews.FillMaxHp`, the span-only `UnitHpBars.Collect`, `HitFlash.UpdateWithSlotMax`), the `Summon` card arm (`AbilityCaster.IsNoTarget`, `DeathMarkers.Collect` skips `Expired`, the tooltip without radius), the sandbox's enemy-storm step 9, BUG-0462 item 5; data D-H3 + the D11 landing: `PassiveContentTests.Compare` fails a second same-trigger sentence (BUG-0470), `AbilityContentTests` colon / hyphen / "dmg" / per-phrase linger (BUG-0440), the identical `DataContentHashTests.cs` hunk | **all three ACCEPT**, 0 fix rounds each (sim PASS_WITH_ISSUES: BUG-0480 / 0481 S3, BUG-0482 S4, none reachable with shipped data; view PASS_WITH_ISSUES: BUG-0490 S4; data PASS_WITH_ISSUES: BUG-0500 S4). Integration sim → data → view; only the sim moves the golden. **M4 10 / 10 criteria (criterion 7 ticked; stealth visuals moved to M8); counters sim 3 / 4, view 3 / 4, data 0 / 4; no open S1 / S2; next: sim M4-H3 and view M4-VH3 (the end-of-M4 hardenings), data D12, then the M4 sign-off** |
| 2026-10-10 | [2026-10-10-1314](sessions/2026-10-10-1314.md) | sim M4-4b-3b: hp techs raise hit points (`UnitStore.TrySpawn` at `World.MaxHpOf`, `ProductionSystem.Researched` adds the max change to live units, `TechState.Adds`, BUG-0421), the exact `whenBelowHp` line (`PassiveSystem.LineScale` millionths, 64-bit compare, `UnitPassive.HpLine`, BUG-0420), summons (`AbilityKind.Summon` = 2 cast like `selfAura` via `AbilitySystem.AtCaster`, `AbilityEffectKind.Spawn` = 3 with `unit` / `count` 1-8 / `lifetime`, `SummonSystem.Summon` at the resolve via `FreeCellSearch.NearestWithin` 3 rings, `SummonSystem.Run` first in phase 5 expiring through `RecordDeath` with `DeathEvent.Expired`, `UnitStore.ExpiresAtTick` hashed bit 19, `SummonedUnits` / `IsSummoned` derived, no pop), `targetUnit` / `autocast` deferred, golden untouched; view M4-V6d + M4-V7 slice 1: `AbilityViews.FrenzyColor` magenta, the `selfAura` card (`ViewApi.AbilityCaster.Lead`, `SelectionController.SelfCaster` / `SelfAbilityOrder` / `SendCast`, `CommandCard.HoveredAbility`, the hover ring), `game/tests/M4PlayableTest.tscn` (fog-on sandbox fight through the HUD, 9 steps, fog rule every frame, bare twin), `tools/qa/scene-loop.ps1` both playables last; data D11: `Content/PassiveContentTests` (both Zealot passives pinned both ways, `PendingPassives`), the Frenzied cell pinned + out of `PendingStatuses`, Blinded "can't attack", the Frenzied description reworded, Martyrdom "+50% attack speed" on the page, golden `data-hash` → 4383EBB9345FABF7 | **all three ACCEPT; data then ESCALATE at integration**: sim 0 fix rounds (PASS_WITH_ISSUES: BUG-0450 S3 view, BUG-0451 S3 pre-existing, BUG-0452 S4) merged 736bcd2, view 2 fix rounds (FAIL → FAIL → PASS_WITH_ISSUES: BUG-0460 / 0463 S2 scene flakes fixed; BUG-0461 S3 → sim, BUG-0462 S4) merged 8bb7513 (merged tree 4,873 / 8 / 0, loop 41 / 41), data 0 (PASS_WITH_ISSUES: BUG-0470 S4) **held on its branch**: the merged tree fails the sim's `DataContentHashTests` row that pins the shipped `data-hash` as a literal (D22C849A175F9ED5 vs D11's 4383EBB9345FABF7); a one-line fix lands it next session. **M4 9 / 10 (criteria 6, 8 and 10 ticked); counters sim 2 / 4, view 2 / 4, data 1 / 4; no open S1 / S2; next: sim M4-5 stealth, view M4-V7 slice 2 (BUG-0450 first), data inbox tweaks or D-H3** |
| 2026-10-10 | [2026-10-10-1015](sessions/2026-10-10-1015.md) | sim M4-4b-3a: the `frenzied` status kind (`StatusKind.Frenzied` = 3; `StatusSystem.AttackCooldown` = `max(1, round(cd / (1 + m)))` at the swing, wind-up unchanged, largest magnitude), unit passives (`units.json` `passives`, `UnitPassive` / `PassiveTrigger`, `DataLoader.Passives`, `PassiveSystem.Run` first in phase 5 re-applying for 2 ticks while `hp < fraction x maxHp`, `PassiveSystem.OnDeath` in `CombatSystem.HitUnit` before the free, death order, `World.ShoveNeighbors` scratch; `UnitStore.BelowHpUnits` derived count), the `selfAura` kind (`AbilityKind.SelfAura` = 1, no `range`, resolves at the caster's position), the Zealot's two passives in `whirlwind/units.json`, `frenzied` in `common/statuses.json`, the memory bound re-baselined and itemised (231,920,000), golden `data-hash` → D22C849A175F9ED5; view M4-V6c: `ViewApi.ZoneDiscs` (`Collect` / `Alpha` / `SeesStorm`: own always, enemy where a visible cell is within radius + 2 m, fade over the last 20 ticks) + `ZoneDisc`, `AbilityViews.SyncZones` (one MultiMesh of `ZoneStore.Capacity`, `zone_disc.gdshader`, sand fill, own / owner-colour rim), `BlindColor` violet, `AbilityViewTest` storm rows, QA's `QaV6cTest`; data D-H2: `AbilityContentTests.DescriptionProblems` by role (duration / lingering / cooldown / cast / distance / percent / damage / damage rate; BUG-0410), `Compare` through the passed status list, `AgesRuleQaTests` on `PageTables.Heading`, QA's `DescriptionRoleQaTests` | **all three ACCEPT**, 0 fix rounds each (sim PASS_WITH_ISSUES: BUG-0420 / 0421 S3, BUG-0422 S4; view PASS_WITH_ISSUES: BUG-0430 S3, BUG-0431 S4 (renumbered by the Producer from the QA's 0420 / 0421); data PASS_WITH_ISSUES: BUG-0440 S4 (renumbered from 0431)). Integration sim → view → data; only the sim moves the golden. **M4 6 / 10 (criteria 6 and 8 advanced: all four signature abilities work in the rules; 8's tick waits on BUG-0421); counters sim 1 / 4, view 1 / 4, data 0 / 4; no open S1 / S2; next: sim M4-4b-3b (BUG-0421 first), view M4-V6d + M4-V7, data D11** |
| 2026-10-10 | [2026-10-10-0624](sessions/2026-10-10-0624.md) | sim M4-H2 hardening: a storm hides a unit by its own centre (`FogStore.SeesUnitNearZones`, per-zone fog records hashed while live, BUG-0360; a new storm hides at once and `VisionSystem.BlockerNear` keeps the scan shortcut, BUG-0363 1-2), the recorder's `ZoneCapacity` guard (BUG-0361), the death list units + buildings (BUG-0330), a ghost Attack ends on `fog.SeesFootprint` (BUG-0311), `UnitStore.ChaseSwitches` / `ChaseChainBest` give-up (BUG-0241), the kept chase on a redirect for attack-move legs with the mean bound (BUG-0157), a tower's high-ground reveal per (building slot, player) hashed while in force (BUG-0270), containers-first load messages (BUG-0242), docs/01 / 02 / 03; view M4-VH2 hardening: `AbilityCaster.HasQueuedCast` + `ViewApi.SentCasts` (BUG-0370), `SlowColor` deep blue + `ResolveFlashes` first-frame decisions (BUG-0371), `game/tests/SceneExit.cs` in all 44 scripts (BUG-0251), `ViewApi.SeenResources` for props + minimap and `MinimapRaster.DrawnEnemyDotAt` (BUG-0281 1 + 3, BUG-0126 3), BUG-0250 1 + 3; data D10c: Sandstorm (`createZone`) and Blinded (`blind`) pinned by field both ways, `PendingAbilities` empty, BUG-0380, the Slowed / Telas Fire / Cusser descriptions as proposed + Sandstorm's "Your own units are unaffected", golden `data-hash` → EA5CB5A0AFCEEDBE | **all three ACCEPT**, 0 fix rounds each (sim PASS_WITH_ISSUES: BUG-0390 S3 (a view file) + BUG-0391 S4; view PASS_WITH_ISSUES: BUG-0400 S4; data PASS: BUG-0410 S4). Producer's own `QaGhostViewTest` run confirms BUG-0390 (the sim right, the scene's 10 m mean-walk check stale). Integration sim → view (+ a conductor-dispatched `QaGhostViewTest` rewrite before the merged loop) → data. **M4 6 / 10; counters sim 0 / 4, view 0 / 4, data 3 / 4; no open S1 / S2; next: sim M4-4b-3, view M4-V6c, data STOP (planned)** |
| 2026-10-10 | [2026-10-10-0215](sessions/2026-10-10-0215.md) | sim M4-4b-2: the `createZone` effect (`blocksVision`, `statuses`; the ability's `duration` = the zone's lifetime), `ZoneStore` (64, hashed while live) + `ZoneSystem` (phase 5 after the status countdown), the `blind` status kind (`sight` / `reach`; `StatusStore.BlindOf` derived, the fog stamp's blind mask, `CombatSystem.RangeLimit`), the vision blocker (`FogStore.MarkBlocked` per zone box, `VisionSystem.ZoneHides`), Sandstorm in `whirlwind/abilities.json` on the Priest, golden `data-hash` → 5896D3E7C9FD36AD; view M4-V6b: `ViewApi.StatusMarkers` / `StatusMark` / `ResolveFlashes`, `AbilityViews` coloured marker MultiMesh + flash discs + the thicker violet cast bar, Shift keeps the ability armed, `FogView.CollectGhosts` skips a footprint with a visible cell (BUG-0310), prebuilt "+N" strings + minimap `StringName`s (BUG-0340), `AbilityViewTest` rows, QA's `QaV6bTest`; data D10b: `AbilityContentTests.EffectProblems` by field (amount / type / buildings / friendlyFire / affects), `PendingAbilities = { Sandstorm }`, `CompareStatuses` with landed reports, the malazan.md Cusser Effect cell reworded, the text review; fix round: BUG-0362 | **all three ACCEPT**: sim 0 fix rounds (PASS_WITH_ISSUES: BUG-0360 / 0361 S3, BUG-0363 S4; BUG-0362 S2 filed against data), view 0 (PASS_WITH_ISSUES: BUG-0370 S3, BUG-0371 S4; BUG-0310 / 0340 / 0342 fixed), data 1 (PASS_WITH_ISSUES: BUG-0380 S3; BUG-0350 / 0351 / 0362 fixed; QA's scratch merge sim + data non-Perf 4,444 / 0 failed). Integration sim → view → data. **M4 6 / 10 (criteria 6 and 8 advanced: zones + Sandstorm in the rules, markers + flash on screen); no open S1 / S2; next session: sim + view hardening** |
| 2026-10-09 | [2026-10-09-1155](sessions/2026-10-09-1155.md) | sim M4-4b-1: `AbilitySystem.CooldownOf` (the `abilityCooldown` tech bonus at the resolve, floor 1 tick), the `damage` effect's `buildings` (other players' buildings whose footprint is within the radius via `CombatSystem.BuildingDistanceSquared`, as `structure`, through `HitBuilding`; never own) and `friendlyFire` (0-1, own units incl. the caster, `ProjectileSystem.Scale`), no falloff, the DoT whole-seconds loader rule + two extra loader rules, `cusser` in `malazan/abilities.json` + the Sapper's `abilities`, `ContentHash` extended, golden `data-hash` → 41842085985611BF; view M4-V6a: `ViewApi.AbilityCaster` (nearest ready caster pick, busy rule, `SoonestReady`, `Progress`), `AbilityViews` (range ring, radius circle, fog-gated cast bars, cast-point rings), the card's ability row (Q W E R, tooltip, "N s" dimmed), targeting in `SelectionController` (one `UseAbility` per click, Shift queues, Esc / right-click cancel), `ui.json` hud keys, `AbilityViewTest.tscn`; fix round: BUG-0341 (cached `StringName`); data D10a: `Content/AbilityContentTests` (24) + `PageTables.NamedTable`, every loaded ability's page row and every status name pinned both ways, `PendingAbilities` / `PendingStatuses` allowances, the Duration column on malazan.md, no data change | **all three ACCEPT**: sim 0 fix rounds (PASS_WITH_ISSUES: BUG-0330 S3 pre-existing), view 1 (FAIL → PASS_WITH_ISSUES: BUG-0340 / 0342 S4), data 0 (PASS_WITH_ISSUES: BUG-0350 S3, BUG-0351 S4). Half-size tasks under the 94 % usage cap; Producer verification targeted (the data pins on a scratch tree with the real Cusser 24 / 24). Integration sim → view → data. **M4 6 / 10 (criteria 6 and 8 advanced); the Cusser in the rules, spells castable from the window; no open S1 / S2** |
| 2026-10-09 | [2026-10-09-0724](sessions/2026-10-09-0724.md) | sim M4-4a: BUG-0280 (`CanPlace` ignores hidden enemy units; a Build onto one is dropped on apply), `common/statuses.json` (`damageOverTime` / `slow`; `burning`, `slowed`) + `factions/malazan/abilities.json` (`telas_fire`) + the Cadre Mage's `abilities`, `UnitDef.Abilities` (max 4), `Command.UseAbility` (kind 17, format 4 unchanged), `AbilitySystem` phase 6 (walk then cast, `UnitState.Casting` planted, any order cancels free, cooldown from the resolve, resolve through the spatial hash by `affects`), `StatusStore` (8 per unit, hashed under bit 18, pulse clock per entry) + `StatusSystem` phase 5 (DoT pulses once a second through `DamageCalc`, slows recompute `Speed`), `World.AbilityEvents`; fix rounds: BUG-0300 (the `ui.json` `states.casting` line, cross-track, allowed), BUG-0301 (`PulseTicks`), BUG-0303 (the M4-4a hash in `GatherWedgeQaTests`); view M4-V5: `FogView.CollectGhosts` / `GhostShown` / `Ghosts` / `GhostHandle`, `BuildingPicker.PickGhostRay`, `BuildingViews` ghost pool (owner colour x 0.4, no bar), right-click / A + click on a ghost = Attack on the remembered handle, the "Unexplored" hover row, the seed-21 replay re-recorded (BUG-0273, 12,131 checkpoints), `QaGhostViewTest.tscn`; data D-H1: BUG-0290 (compare by column name), BUG-0090 closed (tower text pinned both ways, K / K2 / K3), `Content/PageTables.cs`, no number / text change | **all three ACCEPT**: sim 2 fix rounds (QA FAIL → FAIL → PASS_WITH_ISSUES; BUG-0302 b + c S4 open), view 0 (PASS_WITH_ISSUES: BUG-0310 S3 view, BUG-0311 S3 → sim), data 0 (PASS). Integration sim → view → data; QA's scratch merge sim + view non-Perf 4,258 / 0 failed, smoke PASS, loop 36 / 36; sim branch full suite incl. Perf 4,392 / 0. **M4 6 / 10 (criterion 5 complete); Telas Fire in the rules; no open S1 / S2** |
| 2026-10-09 | [2026-10-09-0125](sessions/2026-10-09-0125.md) | sim M4-3b: `buildings.json` `attack` / `detector` schema + both towers' values, `TowerSystem` (phases 7 / 10: scan by the unit priority, never a building, the building as the vision viewer, a led aimed shot from the footprint centre, Ranged Weapons reaches towers, no retaliation / reveal on a tower's hit), `Fog.Ghosts` (one last-known entry per enemy building slot, hashed; an Attack on a remembered-but-unseen building accepted, ends when the ground is seen empty), `PlacementError.Unexplored`; fix round: BUG-0276 (two Perf rows' setup), BUG-0274 (five view scenes, fixed on the view branch); view M4-V4: `FogOfWar` + `ViewApi.FogView` (R8 fog texture per update, three shaders black / 40 % dark + desaturated / clear), units / buildings / bars / shots / marks hidden by the fog, picks only on what is drawn, the minimap's fog layer + Attack on a visible dot, `--no-fog` (7 scenes), `placement.unexplored` "Unexplored", `FogViewTest.tscn` + QA's `QaFogViewTest`; data D8: `CounterTriangleMarginsTests` on the shared `CounterTriangleScene`, every page cell pinned (a new "Winner hp left" column), the Lancer seat-1 time 21.5 → 22.0 s (BUG-0243), the Ages anchor on the file's lines (BUG-0260) | **all three ACCEPT**: sim 1 fix round (QA FAIL → PASS_WITH_ISSUES; BUG-0270 / 0273 S3, BUG-0271 / 0272 / 0275 S4 open), view 1 conductor-dispatched round for the sim's BUG-0274 (PASS_WITH_ISSUES: BUG-0280 S3 → sim, BUG-0281 S4), data 0 (PASS_WITH_ISSUES: BUG-0290 S4). **Sim + view integrate together** (the sim alone fails smoke on the view's `ui.json` key); merged full suite incl. Perf 4,313 / 0 failed, loop 35 / 35. **M4 5 / 10; criterion 5 owes only the ghost drawing (M4-V5); no open S1 / S2** |
| 2026-10-08/09 | [2026-10-08-2144](sessions/2026-10-08-2144.md) | sim M4-H1 hardening: the fog's visible bits packed and hashed (BUG-0215, golden regen once with the proof), the seed-21 replay re-recorded and checked on all 11,541 ticks (BUG-0211), BUG-0216's four nits, the public `Scenario/CounterTriangleScene` harness (BUG-0230 item 2), `UnitStore.ChasePrev` (BUG-0149: a new target is a fresh chase), `CheckAnyOfReachable` honours `researchedAt` / `trainedAt` (BUG-0134), van der Corput leftover radii (BUG-0133), medians in the blob-scan Perf row (BUG-0158), wrong-type load messages (BUG-0113 item 1); view BUG-0219 first (one commit, +5), then M4-VH1 hardening: `WallClock.Wait` + `SfxTest` on the wall clock with 8x regression rows (BUG-0220), `TerrainHeight.MaxUnder` three step bands (BUG-0226), the lob off-line restart (BUG-0222), the unseen-target Attack sweep, the rally-forest check (BUG-0148 item 3), the double-Cancel guard (BUG-0126 item 4), dot-refresh rows best of three batches, docs/03 "Build and export" corrected; data D7: a Sight column on both pages pinned both ways (`BuildingContentTests` J-J5), docs/02's 12 m / 24 m sentence, BUG-0230 items 1 and 3, BUG-0090's tower item, the BUG-0240 spotter line | **all three ACCEPT**, 0 fix rounds each (PASS_WITH_ISSUES x3: sim BUG-0241 S3 pre-existing + BUG-0243 S3 → data + BUG-0242 S4; view BUG-0251 S3 flake + BUG-0250 S4; data BUG-0260 S4). The common base was red on two rows (BUG-0219 view, BUG-0240 data), both fixed; **integration view → data → sim** so every pushed `main` is green; QA's scratch integration 4,041 / 0 failed, loop 33 / 33. **The fog lands on `main`; M4 5 / 10; no open S1 / S2** |
| 2026-10-08 | [2026-10-08-1814](sessions/2026-10-08-1814.md) (the resumption of 1435, which the 3-hour lock rule declared dead mid-fix-loop; incident folded into the log) | sim M4-3a: `Rts.Sim.Vision` (`FogStore` per player: byte per cell + packed explored bits, circle masks per radius, row-span stamping per level with the high-ground rule and the 4 m lip, every 4 ticks + a tick-0 stamp; `VisionSystem` phase 12, `UnitSeesUnit / Building` for scans / retaliation / explicit Attack / kept targets, the 40-tick high-ground reveal keyed on the firing level via `PendingHit.AttackerLevel` / `ProjectileStore.Level`), buildings' `sight` + `rules.json` `buildingSight` 12 (towers 24), `World.Fog` read-only surface, BUG-0184 item 1; fix round: BUG-0214 (memory bound re-baselined, itemised), BUG-0217 (gate after the compare, 1,000 v 1,000 on 3 levels 29.5 → 23.4 ms); golden regen once (`k` lines equal with the fog excluded); view M4-V3: `ProjectileViews` (aimed streaks, lob stones on an arc, flash / dust / burst marks), `ViewApi.ProjectileTracker` / `ImpactMarks` / `TerrainHeight.MaxUnder`, BUG-0210 (`--no-combat`, the slot was shot dead), BUG-0190, `AttackStage.cs` for BUG-0218, `--no-combat` for BUG-0212 / 0213, `ProjectileViewTest.tscn` + QA's `QaV7Test`; the held M4-V2 lands with it; data D6: `Content/CounterTriangleMarginsTests` (8 pairs x 2 seats, winner pinned, table on both pages with the proposed 40-65 % band), `SapperSplashReportTests` (shipped / minRange 2 m / splash 1 m), BUG-0200 | **all three ACCEPT; sim then ESCALATE at integration**: sim 1 fix round (QA FAIL → PASS_WITH_ISSUES → confirm FAIL on the view-owned BUG-0218, fixed on the view branch; BUG-0211 / 0215 S3, BUG-0216 S4 open) then **held on `origin/studio/2026-10-08-1435-sim`** (with `main` merged in, the full non-Perf suite is 4,003 / 15 / 1: `UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything`, a view QA row from before fog, **BUG-0219 S2**; both QAs' scratch merges ran only the scene loop), view 2 fix rounds (PASS_WITH_ISSUES x3; BUG-0220 S3 pre-existing, BUG-0222 / 0226 S4 open) **merged to `main` first (853a60c, green)**, data 0 (PASS_WITH_ISSUES: BUG-0230 S4) merges after the view. **M4 5 / 10**, criterion 5's sim half accepted and held; one open S2 (BUG-0219); new rule: the integration gate runs the full non-Perf suite on the merged tree |
| 2026-10-08 | [2026-10-08-0913](sessions/2026-10-08-0913.md) (integration update: **view ESCALATE**, `main` red on `MinimapTest` since M4-2b, BUG-0210 S2; the view branch kept, fix first next session; data merges) | sim M4-2b: `common/projectiles.json` schema (`aimed` / `lob`, `speed`, `hitTolerance`, `leadSpeed`), `ProjectileStore` (SoA, hashed while in flight) + `ProjectileSystem` (Fly / Fire / Track / Land, splash with falloff, friendly fire at 50 % never buildings, `attack.minRange` no kiting), `CanFight` for every attack, BUG-0156 via `AttackerInScanRange`, `Scenario/CounterTriangleTests` (8 pairs both seats), `World.Impacts` + view spans; fix round: the lead rule (BUG-0183 S2), BUG-0180 / 0181 / 0182 item 1; golden `data-hash` x2, `k` identical; view M4-V2: `ViewApi.UnitPicker` (capsule ray pick + `ResolveEnemy`), `TargetMark` / `TargetRing`, right-click / A + click Attack per selected unit, `states.ordered_attack` "Pursuing", F12 target slot, BUG-0160 (four F12 lines, first-sight flash, corpse rim, no literals), `AttackOrderViewTest.tscn`, QA's `QaV6Test` + `tools/qa/scene-loop.ps1`; data D5: Targets column on both faction pages + pins (C / G / new H), docs/02 "Ages" exact wording + `TechContentTests.G` parser, BUG-0155 (H any-case + faction tech names), QA's `AgesRuleQaTests` | **all three ACCEPT**: sim 1 fix round (QA FAIL → PASS_WITH_ISSUES; the lead rule accepted as a Producer decision, docs/02 rewritten at ACCEPT; BUG-0182 item 2 / BUG-0184 S4 open), view 0 (PASS_WITH_ISSUES: BUG-0190 S4), data 0 (PASS_WITH_ISSUES: BUG-0200 S4). **M4 5 / 10** (criterion 3 and the counter-triangle rows ticked; criterion 1's view half landed); no open S1 / S2 |
| 2026-10-08 | [2026-10-08-0313](sessions/2026-10-08-0313.md) | sim M4-2a: the BUG-0146 wedge fix (`MovementSystem.QueuesBehind` / `StandArrival2`, three stand points per cell edge), `Command.Attack` (kind 16, `CombatMode.Ordered`, queued handle in the existing queue arrays), `attack.targets` schema (the ram `buildings`, BUG-0139), replay format 4 (`combat` header, targets on command lines; golden regen, `k` lines identical); fix rounds: BUG-0150 (lost-from-sight give-up on a switch), BUG-0152 (`ReaffirmAttack` / `KeepFightForAttackMove`), BUG-0154 (`UnitStore.Repick`); view M4-V1: `--no-combat` dev flag (BUG-0147's five scenes), `ViewApi.UnitHpBars` / `HitFlash` / `DeathMarkers`, `CombatViews` (bars, corpse discs, rubble), K / L counts, live panel hp, `CombatViewTest.tscn`; data STOP (planned) | sim ACCEPT after 2 fix rounds (QA FAIL x3, the last on BUG-0151 alone: Producer re-stated criterion 3 and re-triaged it S2 → S3; BUG-0156 / 0157 S3, BUG-0153 S4 open); view ACCEPT, 0 fix rounds (PASS_WITH_ISSUES: BUG-0160 S4). **M3 signed off**; M4 3 / 10; no open S1 / S2 |
| 2026-10-07/08 | [2026-10-07-2315](sessions/2026-10-07-2315.md) (resumed the dead 2014 session, which left no log) | sim M4-1 combat slice 1 (`Rts.Sim.Combat`: `CombatSystem` acquire / attack / resolve, `DamageCalc`, `CombatMode`, give-up memory + friend exception, `SpatialHash.QueryEnemies`, `UnitState.Attacking`, `SimConfig.Combat` test switch, death events + kills / losses; BUG-0135 / 0136 / 0137 / 0138 / 0140 / 0141 / 0143 fixed in-session); view M3-V4 (`M3PlayableTest.tscn` scripted playtest through the real HUD, drawn-shape node pick BUG-0125, "Locked" / "Researched" / "In a queue" BUG-0126 1-2, `states.attacking`, BUG-0145 / 0148 2 + 4 fixed); data D4 (Age II description faction-neutral, seven shared techs pinned to docs/02, BUG-0132, golden `data-hash` A863BAF8637CC860) | **all three ACCEPT**: sim 2 fix rounds (QA FAIL / FAIL / PASS_WITH_ISSUES: BUG-0139 S3 → M4-2a, BUG-0144 S3, BUG-0149 S3, BUG-0142 S4 open), view 1 fix round (QA FAIL on BUG-0145 S2 → PASS_WITH_ISSUES: **BUG-0146 S2 filed against the sim**, BUG-0147 S3, BUG-0148 S4), data 0 (PASS_WITH_ISSUES: BUG-0155 S4). **M3 8 / 8**, sign-off held by BUG-0146; M4 1 / 10 |
| 2026-10-07 | [2026-10-07-1715](sessions/2026-10-07-1715.md) | sim M3-H2 end-of-M3 hardening: D3 merged with the BUG-0112 fixture fixes (`TestSim.DataWithoutBuildingRequires`, golden `data-hash` regen, checkpoints identical), `Map/Plateaus` + plateau-bounded `FreeCellSearch` + per-plateau spawn memo (BUG-0097 / 0095), `SealCheck` memo on `NavGrid.Version` (BUG-0096), unmeetable requirements as load errors incl. any-of reachability (BUG-0100), BUG-0099 item 2, door fuzz kinds 0-15; view M3-V3b: the held M3-V3 HUD re-landed with two-hall Age II fixtures (BUG-0124), panel allocation-free + dimmed greyed buttons (BUG-0123), per-seed bench bound (BUG-0104), word wrap / Shift-click / ghost priority / reason text (BUG-0122), mauve sites / zoom-scaled cargo / resource-keyed minimap (BUG-0107), docs figures + split Perf rows (BUG-0105), terrain-occluded building + node ray picks; data STOP (planned) | sim ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES: BUG-0134 S3, BUG-0133 S4); view ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES: BUG-0125 S3 accepted open for M3-V4, BUG-0126 S4). **M3 7 / 8**: "factions fully defined in data" and "HUD" ticked; `main` smoke green again; no open S1 / S2 |
| 2026-10-07 | [2026-10-07-1415](sessions/2026-10-07-1415.md) | sim M3-6 `requires` gating (`RequiresTechs` / `RequiresBuildings` resolved at load, `requiresAnyOf` by slot on `age_ii`, `Requires` reasons in `CanPlace` / `CanResearch`, real `LockedByRequirement`, queue-time rule, `PlayerLedger` finished counts, loader nits BUG-0008 / 0010 / 0098 / 0099); view M3-V3 selection panel + production card + queue strip + rally marker + pop + Age II flash, BUG-0108 / 0109 / 0110 fixed; data D3 techs text + building `requires` + page Techs tables + pins, BUG-0111 fixed | sim ACCEPT (on `main`), 0 fix rounds (QA PASS_WITH_ISSUES: 2 S3 (BUG-0100, BUG-0112) + 1 S4); **view ACCEPT then ESCALATE at integration** (QA PASS_WITH_ISSUES, 1 S3 BUG-0123; merged with M3-6 one fixture queues Age II without halls: BUG-0124 S2, branch held, `main` smoke red); **data REJECT-hold** (QA PASS_WITH_ISSUES, 1 S4; integration only: BUG-0112, lands via the sim's M3-H2). M3 5 / 8 on `main` |
| 2026-10-07 | [2026-10-07-1131](sessions/2026-10-07-1131.md) | sim M3-5 Age II research + Forge upgrades (`common/techs.json` + faction `techs.json` schema, `Research` kind 15 through `CanResearch`, queue items unit-or-tech, `TechState` flags hashed + `TechBonus` derived, building `requires` field, CLI `age N`); view M3-V2 command card (5 x 3 grid, `ui.json` view text, B / V build menus, `BuildGhost` via `CanPlace` once a frame, building click-select + outline + site Cancel, right-click Repair / join, M key); data STOP | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S3 (BUG-0098) + 1 S4; view 3 S3 (BUG-0108 / 0109 / 0110, folded into M3-V3) + 1 S4). M3 5 / 8 |
| 2026-10-07 | [2026-10-07-0925](sessions/2026-10-07-0925.md) | sim M3-4 production queues (`Train` / `CancelTrain` / `SetRally` / `ClearRally`, `ProductionSystem` phase 3, `PlayerLedger` pop + cap, `FreeCellSearch` capped at the level box, `trainedAt` resolved, `UnitsTrainedAt`, CLI pop); view M3-V1 economy HUD (`StartBase` Town Hall + workers, `ResourceBar`, right-click Gather via `ResourcePicker`, `BuildingViews` + `BuildingBars`, worker tints + cargo markers, F12 worker counts); data STOP | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S3 (BUG-0097); view 1 S3 (BUG-0106, reworded away) + 1 S4). M3 4 / 8 |
| 2026-10-07 | [2026-10-07-0800](sessions/2026-10-07-0800.md) | sim M3-H1 hardening (pocket rule `NavFlags.Pocket` after Cancel / destruction, cheap-first Build checks, ring-by-ring push-out through the spatial hash, BUG-0080 measured bound documented, BUG-0081 save/load decision, loader / CLI / plug-cache nits: BUG-0071 / 0072 / 0076 / 0079 / 0081 / 0091 / 0092 / 0093 fixed); view M2-H2 hardening (bench marches across + true `fps`, start blocks in a clearing, `Sfx` stops at quit, overlay label on change, 2 x 2 minimap dots, S4 batch, M6 export notes: 11 bugs fixed) → **M2 signed off**; data STOP (resumed after an OS restart killed 2026-10-06-2326) | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 2 S3 + 1 S4, view 1 S3 + 1 S4) |
| 2026-10-06 | [2026-10-06-2114](sessions/2026-10-06-2114.md) | sim M3-3 placement validity (`World.CanPlace`, never-seal `SealCheck`), `Build` / `Cancel` / `Repair`, multi-builder construction (BUG-0078 fixed by rule); view M2-7 `--bench` / `--vsync`, `PrevFacing` blend, screenshot set (M2 10 / 10); data D2 unit stats pinned to the faction pages, building tables on the pages | all ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x3: sim 2 S3 + 1 S4, view 2 S3 + 1 S4, data 1 S4). M3 3 / 8 |
| 2026-10-06 | [2026-10-06-1744](sessions/2026-10-06-1744.md) | sim M3-2b closing vs opening grid changes (`BlockVersion`, usable-stale fields refreshed lazily, progress-mark reset, wood 1 x 1 rule, 2x faster field build; BUG-0073 / 0074 / 0077 fixed); view M2-6 placeholder audio (`Sfx`, Select / Command tones, `--mute`); data D1 all ten buildings per faction in data | all ACCEPT; sim 1 fix round (QA FAIL on S2 BUG-0082 perf, fixed; 2 S3 filed), view 0 (1 S3 + Producer's S4 BUG-0088), data 0 (1 S4). M3 2 / 8, M2 9 / 10 |
| 2026-10-06 | [2026-10-06-1503](sessions/2026-10-06-1503.md) | sim M3-2 worker gather / return loop (`Gather`, `EconomySystem`, `BuildingStore` + dev `SpawnBuilding`, `buildings.json` Town Hall, player totals, CLI `--workers`); view M2-3b trees and mines as MultiMesh props, 12 / 8 defaults, minimap resource layer | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 2 S3 + 1 S4, BUG-0075 fixed; view 1 S3 + 1 S4). M3 2 / 8, M2 8 / 10. Data track added by the owner mid-session |
| 2026-10-06 | [2026-10-06-1255](sessions/2026-10-06-1255.md) | sim M3-1 resource entities (`ResourceStore`, placer, depletion → nav grid, hash, replay format 3, CLI flags); view M2-5 debug overlay (F12: nav grid, flow arrows, tick graph, counts) | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 3 S3 + 1 S4; view 1 S3 + 1 S4). M3 1 / 8, M2 7 / 10 |
| 2026-10-06 | [2026-10-06-0905](sessions/2026-10-06-0905.md) | sim M1-9 M1 end-of-milestone hardening (9 items, 8 bugs fixed + BUG-0058); view M2-H1 view hardening (5 bugs fixed, rimmed minimap dots) | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S3 + 1 S4; view 1 S3 + 1 S4). **M1 signed off by the Producer** |
| 2026-10-06 | [2026-10-06-0655](sessions/2026-10-06-0655.md) | sim M1-8 headless CLI (`tools/Rts.Cli`) + `FlowFieldCache.PeekCached`; view M2-3 A / S / H / Shift-queue, type select, control groups, Tab subgroups | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 1 S4; view 1 S3 + 1 S4). All 8 M1 criteria met |
| 2026-10-05 | [2026-10-05-2330](sessions/2026-10-05-2330.md) | sim M1-7 perf criterion + Stop / HoldPosition / AttackMove + shift-queue (replay format 2); view M2-4 minimap + `Hud` + `--no-hud` | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: sim 2 S3 + 1 S4, view 1 S4) |
| 2026-10-05 | [2026-10-05-1609](sessions/2026-10-05-1609.md) | sim M1-4d-3 hardening batch (crowd routing + 6 debt bugs); view M2-2 unit views + selection + right-click move + BUG-0041 | both ACCEPT; sim 2 fix rounds (QA FAIL x3: S1 + 2 S2 fixed in-session, BUG-0045 S2 → S3 by the Producer, 4 S3 + 1 S4 filed); view 0 fix rounds (PASS_WITH_ISSUES: 1 S3 + 1 S4) |
| 2026-10-05 | [2026-10-05-1446](sessions/2026-10-05-1446.md) | sim M1-6 replays + golden + BUG-0014; view M2-1 match scene, terrain mesh, camera, screenshot flag | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: 1 S3 + 2 S4 filed, BUG-0014 fixed); first two-track session |
| 2026-10-05 | [2026-10-05-1234](sessions/2026-10-05-1234.md) | M1-5 cross-map scenario test + queued-walker give-up rule + BUG-0035 enemies as hard walls | ACCEPT after 1 fix round (QA FAIL then PASS_WITH_ISSUES; S2 + S4 fixed in-session, 2 S3 filed) |
| 2026-10-05 | [2026-10-05-1013](sessions/2026-10-05-1013.md) | M1-4d-2 shoving of idle units + BUG-0031 fix + re-tightened assertions | ACCEPT after 2 fix rounds (QA FAIL on crowd targets; S2 x2 re-triaged S3, 1 S3 filed, 1 S3 fixed) |
| 2026-10-05 | [2026-10-05-0742](sessions/2026-10-05-0742.md) | M1-4d-1 separation, crowded arrival, give-up, adjacent-cell steering | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 3 S3 open) |
| 2026-10-04 | [2026-10-04-2056](sessions/2026-10-04-2056.md) | M1-4c build-cap determinism + fairness; suite de-flaked | ACCEPT, 0 fix rounds (hardening) |
| 2026-10-04 | [2026-10-04-0120](sessions/2026-10-04-0120.md) | M1-4b GameData in sim + flow fields + LRU cache + `Move` | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
