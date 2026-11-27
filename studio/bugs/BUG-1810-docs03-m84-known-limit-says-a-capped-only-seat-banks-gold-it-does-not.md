# BUG-1810: docs/03 M8-4 "Known limits (2)" says a seat whose composition is only the capped type banks its gold; measured, it does not

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-26-0717, task M8-4 |
| System | docs (docs/03 "Implementation (M8-4)", the data track's table) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~UnitLimitAiQaStressTests.OnlyTheCappedTypeInTheComposition_Report"`
   (QA's row: Malazan Heavy Infantry limited to 6, every Malazan opening's compositions `{ "malazan_heavy_infantry": 100 }`
   in both ages, a Malazan AI seat against a Whirlwind AI seat on a 96-cell generated map, both waving early, 12,000 ticks).

## Expected
The docs say what the seat does. docs/03 "Implementation (M8-4)", Known limits (2): "A seat whose composition is only the
capped type banks its gold at the limit (production has nothing else to pick)"; the "For the data track (M8-4)" table
repeats it ("... or the seat banks its gold at the limit").

## Actual
It does not bank. Over 12,000 ticks the seat's gold peaks at 522 (Easy), 418 (Normal), 500 (Hard) and ends at 23-36; it
reaches the limit, attacks (61 / 100 / 172 attack orders), loses Heavy Infantry (13 / 19 / 14) and trains them again
(11 / 18 / 9 Trains after its first loss), with no Train refused for the limit. The gold goes to workers, houses,
buildings and research; the population is 25-34 at tick 8,000-10,000.

```
Easy:   Heavy Trains sent 11 (11 after the first loss), losses 13, attack orders 61,  max gold 522, refused for the limit 0
Normal: Heavy Trains sent 18 (18 after the first loss), losses 19, attack orders 100, max gold 418, refused for the limit 0
Hard:   Heavy Trains sent 11 (9 after the first loss),  losses 14, attack orders 172, max gold 500, refused for the limit 0
```

## Notes
Docs only; the code does what the brief asks. The advice to the data track ("list other units beside a limited one")
is still good, since an army of seven is small, but the stated reason (banked gold) is not what happens on this scene.
Write what was measured, or say "not measured". One scene, one seed: a richer start may bank more.
