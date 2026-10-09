namespace Rts.Sim.Vision;

/// <summary>
/// One entry of a player's "last known buildings" list (M4-3b, docs/02 "Vision and fog of war": enemy buildings seen once
/// stay as ghosts in explored fog until the cell is seen again). The list is indexed by building slot
/// (<see cref="FogStore.Ghosts"/>); an entry with <see cref="Generation"/> 0 is empty.
/// </summary>
/// <param name="Generation">The building's generation when last seen (with the slot index, its handle); 0 for an empty entry.</param>
/// <param name="TypeId">Its building type (<see cref="Data.GameData.Buildings"/>) when last seen.</param>
/// <param name="Cell">Its anchor cell (the footprint's lowest x, y; <c>y * Width + x</c>) when last seen.</param>
/// <param name="Owner">Its owner.</param>
public readonly record struct BuildingGhost(int Generation, int TypeId, int Cell, int Owner)
{
    /// <summary>True for an entry that holds a building (its generation is never 0).</summary>
    public bool Known => Generation != 0;
}
