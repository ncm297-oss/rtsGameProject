using System.Numerics;

namespace Rts.Sim.ViewApi;

/// <summary>What <see cref="StartBase.Plan"/> chose for each player: a Town Hall type and anchor, and worker positions (M3-V1).</summary>
public sealed class StartBasePlan
{
    internal StartBasePlan(int players)
    {
        HallType = new int[players];
        HallAnchor = new int[players];
        WorkerType = new int[players];
        Workers = new Vector2[players][];
    }

    /// <summary>Per player, the Town Hall building type (the faction's <c>town_hall</c> slot), or -1 if the faction has none.</summary>
    public int[] HallType { get; }

    /// <summary>Per player, the Town Hall's anchor cell, or -1 when no spot fits (then no workers either).</summary>
    public int[] HallAnchor { get; }

    /// <summary>Per player, the worker unit type (the faction's <c>worker</c> slot), or -1.</summary>
    public int[] WorkerType { get; }

    /// <summary>Per player, the worker positions (meters); empty without a hall.</summary>
    public Vector2[][] Workers { get; }
}
