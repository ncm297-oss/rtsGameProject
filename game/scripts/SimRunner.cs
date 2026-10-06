using System;
using System.Diagnostics;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
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

    /// <summary>Wall-clock cost of the most recent <c>Tick()</c> in milliseconds.</summary>
    public double LastTickMs { get; private set; }

    /// <summary>The last <see cref="TickTimeRing.DefaultCapacity"/> tick costs (every <c>Tick()</c>, also several per frame), for the debug overlay's graph. One array write per tick, so it is kept even while the overlay is off.</summary>
    public TickTimeRing TickTimes { get; } = new();

    /// <summary>Forests the map generator places (<see cref="MapGenParams.Forests"/>).</summary>
    [Export] public int Forests { get; set; } = LaunchOptions.DefaultForests;

    /// <summary>Gold mines the map generator places (<see cref="MapGenParams.GoldMines"/>).</summary>
    [Export] public int GoldMines { get; set; } = LaunchOptions.DefaultMines;

    /// <summary>Creates the simulation from loaded data on the default 128 map with <see cref="Forests"/> and <see cref="GoldMines"/>; ticking starts on the next frame.</summary>
    public void Start(GameData data)
    {
        var config = new SimConfig(unchecked((ulong)Seed), PlayerCount, UnitCapacity, CommandCapacity)
        {
            Data = data,
            Map = MapGenParams.Default with { Forests = Forests, GoldMines = GoldMines },
        };
        Simulation = new Simulation(config);
    }

    public override void _Process(double delta)
    {
        if (Simulation == null) return;
        int ticks = _clock.Advance(delta, _speed);
        for (int i = 0; i < ticks; i++)
        {
            _stopwatch.Restart();
            Simulation.Tick();
            _stopwatch.Stop();
            LastTickMs = _stopwatch.Elapsed.TotalMilliseconds;
            TickTimes.Add(LastTickMs);
        }
    }
}
