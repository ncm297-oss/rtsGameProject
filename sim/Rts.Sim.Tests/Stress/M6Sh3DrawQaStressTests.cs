using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Draw;
using Rts.Sim.Tests.QA;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-SH3 (session 2026-11-27-0817, tier full, QA focus 2 and 7): the exact "can still train" under a wider fuzz than the
/// developer's (seeds 1,001-1,200 in the suite, 1,500 ticks; 1,001-1,400 run once in QA; wood nodes, drop-off sites placed and finished, workers sent to trees, on top of
/// <see cref="DrawRuleQaTests.Disturb"/>): every tick, for every undefeated seat, the sim's answer equals a store scan
/// (<see cref="DrawReachScan"/>), "new can" implies "old can" (old spent implies new spent), and every counter equals a scan.
/// The tick cost of the draw check with 8 seats and 2,000 units is <see cref="M6Sh3DrawCostQaPerfTests"/>.
/// </summary>
public class M6Sh3DrawQaStressTests
{
    private readonly ITestOutputHelper _out;

    public M6Sh3DrawQaStressTests(ITestOutputHelper output) => _out = output;

    private static int WoodType(GameData d)
    {
        for (int t = 0; t < d.Resources.Length; t++)
            if (d.Resources[t].Resource == ResourceKind.Wood) return t;
        throw new InvalidOperationException("no wood");
    }

    /// <summary>The extra disturbances: a wood node, a Town Hall site for a seat, a site finished at once, a worker to a tree, a worker carrying a forged load.</summary>
    private static void DisturbMore(Simulation sim, ref SimRng rng)
    {
        World w = sim.World;
        int n = w.NavGrid.Width;
        int roll = rng.NextInt(0, 6);
        int p = rng.NextInt(0, w.Config.PlayerCount);
        switch (roll)
        {
            case 0:
                w.Resources.Spawn(WoodType(w.Data), rng.NextInt(4, n - 4) + rng.NextInt(4, n - 4) * n, 1 + rng.NextInt(0, 60), out _);
                break;
            case 1:
            {
                int type = StartBase.BuildingOfSlot(w.Data, w.FactionOf(p), BuildingSlot.TownHall);
                w.Buildings.Spawn(p, type, rng.NextInt(4, n - 10) + rng.NextInt(4, n - 10) * n, out _, true);
                break;
            }
            case 2:
                for (int k = 0; k < w.Buildings.Capacity; k++)
                    if (w.Buildings.Alive[k] && w.Buildings.UnderConstruction[k] && rng.NextInt(0, 2) == 0)
                    {
                        w.Buildings.SetWork(k, w.Buildings.WorkNeeded(w.Buildings.TypeId[k]));
                        break;
                    }
                break;
            case 3:
            case 4:
            {
                // A worker of the seat to a random live node of any kind.
                for (int i = 0; i < w.Units.Capacity; i++)
                {
                    if (!w.Units.Alive[i] || w.Units.Owner[i] != p || !w.Data.Units[w.Units.TypeId[i]].IsWorker) continue;
                    for (int r = 0; r < w.Resources.Capacity; r++)
                        if (w.Resources.Alive[r] && rng.NextInt(0, 2) == 0)
                        {
                            int cell = w.Resources.Cell[r];
                            sim.Enqueue(Command.Gather(p, new EntityHandle(i, w.Units.Generation[i]), new Vector2(cell % n + 0.5f, cell / n + 0.5f) * Map.MapConstants.CellSize));
                            break;
                        }
                    break;
                }
                break;
            }
            default:
                // A new worker for the seat (more seats with the reach terms in play).
                DrawScenes.Unit(sim, p, rng.NextInt(4, n - 4), rng.NextInt(4, n - 4), UnitSlot.Worker);
                break;
        }
    }

    public static TheoryData<int> Batches()
    {
        var rows = new TheoryData<int>();
        for (int b = 0; b < 8; b++) rows.Add(b);
        return rows;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Batches))]
    public void TheExactRule_EqualsAScan_NarrowsTheOld_CountersAgree_EveryTick(int batch)
    {
        long states = 0, narrowed = 0, oldSpent = 0, newSpent = 0, drawn = 0;
        for (int k = 0; k < 25; k++)
        {
            ulong seed = (ulong)(1001 + batch * 25 + k);
            Simulation sim = DrawRuleQaTests.Scene(seed, DrawScenes.Short, out SimRng rng);
            World w = sim.World;
            for (int t = 0; t < 1500 && !sim.MatchOver; t++)
            {
                int r = rng.NextInt(0, 4);
                if (r == 0) DrawRuleQaTests.Disturb(sim, ref rng);
                else if (r == 1) DisturbMore(sim, ref rng);
                sim.Tick();
                string? fail = DrawRuleQaTests.CountersAgree(w);
                Assert.True(fail == null, $"seed {seed} tick {sim.TickNumber}: {fail}");
                bool anyOld = false, anyNew = false;
                for (int p = 0; p < w.Config.PlayerCount; p++)
                {
                    if (w.IsDefeated(p)) continue;
                    bool now = DrawReachScan.NewCan(w, p), old = DrawReachScan.OldCan(w, p);
                    Assert.True(!now || old, $"seed {seed} tick {sim.TickNumber}: new can, old cannot: {DrawReachScan.Line(w, p)}");
                    Assert.True(w.CanStillTrain(p) == now, $"seed {seed} tick {sim.TickNumber}: sim {w.CanStillTrain(p)}: {DrawReachScan.Line(w, p)}");
                    if (old && !now) narrowed++;
                    anyOld |= old;
                    anyNew |= now;
                }
                if (!anyOld) oldSpent++;
                if (!anyNew) newSpent++;
                states++;
            }
            if (w.MatchDrawn) drawn++;
        }
        _out.WriteLine($"batch {batch}: {states} states, no seat can train: old {oldSpent}, new {newSpent}; seat-states narrowed {narrowed}; drawn {drawn} of 25");
        Assert.True(narrowed > 0);
        Assert.True(newSpent >= oldSpent);
    }
}
