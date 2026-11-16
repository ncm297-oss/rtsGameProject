# BUG-1480: shadow.md "Decisions (D-R38)" numbers its lines 1-11 and tags line 11 "(Q4)"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D-R39, session 2026-11-15-1536: the Decisions list is bullets, "(Q4)" kept; verified by QA) |
| Found | 2026-11-15-0506, task D-H9 (QA) |
| System | docs (`docs/factions/shadow.md`) |
| Fixed by | D-R39 |

## Repro
1. Open `docs/factions/shadow.md`, section "## Decisions (D-R38)".
2. Line 4 reads "4. The Edur Ram is a melee ram ..."; line 11 reads "11. (Q4) The content checks that refuse another
   faction's names ...".

## Expected
The list's numbers are not mistaken for D-R38's Q numbers. For example, use bullets instead of a numbered list, or
drop the numbers and keep only the "(Q4)" tag the repo can prove.

## Actual
A numbered list whose eleventh line says "(Q4)". A reader sees two "4"s and may read lines 1-10 as Q1-Q10, a mapping
the section's own intro says the repo cannot give (the Q numbers lived only in D-R38's returned report).

## Notes
Content is otherwise faithful to docs/01 2026-11-15 (a) item (3). No pin reads the section.
