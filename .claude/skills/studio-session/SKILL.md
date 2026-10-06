---
name: studio-session
description: Run one autonomous RTS studio session - the Producer plans a task for each of the three tracks (sim, view, data), game-devs build them in parallel, the QA inspector stress-tests each, the Producer accepts or rejects each, and accepted work is pushed to main. Use only when the owner or the scheduled "RTS studio session" routine explicitly asks for a studio session.
---

# Studio session (conductor)

You are the **conductor**. You coordinate one studio session that works on **up to three tracks
at once**: `sim` (game rules) and `view` (the Godot presentation) since 2026-10-05, and `data`
(faction content: rosters, stats, costs, names) since 2026-10-06. You run this loop and the git
plumbing exactly as written. You make no product decisions: the `producer` agent (Fable) plans
and judges all tracks together, `game-dev` builds, and `qa-inspector` tests. You carry their
outputs between them, run the tracks side by side, and merge their results. Keep your own
messages short. Don't skip steps, and don't do an agent's job yourself if it fails; record the
failure instead.

**Run every shell command as its own tool call.** Never chain commands with `;`, `&&`, `|` or
newlines in one call. Single commands such as `git push origin HEAD:main` match the project
allowlist; chained ones don't.

Definitions:
- `REPO` = the project root (where `.claude/` lives). You stay in `REPO` the whole session and
  never edit its files except `LOCK` and incident notes.
- One worktree per track: `WT_SIM` = `REPO/.claude/worktrees/studio`, `WT_VIEW` =
  `REPO/.claude/worktrees/studio-view`, `WT_DATA` = `REPO/.claude/worktrees/studio-data`. Run git
  in them with `git -C <WT> ...`, and give every agent the absolute path of its track's worktree
  as its working directory. A track's agents never touch another track's worktree. File
  ownership per track is in `.claude/agents/producer.md` ("Tracks").
- `SESSION_ID` = local start time as `YYYY-MM-DD-HHmm` (e.g. `2026-10-05-1500`). Branches are
  `studio/<SESSION_ID>-<track>` (`-sim`, `-view`, `-data`).
- `SOURCE` = `scheduled` if the prompt that started this session says "Source: scheduled",
  otherwise `owner`.
- `LOCK` = `REPO/studio/.session.lock` (gitignored, machine-local).
- **Active tracks** = the tracks the Producer gave a GO this session (one, two, or three).
  Integration order is always **sim, then view, then data**.

## Scheduling: never touch the routine's schedule

A session started by a schedule can't change a scheduled task without a permission prompt, and
an unanswered prompt stalls the studio (owner decision 2026-10-06). So **never call
`update_scheduled_task`** (or any other tool that changes a routine), whatever this session's
source. The next session starts in one of two ways:
- **Watcher:** the owner's interactive "studio watcher" session checks every 10 minutes and
  presses Run now on the routine when no session is running and `studio/STATE.md` on
  `origin/main` shows a GO gate. It also turns Remote Control on for each studio session.
- **Hourly fallback:** the routine itself fires every hour; the gate checks below make that run
  exit within seconds when there's nothing to do.

A `NEXT_GATE: GO` from the Producer is what tells the watcher to start the next session.

## 0. Gate checks (exit cheaply when there's nothing to do)

1. If `LOCK` exists and the time written in it is less than 3 hours ago, reply "Another studio
   session is running; exiting." and stop.
