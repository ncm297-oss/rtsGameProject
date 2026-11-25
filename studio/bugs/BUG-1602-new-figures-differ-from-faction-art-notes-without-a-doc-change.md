# BUG-1602: Three of the four new figures differ from their faction page's art notes, and neither page records it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (docs half, M6-VH9: docs/04 records the stand-ins; the faction pages are the data track's) |
| Found | 2026-11-17-0816, task M6-V25 (QA) |
| System | docs (factions), unit models (art pass) |
| Fixed by | M6-VH9 docs/04 "The figures as built are the art pass's first reading of the faction pages" |

## Repro
Compare `docs/factions/whirlwind.md` / `docs/factions/malazan.md` "Art notes" with the built figures
(`tools/asset-manifest.json` entries, the M6-V25 picture sheets, docs/04 "Fourth slice (M6-V25)").

## Expected
CLAUDE.md, definition of done 4: docs updated in the same commit when behavior or a decision changed. Or the figure follows
the page.

## Actual
- **Priest of the Whirlwind.** The page says "Priests in tall headdresses with a staff trailing ribbons". The build has a hood,
  a flat crimson veil and a box robe, with no headdress and no staff.
- **Zealot.** The page says "Zealots bare-chested, red-painted, twin knives, so they read differently from Raiders at a
  glance". The build has a team-coloured tunic (the Peasant body), crimson trousers, a head-wrap, and two copies of the
  Raider's 0.7 m curved blade. It is clothed, and it carries swords, not knives.
- **Sapper.** The Malazan page says "Sappers wear leather aprons with munition satchels and a crouched stance". The satchels
  are built. There is no apron, and the idle stance is upright, like the other figures.
- The Cadre Mage ("hooded robes, no armor") matches.

The brief itself named the props ("the priest's robe and veil, the zealot's blade"), and docs/04 lists the team parts as
"developer's pick, owner may revisit". But neither faction page says its art note is replaced or deferred. The next reader
of `whirlwind.md` will expect bare-chested Zealots.

## Notes
Polish, owner's taste. Either a line under each page's "Art notes" ("M6-V25 stand-ins: ..., revisit with the owner's packs")
or a later manifest pass: a Zealot without the body part's shirt, a priest's staff prop.

## QA verification (2026-11-24-1431, M6-VH9)
docs/04 now lists, per figure, where the build differs from `whirlwind.md` / `malazan.md` Art notes and why (Priest: hood, veil, box robe; Zealot: Peasant outfit, two Raider blades; Sapper: satchels, no apron, upright idle). The faction pages themselves are unchanged (out of the view track's files).
