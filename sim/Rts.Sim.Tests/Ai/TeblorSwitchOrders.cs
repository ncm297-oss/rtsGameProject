using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-8 (Request 55): the sim's own forged copies of the shipped Teblor <c>standard</c> opening with the reserve switches
/// (rule 58: each order sets its switches on the steps it finds by action and id in the shipped file, so a copy of data
/// that already carries them is the same copy). "none" is the file with every switch taken out (M7-H3: the baseline a row
/// that means "no switch" reads, whatever the data track ships); "shipped" is the file as it is; "A" adds
/// <c>throughReserve</c> on the Destrier Rider, War-dog and Shaman train steps; "B" adds to A <c>noReserve</c>
/// on the expand step. Read by <c>Stress/ReserveSwitchReportTests</c> and <see cref="ReserveSwitchDeterminismTests"/>.
/// M8-4 (BUG-1771, rule 79): "none", A and B are built on D34's order (<see cref="D34Order"/>, the order M7-8 measured), written
/// with <see cref="AiScenes.SetBuildOrder"/> so the shipped file's <c>buildOrders</c> (D38) are taken out and every seat plays
/// the forged list; while the shipped <c>buildOrder</c> is D34's (<c>Content/TeblorOpeningFixtureTests</c>) the copies are
/// the ones they were.
/// </summary>
internal static class TeblorSwitchOrders
{
    internal static readonly string[] ThroughTrains = { "teblor_destrier_rider", "teblor_war_dog", "teblor_shaman" };

    /// <summary>The order names in report order.</summary>
    internal static readonly string[] Names = { "none", "shipped", "A", "B" };

    /// <summary>A copy of the shipped data whose Teblor <c>standard</c> opening carries order <paramref name="name"/> (one of <see cref="Names"/>).</summary>
    internal static GameData Data(string name) => name switch
    {
        "none" => AiScenes.DataWithAi("teblor", root => RemoveSwitches(OnD34Order(root))),
        "shipped" => TestSim.Data,
        "A" => AiScenes.DataWithAi("teblor", root => Apply(OnD34Order(root), noReserveExpand: false)),
        "B" => AiScenes.DataWithAi("teblor", root => Apply(OnD34Order(root), noReserveExpand: true)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>
    /// M8-4: the <c>buildOrder</c> of <c>Content/Fixtures/teblor_standard_d34.json</c> (read only; a fresh copy), the Teblor
    /// order the M7-8 and M7-9 helpers were measured on, so their forged copies do not move when the shipped order does.
    /// </summary>
    internal static JsonArray D34Order() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", "teblor_standard_d34.json")))!
            ["buildOrder"]!.AsArray().DeepClone().AsArray();

    /// <summary>M8-4: the <c>standard</c> opening of a Teblor ai.json root with D34's order written on it (its <c>buildOrders</c> taken out).</summary>
    internal static JsonObject OnD34Order(JsonNode root)
    {
        JsonObject standard = Standard(root);
        AiScenes.SetBuildOrder(standard, D34Order());
        return standard;
    }

    /// <summary>The opening with id <c>standard</c> in a Teblor ai.json root.</summary>
    internal static JsonObject Standard(JsonNode root)
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
            if ((string?)o!["id"] == "standard") return o.AsObject();
        throw new InvalidOperationException("no standard opening in the Teblor ai.json");
    }

    /// <summary>M7-H3: takes every <c>throughReserve</c> / <c>noReserve</c> off <paramref name="opening"/>'s steps; how many were there.</summary>
    internal static int RemoveSwitches(JsonObject opening)
    {
        int removed = 0;
        foreach (JsonNode? s in opening["buildOrder"]!.AsArray())
            foreach (string field in new[] { "throughReserve", "noReserve" })
                if (s!.AsObject().Remove(field)) removed++;
        return removed;
    }

    /// <summary>Sets order A's switches (and with <paramref name="noReserveExpand"/> B's) on <paramref name="opening"/>; every named step must be there.</summary>
    internal static void Apply(JsonObject opening, bool noReserveExpand)
    {
        JsonArray order = opening["buildOrder"]!.AsArray();
        foreach (string unit in ThroughTrains)
        {
            int set = 0;
            foreach (JsonNode? s in order)
                if ((string?)s!["do"] == "train" && (string?)s["id"] == unit)
                {
                    s["throughReserve"] = true;
                    set++;
                }
            Assert.True(set > 0, $"no train step for {unit} in the Teblor standard opening");
        }
        if (!noReserveExpand) return;
        int expands = 0;
        foreach (JsonNode? s in order)
            if ((string?)s!["do"] == "expand")
            {
                s["noReserve"] = true;
                expands++;
            }
        Assert.True(expands > 0, "no expand step in the Teblor standard opening");
    }

    /// <summary>
    /// A two-seat match on <paramref name="map"/> with <paramref name="seats"/> on <paramref name="data"/>
    /// (<c>HuntStressTests.Match</c>'s set-up: the map, 400 units, each base by command).
    /// </summary>
    internal static Simulation Match(GameData data, string map, PlayerSeat[] seats, ulong seed)
    {
        if (map == "generated") return AiScenes.Generated(seats, seed, false, out _, data: data);
        int size = data.Maps[data.FindMap(map)].Size;
        return AiScenes.Generated(seats, seed, false, out _, data: data,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }

    /// <summary>The Teblor mirror: both seats Teblor AIs at <paramref name="difficulty"/>.</summary>
    internal static PlayerSeat[] Mirror(GameData data, AiDifficulty difficulty)
    {
        int teblor = data.FindFaction("teblor");
        return new[] { new PlayerSeat(teblor, SeatController.Ai, difficulty), new PlayerSeat(teblor, SeatController.Ai, difficulty) };
    }
}
