---
name: studio-session
description: Run one autonomous RTS studio session - the Producer plans, game-dev builds, the QA inspector stress-tests, the Producer accepts or rejects, and accepted work is pushed to main. Use only when the owner or the scheduled "RTS studio session" routine explicitly asks for a studio session.
---

# Studio session (conductor)

You are the **conductor**. You run this loop and the git plumbing exactly as written. You make
no product decisions: the `producer` agent decides, `game-dev` builds, `qa-inspector` tests, and
you carry their outputs between them. Keep your own messages short. Don't skip steps, and don't
do an agent's job yourself if it fails; record the failure instead.

Definitions:
- `REPO` = the project root (where `.claude/` lives).
- `WT` = `REPO/.claude/worktrees/studio`, the studio's own working copy. The studio never edits
  files in the owner's checkout.
- `SESSION_ID` = local start time as `YYYY-MM-DD-HHmm` (e.g. `2026-10-03-1500`).
- `SOURCE` = `scheduled` if the prompt that started this session says "Source: scheduled",
  otherwise `owner`.
- `LOCK` = `REPO/studio/.session.lock` (gitignored, machine-local).

## 0. Gate checks (exit cheaply when there's nothing to do)

Run these in `REPO`, before entering the worktree.

1. Read `REPO/studio/autopilot.md`. If `enabled` is not `yes` and `SOURCE` is `scheduled`, reply
   "Autopilot is disabled; exiting." and stop. An owner-started run continues anyway.
2. If `LOCK` exists and the time written in it is less than 6 hours ago, reply "Another studio
   session is running; exiting." and stop. Otherwise write the current time and `SESSION_ID`
   into `LOCK` (Write tool). From here on, step 9 must run no matter what happens.
3. Tools: `$GODOT` (Bash) / `$env:GODOT` (PowerShell) must point at an existing file, and
   `dotnet --list-sdks` must list an 8.0 SDK. If either fails, go to the incident path with
   "tools not visible to the session".
4. `git fetch origin`. Note `BASE` = `git rev-parse origin/main`.

## 1. Enter the studio worktree

1. If `git worktree list` doesn't show `WT`, create it: `git worktree add --detach
   .claude/worktrees/studio origin/main`.
2. Call `EnterWorktree` with `path: WT`. All later commands and all agent prompts use `WT` as the
   working directory. If `EnterWorktree` is unavailable or fails, go to the incident path. Never
   fall back to working in the owner's checkout.
3. In `WT`, `git status --porcelain` must be empty. If it isn't (a crashed session left work),
   don't delete anything: incident path.
4. `git switch --detach origin/main`.

## 2. Producer: PLAN

Spawn the `producer` agent:

> MODE PLAN. Working directory: `<WT>`. Session id: `<SESSION_ID>`. Source: `<SOURCE>`.
> Today: `<YYYY-MM-DD>`, local time `<HH:mm>`. Base commit: `<BASE>`.

Then:
1. If the producer changed files: `git add studio docs`, commit `studio: <SESSION_ID> plan
   check`, and if `push_to_github` is `yes`, `git push origin HEAD:main`. If that push is rejected
   (main moved), `git fetch origin`, `git merge --no-edit origin/main`, push again; on conflict,
   incident path.
2. If `DECISION: STOP`: if `notify_owner` is `yes` and the producer listed owner items, send a
   `PushNotification` with the REASON (under 160 characters). Go to step 9.

## 3. Branch

`git switch -c studio/<SESSION_ID>`. Record `PLAN_HEAD` = `git rev-parse HEAD`.

## 4. game-dev: build

Spawn `game-dev` with the producer's **Brief for game-dev** verbatim, plus:

> Working directory: `<WT>`. Task `<TASK_ID>`: `<TASK_TITLE>`. Diff base: `<PLAN_HEAD>`.

Then `git add -A` and commit `<TASK_ID>: implement <TASK_TITLE>`. If nothing changed, skip the
commit. If `STATUS: BLOCKED`, skip to step 7 with the report.

## 5. QA: inspect

Spawn `qa-inspector`:

> Inspect task `<TASK_ID>` in working directory `<WT>`. Session id: `<SESSION_ID>`.
> Diff base: `<PLAN_HEAD>` (use `git diff <PLAN_HEAD>...HEAD`).
> Brief: `<brief>`. QA focus: `<QA focus>`. Developer report: `<report>`.

Commit `<TASK_ID>: QA inspection` (tests, bug files, report).

## 6. Fix loop

While the QA `VERDICT` is `FAIL` and fewer than `max_fix_rounds` rounds have run:
1. Spawn `game-dev` in fix mode with the S1/S2 findings (and S3 if listed as cheap), the bug
   file paths, and the same working directory and diff base. Commit `<TASK_ID>: fix QA
   findings (round <r>)`.
2. Spawn `qa-inspector` to re-verify the fixes and rerun the full suite. Commit
   `<TASK_ID>: QA re-check (round <r>)`.

## 7. Producer: ACCEPT

Spawn `producer`:

> MODE ACCEPT. Working directory: `<WT>`. Session id: `<SESSION_ID>`. Branch:
> `studio/<SESSION_ID>`. Diff base: `<PLAN_HEAD>`. Plan: `<plan output>`. Developer reports:
> `<all>`. QA reports: `<all>`.

Commit its updates: `studio: <SESSION_ID> <VERDICT> - <COMMIT_SUMMARY>`.

## 8. Integrate

**ACCEPT:**
1. `git fetch origin`. If `origin/main` moved since `BASE`, `git merge --no-edit origin/main`.
   On conflict: `git merge --abort` and treat as ESCALATE with an owner item ("studio branch
   conflicts with main"). After a clean merge, rerun build and tests; if red, treat as ESCALATE.
2. If `push_to_github` is `yes`: `git push origin HEAD:main`, then confirm
   `git ls-remote origin refs/heads/main` equals `git rev-parse HEAD`.
3. `git switch --detach HEAD` and `git branch -d studio/<SESSION_ID>`.

**REJECT or ESCALATE:**
1. If `push_to_github` is `yes`: `git push -u origin studio/<SESSION_ID>` (keeps the work).
2. Carry the studio's memory to main without the code: `git fetch origin`,
   `git switch --detach origin/main`, `git checkout studio/<SESSION_ID> -- studio`, commit
   `studio: <SESSION_ID> <VERDICT> (state only)`, `git push origin HEAD:main`.

**Incident path** (any unexpected failure in steps 0-8; this is the one case where the conductor
writes studio files itself): write
`studio/sessions/<SESSION_ID>-incident.md` (what failed, the command, the last 30 lines of
output, what state the worktree is in), add a line to "Waiting on you" in `studio/STATE.md`,
commit on a detached `origin/main` checkout and push if possible. Set the notification to
"Studio incident: <one line>".

## 9. Wrap up (always runs)

1. If you entered the worktree, make sure it's clean (commit or record anything left, never
   delete it), then call `ExitWorktree` with `action: "keep"` to return to `REPO`.
2. Delete `LOCK` from `REPO` (`rm studio/.session.lock`).
3. If the producer returned `NOTIFY_OWNER: yes` (or an incident happened) and `notify_owner` is
   `yes`, send one `PushNotification` with its message.
4. Final reply, at most 6 lines: session id, task, verdict, what landed on GitHub `main`, and
   what's waiting on the owner.
