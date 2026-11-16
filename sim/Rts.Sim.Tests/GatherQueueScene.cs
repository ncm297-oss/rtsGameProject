using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S11: a generated map (12 forests, 8 mines) with a Keep and 12 gathering Laborers per player, and a seeded command
/// stream that lands Shift-queued orders of every worker kind (Move, AttackMove, Attack, Gather on any node, Build, Repair,
/// Stop, Hold; now and then nine at once, past the queue's 8) and some unqueued ones on gathering workers, and sends an Idle
/// worker with an empty queue back to a node, so loops with queues behind them happen all match long. Every command goes
/// through the command door (a recorder attached from tick 0 replays it). The stream reads only sim <paramref name="source"/>;
/// <see cref="Issue"/> enqueues the same commands into every sim it is given.
/// </summary>
internal sealed class GatherQueueScene
{
    public const int WorkersPerPlayer = 12;

    public readonly Simulation Sim;
    public readonly ReplayRecorder? Recorder;
    public readonly EntityHandle[] Workers;
    private readonly List<Vector2> _nodes = new();
    private readonly List<Vector2> _open = new();
    private SimRng _rng;
    private readonly int _house;

    private GatherQueueScene(Simulation sim, ReplayRecorder? recorder, EntityHandle[] workers, ulong seed)
    {
        Sim = sim;
        Recorder = recorder;
        Workers = workers;
        _rng = new SimRng(seed, 1490);
        _house = sim.World.Data.FindBuilding("malazan_billet");
        World w = sim.World;
        NavGrid g = w.NavGrid;
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n]) continue;
            int c = w.Resources.Cell[n];
            _nodes.Add(g.CellCenter(c % g.Width, c / g.Width));
        }
        // Points near the workers (the Keeps' surroundings): within 24 m of some worker at the start.
        for (int c = 0; c < g.Width * g.Height; c += 3)
        {
            if (!g.IsPassable(c % g.Width, c / g.Width)) continue;
            Vector2 p = g.CellCenter(c % g.Width, c / g.Width);
            foreach (EntityHandle h in workers)
                if (Vector2.Distance(p, w.Units.Position[h.Index]) < 24f) { _open.Add(p); break; }
        }
    }

    /// <summary>The scene for <paramref name="seed"/> on <paramref name="data"/> (shipped by default), recorded when <paramref name="record"/>.</summary>
    public static GatherQueueScene Create(ulong seed, bool record = false, GameData? data = null)
    {
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512)
            with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8, StartLocations = 0 } };
        if (data != null) config = config with { Data = data };
        var sim = new Simulation(config);
        ReplayRecorder? recorder = record ? new ReplayRecorder(sim, checkpointInterval: 100) : null;
        EntityHandle[] a = EconomyScenario.Setup(sim, WorkersPerPlayer, player: 0, mineIndex: 0);
        EntityHandle[] b = EconomyScenario.Setup(sim, WorkersPerPlayer, player: 1, mineIndex: 1);
        return new GatherQueueScene(sim, recorder, a.Concat(b).ToArray(), seed);
    }

    /// <summary>A copy of the stream's position, so a twin scene (a loaded save) continues the same stream.</summary>
    public SimRng Rng { get => _rng; set => _rng = value; }

    /// <summary>
    /// One step of the stream (call every tick; it acts every 10th): commands built from <see cref="Sim"/>'s state, enqueued
    /// into <paramref name="into"/> (default: <see cref="Sim"/> alone).
    /// </summary>
    public void Issue(int t, params Simulation[] into)
    {
        if (into.Length == 0) into = new[] { Sim };
        if (t % 10 != 0) return;
        World w = Sim.World;
        UnitStore u = w.Units;
        foreach (EntityHandle h in Workers)
        {
            if (!u.IsAlive(h)) continue;
            int i = h.Index, player = u.Owner[i];
            if (!EconomySystem.OnLoop(u, i) && u.QueueCount[i] == 0 && u.State[i] == UnitState.Idle && u.Target[i].Generation == 0)
            {
                if (_rng.NextInt(0, 4) == 0) Send(into, Command.Gather(player, h, _nodes[_rng.NextInt(0, _nodes.Count)]));
                continue;
            }
            int roll = _rng.NextInt(0, 100);
            if (roll < 6) Send(into, Queue(player, h, queued: true));
            else if (roll == 6) Send(into, Queue(player, h, queued: false));
            else if (roll == 7)
                for (int k = 0; k < OrderConstants.QueueCapacity + 1; k++) Send(into, Command.Move(player, h, Near(), queued: true));
        }
    }

    private static void Send(Simulation[] into, Command c)
    {
        foreach (Simulation s in into) s.Enqueue(c);
    }

    private Vector2 Near() => _open[_rng.NextInt(0, _open.Count)];

    private Command Queue(int player, EntityHandle h, bool queued)
    {
        World w = Sim.World;
        switch (_rng.NextInt(0, 9))
        {
            case 0: return Command.Move(player, h, Near(), queued);
            case 1: return Command.AttackMove(player, h, Near(), queued);
            case 2:
            {
                EntityHandle enemy = Workers[_rng.NextInt(0, Workers.Length)];
                return w.Units.IsAlive(enemy) && w.Units.Owner[enemy.Index] != player
                    ? Command.Attack(player, h, enemy, isBuilding: false, queued)
                    : Command.Move(player, h, Near(), queued);
            }
            case 3: case 4: return Command.Gather(player, h, _nodes[_rng.NextInt(0, _nodes.Count)], queued);
            case 5: return Command.Build(player, h, _house, Near(), queued);
            case 6:
            {
                BuildingStore b = w.Buildings;
                for (int k = 0; k < b.Capacity; k++)
                    if (b.Alive[k] && b.Owner[k] == player) return Command.Repair(player, h, w.NavGrid.CellCenter(b.Cell[k] % w.NavGrid.Width, b.Cell[k] / w.NavGrid.Width), queued);
                return Command.Stop(player, h, queued);
            }
            case 7: return Command.Stop(player, h, queued);
            default: return Command.HoldPosition(player, h, queued);
        }
    }
}
