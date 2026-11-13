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

    /// <summary>
    /// Applies the four M7-4 edits to a copy of the data, idempotently (M7-6, BUG-1280): a <see cref="Status"/> row and any
    /// <c>statusDuration</c> effect on the Anointing already in the files (the data track's docs/03 rows) are taken out and
    /// the test's own appended, so the copy has exactly one of each, always last in its list (the layout every M7-4 pin was
    /// taken on), whether or not the data ships them; the ability's status and the Longhouse's scale are set, not added.
    /// </summary>
    public static void Edit(TestDataDir dir)
    {
        dir.EditJson("common/statuses.json", r =>
        {
            JsonArray list = r["statuses"]!.AsArray();
            RemoveWhere(list, n => (string?)n!["id"] == Status);
            list.Add(StatusRow());
        });
        dir.EditJson("factions/teblor/abilities.json", r => r["abilities"]![0]!["effects"]![0]!["status"] = Status);
        dir.EditJson("factions/teblor/techs.json", r =>
        {
            JsonArray effects = r["techs"]![0]!["effects"]!.AsArray();
            RemoveWhere(effects, IsStatusDuration);
            effects.Add(new JsonObject { ["stat"] = "statusDuration", ["amount"] = 4, ["statuses"] = new JsonArray(Status) });
        });
        dir.EditJson("factions/teblor/buildings.json", r =>
        {
            foreach (JsonNode? b in r["buildings"]!.AsArray())
                if ((string)b!["id"]! == "teblor_longhouse") b["scale"] = 1.25;
        });
    }

    /// <summary>Whether a tech effect is a <c>statusDuration</c> one.</summary>
    public static bool IsStatusDuration(JsonNode? effect) => (string?)effect!["stat"] == "statusDuration";

    /// <summary>Removes every element of <paramref name="list"/> matching <paramref name="match"/>.</summary>
    public static void RemoveWhere(JsonArray list, Func<JsonNode?, bool> match)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (match(list[i])) list.RemoveAt(i);
    }

    /// <summary>The Frenzy row's index in the copy <see cref="Edit"/> makes: the last of the statuses, whatever the files ship.</summary>
    public static int RowIndex => Shipped("common/statuses.json", r => r["statuses"]!.AsArray(), n => (string?)n!["id"] == Status);

    /// <summary>The Anointing's <c>statusDuration</c> effect's index in the copy <see cref="Edit"/> makes: the last of its effects.</summary>
    public static int EffectIndex => Shipped("factions/teblor/techs.json", r => r["techs"]![0]!["effects"]!.AsArray(), IsStatusDuration);

    // The shipped list's length less its entries Edit takes out: where Edit's appended entry lands.
    private static int Shipped(string rel, Func<JsonNode, JsonArray> list, Func<JsonNode?, bool> taken)
    {
        JsonArray a = list(JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, rel)))!);
        int n = a.Count;
        foreach (JsonNode? e in a)
            if (taken(e)) n--;
        return n;
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
