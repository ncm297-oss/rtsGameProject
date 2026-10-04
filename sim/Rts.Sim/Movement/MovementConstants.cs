using Rts.Sim.Map;

namespace Rts.Sim.Movement;

/// <summary>Fixed movement geometry derived from the grid (docs/03 "Local movement"); per-unit numbers come from data.</summary>
public static class MovementConstants
{
    /// <summary>A moving unit this close to its goal (meters) has arrived: half a cell.</summary>
    public const float ArrivalDistance = MapConstants.CellSize / 2f;

    /// <summary>Flow fields built per tick at most; units whose field isn't cached past that wait a tick (tick-cost cap, docs/03).</summary>
    public const int MaxFieldBuildsPerTick = 1;
}
