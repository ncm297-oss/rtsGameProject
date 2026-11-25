using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-8 (Request 55): the sim's own forged copies of the shipped Teblor <c>standard</c> opening with the reserve switches
/// (rule 58: each order sets its switches on the steps it finds by action and id in the shipped file, so a copy of data
/// that already carries them is the same copy). <see cref="Shipped"/> is the file as it is; <see cref="A"/> adds
/// <c>throughReserve</c> on the Destrier Rider, War-dog and Shaman train steps; <see cref="B"/> adds to A <c>noReserve</c>
/// on the expand step. Read by <c>Stress/ReserveSwitchReportTests</c> and <see cref="ReserveSwitchDeterminismTests"/>.
/// </summary>
internal static class TeblorSwitchOrders
{
    internal static readonly string[] ThroughTrains = { "teblor_destrier_rider", "teblor_war_dog", "teblor_shaman" };

    /// <summary>The order names in report order.</summary>
    internal static readonly string[] Names = { "shipped", "A", "B" };

    /// <summary>A copy of the shipped data whose Teblor <c>standard</c> opening carries order <paramref name="name"/> (one of <see cref="Names"/>).</summary>
    internal static GameData Data(string name) => name switch
    {
        "shipped" => TestSim.Data,
        "A" => AiScenes.DataWithAi("teblor", root => Apply(Standard(root), noReserveExpand: false)),
        "B" => AiScenes.DataWithAi("teblor", root => Apply(Standard(root), noReserveExpand: true)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>The opening with id <c>standard</c> in a Teblor ai.json root.</summary>
    internal static JsonObject Standard(JsonNode root)
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
            if ((string?)o!["id"] == "standard") return o.AsObject();
        throw new InvalidOperationException("no standard opening in the Teblor ai.json");
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
