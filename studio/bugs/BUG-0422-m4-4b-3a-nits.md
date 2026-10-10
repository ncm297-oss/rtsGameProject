# BUG-0422: M4-4b-3a nits (docs encoding, Martyrdom misses a unit trained this tick)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-1015, task M4-4b-3a |
| System | docs, unit passives |
| Fixed by | |

## Repro / items
1. **Mojibake in docs/03.** Line 2569 ("Implementation (M4-4b-3a)", Cost) reads `478 Âµs here, 482 Âµs on the base`.
   It should say `µs`, as the rest of the file does (`grep -n "Âµ" docs/03-technical-design.md`). The text was probably
   written through a codepage-unaware pipe.
2. **A Zealot trained in this tick's phase 3 is missed by a Martyrdom in the same tick.** The spatial hash is rebuilt after
   phase 1, so production spawns aren't in it. docs/03 documents this and ability resolves and splash behave the same way,
   so it's consistent. It's filed only so the view or balance pass isn't surprised by it.
3. **The memory bound got looser than the growth.** The `World_1024Map_CacheStays32_MemoryBounded` bound went from
   231,862,000 to 231,920,000 (+58,000) for +4,144 bytes measured. The rest restores the 72 KB margin that M4-4b-2 and M4-H2
   had used up. The itemisation is exact (base 231,843,112, head 231,847,256, both measured alone by QA), and the brief
   asked for a re-baseline. Noted so the Producer sees that the margin was restored rather than kept at 18.9 KB.
