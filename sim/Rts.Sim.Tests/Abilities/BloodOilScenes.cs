using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-4: the Teblor's Blood-oil Frenzy as the data track will write it, on a test copy of the shipped data (no shipped file
/// uses the M7-4 fields): a frenzied-kind status <c>teblor_frenzied</c> with <c>damageBonus</c> 0.2 and an <c>onEnd</c> of
/// Slowed 0.2 for 4 s; the Shaman's <c>blood_oil_frenzy</c> applying it at +40 % for 10 s; Blood-oil Anointing's
/// <c>statusDuration</c> +4 s on it; and a building <c>scale</c> of 1.25 on the Longhouse. The numbers are docs/factions/teblor.md's.
/// </summary>
internal static class BloodOilScenes
{
    /// <summary>The status id the copy adds (it sorts after <c>stealthed</c>, so the M4 statuses keep their state-hash numbers).</summary>
    public const string Status = "teblor_frenzied";

    /// <summary>The frenzied row as the docs/03 "For the data track (M7-4)" block gives it.</summary>
    public static JsonObject StatusRow() => new()
    {
        ["id"] = Status, ["displayName"] = "Blood-oil Frenzy", ["description"] = "Test only.", ["kind"] = "frenzied",
        ["damageBonus"] = 0.2,
        ["onEnd"] = new JsonObject { ["status"] = "slowed", ["magnitude"] = 0.2, ["duration"] = 4 },
    };

    /// <summary>Applies the four M7-4 edits to a copy of the shipped data.</summary>
    public static void Edit(TestDataDir dir)
    {
        dir.EditJson("common/statuses.json", r => r["statuses"]!.AsArray().Add(StatusRow()));
        dir.EditJson("factions/teblor/abilities.json", r => r["abilities"]![0]!["effects"]![0]!["status"] = Status);
        dir.EditJson("factions/teblor/techs.json", r => r["techs"]![0]!["effects"]!.AsArray().Add(new JsonObject
        {
            ["stat"] = "statusDuration", ["amount"] = 4, ["statuses"] = new JsonArray(Status),
        }));
        dir.EditJson("factions/teblor/buildings.json", r =>
        {
            foreach (JsonNode? b in r["buildings"]!.AsArray())
                if ((string)b!["id"]! == "teblor_longhouse") b["scale"] = 1.25;
        });
    }

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(Edit));

    /// <summary>The shipped data with <see cref="Edit"/> applied.</summary>
    public static GameData Data => s_data.Value;

    /// <summary>The Frenzy status's id in <see cref="Data"/>.</summary>
    public static int Frenzy => Data.FindStatus(Status);

    /// <summary>Slowed's id in <see cref="Data"/>.</summary>
    public static int Slowed => Data.FindStatus("slowed");

    /// <summary>The Zealot's plain <c>frenzied</c> (no damage bonus, no onEnd).</summary>
    public static int PlainFrenzied => Data.FindStatus("frenzied");

    /// <summary>Blood-oil Anointing's tech id.</summary>
    public static int Anointing => Data.FindTech("teblor_blood_oil_anointing");

    /// <summary>The Frenzy's duration in ticks (10 s) and the aftermath's (4 s), from the copy's data.</summary>
    public static int FrenzyTicks => Data.Abilities[Data.FindAbility("blood_oil_frenzy")].Effects[0].DurationTicks;

    /// <summary>The aftermath's duration in ticks, from the status row.</summary>
    public static int AftermathTicks => Data.Statuses[Frenzy].OnEndTicks;

    /// <summary>A flat 48-cell sim on <see cref="Data"/> with <paramref name="players"/> players.</summary>
    public static Simulation Flat(int players = 2, int units = 128, ulong seed = 1) =>
        new(new SimConfig(seed, players, units, 8 * units + 32) { Data = Data }, LocalMovementTests.Flat(48));

    /// <summary>A unit straight into the store (no tick runs), as <see cref="CombatScenes.Place"/>.</summary>
    public static EntityHandle Place(Simulation sim, int owner, string type, Vector2 at)
    {
        int t = sim.World.Data.FindUnit(type);
        Assert.True(sim.World.Units.TrySpawn(owner, t, sim.World.Data.Units[t], at, out EntityHandle h));
        return h;
    }

    /// <summary>Unit <paramref name="unit"/>'s entry of status <paramref name="status"/>: its ticks left, or -1 without one.</summary>
    public static int TicksOf(World w, EntityHandle unit, int status)
    {
        int at = w.Units.Statuses.IndexOf(unit.Index, status);
        return at < 0 ? -1 : w.Units.Statuses.TicksRemaining[at];
    }

    /// <summary>The Shaman of <paramref name="owner"/> casts its Frenzy (ability 0, a selfAura: the point is ignored).</summary>
    public static void Cast(Simulation sim, int owner, EntityHandle shaman) =>
        sim.Enqueue(Command.UseAbility(owner, shaman, 0, sim.World.Units.Position[shaman.Index]));
}
