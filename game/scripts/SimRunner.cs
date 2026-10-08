using System;
using System.Diagnostics;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Owns the match's <see cref="Simulation"/> and ticks it at 20 Hz from frame time (docs/03 "Presentation timing").</summary>
/// <remarks>Here the view changes sim state only through <see cref="Simulation.Tick"/>; player orders are enqueued by <see cref="Match"/> and <see cref="SelectionController"/>.</remarks>
public partial class SimRunner : Node
{
    /// <summary>Slowest and fastest game speed.</summary>
    public const double MinSpeed = 0.25, MaxSpeed = 8.0;

    [Export] public long Seed { get; set; } = 1;
    [Export] public int PlayerCount { get; set; } = 2;
    [Export] public int UnitCapacity { get; set; } = 2000;
    [Export] public int CommandCapacity { get; set; } = 4096;

    private readonly FixedStepClock _clock = new();
    private readonly Stopwatch _stopwatch = new();
    private double _speed = 1.0;

    /// <summary>The running simulation; null until <see cref="Start"/>.</summary>
    public Simulation? Simulation { get; private set; }

    /// <summary>Game-time multiplier, clamped to [<see cref="MinSpeed"/>, <see cref="MaxSpeed"/>].</summary>
    public double GameSpeed
    {
        get => _speed;
        set => _speed = double.IsNaN(value) ? 1.0 : Math.Clamp(value, MinSpeed, MaxSpeed);
    }

    /// <summary>Interpolation factor between the previous and current tick, in [0, 1).</summary>
    public double Alpha => _clock.Alpha;

    /// <summary>True only while <see cref="Simulation.Tick"/> runs inside <see cref="_Process"/>: reads that write sim scratch (<c>World.CanPlace</c>, M3-V2) must not run then (docs/03 M3-3).</summary>
    public bool Ticking { get; private set; }

    /// <summary>Wall-clock cost of the most recent <c>Tick()</c> in milliseconds.</summary>
    public double LastTickMs { get; private set; }

    /// <summary>The last <see cref="TickTimeRing.DefaultCapacity"/> tick costs (every <c>Tick()</c>, also several per frame), for the debug overlay's graph. One array write per tick, so it is kept even while the overlay is off.</summary>
    public TickTimeRing TickTimes { get; } = new();

    /// <summary>Sum of every <c>Tick()</c> cost so far in milliseconds; with <see cref="TickTimeRing.Total"/> it gives the mean over any span (the <c>--bench</c> line).</summary>
    public double TotalTickMs { get; private set; }

    /// <summary>Forests the map generator places (<see cref="MapGenParams.Forests"/>).</summary>
    [Export] public int Forests { get; set; } = LaunchOptions.DefaultForests;

    /// <summary>Gold mines the map generator places (<see cref="MapGenParams.GoldMines"/>).</summary>
    [Export] public int GoldMines { get; set; } = LaunchOptions.DefaultMines;

    /// <summary>
    /// Scripted runs (M3-V4): when above 0 before <see cref="Start"/>, a <see cref="ReplayRecorder"/> with this
    /// checkpoint interval is attached to the new sim before anything is enqueued, so the whole command stream the HUD
    /// sent can be replayed into a bare twin (<see cref="ReplayPlayer"/>): equal checkpoints prove the views only read.
    /// 0 (the default) records nothing.
    /// </summary>
    public int RecordCheckpointInterval { get; set; }

    /// <summary>Whether the match fights (<see cref="SimConfig.Combat"/>); set before <see cref="Start"/>. False only for <c>--no-combat</c> (dev / tests, M4-V1).</summary>
    public bool Combat { get; set; } = true;

    /// <summary>
    /// Raised after every <see cref="Simulation.Tick"/> this runner runs (M4-V1), while the tick's one-tick outputs
    /// (<c>World.Deaths</c>) are still there: a frame that runs several ticks (fast game speed) would otherwise show only the
    /// last tick's deaths. Handlers only read.
    /// </summary>
    public event Action<Simulation>? Ticked;

    /// <summary>The recorder attached by <see cref="Start"/> when <see cref="RecordCheckpointInterval"/> is set, else null.</summary>
    public ReplayRecorder? Recorder { get; private set; }

    /// <summary>Creates the simulation from loaded data on the default 128 map with <see cref="Forests"/> and <see cref="GoldMines"/>; ticking starts on the next frame.</summary>
    public void Start(GameData data)
    {
        var config = new SimConfig(unchecked((ulong)Seed), PlayerCount, UnitCapacity, CommandCapacity)
        {
            Data = data,
            Map = MapGenParams.Default with { Forests = Forests, GoldMines = GoldMines },
            Combat = Combat,
        };
        Simulation = new Simulation(config);
        if (RecordCheckpointInterval > 0) Recorder = new ReplayRecorder(Simulation, RecordCheckpointInterval);
    }

    public override void _Process(double delta)
    {
        if (Simulation == null) return;
        int ticks = _clock.Advance(delta, _speed);
        for (int i = 0; i < ticks; i++)
        {
            _stopwatch.Restart();
            Ticking = true;
            Simulation.Tick();
            Ticking = false;
            _stopwatch.Stop();
            LastTickMs = _stopwatch.Elapsed.TotalMilliseconds;
            TickTimes.Add(LastTickMs);
            TotalTickMs += LastTickMs;
            Ticked?.Invoke(Simulation);
        }
    }
}