2. `git fetch origin`. From here on read the studio's shared files from `origin/main` with
   `git show origin/main:<path>` (the owner's checkout may be behind).
3. Read `studio/autopilot.md`. If `enabled` is not `yes` and `SOURCE` is `scheduled`, reply
   "Autopilot is disabled; exiting." and stop. An owner-started run continues anyway.
4. Scheduled runs only:
   - **Daily cap:** `git ls-tree --name-only origin/main studio/sessions/`; count the logs whose
     name starts with today's local date and doesn't end in `-incident.md`. If the count is at or
     above `max_sessions_per_day`, reply "Daily cap reached; exiting." and stop.
   - **Waiting:** if `studio/STATE.md` shows Gate **HOLD** (or STOP) for every track, the reason
     is not the daily cap (or the cap was hit on an earlier day), and `studio/inbox.md` has no
     notes under **New**, reply "Studio is on HOLD and the inbox is empty; exiting." and stop.
5. **Usage limit:** call `mcp__ccd_session_mgmt__get_usage` and read `plan.windows`. If any
   weekly or 5-hour window's `percentUsed` is at or above `usage_stop_percent` in autopilot.md,
   reply "<Weekly | 5-hour> usage at <n>%; studio paused until <resetsAt, local time>." and stop
   (the watcher and the hourly fallback re-check later; nothing to re-arm). If the tool is
   unavailable or `plan.status` isn't `ok`, carry on and note it in the session log. Check again
   before step 4 and before each fix round: if a limit has been crossed, let running agents
   finish, skip to step 7 so the Producer records the partial work, then stop.
6. Write the current time and `SESSION_ID` into `LOCK` (Write tool). From here on, step 9 must
   run no matter what happens.
7. **Remote Control** (owner-started runs only; the app refuses it in scheduled runs, and the
   watcher turns it on for those): call `mcp__ccd_session_mgmt__set_remote_control` with
   `session_id: "self"` and `enabled: true`. If it's missing or refused, carry on. Note the
   result for the final reply and the session log (scheduled runs: "watcher").
8. Tools: `$GODOT` (Bash) / `$env:GODOT` (PowerShell) must point at an existing file, and
   `dotnet --list-sdks` must list an 8.0 SDK. If either fails, go to the incident path with
   "tools not visible to the session".
9. Note `BASE` = `git rev-parse origin/main`.

## 1. Prepare the worktrees

For each of `WT_SIM`, `WT_VIEW`, and `WT_DATA`:
1. If `git worktree list` doesn't show it, create it: `git worktree add --detach <path> origin/main`.
2. **Recovery:** if it is on a `studio/...` branch with commits that aren't on `origin/main`, or
   has uncommitted changes, an earlier session died mid-way on that track. Never delete
   anything. Commit any uncommitted changes as `<branch>: recovered work from interrupted
   session`, and mark that track as **resuming**: in step 2, give the Producer the branch name
   and ask it to confirm resuming that track's last plan (from the track's section of
   `studio/handoff.md`) instead of planning new work for it. A resumed track skips step 3 and
   goes straight to QA (step 5) with `PLAN_HEAD` = `git -C <WT> merge-base HEAD origin/main`.
3. Otherwise `git -C <WT> switch --detach origin/main`.

## 2. Producer: PLAN (all tracks)

Spawn the `producer` agent once:

> MODE PLAN for all tracks (sim, view, data). Session id: `<SESSION_ID>`. Source: `<SOURCE>`.
> Today: `<YYYY-MM-DD>`, local time `<HH:mm>`. Base commit: `<BASE>`. Sim worktree: `<WT_SIM>`;
> view worktree: `<WT_VIEW>`; data worktree: `<WT_DATA>` (read shared files in any; they match
> `origin/main`). Resuming: `<none, or track + branch>`.

The Producer returns a GO or STOP for each track, and for each GO a brief, a QA tier, and a QA
focus. Then:
1. If the producer changed files, it did so in `WT_SIM`: `git -C <WT_SIM> add studio docs`,
   commit `studio: <SESSION_ID> plan check`, and if `push_to_github` is `yes`,
   `git -C <WT_SIM> push origin HEAD:main`. If the push is rejected (main moved), fetch, merge
   `origin/main`, push again; on conflict, incident path. Then, for `WT_VIEW` and `WT_DATA`,
   `git -C <WT> fetch origin` and `git -C <WT> switch --detach origin/main` so every track starts
   from the same commit.
2. If every track is STOP: if `notify_owner` is `yes` and the producer listed owner items, send
   a `PushNotification` with the reason (under 160 characters). Go to step 9.

## 3. Branch

For each active track: `git -C <WT> switch -c studio/<SESSION_ID>-<track>`, and record that
track's `PLAN_HEAD` = `git -C <WT> rev-parse HEAD`.

## 4. game-dev: build the tracks in parallel

For each active track, spawn a `game-dev` agent **in the background** (all at once), with the
producer's brief for that track verbatim, plus:

> Track: `<track>`. Working directory: `<WT>` (use absolute paths; stay inside it). Task
> `<TASK_ID>`: `<TASK_TITLE>`. Diff base: `<PLAN_HEAD>`. The other tracks are being built at the
> same time in their own worktrees; only change your track's files.

Wait for all of them to finish. For each: `git -C <WT> add -A`, commit `<TASK_ID>: implement
<TASK_TITLE>` (skip if nothing changed). A track whose report says `STATUS: BLOCKED` skips to
step 7.

## 5. QA: inspect each track

For each active track, spawn `qa-inspector` (in parallel, background):

> Inspect task `<TASK_ID>` (track `<track>`) in working directory `<WT>` (absolute paths; stay
> inside it). Session id: `<SESSION_ID>`. Diff base: `<PLAN_HEAD>` (use `git -C <WT> diff
> <PLAN_HEAD>...HEAD`). QA tier: `<QA_TIER>`. Other tracks' QA may run at the same time, so
> rerun any wall-clock Perf failure once before reporting it. Brief: `<brief>`. QA focus:
> `<QA focus>`. Developer report: `<report>`.

