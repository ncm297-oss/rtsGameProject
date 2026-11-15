using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S9 test copies and scenes: the shipped data with <c>rules.json</c> <c>gatherOverlap</c> written (or removed, for off) in a
/// temp copy, and hand-made gather scenes: a Keep beside one gold mine or a tree line, N workers of one radius (the Malazan
/// Laborer 0.4 m, the Teblor Gatherer 0.75 m carrying 15) or mixed, with the per-run figures docs/03 "Implementation (M6-S9)"
/// prints. Each copy sets or removes the field itself, so it holds whatever the shipped file carries (rule 58).
/// </summary>
public static class GatherOverlapScenes
{
    /// <summary>The worker types a scene fields.</summary>
    public enum Workers
    {
        /// <summary>Malazan Laborers (0.4 m, the rules' load of 10).</summary>
        Laborer,
        /// <summary>Teblor Gatherers (0.75 m, a load of 15).</summary>
        Gatherer,
        /// <summary>Alternating, a Laborer first.</summary>
        Mixed,
    }

    /// <summary>Where the workers gather.</summary>
    public enum Node
    {
        /// <summary>One 2 x 2 gold mine of <see cref="MineGold"/>, 10 m east of a Keep.</summary>
        Mine,
        /// <summary>A north-south line of trees two deep (72 trees of the rules' 100 wood) 20 m east of a Keep; every worker is sent to a tree in its middle.</summary>
        TreeLine,
    }

    private static readonly Lazy<GameData> s_off = new(() => DataWith(null));
    private static readonly Lazy<GameData> s_quarter = new(() => DataWith(0.25));
    private static readonly Lazy<GameData> s_half = new(() => DataWith(0.5));
    private static readonly Lazy<GameData> s_threeQuarters = new(() => DataWith(0.75));

    /// <summary>The shipped data with the field removed: off, today's movement.</summary>
    internal static GameData Off => s_off.Value;

    /// <summary>The test copy at <paramref name="overlap"/> (0 = <see cref="Off"/>; 0.25, 0.5 and 0.75 are cached; any other value loads a copy).</summary>
    internal static GameData DataAt(double overlap) => overlap switch
    {
        0 => Off,
        0.25 => s_quarter.Value,
        0.5 => s_half.Value,
        0.75 => s_threeQuarters.Value,
        _ => DataWith(overlap),
    };

