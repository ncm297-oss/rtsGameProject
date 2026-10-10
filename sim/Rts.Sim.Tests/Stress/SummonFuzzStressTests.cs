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
/// M4-4b-3b criterion 6: three hostile players on a generated map, each with summoners (Cadre Mages summoning Heavy Infantry,
/// Priests summoning Zealots, the test-only abilities of <see cref="SummonTests"/>) casting their summon whenever it is ready,
/// among Raiders and Heavy Infantry attack-moving into each other, for 2,000 ticks in a store small enough to fill. Twins
/// hash-equal after every tick. After every tick: every live summoned unit ends after the tick just run, the summoned count
/// is the live summoned slots, a free slot carries no end tick, the population counts no summoned unit, and the losses are
/// exactly the deaths that were not expiries. One seed's replay round-trips and plays back equal.
/// </summary>
public class SummonFuzzStressTests
{
    private const int Ticks = 2000;
    private const int Players = 3;
    private const int CastersPer = 4;
    private const int OthersPer = 8;
    private const int Capacity = 64; // 36 to start; 12 casters x 3 summons would need 72: the store fills at times
    private readonly ITestOutputHelper _out;

    public SummonFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(new SimConfig(seed, Players, Capacity, 512) { Data = SummonTests.Data });
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        NavGrid g = sim.World.NavGrid;
        List<int> open = Open(g);
        var rng = new SimRng(seed, 421);
        GameData d = SummonTests.Data;
        int mage = d.FindUnit("malazan_cadre_mage"), priest = d.FindUnit("whirlwind_priest");
        int raider = d.FindUnit("whirlwind_raider"), heavy = d.FindUnit("malazan_heavy_infantry");
        int centre = open[rng.NextInt(0, open.Count)];
        int cx = centre % g.Width, cy = centre / g.Width;
        for (int p = 0; p < Players; p++)
            for (int k = 0; k < CastersPer + OthersPer; k++)
            {
                int c = Near(g, open, cx + (p - 1) * 8, cy + (p == 1 ? 6 : 0), ref rng);
                int type = k < CastersPer ? (k + p) % 2 == 0 ? mage : priest : p == 1 ? heavy : raider;
                sim.Enqueue(Command.SpawnUnit(p, type, g.CellCenter(c % g.Width, c / g.Width)));
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

    private static bool IsSummoner(World w, int i) => w.Data.Units[w.Units.TypeId[i]].Abilities.Length > 1;

    /// <summary>Every live non-summoner attack-moves to a random live enemy's position.</summary>
    private static void Orders(Simulation sim, ref SimRng rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || IsSummoner(w, i)) continue;
            Vector2 to = u.Position[i];
            for (int tries = 0; tries < 8; tries++)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (u.Alive[j] && u.Owner[j] != u.Owner[i]) { to = u.Position[j]; break; }
            }
            sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), to));
        }
    }

    /// <summary>Every live summoner whose summon (ability 1) is ready and that isn't casting casts it (the point: where it stands).</summary>
    private static void Summon(Simulation sim)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || !IsSummoner(w, i) || u.CastAbility[i] >= 0) continue;
            if (sim.TickNumber + 1 < u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities + 1]) continue;
            sim.Enqueue(Command.UseAbility(u.Owner[i], new EntityHandle(i, u.Generation[i]), 1, u.Position[i]));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(5UL)]
    [InlineData(9UL)]
    public void ThreeArmiesWithSummoners_TwinsEqual_SummonsHold_ReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[seed == 1UL ? 1 : 0];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        UnitStore u = w.Units;
        GameData d = w.Data;
        var rngA = new SimRng(seed, 422);
        var rngB = new SimRng(seed, 422);
        int summoned = 0, expired = 0, killedEarly = 0, ordinaryDeaths = 0, storeFull = 0, maxLive = 0;
        var wasSummoned = new bool[u.Capacity];
        var generation = new int[u.Capacity];
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 150 == 2)
            {
                Orders(a, ref rngA);
                Orders(b, ref rngB);
            }
            if (t > 2)
            {
                Summon(a);
                Summon(b);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            int ran = a.TickNumber - 1;
            foreach (DeathEvent e in w.Deaths)
            {
                if (e.IsBuilding) continue;
                if (e.Expired)
                {
                    expired++;
                    Assert.Equal(-1, e.KillerOwner);
                    Assert.True(wasSummoned[e.Victim.Index], $"seed {seed} tick {ran}: a permanent unit expired");
                }
                else
                {
                    ordinaryDeaths++;
                    if (wasSummoned[e.Victim.Index] && generation[e.Victim.Index] == e.Victim.Generation) killedEarly++;
                }
            }
            int live = 0;
            var halfPop = new int[Players];
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i])
                {
                    Assert.Equal(0, u.ExpiresAtTick[i]);
                    continue;
                }
                bool s = u.IsSummoned(i);
                if (s)
                {
                    live++;
                    Assert.True(u.ExpiresAtTick[i] > ran, $"seed {seed} tick {ran}: summoned unit {i} ends at {u.ExpiresAtTick[i]}");
                    if (!wasSummoned[i] || generation[i] != u.Generation[i]) summoned++;
                }
                else halfPop[u.Owner[i]] += d.Units[u.TypeId[i]].HalfPop;
                wasSummoned[i] = s;
                generation[i] = u.Generation[i];
            }
            Assert.Equal(live, u.SummonedUnits);
            for (int p = 0; p < Players; p++) Assert.Equal(halfPop[p], w.HalfPop[p]);
            int losses = 0;
            for (int p = 0; p < Players; p++) losses += w.Losses[p];
            Assert.Equal(ordinaryDeaths, losses);
            if (u.FreeCount == 0) storeFull++;
            maxLive = Math.Max(maxLive, live);
        }
        _out.WriteLine($"seed {seed}: {summoned} summoned, {expired} expired, {killedEarly} killed early, {ordinaryDeaths} ordinary deaths, "
            + $"max {maxLive} live summons, store full after {storeFull} ticks, units left {u.Count}");
        Assert.True(summoned > 30 && expired > 10, $"{summoned} summoned, {expired} expired");
        Assert.True(killedEarly > 0, "no summoned unit was killed before its time");
        if (rec.Length == 0) return;
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, SummonTests.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
        Assert.Equal(Ticks, result.TicksRun);
    }
}
