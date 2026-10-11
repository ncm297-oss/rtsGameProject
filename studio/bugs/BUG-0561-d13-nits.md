# BUG-0561: D13 nits: two future-faction pages still give their line unit the old ×1.5 vs Mounted template value; whirlwind.md calls ×1.1 "the nearest the band that lever could go"; the 1.1-over-1.3 pick is not argued

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-11-0314, task D13 |
| System | docs (faction pages) |
| Fixed by | |

## Repro
1. `grep -n "vs Mounted" docs/factions/*.md`
2. Read docs/factions/whirlwind.md "Balance baseline", last paragraph.
3. Compare the sweep in docs/factions/malazan.md "Target band" (cells HI s0 / HI s1 / Raider s0 / Raider s1).

## Expected
docs/02's template bonus is now "Line deals ×1.1 vs Mounted". Pages that copied the old template value should either follow
it or say why they differ. Each page's statement about the sweep should agree with the sweep.

## Actual
1. `docs/factions/andii.md:44` "**Andii Blade:** ×1.5 vs Mounted" and `docs/factions/shadow.md:44` "**Edur Warrior:** ×1.5 vs
   Mounted": both are the old template value, and no note says they deliberately stay above the template. (teblor.md's ×1.25
   is its own value.) Neither faction has data yet (M7-M9), so nothing plays wrong today, but whoever writes that data will meet
   a page that contradicts docs/02.
2. whirlwind.md: "D13 cut the line units' bonus vs Mounted from ×1.5 to ×1.1, the nearest the band that lever could go". 1.0
   was nearer (79-94 %). It was ruled out by a stress floor, as malazan.md says correctly. Suggest "the nearest value that kept
   the suite green".
3. The pick between passing values is close to a tie: summed points over 65 %, 1.1 = 23+23+35+24 = 105 and 1.3 = 23+29+30+24 = 106.
   1.3 has the smaller worst cell (95 % v 100 %) and the closer seats (6 / 6 points v 0 / 11). The report and pages don't say what
   "nearest" measured. That is fine for a fallback, but worth one clause when the owner revisits.

## Notes
None of these changes a number in the data. Item 1 may be a deliberate faction choice; if so, a short note on each page is enough.
