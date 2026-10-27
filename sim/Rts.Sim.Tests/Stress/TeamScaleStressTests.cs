using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Spatial;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA for M6-S4 at scale: 2,500 units of four seats as 2v2 (teams 1, 2, 1, 2: each side two non-adjacent players, so a side's
/// hash code is its lowest player and the other's units carry a code that is not their owner) attack-moving through each
/// other on a flat 192 map, the two players of a side interleaved in its blocks. Every tick for 500 ticks: no unit targets an
/// ally, and a spatial hash rebuilt on the live units with the world's alliance table answers QueryEnemies, NearestEnemy and
/// OthersThan as a brute force does. Seeds 1-3. A second row times the tick against the same layout as a plain 1v1 (players
/// 0 and 1 owning the same units), the cost of sides over owners.
/// </summary>
[Collection(SerialCollection.Name)]
public class TeamScaleStressTests
{
    private readonly ITestOutputHelper _out;

    public TeamScaleStressTests(ITestOutputHelper output) => _out = output;

    private const int PerSide = 1250, Size = 192;

    /// <summary>The scene: <paramref name="twoVsTwo"/> four seats 1, 2, 1, 2, else two seats; west block side A, east block side B.</summary>
    private static Simulation Brawl(ulong seed, bool twoVsTwo)
    {
        int players = twoVsTwo ? 4 : 2;
        var config = TestSim.Config(seed, players, 2 * PerSide + 16, 8192) with
        {
            Seats = twoVsTwo ? TeamTests.Seats(1, 2, 1, 2) : TeamTests.Seats(0, 0),
        };
        var sim = new Simulation(config, LocalMovementTests.Flat(Size));
        var rng = new Random((int)seed);
        int[] types = { HeavyInfantry, Crossbowman, HeavyInfantry, Laborer };
        float meters = Size * Map.MapConstants.CellSize;
        for (int k = 0; k < PerSide; k++)
        {
            for (int side = 0; side < 2; side++)
            {
                // Side A = players 0 and 2 (2v2) or 0; side B = 1 and 3, or 1. Interleaved in each block.
                int owner = twoVsTwo ? side + 2 * (k % 2) : side;
                float x = side == 0 ? 40f + (k / 25) * 1.6f : meters - 40f - (k / 25) * 1.6f;
                float y = 60f + (k % 25) * 1.6f + (float)rng.NextDouble() * 0.2f;
                EntityHandle h = Place(sim, owner, types[k % types.Length], new Vector2(x, y));
                sim.Enqueue(Command.AttackMove(owner, h, new Vector2(side == 0 ? meters - 20f : 20f, y)));
            }
        }
        return sim;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void TwoThousandFiveHundredUnits_2v2_NoAllyTargeted_TheHashEqualsTheBruteForce(ulong seed)
    {
        Simulation sim = Brawl(seed, twoVsTwo: true);
        World w = sim.World;
        UnitStore u = w.Units;
        var allied = new bool[16];
        for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) allied[a * 4 + b] = w.AreAllied(a, b);
        var oracle = new SpatialHash(u.Capacity, w.NavGrid.Width, w.NavGrid.Height, allied, 4);
        var found = new int[u.Capacity];
        var probe = new Random((int)seed + 11);
        float meters = Size * Map.MapConstants.CellSize;
        int targeted = 0;
        for (int t = 0; t < 500; t++)
        {
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                EntityHandle tg = u.Target[i];
                if (tg.Generation == 0 || u.TargetIsBuilding[i] || !u.IsAlive(tg)) continue;
                targeted++;
                Assert.True(w.IsEnemy(u.Owner[i], u.Owner[tg.Index]), $"seed {seed} tick {sim.TickNumber}: unit {i} (p{u.Owner[i]}) targets an ally's unit");
            }
            if (t % 5 != 0) continue;
            oracle.Rebuild(u);
            for (int q = 0; q < 4; q++)
            {
                // Queries round the fronts (where the blocks meet) and anywhere.
                int s = probe.Next(u.Capacity);
                Vector2 c = q < 2 && u.Alive[s]
                    ? u.Position[s]
                    : new Vector2((float)probe.NextDouble() * meters, 40f + (float)probe.NextDouble() * 60f);
                if (!float.IsFinite(c.X)) continue;
                float r = 2f + (float)probe.NextDouble() * 14f;
                int player = q % 4;
                int n = oracle.QueryEnemies(c, r, player, found);
                int[] got = found[..Math.Min(n, found.Length)];
                Array.Sort(got);
                var want = new List<int>();
                int best = -1;
                float bestD = float.MaxValue;
                int enemies = 0;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || w.AreAllied(player, u.Owner[i])) continue;
                    enemies++;
                    float d2 = Vector2.DistanceSquared(u.Position[i], c);
                    if (!(d2 <= r * r)) continue;
                    want.Add(i);
                    if (d2 < bestD) (best, bestD) = (i, d2);
                }
                Assert.Equal(want.ToArray(), got);
                Assert.Equal(best >= 0, oracle.NearestEnemy(c, r, player, out int slot));
                Assert.Equal(best, slot);
                Assert.Equal(enemies, oracle.OthersThan(player));
            }
        }
        _out.WriteLine($"seed {seed}: {targeted} unit-ticks of targeting; losses {string.Join(", ", w.Losses.ToArray())}");
        Assert.True(targeted > 10_000, "no fight");
    }

    /// <summary>The tick of the 2v2 against the same layout as a 1v1, measured (reported, with a loose 2x bound: sides must not cost more than owners did).</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TwoThousandFiveHundredUnits_2v2_TickCost_NearThe1v1s()
    {
        double Run(bool twoVsTwo)
        {
            Simulation sim = Brawl(1, twoVsTwo);
            for (int t = 0; t < 60; t++) sim.Tick(); // to contact
            var sw = Stopwatch.StartNew();
            for (int t = 0; t < 100; t++) sim.Tick();
            return sw.Elapsed.TotalMilliseconds / 100;
        }
        Run(true); // warm-up
        double ffa = Math.Min(Run(false), Run(false)), teams = Math.Min(Run(true), Run(true));
        _out.WriteLine($"2,500 units brawl, ms per tick: 1v1 {ffa:F2}, 2v2 {teams:F2} ({teams / ffa:F2}x)");
        Assert.True(teams < 2 * ffa, $"2v2 {teams:F2} ms vs 1v1 {ffa:F2} ms");
    }
}
