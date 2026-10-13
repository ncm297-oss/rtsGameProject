# BUG-0662: GroundDiscPool's reclaim (BUG-0550 item 1) moves stable keys to other nodes, so their meshes are rebuilt

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-13-0527, task M6-VH1 (QA) |
| System | view: `game/scripts/GroundDiscPool.cs` (`Take` / `Reclaim`), used by `AbilityViews` cast rings, flashes, lost-cast cues |
| Fixed by | M6-V6 (442a3e6): `GroundDiscPool.Reclaim(key)` takes a lower stale key's node first, else the highest untaken key's; `AbilityViewTest.StableKeysRow` |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaVh1Test.tscn` prints
   `KNOWN BUG-0662 probe: a new key 5 in a full pool moved 3 of 3 stable keys (10-12) to other nodes (3 extra mesh rebuilds), pool 4 nodes`.
2. The probe: a pool of 4 nodes; frame 1 takes keys 10, 11, 12, 13; frame 2 takes 5, 10, 11, 12 (key order, as `AbilityViews`
   iterates unit / flash / cue slots), the discs of 10-12 at the same centres.

## Expected
The class doc (and BUG-0520): "a key keeps its node from frame to frame while it is taken, so a disc whose centre doesn't
move is never rebuilt". Keys 10-12 keep their nodes; only key 5 builds a mesh.

## Actual
`Take(5)` finds no free node (13's node is freed only at `End`), so `Reclaim` takes the first node whose key isn't taken yet
this frame: key 10's. Then `Take(10)` takes 11's, `Take(11)` takes 12's, `Take(12)` takes 13's. Every stable disc changes node
and `GroundDisc.Place` rebuilds each mesh (4 rebuilds instead of 1). Before M6-VH1 the pool grew once here and then kept every
key's node. Now it never grows past a full frame, so the cascade can repeat on any frame where a new low key arrives while the
pool is full (casters starting and finishing casts in slot order).

## Notes
Display is correct (each taker re-places and re-colours its node); the cost is terrain meshing per rebuilt disc, which
BUG-0520 removed. A possible fix without allocation: `Reclaim` only takes a node whose key is lower than the key being
taken (callers take keys in ascending order, so such a key was skipped this frame and is truly stale); otherwise grow as before.
That keeps the 16-for-16 swap at 16 nodes and leaves stable keys alone.

## QA verification (2026-10-13-0916, M6-V6)
`AbilityViewTest` PASS in the scene loop (StableKeysRow: 3 of 3 stable keys kept, 1 rebuild; then 3 of 3 below the highest, 2 rebuilds, 8 nodes); `QaVh1Test` and `QaM5V1Test` (the 16-for-16 swap, no growth) PASS. Traced against the old `Reclaim` (lowest untaken first): frame 2 of the row would move 10 -> 11 -> 12 -> 13 along, kept 0 of 3, so the row fails on the old code. Deviation from the brief ("else grows"): the highest untaken key gives its node instead, so the swap row keeps its pool size; accepted. Per-call scan cost noted in BUG-0684 item 4.
