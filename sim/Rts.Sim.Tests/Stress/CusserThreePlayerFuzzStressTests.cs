using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-4b-1: three hostile players, combat on, each with Sappers, infantry and four Keeps on a generated map; 2,000 ticks
/// of UseAbility spam aimed at enemy buildings, own units, the caster's own feet, off-map and NaN points. Twins
/// hash-equal after every tick; unit and building hit points stay in 0..max; the building count matches the live slots;
/// every death's killer and victim are real players; the replay round-trips and plays back equal.
/// </summary>
public class CusserThreePlayerFuzzStressTests
{
    private const int Ticks = 2000;
    private const int Players = 3;
    private const int SappersPer = 8;
    private const int InfantryPer = 12;
    private readonly ITestOutputHelper _out;

    public CusserThreePlayerFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Keeps = { "malazan_garrison_keep", "whirlwind_holy_camp", "malazan_garrison_keep" };

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: Players, UnitCapacity: Players * (SappersPer + InfantryPer), CommandCapacity: 512));
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        NavGrid g = sim.World.NavGrid;
        List<int> open = Open(g);
        var rng = new SimRng(seed, 301);
        int sapper = TestSim.Data.FindUnit("malazan_sapper"), heavy = TestSim.Data.FindUnit("malazan_heavy_infantry");
        for (int p = 0; p < Players; p++)
        {
            // Each side in its own third of the open cells, so the sides meet but start apart.
            int lo = open.Count * p / Players, hi = open.Count * (p + 1) / Players;
            for (int k = 0; k < SappersPer + InfantryPer; k++)
            {
                int c = open[rng.NextInt(lo, hi)];
                sim.Enqueue(Command.SpawnUnit(p, k < SappersPer ? sapper : heavy, g.CellCenter(c % g.Width, c / g.Width)));
            }
            for (int k = 0; k < 4; k++)
            {
                int c = open[rng.NextInt(lo, hi)];
                sim.Enqueue(Command.SpawnBuilding(p, TestSim.Data.FindBuilding(Keeps[p]), g.CellCenter(c % g.Width, c / g.Width)));
            }
        }
        return sim;
    }

    private static List<int> Open(NavGrid g)
    {
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        return open;
    }

    private static void Spam(Simulation sim, List<int> open, ref SimRng rng)
    {
        UnitStore u = sim.World.Units;
        BuildingStore b = sim.World.Buildings;
        NavGrid g = sim.World.NavGrid;
        for (int n = 0; n < 10; n++)
        {
            int i = rng.NextInt(0, u.Capacity);
            var h = new EntityHandle(i, u.Generation[i]);
            int player = rng.NextInt(0, 12) == 0 ? rng.NextInt(0, Players) : (u.Alive[i] ? u.Owner[i] : 0);
            int c = open[rng.NextInt(0, open.Count)];
            Vector2 point = g.CellCenter(c % g.Width, c / g.Width);
            int shape = rng.NextInt(0, 10);
            if (shape == 0) point = new Vector2(float.NaN, point.Y);
            else if (shape == 1) point = new Vector2(-5f, point.Y);
            else if (shape == 2 && u.Alive[i]) point = u.Position[i]; // its own feet
            else if (shape <= 6)
            {
                int j = rng.NextInt(0, b.Capacity);
                if (b.Alive[j]) point = CombatSystem.BuildingCentre(sim.World, j) + new Vector2(rng.NextFloat() * 12f - 6f, rng.NextFloat() * 12f - 6f);
            }
            else if (u.Alive[i]) point = u.Position[i] + new Vector2(rng.NextFloat() * 12f - 6f, rng.NextFloat() * 12f - 6f);
            if (rng.NextInt(0, 6) == 0) sim.Enqueue(Command.Move(player, h, point));
            else sim.Enqueue(Command.UseAbility(player, h, rng.NextInt(0, 10) == 0 ? rng.NextInt(-1, 3) : 0, point, rng.NextInt(0, 5) == 0));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(7UL)]
    public void ThreePlayerCusserSpam_TwinsEqual_InvariantsHold_ReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[1];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        List<int> open = Open(w.NavGrid);
        var rngA = new SimRng(seed, 302);
        var rngB = new SimRng(seed, 302);
        int cusser = w.Data.FindAbility("cusser");
        int cussers = 0, buildingDeaths = 0, selfDeaths = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 4 == 2)
            {
                Spam(a, open, ref rngA);
                Spam(b, open, ref rngB);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            foreach (AbilityEvent e in w.AbilityEvents)
                if (e.Resolved && e.Ability == cusser) cussers++;
            foreach (DeathEvent d in w.Deaths)
            {
                Assert.True((uint)d.KillerOwner < Players && (uint)d.VictimOwner < Players, $"seed {seed} tick {t}: death {d}");
                if (d.IsBuilding) buildingDeaths++;
                else if (d.KillerOwner == d.VictimOwner) selfDeaths++;
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"seed {seed} tick {t}: unit {i} at {p}");
                int max = w.Data.Units[u.TypeId[i]].Hp;
                Assert.True(u.Hp[i] > 0 && u.Hp[i] <= max, $"seed {seed} tick {t}: unit {i} hp {u.Hp[i]}");
            }
            int live = 0;
            for (int j = 0; j < bs.Capacity; j++)
            {
                if (!bs.Alive[j]) continue;
                live++;
                Assert.True(bs.Hp[j] > 0 && bs.Hp[j] <= w.Data.Buildings[bs.TypeId[j]].Hp, $"seed {seed} tick {t}: building {j} hp {bs.Hp[j]}");
            }
            Assert.Equal(live, bs.Count);
        }
        _out.WriteLine($"seed {seed}: {cussers} Cussers, building deaths {buildingDeaths}, own-kill deaths {selfDeaths}, buildings left {bs.Count}, units left {u.Count}, kills {w.Kills[0]}/{w.Kills[1]}/{w.Kills[2]}");
        Assert.True(cussers > 20, $"only {cussers} Cussers");
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
    }
}
