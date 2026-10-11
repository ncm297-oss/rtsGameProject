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
/// <param name="Site">
/// M4-H3 (BUG-0275 item 1): it was a construction site when last seen (<c>UnderConstruction</c>), so the view can draw a
/// remembered site as a foundation; refreshed with the rest whenever the building is seen again. View-only: no sim rule reads
/// it, so it is not hashed (hash it the day one does).
/// </param>
public readonly record struct BuildingGhost(int Generation, int TypeId, int Cell, int Owner, bool Site = false)
{
    /// <summary>True for an entry that holds a building (its generation is never 0).</summary>
    public bool Known => Generation != 0;
}
