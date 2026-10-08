using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-2) read-only proof: a sim driven the way the view drives it, with every ViewApi helper reading it each frame, hashes the same every tick as a bare twin fed the same commands.</summary>
/// <remarks>
/// Twin A plays the view: Match's start armies (StartLayout blocks, player p = faction p, roster
/// round-robin), several "frames" per tick that call TerrainHeight.At on every interpolated unit,
/// SelectionSet.Prune, ScreenPicker click and box over a projection of the units, GroundPicker on
/// camera rays, and FixedStepClock; right-click moves go to the GroundPicker point for every
/// selected unit; since M2-3 the order is any of Move / AttackMove / Stop / HoldPosition, queued or
/// not, with control-group assigns, recalls and Tab subgroup refreshes in between. Twin B gets exactly the commands A sent and nothing else. Also asserts A's hash
/// doesn't move across a frame's helper calls.
/// </remarks>
public class ViewHashTwinQaTests
{
    private readonly ITestOutputHelper _out;

    public ViewHashTwinQaTests(ITestOutputHelper output) => _out = output;

    private static SimConfig RunnerConfig(ulong seed) => TestSim.ConfigNoCombat(seed, PlayerCount: 2, UnitCapacity: 2000, CommandCapacity: 4096);

    // Match.SpawnArmies, rewritten here from the brief and docs/03 "Implementation (M2-2)".
    private static List<Command> StartArmies(Simulation sim, int perPlayer)
    {
        var cmds = new List<Command>();
        GameData data = sim.World.Data;
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = data.Factions[p];
            float maxR = 0f;
            foreach (int t in f.Units) maxR = Math.Max(maxR, data.Units[t].Radius);
            Vector2[] spots = StartLayout.Block(sim.World.NavGrid, perPlayer, p == 0, maxR);
            for (int k = 0; k < spots.Length; k++) cmds.Add(Command.SpawnUnit(p, f.Units[k % f.Units.Length], spots[k]));
        }
        return cmds;
    }

    [Theory]
    [InlineData(1UL, 100)]
    [InlineData(77UL, 1000)]
    public void ViewDrivenSim_HashesEqualToABareTwin_EveryTick(ulong seed, int perPlayer)
    {
        var a = new Simulation(RunnerConfig(seed));
        var b = new Simulation(RunnerConfig(seed));
        World w = a.World;
        UnitStore u = w.Units;
        Heightmap map = w.Heightmap;
        var selection = new SelectionSet(u.Capacity);
        var groups = new ControlGroups(u.Capacity);
        var subgroups = new Subgroups(w.Data.Units.Length);
        var kinds = new int[(int)CommandKind.AttackMove + 1];
        var clock = new FixedStepClock();
        var centers = new Vector2[u.Capacity];
        var radii = new float[u.Capacity];
        var cand = new bool[u.Capacity];
        var picked = new int[u.Capacity];
        var rng = new SimRng(seed ^ 0xABCDEF, RngStream.Combat); // the test's own stream, never the sim's
        _ = TerrainMeshBuilder.Build(map);

        foreach (Command c in StartArmies(a, perPlayer)) { a.Enqueue(c); b.Enqueue(c); }

        int ticks = 0, orders = 0, frames = 0;
        while (ticks < 600)
        {
            frames++;
            ulong before = a.StateHash();
            float alpha = (float)clock.Alpha;
            float sum = 0f;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) { cand[i] = false; continue; }
                Vector2 p = Vector2.Lerp(u.PrevPosition[i], u.Position[i], alpha);
                sum += TerrainHeight.At(map, p.X, p.Y);
                centers[i] = p * 4f; // a top-down "screen", 4 px per meter
                radii[i] = u.Radius[i] * 4f;
                cand[i] = u.Owner[i] == 0;
            }
            selection.Prune(u.Alive, u.Generation);
            groups.Prune(u.Alive, u.Generation);
            subgroups.Update(selection.Items, u.TypeId, reset: false);

            if (frames % 37 == 0)
            {
                // Box or click select, then right-click a ground point.
                Vector2 c0 = new(rng.NextFloat() * map.Width * 8f, rng.NextFloat() * map.Height * 8f);
                if (rng.NextInt(0, 2) == 0)
                {
                    int n = ScreenPicker.PickBox(centers, cand, c0, c0 + new Vector2(200, 160), picked);
                    if (rng.NextInt(0, 3) != 0) selection.Clear();
                    for (int k = 0; k < n; k++) selection.Add(new EntityHandle(picked[k], u.Generation[picked[k]]));
                }
                else
                {
                    int s = ScreenPicker.PickClick(centers, radii, cand, c0);
                    if (s >= 0) selection.Toggle(new EntityHandle(s, u.Generation[s]));
                }
                var origin = new Vector3(rng.NextFloat() * map.Width * 2f, rng.NextInt(20, 61), rng.NextFloat() * map.Height * 2f + 30f);
                Vector3 dir = Vector3.Normalize(new Vector3(rng.NextFloat() - 0.5f, -1.2f, -0.8f));
                bool picked3 = GroundPicker.TryPick(map, origin, dir, out Vector3 hit);
                Assert.Equal(before, a.StateHash()); // the reads and picks changed nothing (Enqueue below does, by design)
                // M2-3: SelectionController.Order's kinds (all four, queued or not), control-group
                // assigns and recalls and the Tab subgroup refresh between orders.
                int g = rng.NextInt(0, ControlGroups.Count);
                if (rng.NextInt(0, 3) == 0) groups.Assign(g, selection.Items);
                else if (groups.Recall(g, selection, u.Alive, u.Generation) > 0 && groups.TryMean(g, u.Position, u.Alive, u.Generation, out Vector2 mean)) sum += mean.X;
                subgroups.Update(selection.Items, u.TypeId, reset: true);
                subgroups.Next();
                Assert.Equal(before, a.StateHash());
                var kind = (CommandKind)rng.NextInt((int)CommandKind.Move, (int)CommandKind.AttackMove + 1);
                bool queued = rng.NextInt(0, 2) == 0;
                bool positional = kind is CommandKind.Move or CommandKind.AttackMove;
                if ((picked3 || !positional) && a.PendingCommandCount + selection.Count <= 4096)
                {
                    var target = new Vector2(hit.X, hit.Z);
                    foreach (EntityHandle h in selection.Items)
                    {
                        Command m = kind switch
                        {
                            CommandKind.Move => Command.Move(0, h, target, queued),
                            CommandKind.AttackMove => Command.AttackMove(0, h, target, queued),
                            CommandKind.Stop => Command.Stop(0, h, queued),
                            _ => Command.HoldPosition(0, h, queued),
                        };
                        a.Enqueue(m);
                        b.Enqueue(m);
                        orders++;
                        kinds[(int)kind]++;
                    }
                }
            }
            else Assert.Equal(before, a.StateHash()); // helpers in this frame changed nothing
            Assert.True(float.IsFinite(sum));

            // A ragged frame rate, like a real game loop.
            int due = clock.Advance(0.004 + rng.NextFloat() * 0.03, 1.0);
            for (int k = 0; k < due; k++)
            {
                a.Tick();
                b.Tick();
                ticks++;
                Assert.True(a.StateHash() == b.StateHash(), $"view-driven sim diverged from its bare twin at tick {a.TickNumber}");
            }
        }
        _out.WriteLine($"seed {seed}, {perPlayer}/player: {ticks} ticks, {frames} frames, {orders} orders " +
            $"(move {kinds[2]}, stop {kinds[3]}, hold {kinds[4]}, attack-move {kinds[5]}), {u.Count} units alive, final hash {a.StateHash():X16}");
        Assert.Equal(2 * perPlayer, u.Count);
        Assert.True(orders > 0, "no orders were issued");
        for (int k = (int)CommandKind.Move; k < kinds.Length; k++) Assert.True(kinds[k] > 0, $"no {(CommandKind)k} orders were issued");
    }
}