    /// <summary>The shipped data with <c>gatherOverlap</c> set to <paramref name="overlap"/>, or removed for null.</summary>
    internal static GameData DataWith(double? overlap) => AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r =>
    {
        if (overlap == null) r.Remove("gatherOverlap");
        else r["gatherOverlap"] = overlap.Value;
    }));

    internal static int LaborerType => Laborer;

    internal static int GathererType => TestSim.Data.FindUnit("teblor_gatherer");

    /// <summary>The worker type of the <paramref name="k"/>-th worker of a <paramref name="kind"/> scene.</summary>
    internal static int TypeOf(Workers kind, int k) => kind switch
    {
        Workers.Laborer => LaborerType,
        Workers.Gatherer => GathererType,
        _ => k % 2 == 0 ? LaborerType : GathererType,
    };

    /// <summary>A sim on a flat hand-made map (<paramref name="width"/> x <paramref name="height"/> cells) on <paramref name="data"/>.</summary>
    internal static Simulation NewSim(GameData data, int units, int width = 48, int height = 40, int players = 1, ulong seed = 5) =>
        new(new SimConfig(seed, players, units, 4 * units + 32) { Data = data }, Flat(width, height));

    // The mine and tree-line layouts: a Keep (4 x 4) at cells 10-13 x 16-19; the mine at 19-20 x 17-18 (10 m east of the
    // Keep's face); the trees on columns 24-25, rows 2-37.
    internal const int KeepX = 10, KeepY = 16, MineX = 19, MineY = 17, TreeColumn = 24, TreeTop = 2, TreeBottom = 37;

    /// <summary>
    /// Gold in the scenes' mine: far more than 24 workers take in 5 minutes (about 2,500), so a row measures the loop's pace, not
    /// the mine running out (a start mine's 2,500 capped every row from 16 workers up).
    /// </summary>
    internal const int MineGold = 50_000;

    /// <summary>
    /// The scene: a Keep for player 0, the node(s), and <paramref name="count"/> workers spawned on cell centres west of the Keep
    /// (columns 2-8, rows 10-25, row by row; <paramref name="reversed"/> spawns them in the opposite order, the same points),
    /// every one ordered to gather the mine or the line's middle tree. Applies it all; returns the workers in spawn-list order.
    /// </summary>
    internal static EntityHandle[] Setup(Simulation sim, Node node, Workers kind, int count, bool reversed = false, int player = 0)
    {
        World w = sim.World;
        EntityHandle target = default;
        if (node == Node.Mine) target = Spawn(w, Mine, MineX, MineY, MineGold);
        else
        {
            for (int y = TreeTop; y <= TreeBottom; y++)
                for (int x = TreeColumn; x <= TreeColumn + 1; x++)
                {
                    EntityHandle h = Spawn(w, Tree, x, y, w.Data.Rules.TreeWood);
                    if (x == TreeColumn && y == (TreeTop + TreeBottom) / 2) target = h;
                }
        }
        Building(sim, KeepX, KeepY, player);
        var points = new List<Vector2>();
        for (int y = 10; y <= 25 && points.Count < count; y++)
            for (int x = 2; x <= 8 && points.Count < count; x++)
                points.Add(At(sim, x, y));
        Assert.Equal(count, points.Count);
        UnitStore u = w.Units;
        var before = (bool[])u.Alive.Clone();
        for (int n = 0; n < count; n++)
        {
            int k = reversed ? count - 1 - n : n;
            sim.Enqueue(Command.SpawnUnit(player, TypeOf(kind, k), points[k]));
        }
        Run(sim, 2);
        var bySlot = new List<int>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i]) bySlot.Add(i);
        Assert.Equal(count, bySlot.Count);
        // Spawn-list order: the slots were taken in spawn order, so reversed spawns hold the list back to front.
        var workers = new EntityHandle[count];
        for (int k = 0; k < count; k++)
        {
            int i = bySlot[reversed ? count - 1 - k : k];
            workers[k] = new EntityHandle(i, u.Generation[i]);
        }
        Vector2 site = At(sim, w.Resources.Cell[target.Index] % w.NavGrid.Width, w.Resources.Cell[target.Index] / w.NavGrid.Width);
        for (int n = 0; n < count; n++)
        {
            int k = reversed ? count - 1 - n : n;
            sim.Enqueue(Command.Gather(player, workers[k], site));
        }
        sim.Tick();
        return workers;
    }

    /// <summary>One run's figures (docs/03 "Implementation (M6-S9)", table (a)).</summary>
    public sealed record Figures(int Banked, int Trips, int StuckTicks, int GiveUps, float MinDistance, float MinRatio, float MeanStandGap, int StandSamples, ulong Hash)
    {
        /// <summary>The row's cells: banked, trips, stuck ticks, give-ups, the smallest pair distance (and its share of the radii sum), the mean stand gap.</summary>
        public string Cells => $"{Banked} | {Trips} | {StuckTicks} | {GiveUps} | {MinDistance:F3} m ({MinRatio:F2}) | {MeanStandGap:F3} m";
    }

    /// <summary>
    /// Ticks the scene <paramref name="ticks"/> times and measures, over its <paramref name="workers"/>: resources banked (player 0's
    /// gold + wood since the start), trips (deliveries: a load that went to 0), stuck ticks (a walker's stuck count rising while it
    /// stays Moving), give-ups (a walker on its loop that stopped Moving without its goal), the smallest centre distance between two
    /// of them (and the smallest share of its pair's radii sum), and over the second half the mean distance from each worker
    /// standing at its node (Gathering) to its nearest other worker. <paramref name="everyTick"/> sees the sim after every tick.
    /// </summary>
    internal static Figures Measure(Simulation sim, EntityHandle[] workers, int ticks, Action<Simulation, int>? everyTick = null)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int n = workers.Length;
        int startBank = w.Gold[0] + w.Wood[0];
        var cargo = new int[n];
        var state = new UnitState[n];
        var stuck = new int[n];
        int trips = 0, stuckTicks = 0, giveUps = 0, samples = 0;
        double gapSum = 0;
        float minDistance = float.PositiveInfinity, minRatio = float.PositiveInfinity;
        for (int k = 0; k < n; k++)
        {
            int i = workers[k].Index;
            cargo[k] = u.Cargo[i];
            state[k] = u.State[i];
            stuck[k] = u.StuckTicks[i];
        }
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            for (int k = 0; k < n; k++)
            {
                int i = workers[k].Index;
                Assert.True(u.IsAlive(workers[k]));
                if (cargo[k] > 0 && u.Cargo[i] == 0) trips++;
                if (state[k] == UnitState.Moving && u.State[i] == UnitState.Moving && u.StuckTicks[i] > stuck[k]) stuckTicks++;
                if (state[k] == UnitState.Moving && u.State[i] != UnitState.Moving && u.GoalCell[i] < 0 && u.GatherNode[i].Generation != 0) giveUps++;
                cargo[k] = u.Cargo[i];
                state[k] = u.State[i];
                stuck[k] = u.StuckTicks[i];
                float nearest = float.PositiveInfinity;
                for (int m = 0; m < n; m++)
                {
                    if (m == k) continue;
                    int j = workers[m].Index;
                    float d = Vector2.Distance(u.Position[i], u.Position[j]);
                    if (d < nearest) nearest = d;
                    if (m < k) continue;
                    if (d < minDistance) minDistance = d;
                    float ratio = d / (u.Radius[i] + u.Radius[j]);
                    if (ratio < minRatio) minRatio = ratio;
                }
                if (t >= ticks / 2 && u.State[i] == UnitState.Gathering && n > 1)
                {
                    gapSum += nearest;
                    samples++;
                }
            }
            everyTick?.Invoke(sim, t);
        }
        return new Figures(w.Gold[0] + w.Wood[0] - startBank, trips, stuckTicks, giveUps, minDistance, minRatio,
            samples > 0 ? (float)(gapSum / samples) : float.NaN, samples, sim.StateHash());
    }

    /// <summary>A fresh scene at <paramref name="overlap"/> run for <paramref name="ticks"/> ticks: its figures.</summary>
    internal static Figures Row(double overlap, Node node, Workers kind, int count, int ticks, bool reversed = false)
    {
        Simulation sim = NewSim(DataAt(overlap), count + 4);
        EntityHandle[] workers = Setup(sim, node, kind, count, reversed);
        return Measure(sim, workers, ticks);
    }

    /// <summary>The scaled pack limit for a pair of radii sum <paramref name="sum"/> at <paramref name="overlap"/>: ShoveSpacing x (1 - overlap) x sum.</summary>
    internal static float PackLimit(float sum, double overlap) => MovementConstants.ShoveSpacing * (1f - (float)overlap) * sum;
}
