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

## Processed

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
