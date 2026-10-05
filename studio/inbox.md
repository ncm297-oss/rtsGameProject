# Inbox (owner → studio)

Write anything for the studio here: requests, feedback, playtest notes, decisions on open items,
milestone sign-offs ("M0 accepted"). Add new notes at the top of **New**, with a date. The
Producer reads this at the start of every session, acts on each note, and moves it to
**Processed** with a one-line response.

Commit and push after editing, from either machine (or ask Claude to).

## New

- 2026-10-05 · **Downloads stay with the owner.** Agents can't download anything: winget, curl,
  Invoke-WebRequest, npm/npx and NuGet package adds are on the block list. When downloads are
  needed (M6: Godot .NET export templates for 4.7.2, the Kenney/KayKit/Quaternius packs listed in
  docs/04-art-pipeline.md), put the exact download links under **Waiting on you** well before
  they block work, say which folder in `asset-sources/` each zip goes in, and keep working on
  unblocked tasks meanwhile. Add a line about this to the M6 entry in the feature queue.

## Processed

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
