using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M4-4b-3a criterion 6: three hostile players on a generated map, each with Zealots (both passives), Raiders and Heavy
/// Infantry, attack-moving into each other for 2,000 ticks. Twins hash-equal after every tick; every live Zealot below half
/// its hit points since the last tick is Frenzied +50 %; only Zealots are ever Frenzied (Martyrdom lists only them); the
/// passive count is the live Zealot count; one seed's replay round-trips and plays back equal.
/// </summary>
public class PassiveFuzzStressTests
{
    private const int Ticks = 2000;
    private const int Players = 3;
    private const int ZealotsPer = 14;
    private const int OthersPer = 10;
    private readonly ITestOutputHelper _out;

    public PassiveFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: Players, UnitCapacity: Players * (ZealotsPer + OthersPer), CommandCapacity: 512));
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        NavGrid g = sim.World.NavGrid;
        List<int> open = Open(g);
        var rng = new SimRng(seed, 411);
        GameData d = TestSim.Data;
        int zealot = d.FindUnit("whirlwind_zealot"), raider = d.FindUnit("whirlwind_raider"), heavy = d.FindUnit("malazan_heavy_infantry");
        // All three armies within one patch of open ground, so they meet early and keep fighting.
        int centre = open[rng.NextInt(0, open.Count)];
        int cx = centre % g.Width, cy = centre / g.Width;
        for (int p = 0; p < Players; p++)
            for (int k = 0; k < ZealotsPer + OthersPer; k++)
            {
                int c = Near(g, open, cx + (p - 1) * 8, cy + (p == 1 ? 6 : 0), ref rng);
                int type = k < ZealotsPer ? zealot : p == 1 ? heavy : raider;
                sim.Enqueue(Command.SpawnUnit(p, type, g.CellCenter(c % g.Width, c / g.Width)));
            }
        return sim;
    }

    /// <summary>A passable cell within 6 cells of (x, y) when one is found in a few tries, else any open cell.</summary>
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

    /// <summary>Every live unit attack-moves to a random live enemy's position (a few to their own feet, to stand and fight).</summary>
    private static void Orders(Simulation sim, ref SimRng rng)
    {
        UnitStore u = sim.World.Units;
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

    [Theory]
    [InlineData(1UL)]
    [InlineData(5UL)]
    [InlineData(9UL)]
    public void ThreeArmiesWithZealots_TwinsEqual_PassivesHold_ReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[seed == 1UL ? 1 : 0];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        UnitStore u = w.Units;
        GameData d = w.Data;
        int zealot = d.FindUnit("whirlwind_zealot"), frenzied = d.FindStatus("frenzied");
        var rngA = new SimRng(seed, 412);
        var rngB = new SimRng(seed, 412);
        var belowBefore = new bool[u.Capacity];
        int zealotDeaths = 0, frenziedUnitTicks = 0, martyrs = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 150 == 2)
            {
                Orders(a, ref rngA);
                Orders(b, ref rngB);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            foreach (DeathEvent e in w.Deaths)
                if (!e.IsBuilding && e.VictimType == zealot) zealotDeaths++;
            int zealots = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i])
                {
                    belowBefore[i] = false;
                    continue;
                }
                int at = u.Statuses.IndexOf(i, frenzied);
                if (u.TypeId[i] != zealot)
                {
                    Assert.True(at < 0, $"seed {seed} tick {t}: a {d.Units[u.TypeId[i]].Key} is Frenzied");
                    continue;
                }
                zealots++;
                if (at >= 0)
                {
                    frenziedUnitTicks++;
                    Assert.Equal(0.5f, u.Statuses.Magnitude[at]);
                    if (u.Statuses.TicksRemaining[at] > 1) martyrs++;
                }
                // Below the line since the last tick's end: this tick's phase 5 made it Frenzied (still in force).
                if (belowBefore[i]) Assert.True(at >= 0, $"seed {seed} tick {t}: Zealot {i} at {u.Hp[i]} hp is not Frenzied");
                belowBefore[i] = u.Hp[i] * 2 < d.Units[zealot].Hp;
            }
            Assert.Equal(zealots, u.BelowHpUnits);
        }
        _out.WriteLine($"seed {seed}: {zealotDeaths} Zealot deaths, {frenziedUnitTicks} Frenzied unit-ticks ({martyrs} with a Martyrdom entry), units left {u.Count}");
        Assert.True(zealotDeaths > 5, $"only {zealotDeaths} Zealot deaths");
        Assert.True(frenziedUnitTicks > 50 && martyrs > 0, $"{frenziedUnitTicks} Frenzied unit-ticks, {martyrs} from Martyrdom");
        if (rec.Length == 0) return;
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
        Assert.Equal(Ticks, result.TicksRun);
    }
}
