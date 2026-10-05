---
name: studio-session
description: Run one autonomous RTS studio session - the Producer plans a task for each of the two tracks (sim and view), two game-devs build them in parallel, the QA inspector stress-tests each, the Producer accepts or rejects each, and accepted work is pushed to main. Use only when the owner or the scheduled "RTS studio session" routine explicitly asks for a studio session.
---

# Studio session (conductor)

You are the **conductor**. You coordinate one studio session that works on **two tracks at
once** (owner decision 2026-10-05): `sim` (game rules) and `view` (the Godot presentation). You
run this loop and the git plumbing exactly as written. You make no product decisions: the
`producer` agent (Fable) plans and judges both tracks together, `game-dev` builds, and
`qa-inspector` tests. You carry their outputs between them, run the two tracks side by side, and
merge their results. Keep your own messages short. Don't skip steps, and don't do an agent's job
yourself if it fails; record the failure instead.

**Run every shell command as its own tool call.** Never chain commands with `;`, `&&`, `|` or
newlines in one call. Single commands such as `git push origin HEAD:main` match the project
allowlist; chained ones don't.

Definitions:
- `REPO` = the project root (where `.claude/` lives). You stay in `REPO` the whole session and
  never edit its files except `LOCK` and incident notes.
- `WT_SIM` = `REPO/.claude/worktrees/studio`, `WT_VIEW` = `REPO/.claude/worktrees/studio-view`:
  one working copy per track. Run git in them with `git -C <WT> ...`, and give every agent the
  absolute path of its track's worktree as its working directory. A track's agents never touch
  the other track's worktree. File ownership per track is in `.claude/agents/producer.md`
  ("Tracks").
- `SESSION_ID` = local start time as `YYYY-MM-DD-HHmm` (e.g. `2026-10-05-1500`). Branches are
  `studio/<SESSION_ID>-sim` and `studio/<SESSION_ID>-view`.
- `SOURCE` = `scheduled` if the prompt that started this session says "Source: scheduled",
  otherwise `owner`.
- `LOCK` = `REPO/studio/.session.lock` (gitignored, machine-local).
- `TASK` = the scheduled task id `rts-studio-session`. `HEARTBEAT` = cron `0 9-21/3 * * *`.
- **Active tracks** = the tracks the Producer gave a GO this session (one or both).

## Scheduling: how the next session gets started

The Producer may start the next session whenever it's ready (owner authorization, 2026-10-03).
You implement that by re-arming `TASK` with the `update_scheduled_task` tool
(`mcp__scheduled-tasks__update_scheduled_task`), each call on its own:
- **Chain** (more work is ready): `fireAt` = now + 3 minutes, ISO 8601 with offset (get it from
  `Get-Date (Get-Date).AddMinutes(3) -Format o`).
- **Safety net** (set at the start of real work, step 0.7): `fireAt` = now + 4 hours, so a
  crashed session can't stall the studio forever.
- **Heartbeat** (waiting): `cronExpression` = `HEARTBEAT`.

If the tool is unavailable or refused, set `CHAIN = off`, note it in the session log, and carry
on: the routine then keeps whatever schedule it has.

## 0. Gate checks (exit cheaply when there's nothing to do)

0. **Remote Control:** call `mcp__ccd_session_mgmt__set_remote_control` with `session_id: "self"`
   and `enabled: true`, so the owner can follow the session from the Claude mobile app. If the
   tool is missing or refused, carry on. Then call `mcp__ccd_session_mgmt__get_session` with
   `session_id: "self"` and note `remoteControlState` (on / off / refused, with the tool's
   message if refused) in your final reply and in the session log.
1. Read `REPO/studio/autopilot.md`. If `enabled` is not `yes` and `SOURCE` is `scheduled`, set
   the heartbeat, reply "Autopilot is disabled; exiting." and stop. An owner-started run
   continues anyway.
2. If `LOCK` exists and the time written in it is less than 3 hours ago, reply "Another studio
   session is running; exiting." and stop (don't touch the schedule; that session re-arms it).
3. Waiting check (scheduled runs only): if `REPO/studio/STATE.md` shows Gate **HOLD** for both
   tracks, the reason is not the daily cap (or the cap was hit on an earlier day), and
   `REPO/studio/inbox.md` has no notes under **New**, nothing can have changed: set the
   heartbeat, reply "Studio is on HOLD and the inbox is empty; exiting." and stop.
4. **Usage limit:** call `mcp__ccd_session_mgmt__get_usage` and read `plan.windows`. If any
   **weekly** window's `percentUsed` is at or above `usage_stop_percent` in autopilot.md, set
   `TASK` to `fireAt` = that window's `resetsAt` + 10 minutes, reply "Weekly usage at <n>%;
   studio paused until <reset, local time>." and stop. If the **5-hour** window is at or above
   it, do the same with that window's reset time. If the tool is unavailable or `plan.status`
   isn't `ok`, carry on and note it in the session log. Check again before step 4 and before
   each fix round: if a limit has been crossed, let running agents finish, skip to step 7 so the
   Producer records the partial work, then stop with the same `fireAt` instead of the chain or
   heartbeat.
5. Write the current time and `SESSION_ID` into `LOCK` (Write tool). From here on, step 9 must
   run no matter what happens.
6. Tools: `$GODOT` (Bash) / `$env:GODOT` (PowerShell) must point at an existing file, and
   `dotnet --list-sdks` must list an 8.0 SDK. If either fails, go to the incident path with
   "tools not visible to the session".
7. Set the safety net (`fireAt` = now + 4 hours).
8. `git fetch origin`. Note `BASE` = `git rev-parse origin/main`.

