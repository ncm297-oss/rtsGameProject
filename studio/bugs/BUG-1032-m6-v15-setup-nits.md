# BUG-1032: M6-V15 nits: 64 mines on a 128 map places 60-62 with no word; bug index left open; a loose ShellTest row

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-18-1814, task M6-V15 |
| System | Skirmish setup (view: `SkirmishSetup` counts), `studio/bugs/README.md`, `ShellTest.SpeedAndCounts` |
| Fixed by | M6-VH4 (2026-10-26-2108): item 1 `Match.ShortfallLine` (logged; docs/02 "Game flow" 2 says it), `QaM6V15Test.GeneratorMatrix` checks it; item 2 done by the Producer at the 1814 ACCEPT; item 3 `ShellTest.SpeedAndCounts` pins 6.5 to 7 |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V15Test.tscn -- --only gen`: the 128-map cap rows print
   `placed 64 forests / 61 mines` (2 slots), `64 / 62` (3 slots), `64 / 60` (4 slots). The 192 and 256 maps place 64 / 64.
2. `studio/bugs/README.md` index rows for BUG-1000 and BUG-1001.
3. `game/tests/ShellTest.cs` `SpeedAndCounts`, the `(6.5, 6, 6)` clamp case.

## Expected / Actual
1. The setup offers Gold mines up to 64 on every map size, but the 128 map fits only about 60 at the generator's 24 m mine
   spacing. The placer skips the rest silently (documented: "the shortfall shows in the returned counts"), so the player
   who asks for 64 gets a few less and isn't told. Every base still gets its two start mines and a start forest at every
   size, slot count and count tried. A design call, not a defect: a per-map cap or a hint is optional.
2. Both bug files say `fixed`, but the index still lists them `open` (the index is part of the fix's paperwork).
3. The row accepts 6 or 7 for a value of 6.5 ("rounds to a whole count"). Godot's SpinBox with `Rounded` gives one exact
   answer, so the row could pin it and catch a rounding change.

## Fix (M6-VH4, 2026-10-26-2108, dev)
1. A per-map cap isn't derivable without running the placer (it depends on the terrain and the seed: 60, 61, 62 at 4, 2,
   3 slots), so per the Producer's fallback a new match whose placement fell short logs
   `Map generator: placed 64 of 64 forests and 61 of 64 gold mines asked (the 128 x 128 map has no more room ...)`
   (`Match.ShortfallLine`, "" when nothing fell short), and docs/02 "Game flow" 2 says the 128 map fits about 60.
   `QaM6V15Test.GeneratorMatrix` checks the line on all 18 cases (present with the placed count on the three 128 caps, empty
   elsewhere).
2. Already done (the index rows) at the 1814 ACCEPT.
3. The row now wants exactly 7 forests / 7 mines for 6.5 (Godot's `Range` with `Rounded` and step 1 rounds half away from
   zero), in the picks, the args and the boxes' text.
