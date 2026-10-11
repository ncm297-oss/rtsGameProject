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
/// QA M4-4b-3b (session 2026-10-10-1314): a command-spam fuzz around summons on a generated map with three players. Every
/// tick up to 24 seeded commands: casts with any ability index 0-4 (out of range included) by live, dead or stale handles
/// (the summoned units included), moves, attack-moves, attacks on random handles, stops, points off the map. Twins hash-equal
/// every tick; after every tick: the summoned count equals the live flagged slots, a live summoned unit ends after the tick
/// just run, a free slot has no end tick, the population counts no summoned unit, every live unit has 1..max hp and a finite
/// position on the map, each death event's victim is listed once and is gone, an Expired event belongs to a unit that was
/// summoned with exactly this tick as its end, and the losses are exactly the non-expired deaths. One seed's replay
/// round-trips and plays back equal; the three seeds end on three different hashes.
/// </summary>
public class SummonQaFuzzStressTests
{
    private const int Ticks = 1500;
    private const int Players = 3;
    private const int Capacity = 72;
    private readonly ITestOutputHelper _out;

    public SummonQaFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(new SimConfig(seed, Players, Capacity, 1024) { Data = SummonTests.Data });
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 25);
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        var rng = new SimRng(seed, 4501);
        GameData d = SummonTests.Data;
        int[] types = { d.FindUnit("malazan_cadre_mage"), d.FindUnit("whirlwind_priest"), d.FindUnit("whirlwind_raider"), d.FindUnit("malazan_heavy_infantry") };
        int centre = open[rng.NextInt(0, open.Count)];
        int cx = centre % g.Width, cy = centre / g.Width;
        for (int p = 0; p < Players; p++)
            for (int k = 0; k < 10; k++)
            {
                int c = -1;
                for (int tries = 0; tries < 30 && c < 0; tries++)
                {
                    int x = cx + (p - 1) * 6 + rng.NextInt(-5, 6), y = cy + rng.NextInt(-5, 6);
                    if (x >= 0 && y >= 0 && x < g.Width && y < g.Height && g.IsPassable(x, y)) c = y * g.Width + x;
                }
                if (c < 0) c = open[rng.NextInt(0, open.Count)];
                sim.Enqueue(Command.SpawnUnit(p, types[k < 4 ? k % 2 : 2 + k % 2], g.CellCenter(c % g.Width, c / g.Width)));
            }
        return sim;
    }

    /// <summary>Up to 24 seeded commands, the same on both twins (built from twin a's state, which equals b's).</summary>
    private static void Spam(Simulation a, Simulation b, ref SimRng rng)
    {
        World w = a.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        float wM = g.Width * MapConstants.CellSize, hM = g.Height * MapConstants.CellSize;
        int n = rng.NextInt(0, 25);
        for (int k = 0; k < n; k++)
        {
            int slot = rng.NextInt(0, u.Capacity);
            int gen = u.Generation[slot] - (rng.NextInt(0, 5) == 0 ? 1 : 0); // sometimes stale
            var h = new EntityHandle(slot, gen);
            int player = u.Alive[slot] && rng.NextInt(0, 6) != 0 ? u.Owner[slot] : rng.NextInt(0, Players);
            var point = new Vector2(rng.NextInt(-20, (int)wM + 20), rng.NextInt(-20, (int)hM + 20));
            if (rng.NextInt(0, 3) != 0) point = u.Position[slot] + new Vector2(rng.NextInt(-12, 13), rng.NextInt(-12, 13));
            bool queued = rng.NextInt(0, 4) == 0;
            Command c = rng.NextInt(0, 6) switch
            {
                0 or 1 => Command.UseAbility(player, h, rng.NextInt(0, 5), point, queued),
                2 => Command.Move(player, h, point, queued),
                3 => Command.AttackMove(player, h, point, queued),
                4 => Command.Attack(player, h, new EntityHandle(rng.NextInt(0, u.Capacity), u.Generation[rng.NextInt(0, u.Capacity)]), false, queued),
                _ => Command.Stop(player, h, queued),
            };
            a.Enqueue(c);
            b.Enqueue(c);
        }
    }

    [Theory]
    [InlineData(21UL)]
    [InlineData(22UL)]
    [InlineData(23UL)]
    public void CommandSpamAroundSummons_TwinsEqual_InvariantsHoldEveryTick(ulong seed)
    {
        ulong final = Run(seed);
        lock (s_finals)
        {
            Assert.DoesNotContain(final, s_finals.Values);
            s_finals[seed] = final;
        }
    }

    private static readonly Dictionary<ulong, ulong> s_finals = new();

    private ulong Run(ulong seed)
    {
        var rec = new ReplayRecorder?[seed == 21UL ? 1 : 0];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        UnitStore u = w.Units;
        GameData d = w.Data;
        var rng = new SimRng(seed, 4502);
        var prevEnd = new int[u.Capacity];
        var prevGen = new int[u.Capacity];
        int expired = 0, killed = 0, summonedSeen = 0, maxLive = 0;
        var seen = new HashSet<EntityHandle>();
        for (int t = 0; t < Ticks; t++)
        {
            if (t > 2) Spam(a, b, ref rng);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            int ran = a.TickNumber - 1;
            seen.Clear();
            foreach (DeathEvent e in w.Deaths)
            {
                if (e.IsBuilding) continue;
                Assert.True(seen.Add(e.Victim), $"seed {seed} tick {ran}: {e.Victim} died twice");
                Assert.False(u.IsAlive(e.Victim), $"seed {seed} tick {ran}: {e.Victim} has a death event and lives");
                if (e.Expired)
                {
                    expired++;
                    Assert.Equal(-1, e.KillerOwner);
                    Assert.True(prevGen[e.Victim.Index] == e.Victim.Generation && prevEnd[e.Victim.Index] == ran,
                        $"seed {seed} tick {ran}: {e.Victim} expired, its end was {prevEnd[e.Victim.Index]} (gen {prevGen[e.Victim.Index]})");
                }
                else killed++;
            }
            int live = 0;
            var halfPop = new int[Players];
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i])
                {
                    Assert.Equal(0, u.ExpiresAtTick[i]);
                    prevEnd[i] = 0;
                    continue;
                }
                Assert.InRange(u.Hp[i], 1, w.MaxHp(i));
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && w.NavGrid.WorldToCell(p, out _, out _), $"seed {seed} tick {ran}: unit {i} at {p}");
                if (u.IsSummoned(i))
                {
                    live++;
                    Assert.True(u.ExpiresAtTick[i] > ran, $"seed {seed} tick {ran}: summoned {i} ends at {u.ExpiresAtTick[i]}");
                    if (prevEnd[i] == 0 || prevGen[i] != u.Generation[i]) summonedSeen++;
                }
                else halfPop[u.Owner[i]] += d.Units[u.TypeId[i]].HalfPop;
                prevEnd[i] = u.ExpiresAtTick[i];
                prevGen[i] = u.Generation[i];
            }
            Assert.Equal(live, u.SummonedUnits);
            for (int p = 0; p < Players; p++) Assert.Equal(halfPop[p], w.HalfPop[p]);
            int losses = 0;
            for (int p = 0; p < Players; p++) losses += w.Losses[p];
            Assert.Equal(killed, losses);
            maxLive = Math.Max(maxLive, live);
        }
        _out.WriteLine($"seed {seed}: {summonedSeen} summoned, {expired} expired, {killed} killed, max {maxLive} live summons, {u.Count} units left");
        Assert.True(summonedSeen > 20 && expired > 5, $"seed {seed}: {summonedSeen} summoned, {expired} expired: not a summon fuzz");
        if (rec.Length > 0)
        {
            Replay replay = rec[0]!.ToReplay();
            Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
            ReplayResult result = ReplayPlayer.Run(back!, SummonTests.Data);
            Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
            Assert.Equal(Ticks, result.TicksRun);
        }
        return a.StateHash();
    }
}
