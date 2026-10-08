# Inbox (owner → studio)

Write anything for the studio here: requests, feedback, playtest notes, decisions on open items,
milestone sign-offs ("M0 accepted"). Add new notes at the top of **New**, with a date. The
Producer reads this at the start of every session, acts on each note, and moves it to
**Processed** with a one-line response.

Commit and push after editing, from either machine (or ask Claude to).

## New

## Processed

- 2026-10-08 · **Art direction + packs downloaded (owner):** I prefer a grounded, realistic art style
  over a cartoonish one. The humanoid base is now Quaternius Universal Base Characters + Modular
  Character Outfits – Fantasy + Universal Animation Library 1 and 2, with Mixamo packs (Great Sword,
  Pro Longbow, Pro Magic, Pro Sword and Shield) retargeted for gaps; KayKit is a fallback only.
  docs/04 (style guide, characters, coverage plan, animation setup) and the docs/01 change log
  are updated. The packs are already in `asset-sources/` on the desktop (list in docs/04), and
  Blender 5.2 is installed, so I shouldn't be the bottleneck when art work starts. **Don't pull
  the look test or real-art work forward:** keep M6 where it is in the roadmap; this is just
  getting ahead. Please update the M6 line in the feature queue (downloads done except nature,
  UI, fonts, audio, export templates), and clear the stale "restore the routine's hourly
  schedule" item from Waiting on you (it has been back on hourly since 2026-10-06).
  → Done at the 2026-10-08-0913 integration update: the M6 line in STATE's sim feature queue now
  carries the art direction and the download state as docs/04 records it after your later commits
  (nature, UI and audio are in too; fonts, music and the Godot export templates remain, listed under
  Waiting on you when M5 starts); the hourly-schedule item is cleared. M6 stays where it is; no
  look test is pulled forward. Your docs/01 row is an owner decision; nothing for the studio to
  change.

- 2026-10-06 · **Third track: data (owner):** sessions now carry up to three tracks: `sim`,
  `view`, and a new `data` track (faction content: full rosters, stats, costs, build times,
  techs, `displayName` / `description` text, AI build orders, balance passes, and later the
  M7-M9 factions' data). Rules: `.claude/agents/producer.md` ("Tracks", incl. the data rule:
  the sim track keeps every schema and `game/data/common/`; the data track fills
  `game/data/factions/` against schemas already on main and writes no C# outside
  `sim/Rts.Sim.Tests/Content/` + `QA/Content/`). I want to review data work actively: every
  accepted data task gets a For your review table (unit / field, old → new, why, plus quoted
  names and descriptions); my inbox replies become the data track's next task. Please add a
  Data block to the Now table, a "## Data track" section to `studio/handoff.md`, and a data
  feature queue, and start the data track on whatever the current schemas already support
  (e.g. completing the Malazan and Whirlwind unit rosters toward M3's "factions fully defined in
  data"). Also: sessions no longer re-arm the routine (see docs/07 "How sessions start"); a
  watcher session starts the next one when STATE's Gate is GO, so keep the Gate rows accurate.
  → Done at the 2026-10-06-1503 ACCEPT (the note arrived mid-session, so this session ran two
  tracks): STATE has a Data block in the Now table and a data feature queue, `studio/handoff.md`
  has a "## Data track" section, and the data track's first task (next session) is the one the
  current schemas support: the nine missing building types per faction in `buildings.json`
  (M3-2 shipped the schema with the Town Hall only; the unit rosters already hold all 7 slots
  per faction from M1-2, so the rest of "fully defined in data" waits on the techs / abilities
  schemas from M3-5 / M4). Gate rows are kept per track; the data track's Gate is GO.

- 2026-10-05 · **Two tracks in parallel (owner):** every session now works on a `sim` task and a
  `view` task at the same time; you plan and judge both, two game-devs build side by side, and
  the conductor merges sim then view. Rules: `.claude/agents/producer.md` ("Tracks") and
  `.claude/skills/studio-session/SKILL.md`. Please restructure `studio/STATE.md` (a Now row
  block per track, Requests for the sim track, per-track queues and debt backlogs) and
  `studio/handoff.md` (a section per track), and start the view track on M2 work that the
  current sim API already supports (camera, terrain mesh, placeholder unit views, selection,
  move orders). → Done at the 2026-10-05-1446 PLAN: STATE and handoff restructured per track;
  view track starts with M2-1 (match scene that runs the sim, terrain mesh, RTS camera,
  `--screenshot` flag) while sim does M1-6 (replays); unit views, selection and move orders are
  M2-2 next session. Change-log row in docs/01; one Producer refinement (where ViewApi tests
  live) listed under For your review.

- 2026-10-05 · **Downloads stay with the owner (owner):** agents never download; the Producer lists
  the exact links (Godot 4.7.2 .NET export templates, the Kenney/KayKit/Quaternius packs from
  docs/04) with their `asset-sources/` folders under **Waiting on you** well before M6 needs them.
  → Acknowledged in session 2026-10-05-1234: the M6 line in STATE's feature queue carries the rule;
  the Producer posts the full link list (with folders) under Waiting on you when M5 starts, one
  milestone ahead, or earlier if an art look test is pulled forward. Links are already in docs/04.

- 2026-10-05 · **Speed up (owner):** QA depth by risk tier, S3/S4 bugs wait for a hardening
  session every 4th session and at milestone end (S1/S2 still first), tasks up to 1,500 lines
  when the design is clear. Rules are in `.claude/agents/producer.md`, `qa-inspector.md`, and
  `studio/autopilot.md` (`hardening_every`, `max_task_lines`). Please re-order the backlog in
  STATE.md accordingly and count the current M1-4c session as a hardening session. → Done in
  session 2026-10-05-1013: STATE backlog split into a feature queue and a debt backlog (hardening
  sessions only); session 2026-10-04-2056 (M1-4c) logged as Type `hardening`, so the count stands at
  1 feature session since the last hardening one; BUG-0030 moved out of M1-4d-2 into the debt backlog
  (BUG-0031 stays folded in: same code, a few lines).

- 2026-10-03 · **Producer authority (owner):** chain sessions, sign off milestones, default open
  items, cap 10/day; add a **For your review** section to STATE.md. → Acknowledged in session
  2026-10-03-1151: STATE.md now has "For your review" (non-blocking) separate from "Waiting on
  you" (blocking); the studio chains sessions under autopilot.md until the end of plan.

- 2026-10-03 · "M0 accepted. Go ahead with M1." → M0 marked **Done** in docs/05-roadmap.md,
  retro finalized, change log line in docs/01-vision.md, HOLD lifted; M1-1 planned in session
  2026-10-03-0907.
- 2026-10-03 · "Routine confirmed on auto permission mode." → Cleared from Waiting on you.

- 2026-10-02 · "High ground bonus yes, all other open items default." → Recorded in
  docs/01-vision.md and docs/02-game-design.md; roadmap updated (M1 terraced terrain, M4 vision
  rule, M5 AI ramp scouting).
- 2026-10-02 · "Build a Fable PM agent that runs checks between sessions and kicks off the next
  sessions, plus a QA inspector that stress-tests everything." → Studio set up; see
  docs/07-studio-workflow.md.
