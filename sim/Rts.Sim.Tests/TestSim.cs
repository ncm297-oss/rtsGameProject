using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>Shared sim setup for tests: the shipped <c>game/data</c>, loaded once per test run.</summary>
public static class TestSim
{
    private static readonly Lazy<GameData> s_data = new(() =>
    {
        DataLoadResult result = DataLoader.LoadAll(TestDataDir.Shipped);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    });

    /// <summary>The shipped data (cached; GameData is immutable, so sharing it is safe).</summary>
    public static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_withoutBuildingRequires = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach (string faction in Directory.GetDirectories(dir.FullPath("factions")))
            dir.EditJson($"factions/{System.IO.Path.GetFileName(faction)}/buildings.json", root =>
            {
                foreach (JsonNode? b in root["buildings"]!.AsArray()) b!["requires"] = new JsonArray();
            });
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    });

    /// <summary>
    /// The shipped data with every building's <c>requires</c> cleared (BUG-0112): for geometry oracles (the seal flood,
    /// never-seal) that place every type anywhere, where a D3 gate answering <c>Requires</c> first would starve them.
    /// Every id is the same as in <see cref="Data"/> (ids follow the string ids).
    /// </summary>
    public static GameData DataWithoutBuildingRequires => s_withoutBuildingRequires.Value;

    /// <summary>Number of unit types in the shipped data; valid type ids are 0 to this minus 1.</summary>
    public static int UnitTypeCount => Data.Units.Length;

    /// <summary>A <see cref="SimConfig"/> carrying the shipped data.</summary>
    public static SimConfig Config(ulong Seed, int PlayerCount, int UnitCapacity, int CommandCapacity) =>
        new(Seed, PlayerCount, UnitCapacity, CommandCapacity) { Data = Data };

    /// <summary>
    /// <see cref="Config"/> with combat off (<see cref="SimConfig.Combat"/>, BUG-0135): for the pre-M4 movement, economy,
    /// production and view scenes whose two owners stand for "enemies are walls", not for a fight.
    /// </summary>
    public static SimConfig ConfigNoCombat(ulong Seed, int PlayerCount, int UnitCapacity, int CommandCapacity) =>
        Config(Seed, PlayerCount, UnitCapacity, CommandCapacity) with { Combat = false };

    /// <summary>A <see cref="SimConfig"/> carrying <see cref="DataWithoutBuildingRequires"/>.</summary>
    public static SimConfig ConfigWithoutBuildingRequires(ulong Seed, int PlayerCount, int UnitCapacity, int CommandCapacity) =>
        new(Seed, PlayerCount, UnitCapacity, CommandCapacity) { Data = DataWithoutBuildingRequires };

    /// <summary>
    /// M4-3b: every player has explored the whole map (<c>FogStore.ExploreAllForTests</c>), so a building may be placed on
    /// any cell the other rules allow. For scenes about another placement rule that build far from their workers; returns
    /// <paramref name="sim"/>. Hand-made maps only: a replay can't rebuild the explored bits.
    /// </summary>
    public static Simulation Explored(Simulation sim)
    {
        sim.World.Fog.ExploreAllForTests();
        return sim;
    }

    /// <summary>
    /// M5-4e: keeps <paramref name="player"/> in for a scene about something else. Since the new defeat rule a player that
    /// has built is out once it has no live unit and no finished unit-producing building, so a scene whose player holds
    /// only Houses, depots, a Forge, towers or sites (and no unit yet) would see its later commands dropped. This puts one
    /// summoned worker of the player's faction, which never expires and takes no population, on the passable cell nearest
    /// the map's far corner (the store seam: no tick runs, and a replay can't carry it). Returns its handle.
    /// <para>
    /// Side effects a scene should know about (BUG-0704): the keeper is a live summon for the whole scene, so
    /// <c>SummonSystem</c> scans the unit slots every tick, and it gives its player sight around that corner. Keep it out of
    /// fog and perf scenes whose numbers would change. Two players kept in a combat scene must not share a corner (they
    /// would fight): pass <paramref name="nearCorner"/> for one of them to use the passable cell nearest cell (0, 0).
    /// </para>
    /// <para>
    /// BUG-0763: the keeper is a summon, and a summoned unit can't be stealthed (<c>World.IsStealthed</c> /
    /// <c>World.IsDetectedBy</c> stay false whatever its statuses), so it is always seen by every player. A stealth scene or
    /// oracle that adds one must model that exemption or stage its towers after its units so no keeper is needed.
    /// </para>
    /// </summary>
    public static Rts.Sim.Entities.EntityHandle KeepIn(Simulation sim, int player, bool nearCorner = false) =>
        KeepInAt(sim, player, nearCorner ? 0 : 3);

    /// <summary>
    /// M5-H2 (standing rule 24, BUG-0703): keeps <paramref name="player"/> in (<see cref="KeepInAt"/>, corner player mod 4)
    /// when a building of <paramref name="type"/> (a <paramref name="site"/> or not) about to be staged would leave it out on
    /// the next tick: it has no live unit, no finished unit-producing building, and this one won't be one either.
    /// </summary>
    public static void KeepInFor(Simulation sim, int player, int type, bool site = false)
    {
        World w = sim.World;
        if (w.LiveUnits(player) > 0 || w.ProductionBuildings(player) > 0) return;
        if (!site && (uint)type < (uint)w.Data.Buildings.Length && !w.Data.UnitsTrainedAt(type).IsEmpty) return;
        KeepInAt(sim, player, player % 4);
    }

    /// <summary>
    /// <see cref="KeepIn"/> at map corner <paramref name="corner"/> (bit 0: the high-x side, bit 1: the high-y side; 0 is cell
    /// (0, 0), 3 the far corner), so up to four players kept in a scene don't share a cell (M5-H2).
    /// </summary>
    public static Rts.Sim.Entities.EntityHandle KeepInAt(Simulation sim, int player, int corner)
    {
        World w = sim.World;
        Map.NavGrid g = w.NavGrid;
        int worker = Rts.Sim.ViewApi.StartBase.UnitOfSlot(w.Data, w.FactionOf(player), UnitSlot.Worker);
        for (int dy = 0; dy < g.Height; dy++)
            for (int dx = 0; dx < g.Width; dx++)
            {
                int x = (corner & 1) != 0 ? g.Width - 1 - dx : dx, y = (corner & 2) != 0 ? g.Height - 1 - dy : dy;
                if (!g.IsPassable(x, y)) continue;
                Assert.True(w.Units.TrySpawn(player, worker, w.Data.Units[worker], g.CellCenter(x, y), int.MaxValue, out Rts.Sim.Entities.EntityHandle h));
                return h;
            }
        throw new InvalidOperationException("no passable cell");
    }
}
