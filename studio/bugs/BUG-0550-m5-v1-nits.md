# BUG-0550: M5-V1 nits: a disc pool grows on key churn though no frame needs more nodes; the lost-cast disc hides under the crowd that caused it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-11-0314, task M5-V1 (QA) |
| System | view: `game/scripts/GroundDiscPool.cs`, `game/scripts/AbilityViews.cs` (`SyncCues`) |
| Fixed by | |

## Item 1: `GroundDiscPool` grows when a frame swaps its keys
`Take` grows the pool when the free stack is empty, but nodes held by keys that this frame won't take are only
freed in `End`. So a frame that takes 16 keys followed by a frame that takes 16 *other* keys makes 32 nodes (one
`Array.Resize` x 4 and 16 new `GroundDisc` nodes, after warm-up) although no frame ever draws more than 16.
docs/03 "Implementation (M5-V1)" says a pool "doubles only when a frame needs more than ever before".

Repro: `game/tests/QaM5V1Test.tscn` (headless), the pool churn row prints
`pool churn: 16 keys -> 16 nodes; 16 other keys next frame -> 32 nodes (grew 1); 10 more alternations -> 32`.
In play it needs more than 16 own casts (or 16 flashes) to end and as many others to start in one frame, so the cost
is one growth step, bounded by the key count, and never repeats.

Fix idea: in `Take`, when the free stack is empty, first reclaim nodes whose key was not taken last frame and has not
been taken yet this frame (or free stale nodes in `Begin` and let a key re-take its old node if it is still free);
or correct the docs sentence.

## Item 2: the red cue disc is under the units that blocked the walk-in
The cue disc is the caster's body radius + 0.9 m (`CueMargin`), drawn 0.14 m over the ground. A walk-in is dropped
mostly when it is stuck in a crowd, so the disc lies under the crowd: in the developer's still
`build/shots/m5-v1/ability-seed1-cast-lost.png` only a few red pixels show between the eight Raiders; the "Cast lost"
words carry the cue. Without the HUD (`--no-hud`) there are no words, so the cue is the sound and those few pixels.
QA's own windowed stills from `game/tests/QaM5V1Test.tscn -- --shots <dir>` (12 ringed mages, zoom 30):
`build/shots/qa-m5v1/qa-m5v1-seed1-hud-cues.png` (the words read; the discs barely) and
`build/shots/qa-m5v1/qa-m5v1-seed6-nohud-cues.png` (no words: a red sliver at each knot is all that shows). The
words sit in world space, so near the bottom of the screen they show through the translucent selection panel and
command card, as units do. Taste call for the M6 art pass (a disc wider than the crowd, a tint on the caster's own mesh, or an above-ground mark).

## Expected
1. A pool makes a node only when a frame draws more discs than any frame before. 2. The red flash on the caster reads
at RTS zoom when the caster is surrounded.

## Update (2026-10-13-0527 ACCEPT, Producer)
Item 1 **fixed** in M6-VH1 (650d423): `GroundDiscPool.Take` reclaims a node whose key hasn't been taken this frame before it grows (`QaM5V1Test` pool churn: 16 keys for 16 others stays at 16 nodes). Side effect filed as BUG-0662 (S3: the reclaim moves stable keys, so their meshes are rebuilt). Item 2 (the lost-cast disc under the crowd) stays open for the M6 art pass, so the bug stays open.
