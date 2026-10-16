using System;
using System.Collections.Immutable;
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

    /// <summary>
    /// Scripted runs only (M6-VH2, BUG-0745): when set, each frame advances the clock by this many seconds instead of the
    /// frame's real delta, so a frame runs the same ticks however loaded the machine is and the run is the same every time;
    /// null (the game) ticks from frame time.
    /// </summary>
    public double? FixedFrameDelta { get; set; }

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

    /// <summary>Square map side in cells (<see cref="MapGenParams.Width"/> and <see cref="MapGenParams.Height"/>); the default is the 128 map (M6-V1: the skirmish setup's map size).</summary>
    [Export] public int MapSize { get; set; } = MapGenParams.Default.Width;

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

    /// <summary>
    /// The faction ids picked for players 0 and 1 (the setup's two slots, <c>--faction</c>; M6-V2), or -1 for the default
    /// (<c>p mod factions</c>); set before <see cref="Start"/>. Players past these two always play the default.
    /// </summary>
    public int[] SlotFactions { get; } = { -1, -1 };

    /// <summary>Player 1's AI difficulty (the setup's AI slot, <c>--ai</c>; M6-V2), or null for a human seat; set before <see cref="Start"/>.</summary>
    public AiDifficulty? Player1Ai { get; set; }

    /// <summary>The seats <see cref="Start"/> gave the sim (<see cref="SimConfig.Seats"/>, one per player); empty before it.</summary>
    public ImmutableArray<PlayerSeat> Seats { get; private set; } = ImmutableArray<PlayerSeat>.Empty;

    /// <summary>The recorder attached by <see cref="Start"/> when <see cref="RecordCheckpointInterval"/> is set, else null.</summary>
    public ReplayRecorder? Recorder { get; private set; }

    /// <summary>Creates the simulation from loaded data on a <see cref="MapSize"/> map (the generator's other defaults) with <see cref="Forests"/> and <see cref="GoldMines"/> and explicit <see cref="Seats"/>; ticking starts on the next frame.</summary>
    public void Start(GameData data)
    {
        Seats = BuildSeats(PlayerCount, data.Factions.Length, SlotFactions, Player1Ai);
        var config = new SimConfig(unchecked((ulong)Seed), PlayerCount, UnitCapacity, CommandCapacity)
        {
            Data = data,
            Map = MapGenParams.Default with { Width = MapSize, Height = MapSize, Forests = Forests, GoldMines = GoldMines },
            Combat = Combat,
            Seats = Seats,
        };
        Simulation = new Simulation(config);
        if (RecordCheckpointInterval > 0) Recorder = new ReplayRecorder(Simulation, RecordCheckpointInterval);
    }

    /// <summary>
    /// One seat per player (M6-V2): player p plays <paramref name="slotFactions"/>[p] when it is set (not -1), else faction
    /// <c>p mod factions</c> (<see cref="SimConfig.SeatOf"/>'s default); player 1 is an AI of <paramref name="player1Ai"/> when
    /// that is set; every other seat is human, difficulty Normal. With no factions every seat's faction is -1.
    /// </summary>
    public static ImmutableArray<PlayerSeat> BuildSeats(int playerCount, int factionCount, int[] slotFactions, AiDifficulty? player1Ai)
    {
        var seats = ImmutableArray.CreateBuilder<PlayerSeat>(Math.Max(0, playerCount));
        for (int p = 0; p < playerCount; p++)
        {
            int faction = factionCount == 0 ? -1 : p < slotFactions.Length && slotFactions[p] >= 0 ? slotFactions[p] : p % factionCount;
            bool ai = p == 1 && player1Ai != null;
            seats.Add(new PlayerSeat(faction, ai ? SeatController.Ai : SeatController.Human, ai ? player1Ai!.Value : AiDifficulty.Normal));
        }
        return seats.MoveToImmutable();
    }

    public override void _Process(double delta)
    {
        if (Simulation == null) return;
        int ticks = _clock.Advance(FixedFrameDelta ?? delta, _speed);
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
            // M6-V4: a Ticked handler paused the tree (the match-over banner): the frame's other ticks don't run, so the
            // banner shows the tick that ended the match (the clock's time is dropped, as for any pause).
            if (!CanProcess()) break;
        }
    }
}
