# Session logs

One file per studio session, written by the Producer at the end of the session.

- Name: `<SESSION_ID>.md`, where `SESSION_ID` is the local start time `YYYY-MM-DD-HHmm`.
  Interactive sessions end in `-interactive`, incidents in `-incident`.
- Keep each log short enough to read on a phone.

## Template

```markdown
# Session <SESSION_ID>

| Field | Value |
| --- | --- |
| Task | <TASK_ID>: <title> |
| Type | feature / hardening |
| QA tier | full / standard / light |
| Verdict | ACCEPT / REJECT / ESCALATE |
| Branch | studio/<SESSION_ID> (merged / pushed for later) |
| Fix rounds | <n> |
| Tests | <passed>/<total>, smoke <pass/fail> |

## What changed
- <2-5 bullets, plain language>

## Acceptance criteria
1. <met / not met> — <evidence>

## QA
<verdict, findings by severity with bug ids, stress numbers worth remembering>

## Decisions and surprises
- <anything the next session or the owner should know>

## Next
<what the handoff recommends>
```
