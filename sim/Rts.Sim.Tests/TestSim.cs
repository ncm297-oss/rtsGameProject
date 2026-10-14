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
    /// </summary>
    public static Rts.Sim.Entities.EntityHandle KeepIn(Simulation sim, int player)
    {
        World w = sim.World;
        Map.NavGrid g = w.NavGrid;
        int worker = Rts.Sim.ViewApi.StartBase.UnitOfSlot(w.Data, w.FactionOf(player), UnitSlot.Worker);
        for (int c = g.Width * g.Height - 1; c >= 0; c--)
        {
            if (!g.IsPassable(c % g.Width, c / g.Width)) continue;
            Assert.True(w.Units.TrySpawn(player, worker, w.Data.Units[worker], g.CellCenter(c % g.Width, c / g.Width), int.MaxValue, out Rts.Sim.Entities.EntityHandle h));
            return h;
        }
        throw new InvalidOperationException("no passable cell");
    }
}
