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

On 2026-10-03 the owner authorized the Producer to **start the next session whenever it's
ready** and to check in only at the end of the plan.

| Trigger | What happens |
| --- | --- |
| **Chain** (the main path) | A session ends with the Producer's `NEXT_GATE: GO`, so the conductor re-arms the routine to fire again 3 minutes later. The studio works through the roadmap session after session |
| **Heartbeat** | While the studio is waiting (on HOLD, cap reached), the routine falls back to 9am, noon, 3pm, 6pm, 9pm. A heartbeat run exits within seconds unless something changed (a new inbox note, a new day after the cap) |
| **Safety net** | Each working session first re-arms the routine 4 hours out, so a crash mid-session can't stall the studio for good |
| You type `/studio-session` | Runs a session now, even if autopilot is off; it chains afterwards like any other |
| You work with Claude directly | Normal interactive work; finish with `/handoff` so the Producer records it |

The routine only runs while the desktop app is open (a run missed while it was closed fires on
the next launch). `max_sessions_per_day` in `studio/autopilot.md` caps the chain.

## What the Producer may decide on its own

With `stop_at_milestone_end: no` and `open_decisions: producer_default` in `studio/autopilot.md`:

- **Milestone sign-off:** after verifying every required criterion and the QA coverage bar
  (Unit, Invariant fuzz, Determinism), it marks the milestone Done, writes the retro, moves on,
  and adds a "how to try it" note under **For your review** in `studio/STATE.md`.
- **Routine design decisions:** [OPEN] items and minor design questions get the option the docs
  recommend, recorded in the doc and the vision change log as "Producer decision, owner may
  revisit", and listed under **For your review**. It never reverses a decision you made.
- **Ordering:** if a task waits on you (a download, an account), it lists it under **Waiting on
  you** and works on the next unblocked task, even from a later milestone.

Flip either setting back to require your sign-off or your decisions again.

## When the studio stops and waits for you

The Producer stops the loop and adds an item to **Waiting on you** at the top of
[studio/STATE.md](../studio/STATE.md) when:

- **End of plan:** the roadmap through M9 is done, or the only work left isn't in the roadmap.
  It summarizes where the game stands and proposes what to design next; you approve new work.
- Every remaining task needs something only you can do: a download, an account, an install,
  money, or a taste call that has no documented default. (Agents never do these, whatever the
  settings say.)
- The build is red on `main` and the cause isn't clear, or the same task was rejected twice.
- The daily cap in `studio/autopilot.md` is reached (it resumes the next day by itself).
- An incident happened (a crash, a git conflict it won't resolve on its own).

Expected stops along the current roadmap: **M6** needs you to download the asset packs (and
ideally do the KayKit look test), and the AI-generated models need a Meshy or Tripo account. The
studio keeps working on other tasks while those wait.

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
- **Every session in this project starts in auto mode** (`"defaultMode": "auto"` in
  `.claude/settings.json`, added 2026-10-03 because new sessions were starting in manual mode
  and stalling on prompts). Anything not on the lists is reviewed by Claude Code's safety
  classifier before it runs. You can still switch a single session's mode in the app.
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
