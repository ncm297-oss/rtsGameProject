# 07 — Studio Workflow

How the AI studio works on this game between your sessions: who does what, how a session
runs, when it stops to wait for you, and how to steer or pause it.

## The team

| Role | Who | Model | Does | Never does |
| --- | --- | --- | --- | --- |
| **Owner** | You | — | Taste, playtests, [OPEN] decisions, downloads and accounts, milestone sign-off | — |
| **Producer** (head game maker) | `producer` agent | Fable 5.1 | Between-session checks, plans each session, accepts or rejects work, keeps the roadmap, state, and handoff current, decides whether the studio keeps going | Write game code or tests |
| **Game developer** | `game-dev` agent | Opus 5.5 | Implements the planned task with unit tests; fixes QA findings | Decide scope, grade its own work, commit |
| **QA inspector** | `qa-inspector` agent | Opus 5.5 | Verifies every change independently, stress-tests it, grows the permanent stress suite, files bugs | Fix production code |
| **Conductor** | The session running `/studio-session` | Session default | Runs the loop and the git steps exactly as scripted | Make product decisions |

Why the split: whoever builds doesn't grade, whoever tests doesn't fix (so tests never get bent
to fit the code), and whoever plans doesn't code. The Producer runs on Fable because its calls
(what to build next, is this good enough, when to stop) matter most.

Agent definitions live in `.claude/agents/`; the session scripts are skills in `.claude/skills/`.

## One studio session

```
 Gate checks ── autopilot off? another session running? ──► exit (cheap)
      │
 Enter studio worktree (.claude/worktrees/studio, its own copy of the repo)
      │
 Producer: PLAN ── inbox, build/tests on main, verify last session's claims,
      │            docs drift, open bugs, daily cap
      ├── STOP ──► record "Waiting on you", notify you, exit
      ▼ GO (one task + acceptance criteria + QA focus)
 game-dev: build on branch studio/<session id>
      ▼
 QA inspector: verify criteria, attack, file bugs ──┐
      ▲                                              │ FAIL (S1/S2)
      └──── game-dev: fix (max 2 rounds) ◄───────────┘
      ▼
 Producer: ACCEPT / REJECT / ESCALATE, update roadmap, STATE, session log, handoff
      ├── ACCEPT ──► merge to GitHub main
      └── REJECT/ESCALATE ──► push branch for later; studio notes go to main
      ▼
 Exit worktree, release lock, notify you if something needs you
```

**`main` is always green.** Work only reaches `main` after the Producer's ACCEPT, with tests
passing.

**The studio never touches your files.** It works in a git worktree at
`.claude/worktrees/studio` (gitignored) and delivers through GitHub. Run `git pull` in your
checkout to see its work. If you and the studio change the same thing, git merges it on pull.

## How sessions start

| Trigger | What happens |
| --- | --- |
| Scheduled routine **"RTS studio session"** in the Claude desktop app | Fires at 9am, noon, 3pm, 6pm, 9pm. Exits within seconds if there's nothing to do |
| You type `/studio-session` | Runs a session now, even if autopilot is off |
| You work with Claude directly | Normal interactive work; finish with `/handoff` so the Producer records it and the next scheduled session continues from there |

The routine only runs while the desktop app is open. A run missed while the app was closed fires
on the next launch. Change the schedule or pause it from the app's routines/scheduled tasks
list, or ask Claude to.

## When the studio stops and waits for you

The Producer stops the loop and adds an item to **Waiting on you** at the top of
[studio/STATE.md](../studio/STATE.md) when:

- **A milestone is complete.** It writes playtest instructions; you play, then write
  "M<n> accepted" (plus any feedback) in the inbox.
