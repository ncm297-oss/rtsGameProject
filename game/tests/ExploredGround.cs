using System;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>Scene-test spot helpers for the explored-ground placement rule (BUG-0274).</summary>
/// <remarks>
/// Since M4-3b the sim refuses a footprint touching any cell the player hasn't explored (docs/02 "Buildings",
/// <c>PlacementError.Unexplored</c>). A spot for the dev <c>SpawnBuilding</c> command, which checks no player rule, may take
/// that refusal as it takes <c>CannotAfford</c> (<see cref="IsUnexplored"/>); a spot a worker's Build or a ghost click will
/// use is scouted first (<see cref="Scout"/>), so the scenes keep the spots they picked before the rule and pass whether
/// or not the sim they run against has it yet.
/// </remarks>
public static class ExploredGround
{
    // The enum value by name: the view branch builds against a sim without it until M4-3b merges (then -1 never matches).
    private static readonly int UnexploredValue = Enum.TryParse("Unexplored", out PlacementError e) ? (int)e : -1;

    /// <summary>True when every footprint cell of <paramref name="def"/> anchored at <paramref name="anchor"/> is explored by <paramref name="player"/>.</summary>
    public static bool Footprint(World world, BuildingDef def, int anchor, int player = 0)
    {
        int w = world.NavGrid.Width, h = world.NavGrid.Height;
        if ((uint)anchor >= (uint)(w * h)) return false;
        int x0 = anchor % w, y0 = anchor / w;
        if (x0 + def.FootprintWidth > w || y0 + def.FootprintHeight > h) return false;
        for (int y = y0; y < y0 + def.FootprintHeight; y++)
            for (int x = x0; x < x0 + def.FootprintWidth; x++)
                if (!world.Fog.IsExplored(player, y * w + x)) return false;
        return true;
    }

    /// <summary>
    /// True when <paramref name="error"/> is the sim's unexplored-ground refusal. The sim checks that rule after the terrain
    /// and never-seal rules, so such a spot passes both: fine for a dev spawn.
    /// </summary>
    public static bool IsUnexplored(PlacementError error) => (int)error == UnexploredValue;

    /// <summary>
    /// Walks player 0's <paramref name="unit"/> toward the footprint's centre, ticking <paramref name="sim"/> one tick at a
    /// time, until the whole footprint is explored, then stops it there. Returns the ticks spent (0 when already explored),
    /// or -1 if it still isn't explored after <paramref name="maxTicks"/>.
    /// </summary>
    public static int Scout(Simulation sim, EntityHandle unit, BuildingDef def, int anchor, int maxTicks = 2000)
    {
        World w = sim.World;
        if (Footprint(w, def, anchor)) return 0;
        sim.Enqueue(Command.Move(0, unit, StartBase.FootprintCenter(w.NavGrid, def, anchor)));
        for (int t = 1; t <= maxTicks; t++)
        {
            sim.Tick();
            if (!Footprint(w, def, anchor)) continue;
            sim.Enqueue(Command.Stop(0, unit));
            sim.Tick();
            return t + 1;
        }
        return -1;
    }
}
