using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// A resource node as a <see cref="PlayerView"/> answers it (M5-1): a live node with a footprint cell the player has
/// explored. What is left in it is known only while the player sees a footprint cell.
/// </summary>
/// <param name="Handle">The node's slot and generation.</param>
/// <param name="TypeId">Its resource type (<c>GameData.Resources</c>).</param>
/// <param name="Cell">Its footprint's anchor cell.</param>
/// <param name="Remaining">What is left in it while a footprint cell is visible to the player; -1 (unknown) otherwise.</param>
public readonly record struct SeenResource(EntityHandle Handle, int TypeId, int Cell, int Remaining);
