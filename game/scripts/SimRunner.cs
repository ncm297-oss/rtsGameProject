using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Owns the match's <see cref="Simulation"/> and ticks it at 20 Hz from frame time (docs/03 "Presentation timing").</summary>
/// <remarks>
/// Here the view changes sim state only through <see cref="Simulation.Tick"/>; player orders are enqueued by <see cref="Match"/>
/// and <see cref="SelectionController"/>. M6-V9: <see cref="StartPlayback"/> plays a recorded <see cref="Replay"/> on the same
/// clock instead (speed and pause as in a live match), feeding the logged commands before the tick each is stamped for, as
/// <see cref="ReplayPlayer.Run(Replay, GameData)"/> does, and comparing each checkpoint as it passes.
/// </remarks>
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
    /// The faction ids picked for players 0 to 3 (the setup's slots, <c>--faction</c>; M6-V2, M6-V5), or -1 for the default
    /// (<c>p mod factions</c>); set before <see cref="Start"/>. Players past these always play the default.
    /// </summary>
    public int[] SlotFactions { get; } = { -1, -1, -1, -1 };

    /// <summary>Per player 0 to 3, its AI difficulty (the setup's AI slots, <c>--ai</c>; M6-V5), or null for a human seat; set before <see cref="Start"/>. Player 0 is the local human whatever this holds.</summary>
    public AiDifficulty?[] SlotAi { get; } = new AiDifficulty?[LaunchOptions.SlotCount];

    /// <summary>Player 1's AI difficulty (<see cref="SlotAi"/>[1]; the setup's AI slot, <c>--ai</c>; M6-V2), or null for a human seat; set before <see cref="Start"/>.</summary>
    public AiDifficulty? Player1Ai
    {
        get => SlotAi[1];
        set => SlotAi[1] = value;
    }

    /// <summary>
    /// Start locations the map generator picks (<see cref="MapGenParams.StartLocations"/>, 2 to 4; M6-V5): <c>Match</c> sets it
    /// to the match's <c>--players</c>. A runner whose <see cref="PlayerCount"/> a test raised by hand keeps the default 2,
    /// so those scenes keep their map; players past the locations get the old start-base search.
    /// </summary>
    public int StartLocations { get; set; } = MapGenParams.Default.StartLocations;

    /// <summary>The seats <see cref="Start"/> gave the sim (<see cref="SimConfig.Seats"/>, one per player); empty before it.</summary>
    public ImmutableArray<PlayerSeat> Seats { get; private set; } = ImmutableArray<PlayerSeat>.Empty;

    /// <summary>The recorder attached by <see cref="Start"/> when <see cref="RecordCheckpointInterval"/> is set, or by <see cref="StartPlayback"/> (the playback sim's own, M6-V9), else null.</summary>
    public ReplayRecorder? Recorder { get; private set; }

    /// <summary>
    /// The seats as the match was played (M6-V9): <see cref="Seats"/> for a live match; for playback the replay header's (an
    /// AI seat stays AI here, for the stats screen, while the sim runs it human). Empty before a start.
    /// </summary>
    public ImmutableArray<PlayerSeat> PlayedSeats { get; private set; } = ImmutableArray<PlayerSeat>.Empty;

    /// <summary>The replay <see cref="StartPlayback"/> is playing, or null for a live match.</summary>
    public Replay? Playback { get; private set; }

    /// <summary>Where playback is (<see cref="PlaybackState.None"/> for a live match).</summary>
    public PlaybackState PlaybackState { get; private set; }

    /// <summary>Why playback stopped (<see cref="ReplayError.CheckpointMismatch"/>, <see cref="ReplayError.SequenceMismatch"/> or <see cref="ReplayError.CommandRejected"/>); <see cref="ReplayError.None"/> otherwise.</summary>
    public ReplayError PlaybackError { get; private set; }

    /// <summary>Checkpoints compared equal so far in playback.</summary>
    public int CheckpointsCompared => _compared;

    /// <summary>Raised once when playback ends (<see cref="PlaybackState.Ended"/>) or stops on a mismatch (<see cref="PlaybackState.Stopped"/>), after that tick's <see cref="Ticked"/>.</summary>
    public event Action<SimRunner>? PlaybackFinished;

    // Playback: the next logged command to feed, and the checkpoints compared so far.
    private int _next, _compared;

    /// <summary>Creates the simulation from loaded data on a <see cref="MapSize"/> map (the generator's other defaults) with <see cref="Forests"/> and <see cref="GoldMines"/> and explicit <see cref="Seats"/>; ticking starts on the next frame.</summary>
    public void Start(GameData data)
    {
        Seats = SeatsFor(PlayerCount, data.Factions.Length, SlotFactions, SlotAi);
        var config = new SimConfig(unchecked((ulong)Seed), PlayerCount, UnitCapacity, CommandCapacity)
        {
            Data = data,
            Map = MapGenParams.Default with { Width = MapSize, Height = MapSize, Forests = Forests, GoldMines = GoldMines, StartLocations = StartLocations },
            Combat = Combat,
            Seats = Seats,
        };
        Simulation = new Simulation(config);
        PlayedSeats = Seats;
        if (RecordCheckpointInterval > 0) Recorder = new ReplayRecorder(Simulation, RecordCheckpointInterval);
    }

    /// <summary>
    /// Resumes a loaded match (M6-V14): <paramref name="sim"/> is what <see cref="Simulation.TryLoad"/> read, at the saved
    /// tick. The runner takes its config (seed, players, capacities, map, combat) and its seats, one per player
    /// (<see cref="SimConfig.SeatOf"/>: the save header's faction, controller and difficulty), and attaches no
    /// <see cref="Recorder"/>: a recorder attaches at tick 0 only, so a loaded match is not recorded. Ticking resumes on the
    /// next frame.
    /// </summary>
    public void StartLoaded(Simulation sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        SimConfig c = sim.World.Config;
        Seed = unchecked((long)c.Seed);
        PlayerCount = c.PlayerCount;
        UnitCapacity = c.UnitCapacity;
        CommandCapacity = c.CommandCapacity;
        MapSize = c.Map.Width;
        Forests = c.Map.Forests;
        GoldMines = c.Map.GoldMines;
        StartLocations = c.Map.StartLocations;
        Combat = c.Combat;
        var seats = ImmutableArray.CreateBuilder<PlayerSeat>(c.PlayerCount);
        for (int p = 0; p < c.PlayerCount; p++) seats.Add(c.SeatOf(p));
        Seats = PlayedSeats = seats.MoveToImmutable();
        Recorder = null;
        Simulation = sim;
    }

    /// <summary>
    /// Starts playing <paramref name="replay"/> (M6-V9). Refuses before any sim exists, returning the code, when it breaks
    /// <see cref="Replay.Validate"/>, was recorded with other data (<see cref="ReplayError.DataMismatch"/>: <c>ContentHash</c>
    /// differs) or names a faction the data lacks (<see cref="ReplayError.InvalidHeader"/>). Otherwise builds the sim from the
    /// header (seed, players, capacities, map, combat) with every seat human on the header's faction (no AI runs: its commands
    /// are in the log), attaches a fresh <see cref="Recorder"/>, queues the commands stamped for tick 1 and returns
    /// <see cref="ReplayError.None"/>; ticking starts on the next frame.
    /// </summary>
    public ReplayError StartPlayback(GameData data, Replay replay)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(replay);
        ReplayError invalid = replay.Validate();
        if (invalid != ReplayError.None) return invalid;
        if (data.ContentHash() != replay.DataHash) return ReplayError.DataMismatch;
        ImmutableArray<PlayerSeat> seats = ImmutableArray<PlayerSeat>.Empty, played = ImmutableArray<PlayerSeat>.Empty;
        if (!replay.Seats.IsDefaultOrEmpty)
        {
            var human = ImmutableArray.CreateBuilder<PlayerSeat>(replay.Seats.Length);
            var asPlayed = ImmutableArray.CreateBuilder<PlayerSeat>(replay.Seats.Length);
            foreach (ReplaySeat seat in replay.Seats)
            {
                int faction = seat.Faction == "-" ? -1 : data.FindFaction(seat.Faction);
                if (faction < 0 && (seat.Faction != "-" || data.Factions.Length > 0)) return ReplayError.InvalidHeader;
                human.Add(new PlayerSeat(faction, SeatController.Human, seat.Difficulty));
                asPlayed.Add(new PlayerSeat(faction, seat.Controller, seat.Difficulty));
            }
            seats = human.MoveToImmutable();
            played = asPlayed.MoveToImmutable();
        }
        Seed = unchecked((long)replay.Seed);
        PlayerCount = replay.PlayerCount;
        UnitCapacity = replay.UnitCapacity;
        CommandCapacity = replay.CommandCapacity;
        MapSize = replay.Map.Width;
        Forests = replay.Map.Forests;
        GoldMines = replay.Map.GoldMines;
        StartLocations = replay.Map.StartLocations;
        Combat = replay.Combat;
        Seats = seats;
        PlayedSeats = played;
        var config = new SimConfig(replay.Seed, replay.PlayerCount, replay.UnitCapacity, replay.CommandCapacity)
        {
            Data = data,
            Map = replay.Map,
            ResourceCapacity = replay.ResourceCapacity,
            Combat = replay.Combat,
            Seats = seats,
        };
        Simulation = new Simulation(config);
        Recorder = new ReplayRecorder(Simulation, replay.CheckpointInterval, replay.TickCount, replay.Commands.Length);
        Playback = replay;
        PlaybackState = PlaybackState.Playing;
        PlaybackError = ReplayError.None;
        _next = 0;
        _compared = 0;
        ReplayError fed = FeedPlayback();
        if (fed != ReplayError.None) StopPlayback(fed);
        return ReplayError.None;
    }

    // Enqueues every logged command stamped for the next tick, in log order, and checks the sim stamped each as logged.
    private ReplayError FeedPlayback()
    {
        Replay replay = Playback!;
        Simulation sim = Simulation!;
        while (_next < replay.Commands.Length && replay.Commands[_next].Tick == sim.TickNumber + 1)
        {
            Command logged = replay.Commands[_next];
            try
            {
                sim.Enqueue(logged);
            }
            catch (InvalidOperationException)
            {
                return ReplayError.CommandRejected;
            }
            ref readonly Command stamped = ref Recorder!.CommandAt(Recorder.CommandCount - 1);
            if (stamped.Tick != logged.Tick || stamped.Sequence != logged.Sequence) return ReplayError.SequenceMismatch;
            _next++;
        }
        return ReplayError.None;
    }

    // After a playback tick: the newest checkpoint against the file's, then the next tick's commands.
    private ReplayError CheckPlayback()
    {
        if (Recorder!.CheckpointCount > _compared)
        {
            if (Recorder.CheckpointAt(_compared) != Playback!.Checkpoints[_compared]) return ReplayError.CheckpointMismatch;
            _compared++;
        }
        return FeedPlayback();
    }

    private void StopPlayback(ReplayError error)
    {
        PlaybackState = PlaybackState.Stopped;
        PlaybackError = error;
        GD.Print($"Replay stopped at tick {Simulation!.TickNumber}: {error}");
    }

    // Ends playback (None: the recording's end, every checkpoint equal; else the mismatch) and tells the shell, once.
    private void FinishPlayback(ReplayError error)
    {
        if (error != ReplayError.None) StopPlayback(error);
        else
        {
            PlaybackState = PlaybackState.Ended;
            GD.Print($"Replay ended at tick {Simulation!.TickNumber}: {_compared} checkpoints equal");
        }
        PlaybackFinished?.Invoke(this);
    }

    /// <summary>
    /// One seat per player (M6-V2): player p plays <paramref name="slotFactions"/>[p] when it is set (not -1), else faction
    /// <c>p mod factions</c> (<see cref="SimConfig.SeatOf"/>'s default); player 1 is an AI of <paramref name="player1Ai"/> when
    /// that is set; every other seat is human, difficulty Normal. With no factions every seat's faction is -1.
    /// </summary>
    public static ImmutableArray<PlayerSeat> BuildSeats(int playerCount, int factionCount, int[] slotFactions, AiDifficulty? player1Ai) =>
        SeatsFor(playerCount, factionCount, slotFactions, new AiDifficulty?[] { null, player1Ai });

    /// <summary>
    /// One seat per player (M6-V5): player p plays <paramref name="slotFactions"/>[p] when it is set (not -1), else faction
    /// <c>p mod factions</c>; player p &gt; 0 is an AI of <paramref name="slotAi"/>[p] when that is set; every other seat (player 0
    /// always, the local human) is human, difficulty Normal. With no factions every seat's faction is -1.
    /// </summary>
    public static ImmutableArray<PlayerSeat> SeatsFor(int playerCount, int factionCount, int[] slotFactions, AiDifficulty?[] slotAi)
    {
        var seats = ImmutableArray.CreateBuilder<PlayerSeat>(Math.Max(0, playerCount));
        for (int p = 0; p < playerCount; p++)
        {
            int faction = factionCount == 0 ? -1 : p < slotFactions.Length && slotFactions[p] >= 0 ? slotFactions[p] : p % factionCount;
            AiDifficulty? ai = p > 0 && p < slotAi.Length ? slotAi[p] : null;
            seats.Add(new PlayerSeat(faction, ai != null ? SeatController.Ai : SeatController.Human, ai ?? AiDifficulty.Normal));
        }
        return seats.MoveToImmutable();
    }

    public override void _Process(double delta)
    {
        if (Simulation == null) return;
        int ticks = _clock.Advance(FixedFrameDelta ?? delta, _speed);
        for (int i = 0; i < ticks; i++)
        {
            // M6-V9: a finished or stopped playback runs no more ticks; one already at its end finishes now (a 0-tick file).
            if (PlaybackState is PlaybackState.Ended or PlaybackState.Stopped) break;
            if (PlaybackState == PlaybackState.Playing && Simulation.TickNumber >= Playback!.TickCount)
            {
                FinishPlayback(ReplayError.None);
                break;
            }
            _stopwatch.Restart();
            Ticking = true;
            Simulation.Tick();
            Ticking = false;
            _stopwatch.Stop();
            LastTickMs = _stopwatch.Elapsed.TotalMilliseconds;
            TickTimes.Add(LastTickMs);
            TotalTickMs += LastTickMs;
            ReplayError playback = PlaybackState == PlaybackState.Playing ? CheckPlayback() : ReplayError.None;
            Ticked?.Invoke(Simulation);
            if (PlaybackState == PlaybackState.Playing && (playback != ReplayError.None || Simulation.TickNumber == Playback!.TickCount))
            {
                FinishPlayback(playback);
                break;
            }
            // M6-V4: a Ticked handler paused the tree (the match-over banner): the frame's other ticks don't run, so the
            // banner shows the tick that ended the match (the clock's time is dropped, as for any pause).
            if (!CanProcess()) break;
        }
    }
}
