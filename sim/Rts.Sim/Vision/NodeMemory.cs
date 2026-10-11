namespace Rts.Sim.Vision;

/// <summary>
/// One entry of a player's resource-node memory (M5-2, BUG-0540): the node the player last saw in a resource slot, kept after
/// the node is gone until a cell of its footprint is visible again, as <see cref="BuildingGhost"/> is for an enemy building.
/// The list is indexed by resource slot (<see cref="FogStore.NodeAt"/>); an entry with <see cref="Generation"/> 0 is empty.
/// </summary>
/// <param name="Generation">The node's generation when last seen (with the slot index, its handle); 0 for an empty entry.</param>
/// <param name="TypeId">Its resource type (<see cref="Data.GameData.Resources"/>).</param>
/// <param name="Cell">Its footprint's anchor cell.</param>
internal readonly record struct NodeMemory(int Generation, int TypeId, int Cell)
{
    /// <summary>True for an entry that holds a node (a live node's generation is never 0).</summary>
    public bool Known => Generation != 0;
}
