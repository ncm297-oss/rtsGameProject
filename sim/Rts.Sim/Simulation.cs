using System;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;

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
    private ReplayRecorder? _recorder;

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
    /// command never changes the tick that is already running. A command for an unknown player, of an
    /// undefined kind, or with flag bits its kind doesn't take (<see cref="Command.IsWellFormed"/>) is
    /// refused with an exception and changes nothing, so everything accepted can be recorded and read
    /// back (BUG-0054). A well-formed command with a bad payload (a NaN target, a dead unit) is
    /// accepted and dropped when it applies.
    /// </remarks>
    public void Enqueue(Command command)
    {
        if ((uint)command.Player >= (uint)_nextSequence.Length)
            throw new ArgumentOutOfRangeException(nameof(command), $"Unknown player {command.Player}.");
        if (!command.IsWellFormed())
            throw new ArgumentException($"Malformed command: kind {(int)command.Kind}, flags {command.Flags}.", nameof(command));
        command.Tick = World.TickNumber + 1;
        command.Sequence = _nextSequence[command.Player];
        _commands.Add(in command); // throws when full; bump the counter only once accepted (BUG-0004)
        _nextSequence[command.Player]++;
        _recorder?.OnEnqueued(in command);
    }

    /// <summary>Runs one tick: applies this tick's commands in (player, sequence) order, rebuilds the spatial hash, trains and spawns units, runs gather loops, then builders and repairers, starts queued orders, moves units, then bumps <see cref="TickNumber"/>.</summary>
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

        // Phase 3: production (M3-4): training timers, spawns, rally orders (their walks move in phase 9).
        ProductionSystem.Run(World);

        // Phase 4: economy (M3-2): gather loops and drop-offs; their walks start here and move in phase 9.
        EconomySystem.Run(World);
        // M3-3: builders and repairers, after the gatherers.
        ConstructionSystem.Run(World);

        // Phase 7: Idle units start their next shift-queued order.
        OrderSystem.Run(World);

        // Phases 8-9: flow fields (fetched or built on demand) and movement.
        MovementSystem.Run(World);

        // Phase 13: cleanup.
        World.TickNumber++;

        // Phase 14: replay checkpoint hash, when a recorder is attached and the tick is due.
        _recorder?.OnTicked();
    }

    /// <summary>Connects the one recorder this sim reports accepted commands and finished ticks to.</summary>
    internal void AttachRecorder(ReplayRecorder recorder)
    {
        if (_recorder != null) throw new InvalidOperationException("A replay recorder is already attached.");
        _recorder = recorder;
    }

    /// <summary>64-bit FNV-1a hash of all gameplay state: tick, units (Hold, order queues, gather loops and cargo included), RNG streams, flow-field cache metadata, nav grid versions (and the movement pass's last seen block version), resource nodes, buildings (production queues, research items and rally points included), player totals and researched techs, and pending commands.</summary>
    /// <remarks>
    /// Derived state is left out: the spatial hash (rebuilt from the units every tick),
    /// Speed/Radius (they follow from TypeId), and population (M3-4: <see cref="World.HalfPop"/> follows from the live
    /// units and the started production items, <see cref="World.HalfPopCap"/> from the finished buildings). The flow-field cache's keys, versions and LRU stamps
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
            // Orders (M1-7) ride in the high half of the state word, which is zero for a unit with
            // no Hold and an empty queue, so such a unit hashes exactly as before the queue existed.
            h.Add((ulong)(byte)u.State[i] | ((ulong)OrderBits(u, i) << 32));
            h.Add(u.Goal[i]);
            h.Add(u.GoalCell[i]);
            h.Add(u.OrderTick[i]);
            h.Add(u.StuckTicks[i]);
            h.Add(u.BestRemaining[i]);
            h.Add(u.WalkBack[i]);
            AddOrdersToHash(ref h, u, i);
            if (HasEconomy(u, i))
            {
                h.Add(u.GatherNode[i].Index);
                h.Add(u.GatherNode[i].Generation);
                h.Add(u.GatherSite[i]);
                h.Add(u.GatherProgress[i]);
                h.Add(u.Cargo[i]);
                h.Add((int)u.CargoKind[i]);
                h.Add(u.BuildTarget[i].Index);
                h.Add(u.BuildTarget[i].Generation);
            }
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

        // M3-1: resource nodes and the grid version (passability changes with them; it used to be unhashed).
        h.Add(World.NavGrid.Version);
        // M3-2b: the closing-change counter and the movement pass's last view of it (they decide which fields are usable and when progress marks reset).
        h.Add(World.NavGrid.BlockVersion);
        h.Add(World.SeenBlockVersion);
        World.Resources.AddToHash(ref h);

        // M3-2: buildings and the players' totals.
        World.Buildings.AddToHash(ref h);
        for (int p = 0; p < World.Gold.Length; p++)
        {
            // M3-5: a player with any researched tech sets the high half of its gold word and its tech words follow;
            // without techs it hashes exactly as before.
            bool techs = World.Techs.Any(p);
            h.Add((ulong)(uint)World.Gold[p] | (techs ? 1UL << 32 : 0UL));
            h.Add(World.Wood[p]);
            if (techs) World.Techs.AddToHash(ref h, p);
        }

        for (int p = 0; p < _nextSequence.Length; p++)
            h.Add(_nextSequence[p]);
        h.Add(_commands.Count);
        for (int i = 0; i < _commands.Count; i++)
        {
            ref readonly Command c = ref _commands[i];
            h.Add((ulong)(uint)c.Kind | ((ulong)(uint)c.Flags << 32)); // Flags 0: as before M1-7
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

    /// <summary>
    /// Unit <paramref name="i"/>'s order summary for <see cref="StateHash"/>: bit 0 Hold, bit 1 a
    /// non-zero queue count, then one bit per queue entry that isn't default, and bit 16 for gather-loop or
    /// cargo state (M3-2). Zero for a unit with
    /// no orders; <see cref="AddOrdersToHash"/> then adds nothing.
    /// </summary>
    private static uint OrderBits(UnitStore u, int i)
    {
        uint bits = u.Hold[i] ? 1u : 0u;
        if (u.QueueCount[i] != 0) bits |= 2u;
        int head = i * OrderConstants.QueueCapacity;
        for (int k = 0; k < OrderConstants.QueueCapacity; k++)
            if (u.QueueKind[head + k] != CommandKind.Noop || u.QueuePosition[head + k] != Vector2.Zero || u.QueueTypeId[head + k] != 0) bits |= 4u << k;
        if (HasEconomy(u, i)) bits |= 1u << 16;
        return bits;
    }

    /// <summary>True when any gather-loop or cargo field (M3-2) or the build target (M3-3) of unit <paramref name="i"/> isn't default; flagged in <see cref="OrderBits"/>, then hashed.</summary>
    private static bool HasEconomy(UnitStore u, int i) =>
        u.BuildTarget[i] != default || u.GatherNode[i] != default || u.GatherSite[i] != Vector2.Zero || u.GatherProgress[i] != 0f || u.Cargo[i] != 0 || u.CargoKind[i] != default;

    /// <summary>The queue count and every non-default queue entry of unit <paramref name="i"/>, as flagged by <see cref="OrderBits"/> (already hashed), so the stream stays unambiguous.</summary>
    private static void AddOrdersToHash(ref StateHasher h, UnitStore u, int i)
    {
        if (u.QueueCount[i] != 0) h.Add(u.QueueCount[i]);
        int head = i * OrderConstants.QueueCapacity;
        for (int k = 0; k < OrderConstants.QueueCapacity; k++)
        {
            if (u.QueueKind[head + k] == CommandKind.Noop && u.QueuePosition[head + k] == Vector2.Zero && u.QueueTypeId[head + k] == 0) continue;
            // A queued Build's type id (M3-3) rides in the high half of the kind word: zero for every other kind, so they hash as before.
            h.Add((ulong)(uint)u.QueueKind[head + k] | ((ulong)(uint)u.QueueTypeId[head + k] << 32));
            h.Add(u.QueuePosition[head + k]);
        }
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
            case CommandKind.Stop:
            case CommandKind.HoldPosition:
            case CommandKind.AttackMove:
            case CommandKind.Gather:
            case CommandKind.Build:
            case CommandKind.Repair:
                OrderSystem.Apply(World, in command);
                break;
            case CommandKind.Cancel:
                ConstructionSystem.ApplyCancel(World, in command);
                break;
            case CommandKind.SpawnBuilding:
                EconomySystem.ApplySpawnBuilding(World, in command);
                break;
            case CommandKind.Train:
                ProductionSystem.ApplyTrain(World, in command);
                break;
            case CommandKind.CancelTrain:
                ProductionSystem.ApplyCancelTrain(World, in command);
                break;
            case CommandKind.SetRally:
                ProductionSystem.ApplySetRally(World, in command);
                break;
            case CommandKind.ClearRally:
                ProductionSystem.ApplyClearRally(World, in command);
                break;
            case CommandKind.Research:
                ProductionSystem.ApplyResearch(World, in command);
                break;
        }
    }

    private void ApplySpawn(in Command command)
    {
        // An unknown type or a full store drops the spawn: a command must never crash the sim. A dev spawn ignores the
        // population cap but counts toward the population (M3-4).
        GameData data = World.Data;
        if ((uint)command.TypeId >= (uint)data.Units.Length) return;
        World.Units.TrySpawn(command.Player, command.TypeId, data.Units[command.TypeId], command.Position, out _);
    }
}
