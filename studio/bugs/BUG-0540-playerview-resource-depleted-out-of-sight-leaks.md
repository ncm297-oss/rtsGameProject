# BUG-0540: PlayerView drops a resource node depleted out of the player's sight at once (hidden state leaks to the AI)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-11-0314, task M5-1 |
| System | AI `PlayerView` (resource nodes) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PlayerViewQaTests.ANodeDepletedOutOfSight"`.
2. `ANodeDepletedOutOfSight_TodayLeavesTheAnswers_AtOnce` (passes, pins today's behaviour): player 0 explores a 1 x 1 node,
   walks away (the cell is explored, not visible; `TryGetResource` answers it with `Remaining` -1), the node is emptied
   (`ResourceStore.Take`) while player 0 can't see it: `TryGetResource` now answers false and `NextResource` skips it.
3. `ANodeDepletedOutOfSight_IsStillAnswered_UntilItsGroundIsSeen` (skipped with this id) is the expected behaviour.

## Expected
docs/02 "AI opponent": "The AI sees only its own fog-of-war view. It never reads hidden enemy state." A node felled or mined
out where the player can't look is hidden state (it tells the AI an enemy is gathering there). Like a building that dies
unseen (a ghost until its ground is seen), the node should stay known until a footprint cell is visible again.

## Actual
`PlayerView.TryGetResource` answers "a live node with an explored footprint cell": liveness is read from the world, not
from the player's knowledge. docs/03 "Implementation (M5-1)", Lifetime, states this on purpose ("a node felled out of sight
drops out of the answers at once"), and the brief's scope 1 said "resource nodes only on explored cells", so it is the
written rule; it still contradicts docs/02's rule. Same shape for a node spawned on explored, unseen ground (no match path
spawns nodes today).

## Producer ruling (2026-10-11-0314 ACCEPT)
A real leak, S3 stands: the human player's screen keeps a felled tree drawn until its ground is seen again
(`ViewApi.SeenResources`, M4-VH2), so the AI would know more than the player. The brief's "resource nodes only on explored
cells" was my under-specification. **Owner: the sim track, M5-2** (the economy manager is the first consumer of node
answers): per-player node memory in the fog (one last-known entry per resource slot, like `Fog.Ghosts` for buildings, or the
`SeenResources` rule moved sim-side), `PlayerView.TryGetResource` / `NextResource` answering the remembered node until a
footprint cell is visible; the skipped QA row `ANodeDepletedOutOfSight_IsStillAnswered_UntilItsGroundIsSeen` un-skipped and
the "today" row removed; docs/03 "Lifetime" reworded. M5 criterion 1 is not ticked until this is fixed.

## Notes
The M5-2 `Blackboard` can't fix it alone: it can only remember what the view told it, and the view tells it the node is
gone. A fix needs per-player node memory in the fog (as `Fog.Ghosts` for buildings) or a view rule "known nodes = nodes
seen alive at the last update that showed their ground". Producer call whether this is M5-2 work or accepted (the human
player's own screen: check whether the view removes a felled tree out of sight too).
