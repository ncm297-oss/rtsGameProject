using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-9 (Request 59): the sim's own forged Teblor <c>standard</c> build orders for the per-difficulty rows. Rule 58: every
/// order is built from D34's <c>buildOrder</c> (M8-4: the fixture, <see cref="TeblorSwitchOrders.D34Order"/>; was the shipped
/// file's) by finding steps by action and id, and every forge looks the opening up
/// by id and writes (or replaces) its <c>buildOrder</c> and <c>buildOrders</c>, so a shipped file that already carries
/// <c>buildOrders</c> (D38) gives the same copies.
/// <list type="bullet">
/// <item><b>A</b>: the shipped <c>buildOrder</c> with every reserve switch taken out.</item>
/// <item><b>B</b>: A with four differences, so each rule that indexes the steps runs on a list of its own: the opening
/// worker step trains three more (due at tick 0), the War Lodge step moves to the front at time 0 (the rebuild rule's
/// newest-done index moves), <c>throughReserve</c> on the Destrier Rider, War-dog and Shaman train steps and
/// <c>noReserve</c> on the Breakers' Hall step, and the expand step moves to just after the Age II research.</item>
/// <item><b>C</b>: B plus one Hut step at 15:00 (one step longer: the step arrays differ in length from A's and B's).</item>
/// </list>
/// </summary>
internal static class PerDifficultyOrders
{
    internal const string Faction = "teblor";
    internal const string Worker = "teblor_gatherer";
    internal const string Lodge = "teblor_war_lodge";
    internal const string BreakersHall = "teblor_breakers_hall";

    // M8-4 (BUG-1771): built on D34's order (the fixture), not the shipped file's, so A-C and the figures measured on them stay
    // put when the data track ships another order (D38); while the shipped order is D34's they are the lists they were.
    private static readonly Lazy<JsonArray> s_a = new(() =>
    {
        JsonArray order = TeblorSwitchOrders.D34Order();
        foreach (JsonNode? s in order)
        {
            s!.AsObject().Remove("throughReserve");
            s.AsObject().Remove("noReserve");
        }
        return order;
    });

    /// <summary>Order A (a fresh copy).</summary>
    internal static JsonArray A() => (JsonArray)s_a.Value.DeepClone();

    /// <summary>Order B (a fresh copy; see the type's summary).</summary>
    internal static JsonArray B()
    {
        JsonArray order = A();
        JsonNode worker = Find(order, s => Do(s, "train", Worker) && (double?)s["at"]!["time"] == 0);
        worker["count"] = (int)worker["count"]! + 3;
        JsonNode lodge = Find(order, s => Do(s, "build", Lodge));
        order.Remove(lodge);
        lodge["at"] = new JsonObject { ["time"] = 0 };
        order.Insert(0, lodge);
        foreach (string unit in TeblorSwitchOrders.ThroughTrains)
            Find(order, s => Do(s, "train", unit))["throughReserve"] = true;
        Find(order, s => Do(s, "build", BreakersHall))["noReserve"] = true;
        JsonNode expand = Find(order, s => (string?)s["do"] == "expand");
        order.Remove(expand);
        order.Insert(order.IndexOf(Find(order, s => Do(s, "research", "age_ii"))) + 1, expand);
        return order;
    }

    /// <summary>Order C (a fresh copy; see the type's summary).</summary>
    internal static JsonArray C()
    {
        JsonArray order = B();
        order.Add(new JsonObject { ["at"] = new JsonObject { ["time"] = 900 }, ["do"] = "build", ["id"] = "teblor_hut" });
        return order;
    }

    private static bool Do(JsonNode s, string what, string id) => (string?)s["do"] == what && (string?)s["id"] == id;

    private static JsonNode Find(JsonArray order, Func<JsonNode, bool> match)
    {
        foreach (JsonNode? s in order)
            if (match(s!)) return s!;
        throw new InvalidOperationException("a step the per-difficulty orders need is not in the Teblor standard opening");
    }

    /// <summary>
    /// Writes <paramref name="order"/> as the opening's <c>buildOrder</c> and <paramref name="entries"/> as its <c>buildOrders</c>
    /// (the field taken out with none), on <paramref name="opening"/> whatever it carried.
    /// </summary>
    internal static void Write(JsonObject opening, JsonArray order, params (AiDifficulty Difficulty, JsonArray Steps)[] entries)
    {
        AiScenes.SetBuildOrder(opening, order);
        opening.Remove("buildOrders");
        if (entries.Length == 0) return;
        var map = new JsonObject();
        foreach ((AiDifficulty d, JsonArray steps) in entries) map[DataLimits.AiDifficultyIds[(int)d]] = steps;
        opening["buildOrders"] = map;
    }

    /// <summary>A copy of the shipped data whose Teblor <c>standard</c> opening plays <paramref name="order"/>, with <paramref name="entries"/> per difficulty.</summary>
    internal static GameData Data(JsonArray order, params (AiDifficulty Difficulty, JsonArray Steps)[] entries) =>
        AiScenes.DataWithAi(Faction, root => Write(TeblorSwitchOrders.Standard(root), order, entries));

    /// <summary>The Teblor <c>standard</c> opening of <paramref name="data"/>.</summary>
    internal static AiOpening Standard(GameData data)
    {
        AiDef ai = data.Ai[data.FindFaction(Faction)];
        return ai.Openings[ai.FindOpening("standard")];
    }
}
