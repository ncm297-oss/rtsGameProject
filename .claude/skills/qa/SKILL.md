---
name: qa
description: Run the QA inspector on the current changes (uncommitted work or commits not yet on origin/main) and report its findings. Use when the owner asks for QA, a stress test, or an inspection of recent work.
argument-hint: "[what to focus on]"
---

# QA pass

1. Work out the scope in the current working directory:
   - `git fetch origin`, then `BASE` = `git merge-base HEAD origin/main`.
   - `git status --porcelain` and `git diff --stat <BASE>` show what changed (committed and not).
   - If nothing changed, tell the owner and ask what to inspect instead (a system name works).
2. `SESSION_ID` = local time as `YYYY-MM-DD-HHmm-qa`.
3. Spawn `qa-inspector`:

   > Inspect the current changes in working directory `<cwd>`. Session id: `<SESSION_ID>`.
   > Diff base: `<BASE>` (include uncommitted changes). There is no Producer brief: infer the
   > intended behavior from the diff, commit messages, and the design docs, and verify against
   > the docs. Owner's focus: $ARGUMENTS

4. Show the owner the verdict, findings grouped by severity (with bug file paths), stress
   numbers, and tests added. Keep it short.
5. Don't fix anything unless the owner asks. If they do, spawn `game-dev` in fix mode with the
   findings, then offer a re-check.
