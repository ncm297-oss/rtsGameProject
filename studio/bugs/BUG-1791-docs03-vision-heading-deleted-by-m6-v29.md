# BUG-1791: M6-V29 deleted the "## Vision, detection, fog" heading from docs/03

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-11-26-0207, task M6-V29 (QA) |
| System | docs: `docs/03-technical-design.md` |
| Fixed by | The Producer at the 2026-11-26-0717 PLAN: `## Vision, detection, fog` restored before "Fog of war and the high-ground rule are in since M4-3a"; `grep -n "^## Vision, detection, fog" docs/03-technical-design.md` finds one line |

## Repro
1. `git diff 6f9975a...45e0e0d -- docs/03-technical-design.md`: the last hunk ends with `-## Vision, detection, fog`
   (the line replaced by a blank line) right after the new "Implementation (M6-V29)" questions.
2. `grep -n "^## Vision, detection, fog" docs/03-technical-design.md` finds nothing on HEAD (line 4987 on the base).

## Expected
The heading stays; M6-V29's new section is inserted before it.

## Actual
The section "Fog of war and the high-ground rule are in since M4-3a ..." now runs on as part of "Implementation (M6-V29)".
The anchor `#vision-detection-fog` that docs/01 links twice (rows 2026-10-08 and 2026-10-10) is gone, and docs/03's own
references by name ("see "Vision, detection, fog"", lines 139, 2581, 7452) point at a heading that no longer exists.

## Notes
One-line fix: restore `## Vision, detection, fog` followed by a blank line before "Fog of war and the high-ground rule".
