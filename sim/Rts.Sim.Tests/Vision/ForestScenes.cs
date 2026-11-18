using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-1 test copies: the shipped data plus a Malazan <c>forestStealth</c> (the block docs/03 "For the data track (M8-1)" gives
/// the Shadow: <c>stealthed</c>, 3 s, siege excepted; or 5 s), the M4-5 test-only stealth sources (<see cref="StealthTests.AddTo"/>:
/// the Heavy Infantry's 60 s <c>zz_test_stealth</c> and the Malazan Laborer's 10 m detector), and a Whirlwind test zone
/// <c>zz_test_fire</c> (Burning 2 a second while inside, 1 s after) on the Priest. No shipped file changes.
/// </summary>
internal static class ForestScenes
{
    /// <summary>The block on the Malazan faction: 3 s, siege excepted.</summary>
    internal static GameData Data3 => s_data3.Value;

    /// <summary>The block with a 5 s delay (the Shadow page's first balance fix).</summary>
    internal static GameData Data5 => s_data5.Value;

    private static readonly Lazy<GameData> s_data3 = new(() => DataWith(3));
    private static readonly Lazy<GameData> s_data5 = new(() => DataWith(5));

    /// <summary>The block as a JSON object.</summary>
    internal static JsonObject Block(double delay) => new() { ["status"] = "stealthed", ["delay"] = delay, ["exceptSlots"] = new JsonArray("siege") };

    /// <summary>
    /// The test copy with the Malazan block at <paramref name="delay"/> seconds (see the type's summary), then
    /// <paramref name="also"/>'s edits (M8-2: the save-bytes tests add a field to it).
    /// </summary>
    internal static GameData DataWith(double delay, Action<TestDataDir>? also = null) => AiScenes.DataWith(dir =>
    {
        also?.Invoke(dir);
        dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = Block(delay));
        StealthTests.AddTo(dir);
        dir.EditJson("factions/whirlwind/abilities.json", root => root["abilities"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "zz_test_fire", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "targetGround", ["range"] = 18,
            ["radius"] = 3, ["castTime"] = 0, ["cooldown"] = 1, ["duration"] = 1, ["affects"] = "enemy_units", ["autocast"] = false,
            ["effects"] = new JsonArray(new JsonObject
            {
                ["kind"] = "createZone", ["blocksVision"] = false,
                ["statuses"] = new JsonArray(new JsonObject { ["status"] = "burning", ["magnitude"] = 2, ["duration"] = 1 }),
            }),
        }));
        dir.SetUnitField("whirlwind", "whirlwind_priest", "abilities", "[\"sandstorm\", \"zz_test_fire\"]");
    });

    /// <summary>A two-player flat sim (player 0 Malazan with the forest stealth, player 1 Whirlwind) on <paramref name="data"/>.</summary>
    internal static Simulation Scene(GameData data, int units = 64, bool combat = true, int size = 48) =>
        new(new SimConfig(1, 2, units, 8 * units + 32) { Data = data, Combat = combat }, LocalMovementTests.Flat(size));

    /// <summary>A tree on cell (<paramref name="x"/>, <paramref name="y"/>) holding <paramref name="wood"/> (the rules' tree wood by default).</summary>
    internal static EntityHandle Tree(Simulation sim, int x, int y, int wood = 0)
    {
        World w = sim.World;
        Assert.True(w.Resources.Spawn(w.Data.FindResource("tree"), y * w.NavGrid.Width + x, wood > 0 ? wood : w.Data.Rules.TreeWood, out EntityHandle h));
        return h;
    }

    /// <summary>The status id the block holds.</summary>
    internal static int Stealthed(GameData d) => d.FindStatus("stealthed");

    /// <summary>Whether the forest holds unit <paramref name="h"/>'s stealth.</summary>
    internal static bool Held(Simulation sim, EntityHandle h) => sim.World.Units.Forest!.Held[h.Index] != 0;

    /// <summary>The ticks the timed applications left on <paramref name="h"/>'s Stealthed entry (the held tick taken off), or -1 with no entry.</summary>
    internal static int OwnTicks(Simulation sim, EntityHandle h)
    {
        var s = sim.World.Units.Statuses;
        int at = s.IndexOf(h.Index, Stealthed(sim.World.Data));
        return at < 0 ? -1 : s.TicksRemaining[at] - (Held(sim, h) ? 1 : 0);
    }

    /// <summary>Whether unit <paramref name="h"/> qualified for the forest by position in the last tick's phase 5 (its position then: <see cref="UnitStore.PrevPosition"/>).</summary>
    internal static bool WasBesideTree(Simulation sim, EntityHandle h) =>
        Abilities.ForestStealthSystem.BesideTree(sim.World, sim.World.Units.PrevPosition[h.Index]);

    internal static void Ticks(Simulation sim, int n)
    {
        for (int t = 0; t < n; t++) sim.Tick();
    }

    /// <summary>The cell centre of (x, y) offset by (<paramref name="dx"/>, <paramref name="dy"/>) m.</summary>
    internal static Vector2 At(Simulation sim, int x, int y, float dx = 0f, float dy = 0f) => CombatScenes.At(sim, x, y, dx, dy);
}
