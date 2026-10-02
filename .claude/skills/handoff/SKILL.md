---
name: handoff
description: End an interactive work session - QA the changes, have the Producer review them and update the studio's state, roadmap, and next-session brief, then commit and push so scheduled sessions pick up from here. Use when the owner says they're done for now or asks for a handoff.
---

# Handoff after interactive work

1. `git fetch origin` and `git status`. If there are uncommitted changes, commit them with a
   descriptive message (asking for a handoff means the owner wants the work saved).
2. Find the diff base: `BASE` = the most recent commit that touched `studio/sessions/`
   (`git log -1 --format=%H -- studio/sessions`); if there is none, the repo's first commit
   (`git rev-list --max-parents=0 HEAD`). If `git diff --stat <BASE>...HEAD` shows only docs,
   skip QA.
3. Spawn `qa-inspector` in the current working directory with session id
   `<YYYY-MM-DD-HHmm>-interactive`, diff base `<BASE>`, and no brief (infer intent from commits
   and docs).
4. Spawn `producer`:

   > MODE HANDOFF. Working directory: `<cwd>`. Session id: `<YYYY-MM-DD-HHmm>-interactive`.
   > Diff base: `<BASE>`. QA report: `<report or "skipped: docs only">`.

5. Commit the producer's updates: `studio: interactive session handoff`.
6. If `push_to_github` is `yes` in `studio/autopilot.md`: `git pull --no-rebase` (merge if
   main moved), then `git push`.
7. Tell the owner in a few lines: the verdict, what's next, and what's waiting on them. The next
   scheduled session starts from `studio/handoff.md`.
