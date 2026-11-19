using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-3 test copies: the shipped data plus the two hit rules on test rows only (no shipped file changes):
/// <c>attack.fromStealthBonus</c> 0.5 (the Shadow Archer's +50 %, docs/factions/shadow.md) on <see cref="StealthUnits"/>;
/// a <c>nearbyOwn</c> passive (each type counting its own kind, 8 m, +10 % a unit, at most 3: the Hound's Pack Hunter) on
/// <see cref="PackUnits"/>; the M8-2 <c>stillStealth</c> block (3 s) on <see cref="StillUnits"/>; the M8-1 forest block
/// (3 s, siege excepted) on the Malazan faction; the M4-5 test-only stealth sources (<see cref="StealthTests.AddTo"/>: the Heavy
/// Infantry's 60 s <c>zz_test_stealth</c> cast, the Malazan Laborer's 10 m detector); and the Blood-oil Frenzy's status row (<c>teblor_frenzied</c>,
/// <c>damageBonus</c> 0.2) whether or not the files ship it. Every edit overwrites what the shipped row holds (a
/// <c>nearbyOwn</c> already there is taken out first), so a shipped row that already carries a field changes nothing here
/// (rule 58).
/// </summary>
internal static class HitBonusScenes
{
    /// <summary>The types the copy gives <c>attack.fromStealthBonus</c> 0.5: a ranged unit of each faction, a Malazan melee unit (also a pack type) and a Malazan splash caster.</summary>
    internal static readonly (string Faction, string Unit)[] StealthUnits =
    {
        ("malazan", "malazan_crossbowman"), ("malazan", "malazan_heavy_infantry"), ("malazan", "malazan_cadre_mage"),
        ("whirlwind", "whirlwind_desert_archer"), ("teblor", "teblor_javelin_thrower"),
    };

    /// <summary>The types the copy also gives the stand-still stealth (3 s).</summary>
    internal static readonly (string Faction, string Unit)[] StillUnits =
    {
        ("whirlwind", "whirlwind_desert_archer"), ("teblor", "teblor_javelin_thrower"),
    };

    /// <summary>The types the copy gives a <c>nearbyOwn</c> passive listing their own type: a melee unit of each faction.</summary>
    internal static readonly (string Faction, string Unit)[] PackUnits =
    {
        ("malazan", "malazan_heavy_infantry"), ("whirlwind", "whirlwind_raider"), ("teblor", "teblor_war_dog"),
    };

    /// <summary>The copy's numbers: the first-shot bonus, the pack's step, reach and cap (docs/factions/shadow.md's).</summary>
    internal const double Bonus = 0.5, PackStep = 0.1, PackRadius = 8;

    /// <summary>The pack's cap.</summary>
    internal const int PackStacks = 3;

    /// <summary>The <c>nearbyOwn</c> passive counting <paramref name="unit"/> itself, with the copy's numbers.</summary>
    internal static JsonObject Pack(string unit) => new()
    {
        ["trigger"] = "nearbyOwn", ["units"] = new JsonArray(unit), ["radius"] = PackRadius, ["damageBonus"] = PackStep, ["maxStacks"] = PackStacks,
    };

