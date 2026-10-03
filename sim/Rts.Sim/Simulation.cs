using System;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;

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
    public Simulation(SimConfig config)
    {
        World = new World(config);
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

    /// <summary>Runs one tick: applies this tick's commands in (player, sequence) order, then bumps <see cref="TickNumber"/>.</summary>
    public void Tick()
    {
        World.Units.SnapshotPrevPositions();

        // Phase 1: apply commands.
        _commands.Sort();
        int due = _commands.CountDue(World.TickNumber);
        for (int i = 0; i < due; i++)
            Apply(in _commands[i]);
        _commands.RemoveFront(due);

        // Phase 13: cleanup.
        World.TickNumber++;
    }

    /// <summary>64-bit FNV-1a hash of all gameplay state: tick, units, RNG streams, and pending commands.</summary>
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
        }
        h.Add(u.FreeCount);
        for (int i = 0; i < u.FreeCount; i++)
            h.Add(u.FreeListAt(i));

        for (int s = 0; s < World.RngCount; s++)
        {
            h.Add(World.Rng(s).State);
            h.Add(World.Rng(s).Increment);
        }

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
                // A full store drops the spawn: a command must never crash the sim.
                if (World.Units.TryAlloc(out EntityHandle h))
                {
                    UnitStore u = World.Units;
                    u.Position[h.Index] = command.Position;
                    u.PrevPosition[h.Index] = command.Position;
                    u.Owner[h.Index] = command.Player;
                    u.TypeId[h.Index] = command.TypeId;
                }
                break;
        }
    }
}
