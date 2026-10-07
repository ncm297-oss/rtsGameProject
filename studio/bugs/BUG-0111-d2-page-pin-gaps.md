# BUG-0111: D2 page pins miss a few page edits (Provides free text, false "+N pop" claim); culture-dependent range text

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-06-2114, task D2 |
| System | content tests (`sim/Rts.Sim.Tests/Content/`), faction pages |
| Fixed by | 8bcca04 (D3): `BuildingContentTests.G` compares the whole Provides cell to docs/02 with names substituted and rejects any "+N pop" claim but the building's own; `UnitContentTests.G` formats ranges invariant; blank line before the pin line on both pages. QA 2026-10-07-1415: repros 1-3 now each fail `G`; `QA/Content/PagePinCultureQaTests` runs the pins under de-DE / fr-FR / tr-TR / ar-SA (and fails when `FactionPage.Num` is made culture-dependent) |

## Repro
Scratch clone of 35fd9df, one edit at a time, then
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Content|FullyQualifiedName~ReplayGolden"`:
1. `docs/factions/malazan.md` Buildings, Camp row Provides `Drop-off` -> `+8 pop, drop-off`: all pass.
2. Watchtower row Provides `Attack 10 pierce / 2 s` -> `Attack 12 pierce / 2 s`: all pass.
3. Armory row Provides `Upgrades, Moranth Supply` -> `Upgrades only`: all pass.

## Expected
The brief says the page tables become the full design source and are pinned to the data / docs/02 template.
A page row that claims population on a building with `popProvided` 0, or a tower attack that differs from
docs/02, should fail a test.

## Actual
- `BuildingContentTests.G` checks pop as `(t.Pop > 0) == provides.Contains($"+{t.Pop} pop")`. For a 0-pop
  building that looks for "+0 pop", so any other "+N pop" claim passes (repro 1).
- Forge and Watch Tower Provides text is not compared to docs/02 at all (repros 2, 3; the developer noted this).
- `UnitContentTests.G` formats the expected range with `$"{r.Range}"` (current culture) while
  `FactionPage.Num` parses invariant. All shipped ranges are integers, so it passes today; a page range like
  `7.5` would fail on a de-DE machine (`7,5`).
- Cosmetic: the new pin line in both pages sits directly under "Building stats follow the template ..." with no
  blank line, so Markdown renders the two as one paragraph.

## Notes
Cheapest fix: compare the whole Provides cell to the docs/02 cell with the faction's names substituted (as
Requires already does), and format the range with `CultureInfo.InvariantCulture`. Data track, test-only.