    /// <summary>The test copy (see the type's summary).</summary>
    internal static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(Edit));

    /// <summary>The shipped data with only the <c>nearbyOwn</c> passive on <see cref="PackUnits"/> (the perf rows' "with": nothing else differs from the shipped data).</summary>
    internal static GameData PackOnly => s_packOnly.Value;

    private static readonly Lazy<GameData> s_packOnly = new(() => AiScenes.DataWith(AddPacks));

    /// <summary>Gives each of <see cref="PackUnits"/> its <c>nearbyOwn</c> passive (a shipped one taken out first).</summary>
    internal static void AddPacks(TestDataDir dir)
    {
        foreach ((string f, string unit) in PackUnits)
            dir.EditJson($"factions/{f}/units.json", root =>
            {
                foreach (JsonNode? row in root["units"]!.AsArray())
                {
                    if ((string?)row!["id"] != unit) continue;
                    if (row["passives"] is not JsonArray list) row["passives"] = list = new JsonArray();
                    BloodOilScenes.RemoveWhere(list, n => (string?)n!["trigger"] == "nearbyOwn");
                    list.Add(Pack(unit));
                }
            });
    }

    /// <summary>Applies the copy's edits to <paramref name="dir"/>, idempotently.</summary>
    internal static void Edit(TestDataDir dir)
    {
        foreach ((string f, string unit) in StealthUnits) dir.SetUnitField(f, unit, "attack.fromStealthBonus", "0.5");
        foreach ((string f, string unit) in StillUnits) dir.SetUnitField(f, unit, "stillStealth", StillScenes.Block(3));
        dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = ForestScenes.Block(3));
        StealthTests.AddTo(dir);
        AddPacks(dir);
        dir.EditJson("common/statuses.json", r =>
        {
            JsonArray list = r["statuses"]!.AsArray();
            BloodOilScenes.RemoveWhere(list, n => (string?)n!["id"] == BloodOilScenes.Status);
            list.Add(BloodOilScenes.StatusRow());
        });
    }

    /// <summary>The Blood-oil Frenzy status id (frenzied kind, <c>damageBonus</c> 0.2).</summary>
    internal static int Frenzy(GameData d) => d.FindStatus(BloodOilScenes.Status);

    /// <summary>A flat 48-cell sim on <see cref="Data"/> (players 0-2: Malazan, Whirlwind, Teblor unless <paramref name="seats"/> says otherwise).</summary>
    internal static Simulation Scene(int players = 3, int units = 96, System.Collections.Immutable.ImmutableArray<PlayerSeat> seats = default, GameData? data = null) =>
        new(new SimConfig(1, players, units, 8 * units + 32) { Data = data ?? Data, Seats = seats.IsDefault ? System.Collections.Immutable.ImmutableArray<PlayerSeat>.Empty : seats },
            LocalMovementTests.Flat(48));

    /// <summary>A unit of type <paramref name="type"/> for <paramref name="owner"/> straight into the store (no tick runs).</summary>
    internal static EntityHandle Place(Simulation sim, int owner, string type, Vector2 at) =>
        CombatScenes.Place(sim, owner, sim.World.Data.FindUnit(type), at);

    /// <summary>
    /// A target that never answers: the victim player's worker (light, armor 0; a worker never scans), with a million hit
    /// points. Player 0's is the Malazan Laborer, 1's the Camp Follower, 2's the Teblor Gatherer.
    /// </summary>
    internal static EntityHandle Dummy(Simulation sim, int owner, Vector2 at)
    {
        string type = owner switch { 0 => "malazan_laborer", 1 => "whirlwind_camp_follower", _ => "teblor_gatherer" };
        EntityHandle h = Place(sim, owner, type, at);
        sim.World.Units.Hp[h.Index] = 1_000_000;
        return h;
    }

    /// <summary>A Stealthed entry on <paramref name="h"/> for <paramref name="ticks"/> ticks (a timed application, as an ability's), its owner the source.</summary>
    internal static void Hide(Simulation sim, EntityHandle h, int ticks = 100_000) =>
        Abilities.StatusSystem.Apply(sim.World, h.Index, sim.World.Data.FindStatus("stealthed"), 0f, ticks, sim.World.Units.Owner[h.Index]);

    /// <summary>The cell centre of (x, y) offset by (<paramref name="dx"/>, <paramref name="dy"/>) m.</summary>
    internal static Vector2 At(Simulation sim, int x, int y, float dx = 0f, float dy = 0f) => CombatScenes.At(sim, x, y, dx, dy);

    /// <summary>The hits <paramref name="victim"/> takes over <paramref name="ticks"/> ticks: the tick each landed in and its damage.</summary>
    internal static List<(int Tick, int Damage)> Hits(Simulation sim, EntityHandle victim, int ticks, Func<bool>? stop = null)
    {
        var hits = new List<(int, int)>();
        UnitStore u = sim.World.Units;
        for (int t = 0; t < ticks && (stop == null || !stop()); t++)
        {
            int before = u.Hp[victim.Index];
            sim.Tick();
            if (u.Hp[victim.Index] < before) hits.Add((sim.TickNumber - 1, before - u.Hp[victim.Index]));
        }
        return hits;
    }

    /// <summary>Runs ticks until <paramref name="victim"/> has taken <paramref name="count"/> hits (at most <paramref name="max"/> ticks).</summary>
    internal static List<(int Tick, int Damage)> FirstHits(Simulation sim, EntityHandle victim, int count, int max = 600)
    {
        var hits = new List<(int, int)>();
        UnitStore u = sim.World.Units;
        for (int t = 0; t < max && hits.Count < count; t++)
        {
            int before = u.Hp[victim.Index];
            sim.Tick();
            if (u.Hp[victim.Index] < before) hits.Add((sim.TickNumber - 1, before - u.Hp[victim.Index]));
        }
        Assert.True(hits.Count == count, $"only {hits.Count} of {count} hits landed in {max} ticks");
        return hits;
    }

    /// <summary>
    /// The oracle the fuzz holds <see cref="HitFactor.Of"/> to, computed without the grid or the helper: the largest status
    /// damage bonus, x (1 + the attack's bonus) when the unit carries a Stealthed entry and no Revealed one and is not
    /// summoned, x (1 + min(n, cap) x step) with n counted by walking every slot.
    /// </summary>
    internal static float Oracle(World w, int i)
    {
        UnitStore u = w.Units;
        GameData d = w.Data;
        UnitDef def = d.Units[u.TypeId[i]];
        float bonus = 0f;
        bool stealthed = false, revealed = false;
        int head = i * Abilities.StatusStore.PerUnit;
        for (int k = 0; k < u.Statuses.Count[i]; k++)
        {
            StatusDef s = d.Statuses[u.Statuses.StatusId[head + k]];
            if (s.DamageBonus > bonus) bonus = s.DamageBonus;
            if (s.Kind == StatusKind.Stealthed) stealthed = true;
            if (s.Kind == StatusKind.Revealed) revealed = true;
        }
        float factor = 1f + bonus;
        if (def.Attack.FromStealthBonus > 0f && stealthed && !revealed && !u.IsSummoned(i)) factor *= 1f + def.Attack.FromStealthBonus;
        if (def.NearbyPassive >= 0)
        {
            UnitPassive p = def.Passives[def.NearbyPassive];
            int n = 0;
            for (int j = 0; j < u.Capacity; j++)
                if (j != i && u.Alive[j] && u.Owner[j] == u.Owner[i] && p.Units.Contains(u.TypeId[j])
                    && Vector2.DistanceSquared(u.Position[j], u.Position[i]) <= p.Radius * p.Radius) n++;
            n = Math.Min(n, p.MaxStacks);
            if (n > 0) factor *= 1f + n * p.DamageBonus;
        }
        return factor;
    }
}
