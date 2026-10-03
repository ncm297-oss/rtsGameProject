# Inbox (owner → studio)

Write anything for the studio here: requests, feedback, playtest notes, decisions on open items,
milestone sign-offs ("M0 accepted"). Add new notes at the top of **New**, with a date. The
Producer reads this at the start of every session, acts on each note, and moves it to
**Processed** with a one-line response.

Commit and push after editing, from either machine (or ask Claude to).

## New

- 2026-10-03 · **Producer authority (owner):** "I want to give the producer authorization to
  start next sessions whenever ready. Just check with me once we hit the end of the plan before
  new sessions are needed to be designed or approved." Encoded in `studio/autopilot.md`
  (chain_sessions yes, stop_at_milestone_end no, open_decisions producer_default, cap 10/day) and
  in `.claude/agents/producer.md` ("Your authority"). Please restructure STATE.md with a
  **For your review** section for non-blocking items.

- 2026-10-03 · **M0 accepted.** (Owner sign-off, relayed by Claude in an interactive session.
  Build, 4/4 tests, and `tools/qa/smoke.ps1` were re-run green after merging session
  2026-10-03-0826 into main.) Go ahead with M1.
- 2026-10-03 · The "RTS studio session" routine is confirmed on **auto** permission mode; clear
  that item from Waiting on you.

## Processed

- 2026-10-02 · "High ground bonus yes, all other open items default." → Recorded in
  docs/01-vision.md and docs/02-game-design.md; roadmap updated (M1 terraced terrain, M4 vision
  rule, M5 AI ramp scouting).
- 2026-10-02 · "Build a Fable PM agent that runs checks between sessions and kicks off the next
  sessions, plus a QA inspector that stress-tests everything." → Studio set up; see
  docs/07-studio-workflow.md.
