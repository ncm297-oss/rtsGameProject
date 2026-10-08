using System;
using System.Collections.Immutable;
using Rts.Sim.Commands;

namespace Rts.Sim.Replays;

/// <summary>Records a <see cref="Simulation"/> as it runs: every command <c>Enqueue</c> accepts, as stamped, and a state hash every N ticks.</summary>
/// <remarks>
/// Attach it to a fresh sim (tick 0, nothing queued) by constructing it; one recorder per sim.
/// Both buffers are preallocated: the checkpoint hash runs inside <see cref="Simulation.Tick"/>, which
/// must not allocate, so <c>tickCapacity</c> should cover the match (default: one hour). Past it the
/// checkpoint buffer doubles, the one allocation a tick can make here. The command log doubles when
/// full inside <c>Enqueue</c>, which input calls between ticks (off-tick); once the AI enqueues during
/// a tick (M5), size <c>commandCapacity</c> for the match too.
/// </remarks>
public sealed class ReplayRecorder
{
    /// <summary>Ticks between checkpoints unless the caller picks another interval (5 s at 20 Hz).</summary>
    public const int DefaultCheckpointInterval = 100;

    /// <summary>Ticks the checkpoint buffer covers without growing: one hour at 20 Hz.</summary>
    public const int DefaultTickCapacity = 72_000;

    /// <summary>Commands the log holds before it first grows.</summary>
    public const int DefaultCommandCapacity = 4096;

    private readonly Simulation _sim;
    private readonly ulong _dataHash;
    private Command[] _commands;
    private int _commandCount;
    private ReplayCheckpoint[] _checkpoints;
    private int _checkpointCount;

    /// <summary>Attaches a recorder to <paramref name="sim"/>, which must not have ticked or queued anything yet.</summary>
    public ReplayRecorder(Simulation sim, int checkpointInterval = DefaultCheckpointInterval,
        int tickCapacity = DefaultTickCapacity, int commandCapacity = DefaultCommandCapacity)
    {
        ArgumentNullException.ThrowIfNull(sim);
        if (checkpointInterval < 1 || checkpointInterval > Replay.MaxTickCount) throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
        if (tickCapacity < 0) throw new ArgumentOutOfRangeException(nameof(tickCapacity));
        if (commandCapacity < 0) throw new ArgumentOutOfRangeException(nameof(commandCapacity));
        // A replay rebuilds the sim from seed + params and replays from tick 0, so it must see everything.
        if (sim.TickNumber != 0 || sim.PendingCommandCount != 0)
            throw new InvalidOperationException("Attach the recorder before the first command and tick.");
        if (!sim.World.HasGeneratedMap)
            throw new InvalidOperationException("A sim on a hand-made map can't be replayed from its seed.");
        // The format has no building-capacity line, and the capacity is in the state hash (M3-2).
        if (sim.World.Config.BuildingCapacity != Entities.BuildingStore.DefaultCapacity)
            throw new InvalidOperationException("The replay format records only the default building capacity.");

        _sim = sim;
        CheckpointInterval = checkpointInterval;
        _dataHash = sim.World.Data.ContentHash();
        _commands = new Command[Math.Max(commandCapacity, 1)];
        _checkpoints = new ReplayCheckpoint[tickCapacity / checkpointInterval + 1];
        sim.AttachRecorder(this);
    }

    /// <summary>Ticks between checkpoints.</summary>
    public int CheckpointInterval { get; }

    /// <summary>Commands recorded so far.</summary>
    public int CommandCount => _commandCount;

    /// <summary>Checkpoints recorded so far.</summary>
    public int CheckpointCount => _checkpointCount;

    /// <summary>The <paramref name="index"/>-th recorded command, as stamped.</summary>
    public ref readonly Command CommandAt(int index)
    {
        if ((uint)index >= (uint)_commandCount) throw new ArgumentOutOfRangeException(nameof(index));
        return ref _commands[index];
    }

    /// <summary>The <paramref name="index"/>-th recorded checkpoint.</summary>
    public ReplayCheckpoint CheckpointAt(int index)
    {
        if ((uint)index >= (uint)_checkpointCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _checkpoints[index];
    }

    /// <summary>Called by <see cref="Simulation.Enqueue"/> once a command is accepted and stamped.</summary>
    internal void OnEnqueued(in Command command)
    {
        if (_commandCount == _commands.Length) Array.Resize(ref _commands, _commands.Length * 2);
        _commands[_commandCount++] = command;
    }

    /// <summary>Called at the end of every <see cref="Simulation.Tick"/>: hashes the state when the new tick number is a checkpoint.</summary>
    internal void OnTicked()
    {
        int tick = _sim.TickNumber;
        if (tick % CheckpointInterval != 0) return;
        if (_checkpointCount == _checkpoints.Length) Array.Resize(ref _checkpoints, _checkpoints.Length * 2);
        _checkpoints[_checkpointCount++] = new ReplayCheckpoint(tick, _sim.StateHash());
    }

    /// <summary>The recording so far, up to the sim's current tick; throws past <see cref="Replay.MaxTickCount"/> ticks (the file would be unreadable).</summary>
    /// <remarks>
    /// Commands stamped for a later tick (queued since the last tick) are left out: they haven't
    /// applied, and no checkpoint so far includes them. Recording continues; call it again later.
    /// </remarks>
    public Replay ToReplay()
    {
        int tickCount = _sim.TickNumber;
        // A file past the format limit would be refused on read (Replay.Validate): refuse to write it (BUG-0047).
        if (tickCount > Replay.MaxTickCount)
            throw new InvalidOperationException($"The recording is {tickCount} ticks long, past the replay format limit of {Replay.MaxTickCount} (24 h).");
        var commands = ImmutableArray.CreateBuilder<Command>(_commandCount);
        for (int i = 0; i < _commandCount; i++)
            if (_commands[i].Tick <= tickCount) commands.Add(_commands[i]);
        var checkpoints = ImmutableArray.CreateBuilder<ReplayCheckpoint>(_checkpointCount);
        for (int i = 0; i < _checkpointCount; i++) checkpoints.Add(_checkpoints[i]);

        SimConfig config = _sim.World.Config;
        return new Replay
        {
            SimVersion = SimInfo.Version,
            DataHash = _dataHash,
            Map = config.Map,
            Seed = config.Seed,
            PlayerCount = config.PlayerCount,
            UnitCapacity = config.UnitCapacity,
            CommandCapacity = config.CommandCapacity,
            ResourceCapacity = config.ResourceCapacity,
            Combat = config.Combat,
            CheckpointInterval = CheckpointInterval,
            TickCount = tickCount,
            Commands = commands.ToImmutable(),
            Checkpoints = checkpoints.ToImmutable(),
        };
    }
}
