using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// A resource node as a <see cref="PlayerView"/> answers it (M5-1; M5-2, BUG-0540): the node the player last saw in the
/// slot, alive or gone out of its sight (until it sees that ground again). What is left in it is known only while the node is
/// alive and the player sees a footprint cell.
/// </summary>
/// <param name="Handle">The node's slot and generation.</param>
/// <param name="TypeId">Its resource type (<c>GameData.Resources</c>).</param>
/// <param name="Cell">Its footprint's anchor cell.</param>
/// <param name="Remaining">What is left in it while a footprint cell is visible to the player; -1 (unknown) otherwise, and always for a remembered node that is gone.</param>
public readonly record struct SeenResource(EntityHandle Handle, int TypeId, int Cell, int Remaining);
