using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M8-3, session 2026-11-18-0926) at scale: 2,000 units of four players (a 2v2: teams 1, 2, 1, 2, so an ally's packs
/// stand among a player's own), every one of a type carrying a field (the first shot from stealth, the pack passive or both:
/// the developer's copy plus a ranged pack unit and a 0.75 m pack unit), packed in four touching blocks round the middle of a flat 160-cell map and fed a
/// seeded stream of move, attack-move, attack, stop and hold orders and the Heavy Infantry's test stealth cast. Four seeds x
/// 2,000 ticks: twins hash-equal every tick; after every tick no unit position is NaN or off the map and every hit-point
/// value is in 0..max; every 20th tick every live unit's <see cref="HitFactor.Of"/> equals a brute-force oracle (every slot
/// walked). One seed also saves and loads at tick 1,000 and goes on beside the uninterrupted run. The tick cost is reported.
/// </summary>
[Collection(SerialCollection.Name)]
public class HitBonusScaleQaStressTests
{
    private readonly ITestOutputHelper _out;

    public HitBonusScaleQaStressTests(ITestOutputHelper output) => _out = output;

    private const int Units = 2000, Size = 160, Players = 4;

    private static GameData D => s_data.Value;

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        HitBonusScenes.Edit(dir);
        foreach ((string f, string u) in new[] { ("malazan", "malazan_crossbowman"), ("teblor", "teblor_warrior"), ("whirlwind", "whirlwind_desert_archer") })
            dir.EditJson($"factions/{f}/units.json", root =>
            {
                foreach (JsonNode? row in root["units"]!.AsArray())
                {
                    if ((string?)row!["id"] != u) continue;
                    if (row["passives"] is not JsonArray list) row["passives"] = list = new JsonArray();
                    BloodOilScenes.RemoveWhere(list, n => (string?)n!["trigger"] == "nearbyOwn");
                    list.Add(HitBonusScenes.Pack(u));
                }
            });
    }));

    // Types with a field, by faction: Malazan (first shot; both; first shot + splash; ranged pack + first shot),
    // Whirlwind (first shot + stand-still + pack; pack), Teblor (first shot + stand-still; pack 0.4 m; pack 0.75 m).
    private static readonly string[][] Types =
    {
        new[] { "malazan_crossbowman", "malazan_heavy_infantry", "malazan_cadre_mage", "malazan_heavy_infantry" },
        new[] { "whirlwind_desert_archer", "whirlwind_raider" },
        new[] { "teblor_javelin_thrower", "teblor_war_dog", "teblor_warrior" },
        new[] { "malazan_crossbowman", "malazan_heavy_infantry", "malazan_heavy_infantry" },
    };

    private static readonly int[] Faction = { 0, 1, 2, 0 };

    private static Vector2 Block(int p) => p switch
    {
        0 => new Vector2(128f, 128f), 1 => new Vector2(192f, 128f), 2 => new Vector2(192f, 192f), _ => new Vector2(128f, 192f),
    };

    private static Simulation NewSim(ulong seed)
    {
        var seats = System.Collections.Immutable.ImmutableArray.Create(
            new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 1), new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal, 2),
            new PlayerSeat(2, SeatController.Human, AiDifficulty.Normal, 1), new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 2));
        var sim = new Simulation(new SimConfig(seed, Players, Units + 64, 8192) { Data = D, Seats = seats }, LocalMovementTests.Flat(Size));
        var rng = new SimRng(seed, 1621);
        for (int k = 0; k < Units; k++)
        {
            int p = k % Players;
            string type = Types[p][rng.NextInt(0, Types[p].Length)];
            Vector2 at = Block(p) + new Vector2(rng.NextInt(-24, 25) + rng.NextInt(0, 4) * 0.25f, rng.NextInt(-24, 25) + rng.NextInt(0, 4) * 0.25f);
            CombatScenes.Place(sim, p, D.FindUnit(type), at);
        }
        return sim;
    }

    private static void Orders(Simulation sim, ref SimRng rng, List<Command> into)
    {
        into.Clear();
        UnitStore u = sim.World.Units;
        if (sim.TickNumber % 20 == 3)
        {
            int infantry = D.FindUnit("malazan_heavy_infantry");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || rng.NextInt(0, 8) != 0) continue;
                var h = new EntityHandle(i, u.Generation[i]);
                int p = u.Owner[i];
                if (u.TypeId[i] == infantry && rng.NextInt(0, 4) == 0)
                {
                    into.Add(Command.UseAbility(p, h, 0, u.Position[i]));
                    continue;
                }
                int enemy = -1;
                for (int tries = 0; tries < 8 && enemy < 0; tries++)
                {
                    int j = rng.NextInt(0, u.Capacity);
                    if (u.Alive[j] && !sim.World.AreAllied(u.Owner[j], p)) enemy = j;
                }
                switch (rng.NextInt(0, 8))
                {
                    case 0:
                        Vector2 c = rng.NextInt(0, 2) == 0 ? new Vector2(160f, 160f) : Block(p);
                        into.Add(Command.Move(p, h, c + new Vector2(rng.NextInt(-20, 21), rng.NextInt(-20, 21))));
                        break;
                    case 1:
                    case 2:
                    case 3:
                        if (enemy >= 0) into.Add(Command.AttackMove(p, h, u.Position[enemy]));
                        break;
                    case 4:
                    case 7:
                        if (enemy >= 0) into.Add(Command.Attack(p, h, new EntityHandle(enemy, u.Generation[enemy]), isBuilding: false));
                        break;
                    case 5:
                        into.Add(Command.Stop(p, h));
                        break;
                    default:
                        into.Add(Command.HoldPosition(p, h));
                        break;
                }
            }
        }
    }

    private static void AssertBasics(World w, int tick)
    {
        UnitStore u = w.Units;
        float max = Size * Map.MapConstants.CellSize;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X >= 0f && p.Y >= 0f && p.X <= max && p.Y <= max, $"tick {tick}: slot {i} at {p}");
            int hp = u.Hp[i], top = w.Data.Units[u.TypeId[i]].Hp;
            Assert.True(hp > 0 && hp <= top, $"tick {tick}: slot {i} hp {hp} of {top}");
        }
    }

    private static int AssertFactors(World w, int tick)
    {
        UnitStore u = w.Units;
        int packed = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            float of = HitFactor.Of(w, i), oracle = HitBonusScenes.Oracle(w, i);
            Assert.True(of == oracle, $"tick {tick}: slot {i} ({w.Data.Units[u.TypeId[i]].Key}) factor {of} against the oracle's {oracle}");
            if (of > 1f) packed++;
        }
        return packed;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void TwoThousandFieldUnits_TwinsEqualEveryTick_InvariantsAndOracleHold(ulong seed)
    {
        Simulation a = NewSim(seed), b = NewSim(seed);
        var rng = new SimRng(seed, 1622);
        var commands = new List<Command>();
        long factorTicks = 0, deaths = 0;
        var watch = new Stopwatch();
        for (int t = 0; t < 2000; t++)
        {
            Orders(a, ref rng, commands);
            foreach (Command c in commands)
            {
                a.Enqueue(c);
                b.Enqueue(c);
            }
            watch.Start();
            a.Tick();
            watch.Stop();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            deaths += a.World.Deaths.Length;
            AssertBasics(a.World, t);
            if (t % 20 == 0) factorTicks += AssertFactors(a.World, t);
        }
        _out.WriteLine($"seed {seed}: {a.World.Units.Count} left of {Units}, {deaths} deaths, {factorTicks} unit-samples with a factor above 1; {watch.Elapsed.TotalMilliseconds / 2000:F3} ms a tick (twin and checks excluded)");
        Assert.True(deaths > 100, $"only {deaths} deaths");
        Assert.True(factorTicks > 1000, $"only {factorTicks} unit-samples with a factor above 1");
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void TwoThousandFieldUnits_SaveLoadAt1000_EqualsTheUninterruptedRun()
    {
        Simulation reference = NewSim(3), chained = NewSim(3);
        var rng = new SimRng(3, 1622);
        var commands = new List<Command>();
        for (int t = 0; t < 1600; t++)
        {
            if (t == 1000)
            {
                byte[] bytes = SaveTestRun.Save(chained);
                chained = SaveTestRun.Load(D, bytes);
                Assert.Equal(reference.StateHash(), chained.StateHash());
            }
            Orders(reference, ref rng, commands);
            foreach (Command c in commands)
            {
                reference.Enqueue(c);
                chained.Enqueue(c);
            }
            reference.Tick();
            chained.Tick();
            Assert.True(chained.StateHash() == reference.StateHash(), $"the loaded run differs after tick {t}");
        }
    }
}
