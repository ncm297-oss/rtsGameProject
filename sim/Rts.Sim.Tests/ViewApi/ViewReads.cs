using System.Numerics;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>Test shorthand: the M3-V1 view reads fed from a <see cref="World"/>, as the view feeds them (ViewApi itself never names the world).</summary>
internal static class ViewReads
{
    public static int NodeAt(World w, int cell) =>
        ResourcePicker.NodeAt(w.NavGrid, w.Data.Resources, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, cell);

    public static int NodeAtPoint(World w, Vector2 point) =>
        ResourcePicker.NodeAtPoint(w.NavGrid, w.Data.Resources, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, point);

    public static BuildingBarKind Bar(World w, int slot, out float fill) => BuildingBars.Of(w.Buildings, w.Data.Buildings, slot, out fill);

    public static StartBasePlan Plan(World w, Vector2[][] blocks, int workers, float maxRadius)
    {
        var factions = new int[blocks.Length];
        for (int p = 0; p < factions.Length; p++) factions[p] = w.FactionOf(p);
        return StartBase.Plan(w.NavGrid, w.Data, w.Buildings, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, factions, blocks, workers, maxRadius);
    }

    public static bool IsHallSpot(World w, int type, int anchor, int level, bool[]? taken, int minX = 0, int maxX = int.MaxValue) =>
        StartBase.IsHallSpot(w.NavGrid, w.Buildings, w.Data.Buildings, type, anchor, level, taken, minX, maxX);

    public static int HallSpot(World w, int type, Vector2 near, int level, bool[]? taken) =>
        StartBase.HallSpot(w.NavGrid, w.Buildings, w.Data.Buildings, type, near, level, taken);

    public static bool NearestMine(World w, Vector2 from, out Vector2 center) =>
        StartBase.NearestMine(w.NavGrid, w.Data.Resources, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, from, out center);
}