- An **[OPEN]** design decision or taste call is needed.
- Something needs a download, an account, an install, or money.
- The build is red on `main` and the cause isn't clear, or the same task was rejected twice.
- The daily cap in `studio/autopilot.md` is reached (it resumes the next day).
- An incident happened (a crash, a git conflict it won't resolve on its own).

With `notify_owner: yes`, you also get a desktop notification (and on your phone, if Remote
Control is connected).

## Talking to the studio

| File | Direction | Use it for |
| --- | --- | --- |
| [studio/inbox.md](../studio/inbox.md) | You → studio | Requests, feedback, playtest notes, decisions, milestone sign-offs |
| [studio/STATE.md](../studio/STATE.md) | Studio → you | Dashboard: Waiting on you, current task, health, recent sessions |
| [studio/autopilot.md](../studio/autopilot.md) | You → studio | On/off switch, daily cap, fix rounds, push and notification switches |
| [studio/handoff.md](../studio/handoff.md) | Studio → studio | The next session's brief (readable by you too) |
| `studio/sessions/` | Studio → you | One log per session |
| `studio/bugs/` | QA → everyone | One file per bug, with severity and status |
| `studio/qa/` | QA → everyone | QA reports and the test coverage map |

Edit the inbox or autopilot from either machine, commit, and push (or ask Claude to). Every
Claude session in this repo also prints the top of `studio/STATE.md` when it starts.

## Quality bar

The QA inspector checks every change against its acceptance criteria with its own evidence,
then attacks it: seeded fuzzing with invariant checks, determinism, scale at 2× and 5× the
design numbers, edge cases, malformed data, design-doc conformance, architecture rules, headless
Godot boot, screenshots for visual work, and a clean-clone build. Cheap attacks become permanent
tests (`sim/Rts.Sim.Tests/Stress/` and `QA/`), so the suite gets stronger with every session.
[studio/qa/coverage.md](../studio/qa/coverage.md) maps which systems have which kinds of tests.

| Severity | Meaning | Blocks merge? |
| --- | --- | --- |
| S1 | Crash, hang, data loss, determinism break, red build, architecture rule broken | Yes |
| S2 | Acceptance criterion not met, wrong game rule, perf budget blown | Yes |
| S3 | Real bug outside the criteria | No (tracked) |
| S4 | Polish | No (tracked) |

## Permissions and access

Chosen by the owner on 2026-10-02: **project allowlist + auto mode** for unattended runs.

- [.claude/settings.json](../.claude/settings.json) (committed, applies on both machines):
  - **Allowed without asking:** `dotnet`, Godot via `$GODOT`, non-destructive git (status,
    diff, log, add, commit, pull, fetch, push, switch, branch, merge, stash, worktree, lfs, …),
    read-only PowerShell cmdlets.
  - **Always asks:** `winget`, downloads (`curl`, `Invoke-WebRequest`), `npx`/`npm install`,
    `dotnet add`/`dotnet tool`, setting environment variables.
  - **Always blocked:** force-push, deleting remote branches, `git reset --hard`, `git clean`,
    `git rebase`, history rewriting, `git branch -D`, registry edits, execution-policy and
    Defender changes, `netsh`, permission changes (`icacls`, `takeown`), shutdown/restart, disk
    formatting, deleting from the drive root or home, reading `.env` files.
- **Scheduled runs use auto mode:** anything not on the lists is reviewed by Claude Code's
  safety classifier before it runs. Check the routine's permission mode in the desktop app's
  scheduled tasks list; it should say auto.
- An unattended session that hits an "always asks" action can't get an answer, so the Producer
  plans around those and lists them under Waiting on you instead.
- To loosen or tighten access later, edit `.claude/settings.json` (or ask Claude to) and commit.

## Cost and pacing

A full session uses Fable twice (plan and accept) and Opus for building and QA, which costs
noticeably more than one chat. The cheap exits (autopilot off, a session already running, waiting
on you) cost very little. `max_sessions_per_day` in `studio/autopilot.md` is the main budget lever.

## Pausing and stopping

- **Pause the studio:** set `enabled` to `no` in `studio/autopilot.md` (takes effect at the
  next check-in), or disable the routine in the desktop app.
- **Stop a running session:** stop it in the desktop app like any session. If it leaves a stale
  lock, the next session ignores locks older than 6 hours.
- **Undo the studio's work:** everything it merges is a normal commit on `main`; ask Claude to
  revert it (a new commit, never a history rewrite).
