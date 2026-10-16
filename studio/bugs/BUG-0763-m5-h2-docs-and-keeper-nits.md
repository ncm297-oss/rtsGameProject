# BUG-0763: M5-H2 nits: two docs/03 "before / after" claims the numbers don't show; summoned keepers trip the stealth oracle

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-16-0847, task M5-H2 (QA) |
| System | docs/03 "Implementation (pathing slice 1)"; `TestSim.KeepIn`; `World.IsDetectedBy` summary |
| Fixed by | |

## Repro
1. Run `FullyQualifiedName~Surround` with detailed output on 8c39c08 and on 47ca4a2 and compare the printed lines.
2. Stage `QA/StealthQaTests.FuzzSim` with the towers placed before the units (the dev's first attempt), seeds 11 / 13.

## Expected / Actual
1. docs/03 "Reachable spots only (M5-H2, BUG-0732)": "the pocket scene's 20 Heavy Infantry take the 14 reachable spots and
   none drops the order (before: 12 struck, 8 dropped)". Measured: **12 struck on both trees**; what changed is 8 dropped
   orders -> 0 (and the attack-move pocket hold 40 ticks / 9 give-ups -> 1 tick / 0).
2. docs/03 "Two owners, one building": "12 + 8 strike, all 20 spots (before: the second owner skipped the first's claimed
   spots and walked into its strikers on the free ones)". Measured: **12 + 8 on base 8c39c08 too**. The owner layers change
   no number in that scene. The red-then-green proof is the unit test `SurroundTests.TwoOwnersFromOnePoint_*`; the prose
   should not claim a measured "before".
3. The dev's "possible bug" (StealthQaTests oracle fuzz, seeds 11 / 13, with corner keepers: "oracle says player 2 detects a
   stealthed keeper, sim says no") is **not a sim bug**. QA reproduced it (seed 13 tick 135, seed 11 tick 297: unit 23,
   player 1's keeper, detected by player 2's Watchtower at about 15.96 m per the oracle, not by the sim). `TestSim.KeepIn`
   spawns the keeper as a **summon** (expiry `int.MaxValue`, no population), and the sim exempts summons from stealth
   (`World.IsStealthed` / `HiddenFrom` false, `StealthSystem.Detect` returns early: "a summoned unit can't be stealthed"),
   so the keeper is visible to everyone and its detection bits stay clear. The test's oracle doesn't model the exemption.
   The dev's workaround (towers after the units, so no keeper is added) restores the scene as it was. Nits: `KeepIn`'s
   summary doesn't say the keeper is a summon exempt from stealth (a trap for any stealth scene that adds one), and
   `World.IsDetectedBy`'s summary ("False for a unit with no stealth, ...") doesn't list a summoned unit carrying a stealth
   status, for which it is also false while the unit is fully seen.

## Notes
Docs and test-helper comments only; no behaviour changes.
