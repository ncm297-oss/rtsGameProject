using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M7-4 criterion 6: all three status rules in one fight on test data (the <see cref="BloodOilScenes"/> copy, Blood-oil
/// Anointing made free and instant so it is researched through commands and replays). Three players on a generated map:
/// Teblor (player 0, who researches the Anointing; player 2, who doesn't) and Whirlwind (player 1, with Zealots: the plain
/// frenzied passive beside the Frenzy). Shamans cast the Frenzy on cooldown; every army attack-moves into the others.
/// 2,000 ticks, three seeds. After every tick: twins hash-equal; the unit invariants (finite on-map positions, hit points
/// in (0, max], every entry known with ticks left and no repeated status, speed = type speed x (1 - strongest slow)); every
/// Frenzy entry is its owner's, at 0.4, at most 10 s (+4 s with the Anointing); every Slow is an aftermath (0.2, at most
/// 80 ticks), and a unit whose Frenzy had one tick left and survived carries a full 80-tick Slow, while an 80-tick Slow
/// only appears where a Frenzy just ran out. One seed's replay round-trips and plays back equal. Each seed saves and
/// loads in the middle of an aftermath and the loaded sim follows the original to the end, hash for hash.
/// </summary>
public class FrenzyAftermathFuzzStressTests
{
    private const int Ticks = 2000;
    private const int Players = 3;
    private const int PerSide = 20;
    private readonly ITestOutputHelper _out;

    public FrenzyAftermathFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        BloodOilScenes.Edit(dir);
        dir.EditJson("factions/teblor/techs.json", r =>
        {
            JsonNode t = r["techs"]![0]!;
            t["requires"] = new JsonArray();
            t["cost"] = new JsonObject { ["gold"] = 0, ["wood"] = 0 };
            t["researchTime"] = 1;
        });
    }));

    private static GameData Data => s_data.Value;

    private static readonly string[] Teblor = { "teblor_warrior", "teblor_warrior", "teblor_javelin_thrower", "teblor_shaman", "teblor_warrior" };
    private static readonly string[] Whirlwind = { "whirlwind_zealot", "whirlwind_zealot", "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider" };

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        int teblor = Data.FindFaction("teblor"), whirlwind = Data.FindFaction("whirlwind");
        var seats = ImmutableArray.Create(
            new PlayerSeat(teblor, SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(whirlwind, SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(teblor, SeatController.Human, AiDifficulty.Normal));
        var sim = new Simulation(new SimConfig(seed, Players, Players * PerSide + 64, 1024) { Data = Data, Seats = seats });
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 474);
        List<int> open = Open(g);
        int centre = open[rng.NextInt(0, open.Count)];
        int cx = centre % g.Width, cy = centre / g.Width;
        for (int p = 0; p < Players; p++)
            for (int k = 0; k < PerSide; k++)
            {
                int c = Near(g, open, cx + (p - 1) * 8, cy + (p == 1 ? 6 : 0), ref rng);
                string type = p == 1 ? Whirlwind[k % Whirlwind.Length] : Teblor[k % Teblor.Length];
                sim.Enqueue(Command.SpawnUnit(p, Data.FindUnit(type), g.CellCenter(c % g.Width, c / g.Width)));
            }
        // Player 0's Bloodwood Carver, to research the Anointing at (a few spots; the first that fits stands).
        int carver = Data.FindBuilding("teblor_bloodwood_carver");
        for (int k = 0; k < 6; k++)
        {
            int c = Near(g, open, cx - 16, cy - 10, ref rng);
            sim.Enqueue(Command.SpawnBuilding(0, carver, g.CellCenter(c % g.Width, c / g.Width)));
        }
        return sim;
    }

    private static int Near(NavGrid g, List<int> open, int x, int y, ref SimRng rng)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            int px = x + rng.NextInt(-6, 7), py = y + rng.NextInt(-6, 7);
            if (px >= 0 && py >= 0 && px < g.Width && py < g.Height && g.IsPassable(px, py)) return py * g.Width + px;
        }
        return open[rng.NextInt(0, open.Count)];
    }

    private static List<int> Open(NavGrid g)
    {
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        return open;
    }

    /// <summary>
    /// The script of tick <paramref name="t"/>: at tick 3 player 0 researches the Anointing at its first Carver; every 150
    /// ticks every live unit attack-moves to a random enemy; every 40 ticks each live Shaman casts (dropped on cooldown).
    /// </summary>
    private static void Script(Simulation sim, int t, ref SimRng rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        if (t == 3)
        {
            int carver = Data.FindBuilding("teblor_bloodwood_carver");
            for (int b = 0; b < w.Buildings.Capacity; b++)
            {
                if (!w.Buildings.Alive[b] || w.Buildings.Owner[b] != 0 || w.Buildings.TypeId[b] != carver) continue;
                int cell = w.Buildings.Cell[b];
                sim.Enqueue(Command.Research(0, w.NavGrid.CellCenter(cell % w.NavGrid.Width, cell / w.NavGrid.Width), BloodOilScenes.Anointing));
                break;
            }
        }
        if (t % 150 == 5)
        {
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 to = u.Position[i];
                for (int tries = 0; tries < 8; tries++)
                {
                    int j = rng.NextInt(0, u.Capacity);
                    if (u.Alive[j] && u.Owner[j] != u.Owner[i]) { to = u.Position[j]; break; }
                }
                sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), to));
            }
        }
        if (t % 40 == 20)
        {
            int shaman = Data.FindUnit("teblor_shaman");
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.TypeId[i] == shaman)
                    sim.Enqueue(Command.UseAbility(u.Owner[i], new EntityHandle(i, u.Generation[i]), 0, u.Position[i]));
        }
    }

    private sealed class Tally
    {
        public int Aftermaths, FrenzyUnitTicks, LongFrenzies;
    }

    /// <summary>The invariants of the doc comment on <paramref name="sim"/> after tick <paramref name="t"/>; <paramref name="prevFrenzy"/> is each slot's Frenzy ticks at the last tick's end (0 none), updated.</summary>
    private static void Check(Simulation sim, int t, int[] prevFrenzy, int[] prevGen, Tally tally, string tag)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        GameData d = w.Data;
        int frenzy = d.FindStatus(BloodOilScenes.Status), slowed = d.FindStatus("slowed"), plain = d.FindStatus("frenzied");
        float maxX = w.NavGrid.Width * MapConstants.CellSize, maxY = w.NavGrid.Height * MapConstants.CellSize;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i])
            {
                prevFrenzy[i] = 0;
                continue;
            }
            bool same = prevGen[i] == u.Generation[i];
            prevGen[i] = u.Generation[i];
            Vector2 p = u.Position[i];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X >= 0 && p.Y >= 0 && p.X <= maxX && p.Y <= maxY, $"{tag} tick {t}: unit {i} at {p}");
            Assert.True(u.Hp[i] > 0 && u.Hp[i] <= w.MaxHp(i), $"{tag} tick {t}: unit {i} hp {u.Hp[i]} of {w.MaxHp(i)}");
            float slow = 0f;
            int frenzyTicks = 0, slowTicks = 0;
            int head = i * StatusStore.PerUnit;
            for (int k = 0; k < s.Count[i]; k++)
            {
                int at = head + k, id = s.StatusId[at];
                Assert.True((uint)id < (uint)d.Statuses.Length && s.TicksRemaining[at] > 0, $"{tag} tick {t}: unit {i} entry {k} id {id} ticks {s.TicksRemaining[at]}");
                for (int k2 = 0; k2 < k; k2++) Assert.NotEqual(id, s.StatusId[head + k2]);
                if (id == frenzy)
                {
                    int cap = BloodOilScenes.FrenzyTicks + (w.HasTech(u.Owner[i], BloodOilScenes.Anointing) ? 80 : 0);
                    Assert.True(s.SourcePlayer[at] == u.Owner[i] && s.Magnitude[at] == 0.4f && s.TicksRemaining[at] <= cap,
                        $"{tag} tick {t}: unit {i} Frenzy {s.Magnitude[at]} / {s.TicksRemaining[at]} (cap {cap}) from {s.SourcePlayer[at]}");
                    frenzyTicks = s.TicksRemaining[at];
                    if (frenzyTicks > BloodOilScenes.FrenzyTicks) tally.LongFrenzies++;
                }
                else if (id == slowed)
                {
                    Assert.True(s.Magnitude[at] == 0.2f && s.TicksRemaining[at] <= 80 && s.SourcePlayer[at] == u.Owner[i], $"{tag} tick {t}: unit {i} Slowed {s.Magnitude[at]} / {s.TicksRemaining[at]}");
                    slow = 0.2f;
                    slowTicks = s.TicksRemaining[at];
                }
                else Assert.True(id == plain && s.Magnitude[at] == 0.5f, $"{tag} tick {t}: unit {i} has {d.Statuses[id].Key}");
            }
            UnitDef def = d.Units[u.TypeId[i]];
            Assert.Equal(def.SpeedPerTick * (1f - slow), u.Speed[i]);
            if (frenzyTicks > 0) tally.FrenzyUnitTicks++;
            bool ranOut = same && prevFrenzy[i] == 1;
            if (ranOut)
            {
                Assert.True(slowTicks == 80, $"{tag} tick {t}: unit {i}'s Frenzy ran out and its Slow has {slowTicks} ticks");
                tally.Aftermaths++;
            }
            else Assert.True(slowTicks != 80, $"{tag} tick {t}: unit {i} has a fresh Slow with no Frenzy ending");
            prevFrenzy[i] = frenzyTicks;
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    [InlineData(8UL)]
    [Trait("Category", "Stress")]
    public void ThreeArmies_FrenzyAftermathAndAnointing_TwinsEqual_InvariantsHold_SaveAndReplayRoundTrip(ulong seed)
    {
        var rec = new ReplayRecorder?[seed == 1UL ? 1 : 0];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        var rngA = new SimRng(seed, 475);
        var rngB = new SimRng(seed, 475);
        var rngC = default(SimRng);
        Simulation? c = null;
        int savedAt = -1;
        var tally = new Tally();
        int cap = a.World.Units.Capacity;
        int[] prevFrenzy = new int[cap], prevGen = new int[cap];
        int[] prevFrenzyC = new int[cap], prevGenC = new int[cap];
        for (int t = 0; t < Ticks; t++)
        {
            Script(a, t, ref rngA);
            Script(b, t, ref rngB);
            if (c != null) Script(c, t, ref rngC);
            a.Tick();
            b.Tick();
            c?.Tick();
            ulong h = a.StateHash();
            Assert.True(h == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            Check(a, t, prevFrenzy, prevGen, tally, $"seed {seed}");
            if (c != null)
            {
                Assert.True(h == c.StateHash(), $"seed {seed}: the loaded sim (saved after tick {savedAt}) differs after tick {t}");
                Check(c, t, prevFrenzyC, prevGenC, new Tally(), $"seed {seed} loaded");
            }
            else if (t >= 100 && InAftermath(a.World))
            {
                byte[] bytes = SaveTestRun.Save(a);
                c = SaveTestRun.Load(Data, bytes);
                Assert.Equal(h, c.StateHash());
                Assert.Equal(bytes, SaveTestRun.Save(c));
                rngC = rngA;
                savedAt = t;
                Array.Copy(prevFrenzy, prevFrenzyC, cap);
                Array.Copy(prevGen, prevGenC, cap);
            }
        }
        _out.WriteLine($"seed {seed}: {tally.Aftermaths} aftermaths, {tally.FrenzyUnitTicks} Frenzied unit-ticks ({tally.LongFrenzies} past 10 s), saved after tick {savedAt}, units left {a.World.Units.Count}");
        Assert.True(a.World.HasTech(0, BloodOilScenes.Anointing), "player 0 never researched the Anointing");
        Assert.False(a.World.HasTech(2, BloodOilScenes.Anointing));
        Assert.True(tally.Aftermaths > 20 && tally.FrenzyUnitTicks > 500 && tally.LongFrenzies > 0,
            $"too little Frenzy: {tally.Aftermaths} aftermaths, {tally.FrenzyUnitTicks} unit-ticks, {tally.LongFrenzies} lengthened");
        Assert.True(savedAt >= 0, "no tick with an aftermath to save in");
        if (rec.Length == 0) return;
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
        Assert.Equal(Ticks, result.TicksRun);
    }

    /// <summary>Some live unit carries an aftermath Slow and no Frenzy.</summary>
    private static bool InAftermath(World w)
    {
        UnitStore u = w.Units;
        int frenzy = w.Data.FindStatus(BloodOilScenes.Status), slowed = w.Data.FindStatus("slowed");
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Statuses.IndexOf(i, slowed) >= 0 && u.Statuses.IndexOf(i, frenzy) < 0) return true;
        return false;
    }
}
