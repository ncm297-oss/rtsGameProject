using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Pathfinding;

namespace Rts.Sim;

/// <summary>Owns a <see cref="World"/> and advances it one fixed 50 ms tick at a time.</summary>
/// <remarks>
/// The only entry points that change state are <see cref="Enqueue"/> (deferred) and
/// <see cref="Tick"/>. Phase order follows docs/03 "Tick model"; phases after "apply commands"
/// are added by later milestones.
/// </remarks>
public sealed class Simulation
{
    private readonly CommandQueue _commands;
    private readonly int[] _nextSequence;

    /// <summary>Creates a simulation for the given match setup.</summary>
    public Simulation(SimConfig config) : this(config, null)
    {
    }

    /// <summary>Test seam: a simulation on a hand-made map instead of the generated one.</summary>
    internal Simulation(SimConfig config, Heightmap? map)
    {
        World = new World(config, map);
        _commands = new CommandQueue(config.CommandCapacity);
        _nextSequence = new int[config.PlayerCount];
    }

    /// <summary>The simulated state. Read it; never write it from outside the sim.</summary>
    public World World { get; }

    /// <summary>Number of the next tick to run; 0 before the first <see cref="Tick"/>.</summary>
    public int TickNumber => World.TickNumber;

    /// <summary>Number of commands waiting to apply.</summary>
    public int PendingCommandCount => _commands.Count;

    /// <summary>Queues a command for tick <c>TickNumber + 1</c>, stamped with the player's next sequence number.</summary>
    /// <remarks>
    /// Always one tick ahead, whether called between ticks (input) or during one (AI think), so a
    /// command never changes the tick that is already running.
    /// </remarks>
    public void Enqueue(Command command)
    {
        if ((uint)command.Player >= (uint)_nextSequence.Length)
            throw new ArgumentOutOfRangeException(nameof(command), $"Unknown player {command.Player}.");
        command.Tick = World.TickNumber + 1;
        command.Sequence = _nextSequence[command.Player];
        _commands.Add(in command); // throws when full; bump the counter only once accepted (BUG-0004)
        _nextSequence[command.Player]++;
    }

    /// <summary>Runs one tick: applies this tick's commands in (player, sequence) order, rebuilds the spatial hash, moves units, then bumps <see cref="TickNumber"/>.</summary>
    public void Tick()
    {
        World.Units.SnapshotPrevPositions();

        // Phase 1: apply commands.
        _commands.Sort();
        int due = _commands.CountDue(World.TickNumber);
        for (int i = 0; i < due; i++)
            Apply(in _commands[i]);
        _commands.RemoveFront(due);

        // Neighbour index for every later phase; right after commands so this tick's spawns are queryable.
        World.Spatial.Rebuild(World.Units);

        // Phases 8-9: flow fields (fetched or built on demand) and movement.
        MovementSystem.Run(World);

        // Phase 13: cleanup.
        World.TickNumber++;
    }

    /// <summary>64-bit FNV-1a hash of all gameplay state: tick, units, RNG streams, flow-field cache metadata, and pending commands.</summary>
    /// <remarks>
    /// Derived state is left out: the spatial hash (rebuilt from the units every tick) and
    /// Speed/Radius (they follow from TypeId). The flow-field cache's keys, versions and LRU stamps
    /// are in, because they decide which units wait under the build cap (BUG-0021); the fields'
    /// contents are not, since they follow from the grid and the key.
    /// </remarks>
    public ulong StateHash()
    {
        var h = new StateHasher();
        h.Add(World.TickNumber);

        UnitStore u = World.Units;
        h.Add(u.Capacity);
        for (int i = 0; i < u.Capacity; i++)
        {
            // Generations matter even for dead slots: they decide future handles.
            h.Add(u.Generation[i]);
            h.Add(u.Alive[i]);
            if (!u.Alive[i]) continue;
            h.Add(u.Position[i]);
            h.Add(u.PrevPosition[i]);
            h.Add(u.Velocity[i]);
            h.Add(u.Facing[i]);
            h.Add(u.Owner[i]);
            h.Add(u.TypeId[i]);
            h.Add((int)u.State[i]);
            h.Add(u.Goal[i]);
            h.Add(u.GoalCell[i]);
            h.Add(u.OrderTick[i]);
            h.Add(u.StuckTicks[i]);
            h.Add(u.BestRemaining[i]);
        }
        h.Add(u.FreeCount);
        for (int i = 0; i < u.FreeCount; i++)
            h.Add(u.FreeListAt(i));

        for (int s = 0; s < World.RngCount; s++)
        {
            h.Add(World.Rng(s).State);
            h.Add(World.Rng(s).Increment);
        }

        World.FlowFields.AddToHash(ref h);

        for (int p = 0; p < _nextSequence.Length; p++)
            h.Add(_nextSequence[p]);
        h.Add(_commands.Count);
        for (int i = 0; i < _commands.Count; i++)
        {
            ref readonly Command c = ref _commands[i];
            h.Add((int)c.Kind);
            h.Add(c.Player);
            h.Add(c.Tick);
            h.Add(c.Sequence);
            h.Add(c.TypeId);
            h.Add(c.Position);
            h.Add(c.Unit.Index);
            h.Add(c.Unit.Generation);
        }
        return h.Value;
    }

    private void Apply(in Command command)
    {
        // Malformed input (e.g. a NaN ray cast) is dropped, like a spawn into a full store (BUG-0006).
        if (!command.IsValid()) return;
        switch (command.Kind)
        {
            case CommandKind.Noop:
                break;
            case CommandKind.SpawnUnit:
                ApplySpawn(in command);
                break;
            case CommandKind.Move:
                ApplyMove(in command);
                break;
        }
    }

    private void ApplySpawn(in Command command)
    {
        // An unknown type or a full store drops the spawn: a command must never crash the sim.
        GameData data = World.Data;
        if ((uint)command.TypeId >= (uint)data.Units.Length) return;
        if (!World.Units.TryAlloc(out EntityHandle h)) return;
        UnitDef def = data.Units[command.TypeId];
        UnitStore u = World.Units;
        u.Position[h.Index] = command.Position;
        u.PrevPosition[h.Index] = command.Position;
        u.Owner[h.Index] = command.Player;
        u.TypeId[h.Index] = command.TypeId;
        u.Speed[h.Index] = def.SpeedPerTick;
        u.Radius[h.Index] = def.Radius;
    }

    private void ApplyMove(in Command command)
    {
        // Dropped: a dead or recycled unit, someone else's unit, or a target off the map.
        UnitStore u = World.Units;
        if (!u.IsAlive(command.Unit) || u.Owner[command.Unit.Index] != command.Player) return;
        NavGrid grid = World.NavGrid;
        if (!grid.WorldToCell(command.Position, out int x, out int y)) return;
        int cell = y * grid.Width + x;
        Vector2 goal = command.Position;
        if (!grid.IsPassable(x, y))
        {
            // Same rule as the flow field's blocked target: walk to the nearest passable cell's center.
            cell = FlowField.NearestPassable(grid, cell);
            if (cell < 0) return;
            goal = grid.CellCenter(cell % grid.Width, cell / grid.Width);
        }
        int i = command.Unit.Index;
        u.State[i] = UnitState.Moving;
        u.Goal[i] = goal;
        u.GoalCell[i] = cell;
        u.OrderTick[i] = World.TickNumber;
        u.StuckTicks[i] = 0;
        u.BestRemaining[i] = float.PositiveInfinity;
    }
}
