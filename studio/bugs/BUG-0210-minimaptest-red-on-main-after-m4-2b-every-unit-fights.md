# BUG-0210: `MinimapTest.tscn` is red on `main` since M4-2b: 39 of 40 units Moving 3 s after the minimap order (a pre-combat 2,000-unit scene now has archers shooting)

| Field | Value |
| --- | --- |
| Severity | S2 (`main` has a red Godot scene test; it blocks the view branch's merge) |
| Status | open |
| Found | 2026-10-08-0913, integration (the conductor's post-merge scene loop; reproduced on `main` at 941a35a with no view code) |
| System | view scene test `game/tests/MinimapTest.cs` (M2-4) on top of sim M4-2b (`CanFight` = attack > 0, projectiles); filed by the Producer |
| Fixed by | |

## Repro
1. On `main` at 941a35a (sim M4-2b merged): `powershell -File tools/qa/scene-loop.ps1` → 30 / 31, `MinimapTest` FAIL:
   `39 of 40 Moving 3 s after the minimap order`.
2. The scene starts the real `Match` with `--units 1000 --no-bases` (2,000 units, both armies, combat **on**: it was
   not among BUG-0147's five scenes that got `--no-combat`, because at M4-1 only melee units fought and the start blocks
   are out of melee reach), selects the first 40 own units, right-clicks the minimap and expects all 40 `Moving` 3 s
   later with the minimap point as goal.

## Expected
`main` green: the scene loop 31 / 31 (the M4-V2 branch had 31 / 31 before the merge; view QA ran it).

## Actual
One of the 40 is not `Moving` after 3 s on the M4-2b sim. Not diagnosed yet. Most likely: since M4-2b every unit fights,
so the enemy army's archers / crossbowmen (range 14-15 m, well past melee reach) now shoot the nearest units of a start
block, and a selected unit is killed (a freed slot is not `Moving`) or otherwise taken out of its walk within the 3 s.
A unit on a plain Move ignores enemies by the M4-1 rule, so if the unit is alive and not `Moving` with its goal intact,
that is a sim regression instead: check `u.Alive` and `u.Hp` of the failing slot first.

## Fix (view track, first item of its next session; a few lines)
- As BUG-0147: run `MinimapTest` with `--no-combat` (it is a walking-bound M2 scene whose expectations are about armies
  that never fight), or spawn the two armies apart; keep every `Check` as is. Print the failing slot's `Alive` / `Hp` /
  `State` once to confirm the cause; if the unit is alive and stopped on a plain Move, refile against the sim (S2).
- Then merge `origin/main` into `studio/2026-10-08-0913-view`, scene loop 31 / 31, and the conductor merges the branch.

## Process note
The sim track's QA ran smoke only; `tools/qa/scene-loop.ps1` (new this session, QA) would have caught this on the sim
branch. From now on a sim task that changes unit behaviour (combat, movement, economy, orders) runs the scene loop in QA
and the Producer runs it at ACCEPT (handoff "Watch-outs (all tracks)").