When each finishes, commit in its worktree: `<TASK_ID>: QA inspection`.

## 6. Fix loop (per track)

For each track whose QA `VERDICT` is `FAIL`, independently, while fewer than `max_fix_rounds`
rounds have run on that track:
1. Spawn `game-dev` in fix mode with the S1/S2 findings (and S3 if listed as cheap), the bug file
   paths, and the same working directory and diff base. Commit `<TASK_ID>: fix QA findings
   (round <r>)`.
2. Spawn `qa-inspector` to re-verify and rerun the full suite. Commit `<TASK_ID>: QA re-check
   (round <r>)`.

The tracks' fix loops may run at the same time.

## 7. Producer: ACCEPT (all tracks)

Spawn `producer` once:

> MODE ACCEPT for all active tracks. Session id: `<SESSION_ID>`. For each active track: worktree
> `<WT>`, branch `studio/<SESSION_ID>-<track>`, diff base `<PLAN_HEAD>`, plan, developer
> reports, QA reports. Write the shared studio files (STATE, session log, handoff, roadmap) in
> `<WT_SIM>` only, on the sim branch if sim is active, otherwise on a detached checkout of
> `origin/main` there.

It returns a verdict per track (ACCEPT / REJECT / ESCALATE), plus one `NEXT_GATE`,
`COMMIT_SUMMARY` per track, and `NOTIFY_OWNER`. Commit its updates in `WT_SIM`:
`studio: <SESSION_ID> <verdicts>`.

## 8. Integrate (sim, then view, then data)

For each active track in the order **sim, view, data**:

**ACCEPT:**
1. `git -C <WT> fetch origin`. If `origin/main` moved, `git -C <WT> merge --no-edit origin/main`.
   On conflict, list the conflicted files (`git -C <WT> diff --name-only --diff-filter=U`):
   - **Only `docs/` or `studio/` files:** resolve them yourself. These are mostly append-only
     lists and tables, so keep both sides' new entries in date order. For `studio/STATE.md`,
     keep the newer session's version and re-apply anything the other side added under Waiting
     on you or For your review. Then `git -C <WT> add` the files and commit with `--no-edit`.
   - **Any code, data, or config file:** `git -C <WT> merge --abort` and treat as ESCALATE with
     an owner item ("studio branch conflicts with main in <files>").
   After a merge, rerun build and tests in that worktree; if red, treat as ESCALATE.
2. If `push_to_github` is `yes`: `git -C <WT> push origin HEAD:main` (alone in its call), then
   confirm `git ls-remote origin refs/heads/main` equals `git -C <WT> rev-parse HEAD`. If git
   rejects the push because `main` moved, repeat step 1 and push again, up to 3 times. If the
   push is refused by the permission check, don't retry it in another form: push the branch,
   record an incident, and add "merge studio/<SESSION_ID>-<track> into main" to Waiting on you.
3. `git -C <WT> switch --detach HEAD`. Leave the local branch in place.

**REJECT or ESCALATE:**
1. If `push_to_github` is `yes`: `git -C <WT> push -u origin studio/<SESSION_ID>-<track>`
   (keeps the work for a later session).
2. If the studio files for this session live only on this branch, carry them to `main` without
   the code: in that worktree `git -C <WT> fetch origin`, `git -C <WT> switch --detach
   origin/main`, `git -C <WT> checkout studio/<SESSION_ID>-<track> -- studio`, commit
   `studio: <SESSION_ID> <verdict> (state only)`, push to `main`.

**Incident path** (any unexpected failure in steps 0-8; this is the one case where the conductor
writes studio files itself): write `studio/sessions/<SESSION_ID>-incident.md` (what failed, the
command, the last 30 lines of output, what state each worktree is in), add a line to "Waiting on
you" in `studio/STATE.md`, set every track's Gate to **HOLD** there (so the watcher doesn't start
the next session into the same failure), commit on a detached `origin/main` checkout in `WT_SIM`,
and push if possible. Set the notification to "Studio incident: <one line>".

## 9. Wrap up (always runs)

1. Make sure every worktree is clean (commit or record anything left, never delete anything).
2. Delete `LOCK` (`rm studio/.session.lock`).
3. If the producer returned `NOTIFY_OWNER: yes` (or an incident happened) and `notify_owner` is
   `yes`, send one `PushNotification` with its message.
4. Final reply, at most 9 lines: session id, then per track the task and verdict, what landed on
   GitHub `main`, what's waiting on the owner, Remote Control state, and `NEXT_GATE` (GO: the
   watcher starts the next session within about 10 minutes; HOLD: the studio waits).