## 1. Prepare both worktrees

For each of `WT_SIM` and `WT_VIEW`:
1. If `git worktree list` doesn't show it, create it: `git worktree add --detach <path> origin/main`.
2. **Recovery:** if it is on a `studio/...` branch with commits that aren't on `origin/main`, or
   has uncommitted changes, an earlier session died mid-way on that track. Never delete
   anything. Commit any uncommitted changes as `<branch>: recovered work from interrupted
   session`, and mark that track as **resuming**: in step 2, give the Producer the branch name
   and ask it to confirm resuming that track's last plan (from the track's section of
   `studio/handoff.md`) instead of planning new work for it. A resumed track skips step 3 and
   goes straight to QA (step 5) with `PLAN_HEAD` = `git -C <WT> merge-base HEAD origin/main`.
3. Otherwise `git -C <WT> switch --detach origin/main`.

## 2. Producer: PLAN (both tracks)

Spawn the `producer` agent once:

> MODE PLAN for both tracks. Session id: `<SESSION_ID>`. Source: `<SOURCE>`. Today:
> `<YYYY-MM-DD>`, local time `<HH:mm>`. Base commit: `<BASE>`. Sim worktree: `<WT_SIM>`;
> view worktree: `<WT_VIEW>` (read shared files in either; they match `origin/main`).
> Resuming: `<none, or track + branch>`.

The Producer returns a GO or STOP for each track, and for each GO a brief, a QA tier, and a QA
focus. Then:
1. If the producer changed files, it did so in `WT_SIM`: `git -C <WT_SIM> add studio docs`,
   commit `studio: <SESSION_ID> plan check`, and if `push_to_github` is `yes`,
   `git -C <WT_SIM> push origin HEAD:main`. If the push is rejected (main moved), fetch, merge
   `origin/main`, push again; on conflict, incident path. Then `git -C <WT_VIEW> fetch origin`
   and `git -C <WT_VIEW> switch --detach origin/main` so both tracks start from the same commit.
2. If both tracks are STOP: if `notify_owner` is `yes` and the producer listed owner items, send
   a `PushNotification` with the reason (under 160 characters). Go to step 9.

## 3. Branch

For each active track: `git -C <WT> switch -c studio/<SESSION_ID>-<track>`, and record that
track's `PLAN_HEAD` = `git -C <WT> rev-parse HEAD`.

## 4. game-dev: build both tracks in parallel

For each active track, spawn a `game-dev` agent **in the background** (both at once), with the
producer's brief for that track verbatim, plus:

> Track: `<track>`. Working directory: `<WT>` (use absolute paths; stay inside it). Task
> `<TASK_ID>`: `<TASK_TITLE>`. Diff base: `<PLAN_HEAD>`. The other track is being built at the
> same time in its own worktree; only change your track's files.

Wait for both to finish. For each: `git -C <WT> add -A`, commit `<TASK_ID>: implement
<TASK_TITLE>` (skip if nothing changed). A track whose report says `STATUS: BLOCKED` skips to
step 7.

## 5. QA: inspect each track

For each active track, spawn `qa-inspector` (in parallel, background):

> Inspect task `<TASK_ID>` (track `<track>`) in working directory `<WT>` (absolute paths; stay
> inside it). Session id: `<SESSION_ID>`. Diff base: `<PLAN_HEAD>` (use `git -C <WT> diff
> <PLAN_HEAD>...HEAD`). QA tier: `<QA_TIER>`. The other track's QA may run at the same time, so
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

The two tracks' fix loops may run at the same time.

## 7. Producer: ACCEPT (both tracks)

Spawn `producer` once:

> MODE ACCEPT for both tracks. Session id: `<SESSION_ID>`. For each active track: worktree
> `<WT>`, branch `studio/<SESSION_ID>-<track>`, diff base `<PLAN_HEAD>`, plan, developer
> reports, QA reports. Write the shared studio files (STATE, session log, handoff, roadmap) in
> `<WT_SIM>` only, on the sim branch if sim is active, otherwise on a detached checkout of
> `origin/main` there.

It returns a verdict per track (ACCEPT / REJECT / ESCALATE), plus one `NEXT_GATE`,
`COMMIT_SUMMARY` per track, and `NOTIFY_OWNER`. Commit its updates in `WT_SIM`:
`studio: <SESSION_ID> <verdicts>`.

## 8. Integrate (sim first, then view)

For each active track in the order **sim, then view**:

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
you" in `studio/STATE.md`, commit on a detached `origin/main` checkout in `WT_SIM`, and push if
possible. Set the notification to "Studio incident: <one line>".

## 9. Wrap up (always runs)

1. Make sure both worktrees are clean (commit or record anything left, never delete anything).
2. Delete `LOCK` (`rm studio/.session.lock`).
3. Re-arm the schedule (unless `CHAIN` is off). If a usage-limit stop already set `fireAt` to a
   reset time, leave it alone. Otherwise:
   - **Chain** if the producer's ACCEPT returned `NEXT_GATE: GO`, `chain_sessions` is `yes`, and
     no incident happened.
   - Otherwise (both tracks STOP, `NEXT_GATE: HOLD`, or an incident): **heartbeat**.
4. If the producer returned `NOTIFY_OWNER: yes` (or an incident happened) and `notify_owner` is
   `yes`, send one `PushNotification` with its message.
5. Final reply, at most 8 lines: session id, then per track the task and verdict, what landed on
   GitHub `main`, what's waiting on the owner, Remote Control state, and when the next session
   starts (chained in 3 minutes, or heartbeat).
