# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session **2026-10-17-2344** (the fourth full session of 2026-10-17, 4 / 8), base commit
**4f8208f** (the 1624 integration). Sim **M5-8a** (feature: the D14 b openings schema + BUG-0910), view **M6-V12** (feature: BUG-0920
first, then real SFX + alerts), data **STOP** (nothing unblocked, inbox empty). Counters before: sim 0 / 4, view 0 / 4, data 1 / 4.
Bug ids: **sim from BUG-0940, view from BUG-0950, data from BUG-0960** (highest on `main`: 0922). Open S1 / S2: none.

## Checks at this PLAN

- Inbox: **New** empty. Build on 4f8208f: 0 warnings (11 s). Smoke (view worktree): **PASS**. Non-Perf suite on 4f8208f: **6,715 passed / 0 failed /
  13 skipped of 6,728 in 32 m 58 s** (BUG-0915's loader row green this run; the 13 skips carry bug ids). Verified: `Blackboard.StepNoSiteUntil` + `NoSiteRetryThinks` present (7 hits), `export_presets.cfg` excludes
  `assets/models/looktest/*`, docs/03 has "Implementation (M6-VH3)" and the "Re-baselined" text, `.gitattributes` covers `.wav` / `.ogg`.
- **Drift found:** the 1624 handoff said the SFX packs downloaded were "Kenney RPG Audio / Impact / Interface, Sonniss". `asset-sources/`
  holds **no Kenney audio pack** (only `kenney-ui-pack-rpg-expansion`, a UI sprite pack) and the **Sonniss GDC 2026 bundle** (part1-5, 347
  wav, 7.6 GB; licence PDF in each part; docs/04 l. 113 records it). The view brief below uses Sonniss only; the Kenney audio packs go to
  "Wanted, not blocking" for the owner.

## Standing rules (1-34)

Unchanged 1-33 (the 1624 handoff in git history has 32-33; the 2036 one the long form of 1-31). New this session:

- **34 (audio tooling).** `tools/audio/**` (the wav trim script and the SFX manifest) is view-side tooling like `tools/qa/*.ps1` (rule 33):
  the view track may create and change it when the brief names it; the rest of `tools/**` stays the sim's.

---

## Sim track

### Current session plan: M5-8a — named AI openings in `ai.json` (D14 b schema) + the seeded pick; BUG-0910 folded in

**Type** feature (counter 0 → 1 / 4) · **QA** full · **Size** up to 1,500 lines (clear design) · **Files** `sim/Rts.Sim/Data/*` (loader,
`AiDef`, a new `AiOpening`, `DataLimits`, `DataJsonContext`), `sim/Rts.Sim/Ai/*` (`AiPlayer`, `BuildOrderExecutor`, `ProductionManager`,
`MilitaryManager`, `Blackboard`), `Simulation` / `World` (the seat's pick + a read-only accessor), **`game/data/factions/malazan/ai.json`
and `whirlwind/ai.json`** (the sim track edits them this session: re-wrap only, no number changes), the golden pins, docs/02 "AI opponent",
docs/03 "AI architecture". Not `docs/factions/**`, not `game/data/common/**` except `rules.json` if a knob is needed (none expected).

**Goal.** M5 criterion 8's sim half: `ai.json` holds several **named openings** per faction (each a build order, an army mix and attack
thresholds); at match start the AI picks one with its own seeded RNG (**Easy always the first**), so two matches against the same faction
can open differently and a replay still reproduces. Ship the schema, its validation, and the minimum content (today's single order per
faction as one opening) so the data track can write the real openings next session (D21). Fold in BUG-0910 (same executor code).

**Schema (the data track writes against this; keep the names).**
```json
{
  "difficulties": { ... as today ... },
  "retreat": { ... as today ... },
  "openings": [
    { "id": "standard", "displayName": "Standard", "description": "...",
      "buildOrder": [ ... today's steps ... ],
      "compositions": [ ... today's per-age maps ... ],
      "attack": { "minArmyPop": 20, "minArmyCost": 1544 } }
  ]
}
```
- `openings`: 1 to `DataLimits.MaxAiOpenings` (8) entries; `id` snake_case, unique within the file; `displayName` required (1-40 chars),
  `description` optional (≤ 200 chars) — both player-facing (a later stats screen may show "the AI opened with …"), so they live in data.
- `buildOrder` / `compositions` / `attack` keep today's rules and validation unchanged, now per opening. The old top-level `buildOrder`,
  `compositions` and `attack` are **refused with one error each** ("moved into openings[]"), so a stale file can't half-load.
- `difficulties` and `retreat` stay shared (top level). **Age-agnostic:** add no new age-keyed field; the compositions' existing per-age form
  stays as it is (the Ages → tech-tree redesign comes after M5).
- `AiDef.Openings` (ImmutableArray<AiOpening>, file order); `AiOpening { Id, DisplayName, Description, BuildOrder, Compositions,
  AttackMinHalfPop, AttackMinCost }`. `GameData.ContentHash` covers every opening field incl. ids, names and order.

**The pick.** In the `AiPlayer` constructor (or where the sim builds the seat): Easy → index 0; Normal / Hard → one draw from the AI's own
`SimRng` (stream `RngStream.Ai(p)`, the first draw it makes), uniform over the openings. The pick is **AI state, not hashed** (docs/03 "AI
state is not sim state"); it is reproducible from the seed and must be listed for **Requests 42** (the save). Expose it read-only:
`Simulation.AiOpening(player)` → the opening index (−1 for a human seat) and `GameData` gives the id / name. `Blackboard` is sized by the
**chosen** opening's build order. `BuildOrderExecutor`, `ProductionManager` and `MilitaryManager` read `ai.Opening.*` instead of `ai.Def.*`.

**BUG-0910 (S3, folded in).** `AiPlayer.TryBuild` sets `NoSiteFound` only when `FindSite` actually ran its ring search (past
`CanPlaceAnywhere`); a step whose requirement is unmet re-checks every think as on base bf8026e (1-2 thinks after the requirement stands).
Un-skip QA's `SimHardeningM5H3QaTests.ABuildStepWaitingOnARequirement_*` as the regression row (red on base d048a21's behaviour, green on head).

**Acceptance criteria.**
1. Both shipped `ai.json` files load with exactly one opening (`standard`) whose steps, compositions and attack numbers equal today's
   (a test compares the loaded `AiOpening` field by field with the pre-change values; no spending / balance number moves from the
   re-wrap alone).
2. `DataValidationTests` cover: no `openings`, an empty list, 9 openings, a duplicate id, a non-snake_case id, a missing / too-long
   `displayName`, each old top-level field present (one error naming `openings[]`), and a bad step inside an opening reported at
   `openings[i].buildOrder[j]...`.
3. Pick rule: a 3-opening test def → Easy picks index 0 on seeds 1-50; Normal and Hard pick every index at least once over seeds 1-200;
   the same seed + difficulty gives the same pick twice (two sims built from the same `SimConfig`); a replay recorded with a Normal AI
   replays with the same pick (checkpoint hashes equal, `AiOpening` equal).
4. The AI plays each opening it picks: a two-opening test def where opening B's first step differs from A's shows the executor sending
   B's step when B is picked; `MilitaryManager` uses the picked opening's `attack` thresholds (a test with different thresholds per opening).
5. `ContentHash` changes when an opening's id, displayName, order or any inner field changes (extend `DataContentHashTests`); the golden
   `data-hash` is regenerated **once** in the same commit if it moves (reason in `GatherWedgeQaTests.SameGameDataHashes` and the commit
   body, BUG-0750's rule); `ReplayGoldenTests` otherwise green (the pick for a 1-opening faction is always 0, so no `k` line moves).
6. BUG-0910 fixed per the row above; `AiPlayerTests` for BUG-0791 still green (the no-site wait still applies after a real ring search).
7. The think-cost gates (`Category=Perf` AI rows) green alone on head; paired A/B on the storms fuzz within noise (the pick costs nothing
   per think).
8. Docs: docs/02 "AI opponent" gets one sentence (openings, the seeded pick, Easy always the first); docs/03 "AI architecture" gets the
   schema, the pick, the save note (Requests 42 list in STATE updated by the Producer), and the BUG-0910 rule; `rules.json` untouched.

**Design references.** docs/05 M5 criterion 8 (the text above); docs/02 "AI opponent" (build orders / compositions / thresholds are
per-faction data); docs/03 "AI architecture" (`RngStream.Ai(p)` is a copy of the world's stream; AI state is not hashed; `ai.json` in
`ContentHash` since M5-3); CLAUDE.md rules 3, 4, 6, 8.

**Tests required.** The loader tests (criterion 2), `AiOpeningPickTests` (criterion 3), executor / military tests (criterion 4),
`DataContentHashTests` (criterion 5), BUG-0910's un-skipped row (criterion 6), the Perf rows alone (criterion 7). Full non-Perf suite green
but the rule-31 Content cells if BUG-0910's fix moves them (name them in the report: `Content/AiSpendingReportTests`,
`QA/Content/D20PinQaTests`, `CounterTriangleMarginsTests`; the conductor dispatches the data re-print at the integration, as at 1624).

**Constraints.** No new per-think allocation (the opening is a reference held at construction; no LINQ in managers). No `System.Random`.
No dictionary iteration in the loader beyond `SortedKeys`. Player-facing text (`displayName` / `description`) only in data; the sim's
minimum entries are "Standard" / "The faction's standard build" — the data track rewrites them. Don't touch `docs/factions/**`.

### Candidates after (in order)
Requests 42 (the save format: whole sim state + `SurroundClaim` / `SurroundHolder`, the `Blackboard` whole incl. `StepNoSiteUntil`,
`BuildingGhost.Site`, `MatchStats`, `UnitStore.Flank`, **the opening pick + the AI RNG**; slice ~800 lines), Requests 38 (teams), M5-4c
slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`), M5-4d, D14 c, M5-5, M5-6.

### Debt (next sim hardening, counter 4 / 4)
BUG-0912, BUG-0911, BUG-0889, BUG-0144, BUG-0914 + BUG-0915 (S4 flakes: S3 and first item if either fails a second full run), BUG-0913
(7 nits), BUG-0113 item 2, BUG-0704 item 3.

### Watch-outs (sim)
- BUG-0790's stand points move every diagonal footprint walk: any gather / build change re-runs `GatherWedgeQaTests` (seed-21 wedge at
  478 / 600) and the rule-31 cells.
- Criterion 1's HI row sits on its 8 / 10 bound (`FlankTests`); the Perf gates measure this PC's sustained clock (paired A/B shows cost).
- Seed 11's four-seat match reaches Age II at 17:30 (BUG-0921): M5-5 should expect minutes of spread.

---

## View track

### Current session plan: M6-V12 — BUG-0920 first, then real SFX from the Sonniss bundle + attack alerts (text, minimap ping, sound) + the music hook

**Type** feature (counter 0 → 1 / 4) · **QA** standard · **Size** up to 1,500 lines incl. the trim script and tests · **Files** `game/scripts/**`
(`Sfx`, `SfxEvent`, `Minimap`, `Match` / HUD, `RtsCamera`, `KeyMap` if Space is added there), `game/tests/**` (`CombatViewTest`, a new
`SfxTest` / `AlertTest` scene), `game/assets/audio/sfx/<category>/<event>_<variant>.wav` + their `.import` files, `game/default_bus_layout.tres`
(a `Music` bus if missing), **`game/data/common/ui.json` keys `alert.*` (rule 32; named below)**, `tools/audio/trim-wav.ps1` +
`tools/audio/sfx-manifest.json` (rule 34), `sim/Rts.Sim/ViewApi/AttackAlerts.cs` (new, read-only) + its tests beside `HitFlash`'s,
docs/04 (the audio layout note + **the licensing log rows**), docs/03 "Implementation (M6-V12)", docs/02 if a wording lands.
Not `sim/**` outside `ViewApi/`, not `tools/**` outside `tools/audio/` and `tools/qa/`.

**Part 0 — BUG-0920 (S3, a few lines, the gate back to all-green).** `CombatViewTest`'s "hit while lit" row relies on seed 1's brawl
cadence, which the flank rule changed. Stage the row on its own (two attackers on one target inside the 0.15 s flash window) or count
`_hitWhileLit` over both seeds; `tools/qa/scene-loop.ps1` must show 66 / 66.

**Part 1 — real SFX.** Replace the synthesized tones with clips cut from the owner's Sonniss GDC 2026 bundle (`asset-sources/sonniss-gdc-2026/
part1-5/`; licence: royalty-free, no attribution, raw files not redistributed; **no raw pack or uncut file is committed**).
- `tools/audio/trim-wav.ps1 -In <source> -Out <dest> -StartSec -LengthSec [-Normalize]`: copies a PCM / float WAV segment on frame
  boundaries (block align) with a correct header; no decoding, no resampling (Godot's importer does `force/mono`, `force/max_rate` 44100 and
  16-bit in the `.import`). `tools/audio/sfx-manifest.json` lists every clip: source path (relative to `asset-sources/`), destination, start,
  length, event. Each committed clip ≤ 3 s and ≤ 2 MB; ≤ 20 clips in total. Re-running the script from the manifest rebuilds every clip.
- Events (extend `SfxEvent`, one row each in a clip table that maps an event to 1-3 variant files; a missing file falls back to today's
  synthesized tone with one warning, so a clone without LFS still boots): `Select`, `Command`, `Refused` (UI: e.g. "UI Interaction Elements"
  Accept / Deny, "System & UI Feedback" Deny / Ping), `MeleeHit` (Melee Weapons Pack 2 swing-hit, Historical Weapons sword / spear),
  `Shot` (an arrow / bolt loosed: a short swish from Melee Weapons Pack 2 or Tower Defense sword slice), `UnitDeath` (Cinematic Fight body
  impact, Vox Hominis effort), `BuildingPlaced`, `BuildingDone`, `BuildingRazed` (Colossal Impacts / HD Game Materials wood), `Ready`
  (a unit trained or a tech done: "Ting Coins" or a kalimba from Organic UI), `Cast` (Fantasy Game 2 magic), `Alert` (Trailer Alarms, a
  half-note cut ≤ 1.5 s). Pick by file name and waveform length; say in the report which file each came from (the owner judges by ear).
- **World sounds only when on screen and seen:** `MeleeHit` / `Shot` / `UnitDeath` / `BuildingRazed` / `Cast` play only for an event whose
  position is inside the camera's view and visible in the local player's fog; at most 4 world sounds per 100 ms overall and the existing
  per-event `MinGapMs`; UI sounds (`Select` / `Command` / `Refused` / `Ready` / `Alert`) always. Sources: `HitFlash.WasHit` for hits (melee vs
  ranged by the attacker's type is not known: use the **hit unit's** attacker-free rule: play `MeleeHit` for a hit without a projectile
  arriving that frame, `Shot` on a projectile spawn from `World.Projectiles` within view), `DeathMarkers` / death events for deaths and
  razes, `AbilityEvents` (via `ResolveFlashes`) for casts, the production queue's completion (`Buildings.QueueCount` falling with a new own
  unit / `World.HasTech` turning true) for `Ready`.
- **Placeholder music = the hook only:** a `Music` bus (the SFX / music volume settings already exist) and a `MusicPlayer` node that plays
  `res://assets/audio/music/*.ogg` on a loop if any file exists; none is shipped (music stays the owner's).

**Part 2 — alerts (docs/02 HUD: "Alerts ('Your base is under attack') show text, a minimap ping, and a sound"; Controls: Space jumps the
camera to the last alert).**
- `sim/Rts.Sim/ViewApi/AttackAlerts.cs`: allocation-free after construction; given the local player's own unit and building spans (`Alive`,
  `Generation`, `Hp`, `Owner`, positions) per frame, like `HitFlash`, it reports an **alert** when an own unit's or building's hp fell and no
  alert in the last 10 s of view time lies within 30 m of it; `Kind` = `Base` (a building) or `Units`; `Position`; `Count` of hits since.
  xUnit tests: a hit raises one alert, repeated hits within 30 m / 10 s raise none, a hit 40 m away raises a second, a new generation doesn't,
  0 B per frame.
- The view: on an alert whose position is **off screen** (outside the camera's view) show the text 4 s top-centre (`ui.json`
  `alert.base_under_attack` = "Your base is under attack", `alert.units_under_attack` = "Your units are under attack"), a minimap ping
  (a ring that pulses 3 s at the alert's minimap pixel, drawn by `Minimap`), and the `Alert` sound; on screen: ping + sound only, no text.
  **Space** (a fixed key, documented beside Esc / F10 / F12 in docs/02 "Controls" and `KeyMap`'s fixed list) jumps the camera to the last
  alert's position; the replay viewer shows the same alerts for player 1's seat.
- `ShellTest`'s key test covers the two new `ui.json` keys; the smoke stays silent of `ERROR` on import (24-bit / 96 kHz sources must
  import clean: check Godot's log).

**Acceptance criteria.**
1. BUG-0920 closed: `CombatViewTest` PASS on seeds 1 and 6; `scene-loop.ps1` 66 / 66; `sim/` diff empty outside `ViewApi/`.
2. Every committed `.wav` is listed in `tools/audio/sfx-manifest.json` with its Sonniss source and in docs/04's licensing log (one row per
   source pack, naming the files cut); no file > 2 MB or > 3 s; `git lfs ls-files` shows them as LFS objects.
3. A headless `SfxTest` scene: every `SfxEvent` has a clip row, each clip loads (`AudioStreamWav`, mono after import, mix rate ≤ 44,100),
   the fallback tone plays with a warning when a file is missing (the test moves one aside), the world-sound cap holds under a 100 v 100
   brawl (≤ 4 world plays per 100 ms, measured by the counters), UI sounds never dropped by the cap.
4. An `AlertTest` scene: an enemy raid on the local player's base off screen raises the text + the ping + the `Alert` play exactly once per
   10 s / 30 m; Space moves the camera to within 5 m of the alert; an on-screen raid gives ping + sound, no text; a replay of the same match
   shows the same alerts.
5. Smoke PASS (menu + `--skirmish`), build 0 warnings; `ShellTest` green with the two new keys; windowed run of a skirmish: the dev reports
   what played on select / move / a fight / a building finished and one screenshot with the alert text and the minimap ping.
6. docs/03 "Implementation (M6-V12)" (the event table, the on-screen rule, the cap, the alert rule, the music hook), docs/04 (the trim
   script + manifest as steps 2-3 for audio; licensing rows), docs/02 "Controls" (Space fixed) match the code.

**Design references.** docs/02 "HUD layout" (alerts), "Minimap" (alerts shown), "Controls and camera" l. 349 (Space), docs/02 "Game flow" 7
(volumes), docs/04 "UI, fonts, audio" (packs; the Kenney audio packs aren't downloaded: Sonniss only), "Directory layout and naming"
(`audio/sfx/<category>/<event>_<variant>.wav`, `.wav` for SFX, `.ogg` for music), "Import process" steps 4-5, "Licensing log"; docs/03
"Implementation (M6-V6)" (the Sfx bus and volumes), CLAUDE.md rules 1, 7, 8 and the art-pipeline rules (no raw packs, LFS before the first
binary: `.gitattributes` already covers `.wav` / `.ogg`).

**Tests required.** `AttackAlerts` xUnit tests (above); `SfxTest.tscn` and `AlertTest.tscn` in the scene loop; `CombatViewTest` re-staged;
`ShellTest` keys; smoke.

**Constraints.** Views hold no gameplay state (alert bookkeeping is view time + positions); nothing writes the sim; no Godot in `ViewApi/`
(`AttackAlerts` takes spans, as `HitFlash` does); player-facing text only in `ui.json`; no sound chosen by a C# literal name — the clip table
maps events to files under `res://assets/audio/sfx/`; no raw pack or uncut source file committed; every binary through LFS; the trim
script needs no install (PowerShell + .NET only).

### Candidates after (in order)
Save / load when Requests 42 lands, teams when 38 lands, `tools/export.ps1` when the owner's templates arrive, criterion 7's hand-made maps
when the sim ships a map file format (ask: Requests), the art pass when the owner answers the look call, criterion 6.

### Debt (next view hardening)
BUG-0922 (S4), BUG-0550 item 2 (the art pass), BUG-0521 items 2-3 (unreachable), BUG-0743 item 3 (wontfix candidate).

### Watch-outs (view)
- `scene-loop.ps1 -TimeoutSec` (default 600); `-Filter` for one scene. `QaM6V11Test` runs 21,600 ticks (~93 s); `M3PlayableTest` ~2 min.
- A clone without `git lfs pull` fails `LookTest` cleanly; the SFX fallback tone must keep every other scene green in that state.
- Sonniss sources are mostly 96 kHz / 24-bit stereo, 0.2-40 MB per file: cut first, then let Godot's import downmix; never commit an uncut
  file. The Church Bells / Extreme Winds files are minutes long (25-200 MB): avoid them.

---

## Data track

### Current session plan: STOP (cheap)

- Nothing unblocked: the D14 b openings need the schema the sim builds this session (**D21, next session: the openings content for both
  factions** against the schema above and docs/02 "AI opponent", docs/factions; the parked 12th-house step batches with it since D21
  moves the golden anyway); the M7-M9 rosters need schemas the roadmap hasn't reached; the inbox is empty (D19's table, the Shock pass,
  `goldShare` 0.6 → 0.5, BUG-0780, "densest cluster" still want an answer).
- **Rule 31 at the integration:** BUG-0910's fix can move the spending cells (a requirement-blocked Build goes out sooner); if the full
  suite shows `AiSpendingReportTests` / `D20PinQaTests` / `CounterTriangleMarginsTests` red on the merged tree, the conductor dispatches the
  re-print as at 1624 (D-R31: `docs/factions/*.md` only, no `game/data` change, push when the full non-Perf suite is green).
- **D21's shape (for the next PLAN):** 3 openings per faction (e.g. Malazan: a standard, an early Crossbow push, a fast Age II with
  Cadre Mages; Whirlwind: a standard, an early Raider rush, a Holy Camp / Zealot mass), each ≤ 40 steps, `displayName` + `description`
  quoted in the review table; `AiSpendingReportTests` re-printed per opening; golden regen once.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/03: each track appends its own rows; the conductor unions them.
- Builds share the machine: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only when it
  fails again alone.
- BUG-0914 / BUG-0915: if the H3 0-byte probe or the `buildingSight` loader row fails in another full run, it becomes S3 and the sim's
  first item.
- The sim edits both `ai.json` files this session (re-wrap only); the data track touches no `game/data` file (STOP).
