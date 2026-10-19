# BUG-1024: M6-V15 nits: 64 mines on a 128 map places 60-62 with no word; bug index left open; a loose ShellTest row

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-18-1814, task M6-V15 |
| System | Skirmish setup (view: `SkirmishSetup` counts), `studio/bugs/README.md`, `ShellTest.SpeedAndCounts` |
| Fixed by | |

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
