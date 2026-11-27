using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-VH10 (BUG-1790's two-sided half): <c>Match.ResultFor</c> in every order of events, on plain sims (no scene, fast).
/// A data copy whose draw waits 20 s and whose standard preset starts with no gold: halls only, nobody can train, so the
/// clock runs from the start. Each row drives the live sim, a replay twin (a fresh sim fed the recorded commands at their
/// ticks, as playback does) and every sim loaded from a save taken on the way, all in lockstep with the same commands; on
/// every tick each sim's state hash and every seat's <c>ResultFor</c> must equal the live sim's, and at the end each row's
/// result per seat is checked against the design (docs/02 "Game flow" 4, docs/01 2026-11-26 (2)):
/// <list type="bullet">
/// <item>two seats: draw, then a fall (either seat): Draw for both; saves before and after the fall.</item>
/// <item>two seats: a fall, then twice the draw's wait: no draw, Defeat and Victory, for good (save after).</item>
/// <item>teams 1 1 2 2: draw, then the whole of team 1 falls: Draw for all four.</item>
/// <item>teams 1 1 2 2: seat 0 out before the draw (ally stands), draw, then seat 1 out: Draw for all four.</item>
/// <item>three seats, teams 1 1 0 (two sides): draw, then the lone seat falls: Draw for all.</item>
/// <item>three seats free-for-all: seat 2 out first, then the other two draw: 2 Defeat, 0 and 1 Draw.</item>
/// <item>(note) three seats, seat 2 never built: 0 and 1 draw, 0 falls (Draw), then seat 2 places its first building.</item>
/// </list>
/// The recording of each row also plays through <c>ReplayPlayer.Run</c> with every checkpoint matching.
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH10DrawTest.tscn</c>; prints "QA M6-VH10 DRAW TEST PASS"
/// or FAIL lines, exit 0 / 1.
/// </summary>
public partial class QaM6VH10DrawTest : Node
{
    private const int DrawSeconds = 20;
    private const int Wait = DrawSeconds * SimConstants.TicksPerSecond;

    private readonly List<string> _failures = new();
    private readonly List<string> _notes = new();
    private string _root = "";
    private GameData _data = null!;

    // The row's sims in lockstep: [0] live (recorded), [1] the replay twin, then every loaded save.
    private readonly List<(string Name, Simulation Sim)> _sims = new();
    private ReplayRecorder _recorder = null!;
    private string _row = "";
    private int _rowMismatches;

    public override async void _Ready()
    {
        _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qam6vh10draw-{OS.GetProcessId()}"));
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _data = DataLoader.LoadAll(Forge()).Data!;
            TwoSeatsDrawThenFall(victim: 0);
            TwoSeatsDrawThenFall(victim: 1);
            TwoSeatsFallThenWait();
            TeamsDrawThenTeamFalls();
            TeamsOneOutBeforeDrawOtherAfter();
            ThreeSeatsTwoSidesLoneFalls();
            ThreeSeatsFfaFallThenDraw();
            LateBuilderAfterDraw();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception in row '{_row}': {ex}");
        }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string n in _notes) GD.Print($"  note: {n}");
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-VH10 DRAW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-VH10 DRAW TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- rows ----

    private void TwoSeatsDrawThenFall(int victim)
    {
        Begin($"2 seats, draw then seat {victim} falls", Seats(0, 0));
        Halls((0, 1, 2), (1, 3, 2));
        UntilLive(w => w.MatchDrawn, Wait + 40, "the draw");
        Expect("at the draw", MatchOutcome.Draw, MatchOutcome.Draw);
        SaveAndLoad("after the draw, before the fall");
        KnockOut(killer: 1 - victim, victim);
        UntilLive(w => w.IsDefeated(victim), 4000, "the fall");
        Run(5);
        SaveAndLoad("after the fall");
        Run(2 * Wait);
        Check(Live.World.IsDefeated(victim) && Match.SidesThatBuilt(Live.World) == 2, $"{_row}: seat {victim} not out at the end, or sides {Match.SidesThatBuilt(Live.World)}");
        Expect("at the end", MatchOutcome.Draw, MatchOutcome.Draw);
        End();
    }

    private void TwoSeatsFallThenWait()
    {
        Begin("2 seats, fall then wait", Seats(0, 0));
        Halls((0, 1, 2), (1, 3, 2));
        Run(Wait / 4);
        Check(Live.World.DrawTicksLeft >= 0, $"{_row}: the clock does not run (left {Live.World.DrawTicksLeft})");
        KnockOut(killer: 1, victim: 0);
        UntilLive(w => w.IsDefeated(0), 4000, "the fall");
        SaveAndLoad("after the fall");
        Run(2 * Wait + 40);
        Check(!Live.World.MatchDrawn && Live.World.MatchOver, $"{_row}: drawn {Live.World.MatchDrawn}, over {Live.World.MatchOver} (want over, no draw)");
        Expect("2 waits after the fall", MatchOutcome.Defeat, MatchOutcome.Victory);
        End();
    }

    private void TeamsDrawThenTeamFalls()
    {
        Begin("teams 1 1 2 2, draw then team 1 falls", Seats(1, 1, 2, 2));
        Halls((0, 1, 1), (1, 1, 3), (2, 3, 1), (3, 3, 3));
        UntilLive(w => w.MatchDrawn, Wait + 40, "the draw");
        Expect("at the draw", MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw);
        SaveAndLoad("after the draw");
        KnockOut(killer: 2, victim: 0);
        UntilLive(w => w.IsDefeated(0), 4000, "seat 0's fall");
        Expect("seat 0 out, its ally stands", MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw);
        KnockOut(killer: 3, victim: 1);
        UntilLive(w => w.IsDefeated(1), 4000, "seat 1's fall");
        Run(5);
        SaveAndLoad("after team 1's fall");
        Run(40);
        Check(Match.SidesThatBuilt(Live.World) == 2, $"{_row}: sides {Match.SidesThatBuilt(Live.World)} (want 2)");
        Expect("team 1 out after the draw", MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw);
        End();
    }

    private void TeamsOneOutBeforeDrawOtherAfter()
    {
        Begin("teams 1 1 2 2, seat 0 out before the draw, seat 1 after", Seats(1, 1, 2, 2));
        Halls((0, 1, 1), (1, 1, 3), (2, 3, 1), (3, 3, 3));
        KnockOut(killer: 2, victim: 0);
        UntilLive(w => w.IsDefeated(0), 4000, "seat 0's fall");
        Expect("seat 0 out, no draw yet", MatchOutcome.None, MatchOutcome.None, MatchOutcome.None, MatchOutcome.None);
        SaveAndLoad("seat 0 out, before the draw");
        UntilLive(w => w.MatchDrawn, 2 * Wait + 400, "the draw");
        Expect("at the draw", MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw);
        KnockOut(killer: 3, victim: 1);
        UntilLive(w => w.IsDefeated(1), 4000, "seat 1's fall");
        Run(5);
        SaveAndLoad("after seat 1's fall");
        Run(40);
        Expect("team 1 out, half before, half after the draw", MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw);
        End();
    }

    private void ThreeSeatsTwoSidesLoneFalls()
    {
        Begin("3 seats teams 1 1 0, draw then the lone seat falls", Seats(1, 1, 0));
        Halls((0, 1, 1), (1, 1, 3), (2, 3, 2));
        UntilLive(w => w.MatchDrawn, Wait + 40, "the draw");
        SaveAndLoad("after the draw");
        KnockOut(killer: 0, victim: 2);
        UntilLive(w => w.IsDefeated(2), 4000, "seat 2's fall");
        Run(5);
        SaveAndLoad("after the fall");
        Run(40);
        Check(Match.SidesThatBuilt(Live.World) == 2, $"{_row}: sides {Match.SidesThatBuilt(Live.World)} (want 2)");
        Expect("the lone seat out after the draw", MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw);
        End();
    }

    private void ThreeSeatsFfaFallThenDraw()
    {
        Begin("3 seats ffa, seat 2 falls, then the draw", Seats(0, 0, 0));
        Halls((0, 1, 1), (1, 3, 1), (2, 2, 3));
        KnockOut(killer: 0, victim: 2);
        UntilLive(w => w.IsDefeated(2), 4000, "seat 2's fall");
        Expect("seat 2 out", MatchOutcome.None, MatchOutcome.None, MatchOutcome.Defeat);
        SaveAndLoad("seat 2 out, before the draw");
        UntilLive(w => w.MatchDrawn, 2 * Wait + 400, "the draw");
        Run(5);
        SaveAndLoad("after the draw");
        Run(40);
        Expect("drawn, seat 2 out before", MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Defeat);
        End();
    }

    // Seat 2 never builds before the draw (two sides built, so the sim draws 0 against 1), seat 0 falls after it (Draw: two
    // sides), then seat 2 places its first building: the sides count becomes 3 and seat 0's result may change.
    private void LateBuilderAfterDraw()
    {
        Begin("3 seats ffa, seat 2 builds only after the draw", Seats(0, 0, 0));
        Halls((0, 1, 1), (1, 3, 1));
        UntilLive(w => w.MatchDrawn, Wait + 40, "the draw");
        KnockOut(killer: 1, victim: 0);
        UntilLive(w => w.IsDefeated(0), 4000, "seat 0's fall");
        Run(5);
        Expect("seat 0 out after the draw, seat 2 never built", MatchOutcome.Draw, MatchOutcome.Draw, MatchOutcome.Draw);
        Halls((2, 2, 3));
        Run(5);
        World w = Live.World;
        string results = string.Join(", ", Enumerable.Range(0, 3).Select(p => $"{p}:{Match.ResultFor(w, p)}"));
        _notes.Add($"late builder: seat 2 built {w.EverBuilt(2)} after the draw (tick {w.TickNumber}): sides {Match.SidesThatBuilt(w)}, per seat [{results}]");
        End();
    }

    // ---- lockstep machinery ----

    private Simulation Live => _sims[0].Sim;

    private void Begin(string row, ImmutableArray<PlayerSeat> seats)
    {
        _row = row;
        _rowMismatches = 0;
        _sims.Clear();
        var config = new SimConfig(7, seats.Length, 512, 4096) { Data = _data, Seats = seats };
        var live = new Simulation(config);
        _recorder = new ReplayRecorder(live, checkpointInterval: 20);
        _sims.Add(("live", live));
        _sims.Add(("replay", new Simulation(config)));
        Check(live.World.Buildings.Count == 0 && live.World.Units.Count == 0, $"{_row}: the sim starts with buildings or units");
    }

    private void End()
    {
        Replay replay = _recorder.ToReplay();
        ReplayResult r = ReplayPlayer.Run(replay, _data);
        Check(r.Ok, $"{_row}: ReplayPlayer.Run {r.Error} at tick {r.Tick}");
        string results = string.Join(", ", Enumerable.Range(0, Live.World.Config.PlayerCount).Select(p => $"{p}:{Match.ResultFor(Live.World, p)}"));
        _notes.Add($"{_row}: {_sims.Count} sims to tick {Live.TickNumber} (drawn {Live.World.MatchDrawn}), per seat [{results}], replay {r.Error} ({r.TicksRun} ticks)");
    }

    private void Enqueue(Command c)
    {
        foreach ((_, Simulation s) in _sims) s.Enqueue(c);
    }

    private void Step()
    {
        foreach ((_, Simulation s) in _sims) s.Tick();
        World lw = Live.World;
        ulong hash = Live.StateHash();
        for (int k = 1; k < _sims.Count; k++)
        {
            (string name, Simulation s) = _sims[k];
            bool same = s.TickNumber == Live.TickNumber && s.StateHash() == hash;
            for (int p = 0; p < lw.Config.PlayerCount && same; p++) same = Match.ResultFor(s.World, p) == Match.ResultFor(lw, p);
            if (!same && _rowMismatches++ < 3)
                _failures.Add($"{_row}: tick {Live.TickNumber}: '{name}' differs from live (hash {s.StateHash():X16} vs {hash:X16}; results "
                    + string.Join(" ", Enumerable.Range(0, lw.Config.PlayerCount).Select(p => $"{Match.ResultFor(s.World, p)}/{Match.ResultFor(lw, p)}")) + ")");
        }
    }

    private void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++) Step();
    }

    private void UntilLive(Func<World, bool> done, int limit, string what)
    {
        for (int i = 0; i < limit && !done(Live.World); i++) Step();
        if (!done(Live.World)) throw new InvalidOperationException($"{what} not reached by tick {Live.TickNumber}");
    }

    private void SaveAndLoad(string when)
    {
        using var stream = new MemoryStream();
        if (!Check(Live.TrySave(stream, out SaveError saveError), $"{_row}: save {when}: {saveError}")) return;
        stream.Position = 0;
        if (!Check(Simulation.TryLoad(_data, stream, out Simulation? loaded, out SaveError loadError), $"{_row}: load {when}: {loadError}")) return;
        World lw = loaded!.World, w = Live.World;
        for (int p = 0; p < w.Config.PlayerCount; p++)
            Check(Match.ResultFor(lw, p) == Match.ResultFor(w, p), $"{_row}: loaded {when}: seat {p} reads {Match.ResultFor(lw, p)}, live {Match.ResultFor(w, p)}");
        Check(loaded.StateHash() == Live.StateHash(), $"{_row}: loaded {when}: hash differs");
        _sims.Add(($"loaded {when}", loaded));
    }

    private void Expect(string when, params MatchOutcome[] want)
    {
        foreach ((string name, Simulation s) in _sims)
            for (int p = 0; p < want.Length; p++)
            {
                MatchOutcome got = Match.ResultFor(s.World, p);
                Check(got == want[p], $"{_row}, {when}: '{name}' seat {p} reads {got}, want {want[p]} (tick {s.TickNumber})");
            }
    }

    private ImmutableArray<PlayerSeat> Seats(params int[] teams)
    {
        int faction = _data.FindFaction("malazan");
        return teams.Select(t => new PlayerSeat(faction, SeatController.Human, AiDifficulty.Normal, t)).ToImmutableArray();
    }

    // Each player's hall near (x/4, y/4) of the map, in quarters.
    private void Halls(params (int Player, int Qx, int Qy)[] at)
    {
        World w = Live.World;
        NavGrid g = w.NavGrid;
        int before = w.Buildings.Count;
        foreach ((int p, int qx, int qy) in at)
        {
            int hall = DefeatStaging.Hall(_data, w, p);
            int anchor = DefeatStaging.Anchor(w, p, hall, g.Width * qx / 4, g.Height * qy / 4);
            Enqueue(Command.SpawnBuilding(p, hall, g.CellCenter(anchor % g.Width, anchor / g.Width)));
        }
        Run(3);
        Check(w.Buildings.Count == before + at.Length, $"{_row}: {w.Buildings.Count - before} halls placed (want {at.Length})");
    }

    private void KnockOut(int killer, int victim)
    {
        World w = Live.World;
        NavGrid g = w.NavGrid;
        BuildingStore b = w.Buildings;
        EntityHandle hall = default;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == victim) { hall = new EntityHandle(i, b.Generation[i]); break; }
        if (hall == default) throw new InvalidOperationException($"seat {victim} has no building");
        int cell = b.Cell[hall.Index];
        int siege = StartBase.UnitOfSlot(_data, w.FactionOf(killer), UnitSlot.Siege);
        for (int i = 0; i < 8; i++)
        {
            int x = Math.Clamp(cell % g.Width - 4 + i % 4 * 2, 0, g.Width - 1), y = Math.Clamp(cell / g.Width - 5 - i / 4 * 2, 0, g.Height - 1);
            int c = FlowField.NearestPassable(g, y * g.Width + x);
            Enqueue(Command.SpawnUnit(killer, siege, g.CellCenter(c % g.Width, c / g.Width)));
        }
        Run(2);
        foreach (EntityHandle u in DefeatStaging.UnitsOf(w, killer))
            if (w.Units.TypeId[u.Index] == siege) Enqueue(Command.Attack(killer, u, hall, isBuilding: true));
    }

    private string Forge()
    {
        string from = ProjectSettings.GlobalizePath("res://data"), to = Path.Combine(_root, "data");
        if (Directory.Exists(to)) Directory.Delete(to, true);
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        string rules = Path.Combine(to, "common", "rules.json");
        JsonNode root = JsonNode.Parse(File.ReadAllText(rules))!;
        foreach (JsonNode? preset in root["startingResources"]!.AsArray())
            if ((string?)preset!["id"] == StartingResourcesDef.StandardId) preset["gold"] = 0;
        root["draw"] = new JsonObject { ["afterSeconds"] = DrawSeconds };
        File.WriteAllText(rules, root.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(to);
        if (!loaded.Ok) throw new InvalidOperationException($"the copy failed to load: {string.Join("; ", loaded.Errors.Take(5))}");
        return to;
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
