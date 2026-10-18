using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-V11 (the stats columns and the army value graph). Three parts:
/// <list type="bullet">
/// <item>Graph rows on a standalone <see cref="ArmyValueGraph"/>: 0 / 1 / 2 samples, a full 1,024 buffer, four players with one
/// at 0 all game (on the baseline, still drawn), a drop to 0, values at int.MaxValue (no overflow, inside the plot), sample
/// intervals 7 and 50, every sample 0, a resize, and no redraw while nothing changes.</item>
/// <item>An AI match through the shell (four seats, three AIs) checked against an independent oracle built from the sim's raw
/// stores each tick (cargo drops, new non-summoned units, completed sites, death events, the first tick HasTech reads true, the
/// army value recomputed at each sample tick), not from the M6-S1 spans; the stats screen opened at the exact tick an AI first
/// has Age II; per-frame allocation and redraws while the screen is open.</item>
/// <item>The recorded match watched back: the replay's stats screen equals the live one cell for cell, the graph point for point.</item>
/// <item>Layout: four seats with 6-digit counters at 1280 x 720 (cells not clipped, the screen inside the window).</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V11Test.tscn</c>; prints "QA M6-V11 TEST PASS" or FAIL lines.
/// </summary>
public partial class QaM6V11Test : Node
{
    private const double FrameSeconds = 0.25; // 5 ticks a frame (the clock's cap)
    // Seed 11: seat 0 (idle human) falls near tick 11,536, seat 2 first has Age II on tick 17,021; 17,600 runs both rows.
    // BUG-0921 (M6-VH3 QA): since M5-P2's flank rule (merged at 63e3a31) seat 0 falls at tick 11,668 and seat 2 first has
    // Age II on tick 21,001 (17:30), so 21,600 runs both rows again (the checks are unchanged; only the match is longer).
    // BUG-0953 (M6-V13): since M5-H3 the AIs never finish seat 0's workers, so its fall is staged (see StageDefeat).
    private static int MatchTicks = 21600;
    // BUG-0953 (M6-V13): seat 0 now falls to a staged defeat (StageDefeat), not to the AIs; the match runs on this long past
    // MatchTicks at most for its banner.
    private const int StageGraceTicks = 1500;

    private readonly List<string> _failures = new();
    private readonly List<string> _notes = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string _dir = "", _full = "";
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1280, 720);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _dir = $"user://qa-m6v11-{OS.GetProcessId()}";
            string[] user = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < user.Length; i++)
            {
                if (user[i] == "--ticks") MatchTicks = int.Parse(user[i + 1], CultureInfo.InvariantCulture);
                if (user[i] == "--shots") _shots = user[i + 1];
            }
            if (StatsScreenCheck.TryWindow(user, out Vector2I window)) { DisplayServer.WindowSetSize(window); GetTree().Root.Size = window; await Frames(2); }
            _full = Path.GetFullPath(ProjectSettings.GlobalizePath(_dir));
            if (Directory.Exists(_full)) Directory.Delete(_full, true);

            await GraphRows();
            await OracleMatch();
            try { Directory.Delete(_full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string n in _notes) GD.Print($"  note: {n}");
        foreach (string f in _failures.Take(80)) GD.Print($"QA M6-V11 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V11 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- part 1: the graph on its own ----

    private async Task GraphRows()
    {
        var host = new Control { Name = "GraphHost", Size = new Vector2(820f, 220f) };
        AddChild(host);
        var g = new ArmyValueGraph { Name = "Graph", Size = new Vector2(820f, 220f) };
        host.AddChild(g);
        g.Build(_ui);
        await Frames(2);
        Color[] four = { Colors.Red, Colors.Blue, Colors.Green, Colors.Yellow };

        // 0 samples: axes only, no line.
        await Set(g, new[] { Array.Empty<int>(), Array.Empty<int>() }, four[..2], 200, 150);
        Check(g.LineCount == 2 && g.DrawnLines == 0 && g.GraphPoints(0).Length == 0 && g.MaxValue == 0 && g.SpanTicks == 150, $"0 samples: lines {g.LineCount}, drawn {g.DrawnLines}, max {g.MaxValue}, span {g.SpanTicks}");
        await Set(g, Array.Empty<int[]>(), Array.Empty<Color>(), 200, 0);
        Check(g.LineCount == 0 && g.DrawnLines == 0 && g.SpanTicks == 1, $"no players, tick 0: lines {g.LineCount}, drawn {g.DrawnLines}, span {g.SpanTicks}");

        // 1 sample: a dot at tick 200 of 250.
        await Set(g, new[] { new[] { 300 }, new[] { 0 } }, four[..2], 200, 250);
        Rect2 r = g.PlotRect;
        Check(g.DrawnLines == 2 && g.GraphPoints(0).Length == 1, $"1 sample: drawn {g.DrawnLines}, points {g.GraphPoints(0).Length}");
        Near(g.GraphPoints(0)[0], new Vector2(r.Position.X + 200f / 250f * r.Size.X, r.Position.Y), "1 sample, player 0 (top)");
        Near(g.GraphPoints(1)[0], new Vector2(r.Position.X + 200f / 250f * r.Size.X, r.End.Y), "1 sample, player 1 (baseline)");

        // 2 samples.
        await Set(g, new[] { new[] { 100, 400 } }, four[..1], 200, 400);
        r = g.PlotRect;
        Near(g.GraphPoints(0)[0], new Vector2(r.Position.X + 0.5f * r.Size.X, r.End.Y - 0.25f * r.Size.Y), "2 samples, first");
        Near(g.GraphPoints(0)[1], new Vector2(r.End.X, r.Position.Y), "2 samples, last at the plot's top right");

        // A full 1,024 buffer, the match run on past it (sampling stopped): the line ends at tick 204,800 of 250,000.
        int[] full = Enumerable.Range(0, 1024).Select(k => k * 37 % 5000).ToArray();
        await Set(g, new[] { full }, four[..1], 200, 250_000);
        r = g.PlotRect;
        Check(g.GraphPoints(0).Length == 1024 && g.DrawnLines == 1 && g.MaxValue == full.Max(), $"full buffer: points {g.GraphPoints(0).Length}, drawn {g.DrawnLines}, max {g.MaxValue}");
        Near(g.GraphPoints(0)[^1], new Vector2(r.Position.X + 204_800f / 250_000f * r.Size.X, r.End.Y - (float)full[^1] / full.Max() * r.Size.Y), "full buffer, last");
        Monotone(g, 0, "full buffer");

        // Four players, one at 0 all game; one drops to 0 mid-match.
        int[][] fourS =
        {
            new[] { 100, 200, 300, 400, 500 },
            new[] { 0, 0, 0, 0, 0 },
            new[] { 250, 600, 0, 0, 50 },
            new[] { 600, 600, 600, 600, 600 },
        };
        await Set(g, fourS, four, 200, 1000);
        r = g.PlotRect;
        Check(g.DrawnLines == 4, $"four players: drawn {g.DrawnLines} (want 4: the zero player's flat line counts)");
        for (int k = 0; k < 5; k++)
            Check(Math.Abs(g.GraphPoints(1)[k].Y - r.End.Y) < 0.01f, $"zero player: point {k} at y {g.GraphPoints(1)[k].Y}, baseline {r.End.Y}");
        Check(Math.Abs(g.GraphPoints(2)[2].Y - r.End.Y) < 0.01f && Math.Abs(g.GraphPoints(2)[1].Y - r.Position.Y) < 0.01f,
            $"drop to 0: points {g.GraphPoints(2)[1]} / {g.GraphPoints(2)[2]} (want top then baseline)");
        for (int p = 0; p < 4; p++) Check(g.LineColour(p) == four[p], $"four players: colour {p} {g.LineColour(p)}");

        // Every sample 0: everything on the baseline, no NaN.
        await Set(g, new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 0 } }, four[..2], 200, 600);
        r = g.PlotRect;
        Check(g.MaxValue == 0 && g.DrawnLines == 2 && g.GraphPoints(0).ToArray().All(v => Math.Abs(v.Y - r.End.Y) < 0.01f && float.IsFinite(v.X)), "all zero: not every point on the baseline");

        // Values at int.MaxValue: the scale holds them, no overflow.
        await Set(g, new[] { new[] { int.MaxValue, int.MaxValue - 1, 0, int.MaxValue / 2 }, new[] { 1, int.MaxValue, int.MaxValue, 7 } }, four[..2], 200, 800);
        r = g.PlotRect;
        Check(g.MaxValue == int.MaxValue, $"int.MaxValue: max {g.MaxValue}");
        for (int p = 0; p < 2; p++)
            foreach (Vector2 v in g.GraphPoints(p).ToArray())
                Check(float.IsFinite(v.X) && float.IsFinite(v.Y) && v.Y >= r.Position.Y - 0.01f && v.Y <= r.End.Y + 0.01f && v.X <= r.End.X + 0.01f,
                    $"int.MaxValue: point {v} outside the plot {r}");
        Near(g.GraphPoints(0)[0], new Vector2(r.Position.X + 0.25f * r.Size.X, r.Position.Y), "int.MaxValue at the top");
        Near(g.GraphPoints(0)[3], new Vector2(r.End.X, r.End.Y - 0.5f * r.Size.Y), "int.MaxValue / 2 halfway");

        // Other sample intervals: x is (k + 1) x interval over the span.
        foreach (int interval in new[] { 7, 50, 1 })
        {
            int[] s = Enumerable.Range(1, 9).ToArray();
            int ticks = 9 * interval + 3;
            await Set(g, new[] { s }, four[..1], interval, ticks);
            r = g.PlotRect;
            for (int k = 0; k < s.Length; k++)
                Near(g.GraphPoints(0)[k], new Vector2(r.Position.X + (float)((k + 1) * interval) / ticks * r.Size.X, r.End.Y - (float)s[k] / 9 * r.Size.Y), $"interval {interval}, sample {k}");
        }
        // A sample interval the data's samples over-run (more samples x interval than the tick count): the span stretches.
        await Set(g, new[] { new[] { 5, 5, 5 } }, four[..1], 200, 100);
        r = g.PlotRect;
        Check(g.SpanTicks == 600, $"span stretched to the samples: {g.SpanTicks} (want 600)");
        Near(g.GraphPoints(0)[^1], new Vector2(r.End.X, r.Position.Y), "stretched span, last point at the right edge");

        // A resize lays the points out again.
        await Set(g, new[] { new[] { 10, 20 } }, four[..1], 200, 400);
        g.Size = new Vector2(500f, 300f);
        await Frames(2);
        r = g.PlotRect;
        Check(Math.Abs(r.End.X - (500f - 28f)) < 0.5f && Math.Abs(r.End.Y - (300f - 38f)) < 0.5f, $"resize: plot {r}");
        Near(g.GraphPoints(0)[1], new Vector2(r.End.X, r.Position.Y), "resize, last point");

        // No redraw while nothing changes (the screen is paused under the open stats: _Draw must not run every frame).
        int draws = g.DrawCount;
        await Frames(30);
        Check(g.DrawCount == draws, $"idle graph redrew {g.DrawCount - draws} times in 30 frames");
        host.QueueFree();
        await Frames(1);
    }

    private async Task Set(ArmyValueGraph g, int[][] samples, Color[] colours, int interval, int ticks)
    {
        g.SetData(samples, colours, interval, ticks);
        int before = g.DrawCount;
        for (int i = 0; i < 5 && g.DrawCount == before; i++) await Frames(1);
        Check(g.DrawCount > before, "SetData queued no draw");
    }

    private void Near(Vector2 got, Vector2 want, string how) =>
        Check(Math.Abs(got.X - want.X) < 0.5f && Math.Abs(got.Y - want.Y) < 0.5f, $"{how}: point {got} (want {want})");

    private void Monotone(ArmyValueGraph g, int p, string how)
    {
        ReadOnlySpan<Vector2> pts = g.GraphPoints(p);
        for (int k = 1; k < pts.Length; k++)
            if (!(pts[k].X > pts[k - 1].X)) { Check(false, $"{how}: x not increasing at {k}"); return; }
    }

    // ---- part 2: an AI match against the oracle ----

    private sealed class Oracle
    {
        public long[] Gold = null!, Wood = null!;
        public int[] Trained = null!, UnitKills = null!, BuildingKills = null!, UnitLosses = null!, BuildingLosses = null!, Built = null!, AgeTick = null!;
        public List<int>[] Samples = null!;
        public int[] PrevCargo = null!, PrevGen = null!, PrevBGen = null!;
        public ResourceKind[] PrevKind = null!;
        public bool[] PrevAlive = null!, PrevBAlive = null!, PrevSite = null!;
        public int Ticks, StartUnits;
        public int ResearchTickMismatches;
        public string FirstMismatch = "";
        // M6-V13 (BUG-0953): the staged defeat's dev spawns, (owner, spawn point): the sim counts them as no one's trained unit.
        // Matched on the tick they apply by owner, type and the nearest spawn point within 3 m (the tick's separation may
        // already have nudged them); the matched units' handles are the killers.
        public List<(int Owner, int Type, System.Numerics.Vector2 At, int Tick)> DevSpawns = new();
        public List<(EntityHandle Unit, int Type)> DevUnits = new();
    }

    // M6-V13 (BUG-0953): seat 0 (the idle human) is put out by a staged defeat, not by the AIs: since M5-H3 they never hunt its
    // workers, so the banner row tracked AI changes. Seat 1 (an AI) gets dev killers: three Line units beside each of seat 0's
    // units and a catapult ring round each of its buildings (DefeatStaging's shooter, FreeForAllTest's ring), re-ordered every
    // 20 ticks (the AI may re-task them). The AI-side gap (hunting the last units) stays open: M5-4d, seed 11.
    private const int Victim = 0, Killer = 1, ReorderTicks = 20;

    private sealed class Staging
    {
        public int State; // 0 not staged, 1 spawns queued, 2 driving
        public int SpawnTick, OrderTick, StagedAt = -1, LineType, SiegeType, Spawned;
        public List<EntityHandle> Line = new(), Siege = new();
    }

    private void StageDefeat(Simulation sim, Oracle o, Staging st)
    {
        World w = sim.World;
        Rts.Sim.Map.NavGrid g = w.NavGrid;
        st.LineType = Rts.Sim.ViewApi.StartBase.UnitOfSlot(_data, w.FactionOf(Killer), UnitSlot.Line);
        st.SiegeType = DefeatStaging.Shooter(_data);
        var used = new HashSet<int>();
        foreach (EntityHandle v in DefeatStaging.UnitsOf(w, Victim))
        {
            g.WorldToCell(w.Units.Position[v.Index], out int vx, out int vy);
            for (int k = 0; k < 3; k++)
            {
                int c = FreeCell(w, vx - 1 + k, vy + 1, -1, used);
                if (c >= 0) Spawn(c, st.LineType);
            }
        }
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
        {
            if (!b.Alive[i] || b.Owner[i] != Victim) continue;
            BuildingDef def = _data.Buildings[b.TypeId[i]];
            int cx = b.Cell[i] % g.Width, cy = b.Cell[i] / g.Width, level = g.LevelAt(cx, cy), placed = 0;
            for (int r = 4; r <= 5 && placed < 8; r++)
                for (int y = cy - r; y <= cy + def.FootprintHeight - 1 + r && placed < 8; y++)
                    for (int x = cx - r; x <= cx + def.FootprintWidth - 1 + r && placed < 8; x++)
                    {
                        bool ring = x == cx - r || x == cx + def.FootprintWidth - 1 + r || y == cy - r || y == cy + def.FootprintHeight - 1 + r;
                        if (!ring || (x + y) % 2 != 0 || !g.InBounds(x, y) || !g.IsPassable(x, y) || g.LevelAt(x, y) != level || !used.Add(y * g.Width + x)) continue;
                        Spawn(y * g.Width + x, st.SiegeType);
                        placed++;
                    }
        }
        st.State = 1;
        st.SpawnTick = sim.TickNumber;
        st.StagedAt = sim.TickNumber;

        void Spawn(int cell, int type)
        {
            System.Numerics.Vector2 p = g.CellCenter(cell % g.Width, cell / g.Width);
            sim.Enqueue(Rts.Sim.Commands.Command.SpawnUnit(Killer, type, p));
            o.DevSpawns.Add((Killer, type, p, sim.TickNumber + 1));
            st.Spawned++;
        }
    }

    // The nearest passable cell to (x0, y0) not in used and with no live unit on it (so no two spawns share a cell); -1 if none within 6.
    private static int FreeCell(World w, int x0, int y0, int level, HashSet<int> used)
    {
        Rts.Sim.Map.NavGrid g = w.NavGrid;
        for (int r = 0; r <= 6; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = x0 + dx, y = y0 + dy;
                    if (!g.InBounds(x, y) || !g.IsPassable(x, y) || (level >= 0 && g.LevelAt(x, y) != level) || used.Contains(y * g.Width + x)) continue;
                    System.Numerics.Vector2 c = g.CellCenter(x, y);
                    bool taken = false;
                    UnitStore u = w.Units;
                    for (int i = 0; i < u.Capacity && !taken; i++)
                        taken = u.Alive[i] && System.Numerics.Vector2.Distance(u.Position[i], c) < 0.75f;
                    if (taken) continue;
                    used.Add(y * g.Width + x);
                    return y * g.Width + x;
                }
        return -1;
    }

    // After the spawns land (the oracle matched them): the killers, then every ReorderTicks each ordered at seat 0's nearest unit (Line) or building.
    private void DriveDefeat(Simulation sim, Oracle o, Staging st)
    {
        World w = sim.World;
        if (st.State == 1)
        {
            if (sim.TickNumber < st.SpawnTick + 6) return;
            foreach ((EntityHandle h, int type) in o.DevUnits) (type == st.SiegeType ? st.Siege : st.Line).Add(h);
            Check(st.Spawned > 0 && st.Line.Count + st.Siege.Count == st.Spawned && o.DevSpawns.Count == 0,
                $"staged defeat: {st.Line.Count} Line and {st.Siege.Count} catapults landed of {st.Spawned} spawned ({o.DevSpawns.Count} unmatched)");
            st.State = 2;
            st.OrderTick = int.MinValue;
        }
        if (st.State != 2 || w.IsDefeated(Victim) || sim.TickNumber < st.OrderTick + ReorderTicks) return;
        st.OrderTick = sim.TickNumber;
        UnitStore u = w.Units;
        foreach (EntityHandle k in st.Line) Order(k, preferUnits: true);
        foreach (EntityHandle k in st.Siege) Order(k, preferUnits: false);

        void Order(EntityHandle k, bool preferUnits)
        {
            if (!u.Alive[k.Index] || u.Generation[k.Index] != k.Generation) return;
            System.Numerics.Vector2 from = u.Position[k.Index];
            EntityHandle unit = Nearest(w, from, buildings: false), building = Nearest(w, from, buildings: true);
            if (preferUnits && unit != default) sim.Enqueue(Rts.Sim.Commands.Command.Attack(Killer, k, unit, isBuilding: false));
            else if (building != default) sim.Enqueue(Rts.Sim.Commands.Command.Attack(Killer, k, building, isBuilding: true));
            else if (unit != default) sim.Enqueue(Rts.Sim.Commands.Command.Attack(Killer, k, unit, isBuilding: false));
        }
    }

    private static EntityHandle Nearest(World w, System.Numerics.Vector2 from, bool buildings)
    {
        EntityHandle best = default;
        float bestD = float.MaxValue;
        if (buildings)
        {
            BuildingStore b = w.Buildings;
            Rts.Sim.Map.NavGrid g = w.NavGrid;
            for (int i = 0; i < b.Capacity; i++)
            {
                if (!b.Alive[i] || b.Owner[i] != Victim) continue;
                float d = System.Numerics.Vector2.DistanceSquared(g.CellCenter(b.Cell[i] % g.Width, b.Cell[i] / g.Width), from);
                if (d < bestD) { bestD = d; best = new EntityHandle(i, b.Generation[i]); }
            }
            return best;
        }
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != Victim) continue;
            float d = System.Numerics.Vector2.DistanceSquared(u.Position[i], from);
            if (d < bestD) { bestD = d; best = new EntityHandle(i, u.Generation[i]); }
        }
        return best;
    }

    private async Task OracleMatch()
    {
        string[] args = { "--skirmish", "--players", "4", "--ai", "2", "hard", "--ai", "3", "normal", "--ai", "4", "hard", "--seed", "11", "--mute", Main.NoConfineFlag };
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args;
        main.SettingsPath = $"{_dir}/settings.cfg";
        main.ReplayDir = _dir;
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = 1e-9; // no tick until the oracle is attached
        AddChild(main);
        await Frames(2);
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match?.Simulation != null, "the shell started no --skirmish match")) return;
            Simulation sim = match!.Simulation!;
            Check(sim.TickNumber == 0, $"the match ticked {sim.TickNumber} times before the oracle attached");
            World w = sim.World;
            int players = w.Config.PlayerCount;
            int age = _data.AgeTechs[0];
            var o = NewOracle(w);
            int ageSeat = -1; // the first seat seen with Age II, the tick it was seen
            bool stopAtAge = false;
            match.GetNode<SimRunner>("SimRunner").Ticked += s =>
            {
                Observe(o, s, age);
                if (ageSeat < 0)
                    for (int p = 0; p < players; p++)
                        if (o.AgeTick[p] == s.TickNumber) { ageSeat = p; stopAtAge = true; GetTree().Paused = true; break; }
            };
            match.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool bannerChecked = false, ageChecked = false;
            var staging = new Staging();
            // The match runs MatchTicks, and on (at most StageGraceTicks) until the staged defeat's banner.
            while ((sim.TickNumber < MatchTicks || (!bannerChecked && staging.State > 0 && sim.TickNumber < MatchTicks + StageGraceTicks))
                && clock.Elapsed.TotalSeconds < 240)
            {
                await Frames(1);
                // Staged once the Age II row has run (the extra army can't move that tick), or near the end if it never does.
                if (staging.State == 0 && !w.IsDefeated(Victim) && ((ageChecked && !stopAtAge) || sim.TickNumber >= MatchTicks - StageGraceTicks))
                    StageDefeat(sim, o, staging);
                if (staging.State > 0) DriveDefeat(sim, o, staging);
                if (stopAtAge && !ageChecked)
                {
                    ageChecked = true;
                    stopAtAge = false;
                    // Opened on the tick Age II first reads true: the cell is that tick's clock (the +1 rule), the rest not_reached.
                    bool banner = main.BannerUp;
                    if (!banner) main.PauseMatch();
                    main.ShowStats();
                    await Frames(1);
                    MatchStats st = main.Stats!;
                    int col = Col(MatchOverText.AgeIiAt);
                    Check(w.TechResearchedTick(ageSeat, age) == sim.TickNumber && st.Cell(ageSeat, col) == _ui.Clock(sim.TickNumber),
                        $"Age II at its tick: seat {ageSeat} researched tick {w.TechResearchedTick(ageSeat, age)}, now {sim.TickNumber}, cell '{st.Cell(ageSeat, col)}' (want '{_ui.Clock(sim.TickNumber)}')");
                    for (int p = 0; p < players; p++)
                        if (p != ageSeat && !w.HasTech(p, age)) Check(st.Cell(p, col) == _ui.MatchOver(MatchOverText.NotReached), $"Age II at its tick: seat {p} cell '{st.Cell(p, col)}'");
                    CompareAll(st, match, o, $"at Age II (tick {sim.TickNumber})");
                    _notes.Add($"seat {ageSeat} first reached Age II at tick {sim.TickNumber} ({_ui.Clock(sim.TickNumber)})");
                    main.CloseStats();
                    if (!banner) main.Resume();
                    else GetTree().Paused = true;
                    if (!banner) GetTree().Paused = false;
                }
                if (main.BannerUp && !bannerChecked)
                {
                    bannerChecked = true;
                    main.ShowStats();
                    await Frames(1);
                    CompareAll(main.Stats!, match, o, $"at the banner (tick {sim.TickNumber})");
                    _notes.Add($"banner '{main.Banner!.Title.Text}' at tick {sim.TickNumber}");
                    main.CloseStats();
                    main.ContinueWatching();
                }
                if (main.BannerUp && bannerChecked) main.ContinueWatching();
            }
            Check(sim.TickNumber >= MatchTicks, $"the match reached tick {sim.TickNumber} in {clock.Elapsed.TotalSeconds:0} s (want {MatchTicks})");
            int endTick = sim.TickNumber;
            if (staging.State == 0 && w.IsDefeated(Victim)) _notes.Add($"seat {Victim} fell to the AIs at {match.DefeatedAt(Victim)}, before the staging");
            else Check(staging.State == 2 && w.IsDefeated(Victim) && o.DevSpawns.Count == 0,
                $"staged defeat: state {staging.State} (staged at tick {staging.StagedAt}), seat {Victim} defeated {w.IsDefeated(Victim)}, {o.DevSpawns.Count} dev spawns the oracle never saw");
            _notes.Add($"staged defeat of seat {Victim} from tick {staging.StagedAt}: {staging.Line.Count} Line + {staging.Siege.Count} catapults for seat {Killer}; seat {Victim} out at {match.DefeatedAt(Victim)}");
            Check(o.ResearchTickMismatches == 0, $"TechResearchedTick disagreed with the first HasTech tick {o.ResearchTickMismatches} times: {o.FirstMismatch}");
            Check(ageChecked, $"no seat reached Age II by tick {sim.TickNumber}: the exact-tick row did not run (an AI change? raise --ticks or MatchTicks)");
            Check(bannerChecked, $"no banner by tick {sim.TickNumber}: the defeated-seat row did not run");
            _notes.Add($"start-base units seen by tick 2 (excluded from trained): {o.StartUnits}");

            // The end: every cell and the graph v the oracle; allocation and redraws while the screen stays open.
            main.PauseMatch();
            await Frames(1);
            main.ShowStats();
            await Frames(3);
            MatchStats stats = main.Stats!;
            CompareAll(stats, match, o, $"at the end (tick {sim.TickNumber})");
            // M6-V13: three AI seats' "opened with" lines at 1280 x 720 (Fits, in Layout, holds them too); the replay's are the same.
            int[] openings = Enumerable.Range(0, players).Select(p => sim.AiOpening(p)).ToArray();
            foreach (string f in StatsScreenCheck.Openings(stats, _data, w, _ui, openings, "at the end")) Check(false, f);
            Check(stats.OpeningLines.Count == 3, $"{stats.OpeningLines.Count} opening lines for three AI seats");
            foreach (string f in StatsScreenCheck.Fits(stats, GetViewport().GetVisibleRect(), "at the end")) Check(false, f);
            await Shot("qa-v11-ai-end");
            int neverSeat = Enumerable.Range(0, players).FirstOrDefault(p => !w.HasTech(p, age), -1);
            if (neverSeat >= 0) Check(stats.Cell(neverSeat, Col(MatchOverText.AgeIiAt)) == _ui.MatchOver(MatchOverText.NotReached), $"seat {neverSeat} never researched: cell '{stats.Cell(neverSeat, Col(MatchOverText.AgeIiAt))}'");
            else _notes.Add("every seat researched Age II (no never-researched seat in this match)");
            for (int p = 0; p < players; p++)
                if (w.IsDefeated(p)) _notes.Add($"seat {p} defeated at {match.DefeatedAt(p)}: Age II cell '{stats.Cell(p, Col(MatchOverText.AgeIiAt))}', oracle {o.AgeTick[p]}");
            int draws = stats.Graph.DrawCount;
            long before = GC.GetAllocatedBytesForCurrentThread();
            await Frames(120);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Check(stats.Graph.DrawCount == draws, $"the open stats screen's graph redrew {stats.Graph.DrawCount - draws} times in 120 paused frames");
            _notes.Add($"managed allocation on the main thread over 120 frames with the stats open: {after - before} bytes ({(after - before) / 120} per frame)");
            var liveCells = Cells(stats, players);
            var liveSamples = Enumerable.Range(0, players).Select(p => w.ArmyValueSamples(p).ToArray()).ToArray();
            var livePoints = Enumerable.Range(0, players).Select(p => stats.Graph.GraphPoints(p).ToArray()).ToArray();
            _notes.Add($"live end: {string.Join(" | ", liveCells.Select(r => string.Join(",", r)))}");
            await Layout(main, match);
            main.CloseStats();

            // Part 3: watch the recording; its stats equal the live ones.
            string? path = main.ReplayPath;
            main.QuitToMenu();
            await Frames(2);
            if (!Check(path != null && File.Exists(path), $"Quit to menu wrote no replay ({path})")) return;
            if (!Check(main.WatchReplay(path!), "the recorded match would not play")) return;
            await Frames(2);
            Match? play = main.CurrentMatch;
            if (!Check(play?.Simulation != null && play.IsPlayback, "no playback match")) return;
            SimRunner runner = play!.GetNode<SimRunner>("SimRunner");
            runner.FixedFrameDelta = FrameSeconds;
            clock.Restart();
            while (runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 240)
            {
                await Frames(1);
                if (main.BannerUp && runner.PlaybackState == PlaybackState.Playing) main.ContinueWatching();
            }
            Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None, $"playback {runner.PlaybackState} ({runner.PlaybackError}) at tick {play.Simulation!.TickNumber}");
            if (!main.BannerUp) main.PauseMatch();
            main.ShowStats();
            await Frames(2);
            var replayCells = Cells(main.Stats!, players);
            foreach (string f in StatsScreenCheck.Openings(main.Stats!, _data, play.Simulation!.World, _ui, openings, "the watched replay")) Check(false, f);
            for (int p = 0; p < players; p++)
            {
                Check(replayCells[p].SequenceEqual(liveCells[p]), $"replay row {p}: [{string.Join(", ", replayCells[p])}] (live [{string.Join(", ", liveCells[p])}])");
                Check(play.Simulation!.World.ArmyValueSamples(p).SequenceEqual(liveSamples[p]), $"replay row {p}: samples differ from the live match's");
                Check(main.Stats!.Graph.GraphPoints(p).ToArray().SequenceEqual(livePoints[p]), $"replay row {p}: graph points differ from the live screen's");
            }
            Check(main.Stats!.Length.Text == _ui.MatchLength(endTick), $"replay length '{main.Stats.Length.Text}' (want '{_ui.MatchLength(endTick)}')");
            main.CloseStats();
            main.QuitToMenu();
            await Frames(2);
        }
        finally
        {
            if (GetTree().Paused) GetTree().Paused = false;
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    private static Oracle NewOracle(World w)
    {
        int n = w.Config.PlayerCount, uc = w.Units.Capacity, bc = w.Buildings.Capacity;
        var o = new Oracle
        {
            Gold = new long[n], Wood = new long[n], Trained = new int[n], UnitKills = new int[n], BuildingKills = new int[n], UnitLosses = new int[n],
            BuildingLosses = new int[n], Built = new int[n], AgeTick = Enumerable.Repeat(-1, n).ToArray(),
            Samples = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray(),
            PrevCargo = new int[uc], PrevGen = new int[uc], PrevKind = new ResourceKind[uc], PrevAlive = new bool[uc],
            PrevBGen = new int[bc], PrevBAlive = new bool[bc], PrevSite = new bool[bc],
        };
        Snapshot(o, w);
        return o;
    }

    private static void Snapshot(Oracle o, World w)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            o.PrevAlive[i] = u.Alive[i];
            o.PrevGen[i] = u.Generation[i];
            o.PrevCargo[i] = u.Cargo[i];
            o.PrevKind[i] = u.CargoKind[i];
        }
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
        {
            o.PrevBAlive[i] = b.Alive[i];
            o.PrevBGen[i] = b.Generation[i];
            o.PrevSite[i] = b.Alive[i] && b.UnderConstruction[i];
        }
    }

    // One tick's events read from the raw stores, independent of the M6-S1 counters.
    private static void Observe(Oracle o, Simulation s, int age)
    {
        World w = s.World;
        int n = w.Config.PlayerCount;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            bool same = o.PrevAlive[i] && u.Alive[i] && o.PrevGen[i] == u.Generation[i];
            if (same && o.PrevCargo[i] > 0 && u.Cargo[i] < o.PrevCargo[i] && u.CargoKind[i] == o.PrevKind[i] && u.Cargo[i] == 0)
            {
                if (o.PrevKind[i] == ResourceKind.Gold) o.Gold[u.Owner[i]] += o.PrevCargo[i];
                else o.Wood[u.Owner[i]] += o.PrevCargo[i];
            }
            bool fresh = u.Alive[i] && (!o.PrevAlive[i] || o.PrevGen[i] != u.Generation[i]);
            if (fresh && DevSpawn(o, s.TickNumber, i, u.Generation[i], u.Owner[i], u.TypeId[i], u.Position[i])) continue; // M6-V13: the staged defeat's killers (dev spawns: not trained)
            if (fresh && !u.IsSummoned(i) && s.TickNumber > 2) o.Trained[u.Owner[i]]++; // ticks 1-2: the start base's workers (dev spawns, not counted; they appear on tick 2)
            if (fresh && s.TickNumber <= 2) o.StartUnits++;
        }
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
            if (o.PrevSite[i] && b.Alive[i] && b.Generation[i] == o.PrevBGen[i] && !b.UnderConstruction[i]) o.Built[b.Owner[i]]++;
        foreach (DeathEvent d in w.Deaths)
        {
            if (d.Expired || d.Collapsed || d.KillerOwner < 0) continue;
            if (d.IsBuilding) { o.BuildingKills[d.KillerOwner]++; o.BuildingLosses[d.VictimOwner]++; }
            else { o.UnitKills[d.KillerOwner]++; o.UnitLosses[d.VictimOwner]++; }
        }
        for (int p = 0; p < n; p++)
        {
            if (o.AgeTick[p] < 0 && w.HasTech(p, age)) o.AgeTick[p] = s.TickNumber;
            int got = w.TechResearchedTick(p, age);
            if (got != o.AgeTick[p])
            {
                if (o.ResearchTickMismatches++ == 0) o.FirstMismatch = $"seat {p} at tick {s.TickNumber}: researched tick {got}, first HasTech tick {o.AgeTick[p]}";
            }
        }
        if (s.TickNumber % w.StatsSampleTicks == 0 && o.Samples[0].Count < w.StatsSampleCapacity)
        {
            var sum = new long[n];
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                UnitDef def = w.Data.Units[u.TypeId[i]];
                if (def.IsWorker) continue;
                sum[u.Owner[i]] += def.CostGold + def.CostWood;
            }
            for (int p = 0; p < n; p++) o.Samples[p].Add((int)Math.Min(sum[p], int.MaxValue));
        }
        o.Ticks = s.TickNumber;
        Snapshot(o, w);
    }

    private static bool DevSpawn(Oracle o, int tick, int slot, int generation, int owner, int type, System.Numerics.Vector2 at)
    {
        int best = -1;
        float bestD = 3f;
        for (int k = 0; k < o.DevSpawns.Count; k++)
        {
            var d = o.DevSpawns[k];
            if (d.Owner != owner || d.Type != type || tick < d.Tick || tick > d.Tick + 4) continue; // applied on the next tick or so
            float dist = System.Numerics.Vector2.Distance(d.At, at);
            if (dist <= bestD) { bestD = dist; best = k; }
        }
        if (best < 0) return false;
        o.DevSpawns.RemoveAt(best);
        o.DevUnits.Add((new EntityHandle(slot, generation), type));
        return true;
    }

    private void CompareAll(MatchStats st, Match match, Oracle o, string how)
    {
        World w = match.Simulation!.World;
        int n = w.Config.PlayerCount;
        Check(st.Visible && st.RowCount == n, $"{how}: stats visible {st.Visible}, rows {st.RowCount}");
        for (int p = 0; p < Math.Min(n, st.RowCount); p++)
        {
            Want(MatchOverText.GatheredGold, o.Gold[p].ToString(CultureInfo.InvariantCulture));
            Want(MatchOverText.GatheredWood, o.Wood[p].ToString(CultureInfo.InvariantCulture));
            Want(MatchOverText.UnitsTrained, o.Trained[p].ToString(CultureInfo.InvariantCulture));
            Want(MatchOverText.UnitsKilled, o.UnitKills[p].ToString(CultureInfo.InvariantCulture));
            Want(MatchOverText.UnitsLost, o.UnitLosses[p].ToString(CultureInfo.InvariantCulture));
            Want(MatchOverText.BuildingsBuilt, o.Built[p].ToString(CultureInfo.InvariantCulture));
            Want(MatchOverText.BuildingsKilled, o.BuildingKills[p].ToString(CultureInfo.InvariantCulture));
            Want(MatchOverText.BuildingsLost, o.BuildingLosses[p].ToString(CultureInfo.InvariantCulture));
            Want(MatchOverText.AgeIiAt, o.AgeTick[p] >= 0 ? _ui.Clock(o.AgeTick[p]) : _ui.MatchOver(MatchOverText.NotReached));
            Want(MatchOverText.DefeatedAt, match.DefeatedAt(p) >= 0 ? _ui.Clock(match.DefeatedAt(p)) : _ui.MatchOver(MatchOverText.Standing));
            // The graph: the oracle's samples, mapped as the brief says.
            ArmyValueGraph g = st.Graph;
            ReadOnlySpan<Vector2> pts = g.GraphPoints(p);
            List<int> s = o.Samples[p];
            Check(pts.Length == s.Count, $"{how}: row {p}: {pts.Length} graph points, oracle {s.Count} samples");
            Check(w.ArmyValueSamples(p).SequenceEqual(s.ToArray()), $"{how}: row {p}: sim samples [{string.Join(",", w.ArmyValueSamples(p).ToArray().TakeLast(5))}] v oracle [{string.Join(",", s.TakeLast(5))}]");
            int max = 0;
            foreach (List<int> l in o.Samples) foreach (int v in l) max = Math.Max(max, v);
            Rect2 r = g.PlotRect;
            for (int k = 0; k < Math.Min(pts.Length, s.Count); k++)
            {
                float x = r.Position.X + (float)((double)(k + 1) * w.StatsSampleTicks / Math.Max(1, o.Ticks)) * r.Size.X;
                float y = max > 0 ? r.End.Y - (float)((double)s[k] / max) * r.Size.Y : r.End.Y;
                if (Math.Abs(pts[k].X - x) > 0.5f || Math.Abs(pts[k].Y - y) > 0.5f) { Check(false, $"{how}: row {p} point {k} {pts[k]} (want ({x}, {y}))"); break; }
            }

            void Want(MatchOverText label, string want)
            {
                string got = st.Cell(p, Col(label));
                Check(got == want, $"{how}: row {p} {label} '{got}' (oracle '{want}')");
            }
        }
        Check(st.Graph.DrawnLines == Enumerable.Range(0, n).Count(p => o.Samples[p].Count > 0), $"{how}: graph drew {st.Graph.DrawnLines} lines");
    }

    private static int Col(MatchOverText label) => Array.FindIndex(MatchStats.Columns, c => c.Label == label);

    private static string[][] Cells(MatchStats st, int players) =>
        Enumerable.Range(0, players).Select(p => Enumerable.Range(0, MatchStats.Columns.Length).Select(c => st.Cell(p, c)).ToArray()).ToArray();

    // ---- part 4: four seats, 6-digit counters at 1280 x 720 ----

    private static readonly System.Reflection.PropertyInfo LedgerProperty =
        typeof(World).GetProperty("Ledger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private async Task Layout(Main main, Match match)
    {
        World w = match.Simulation!.World;
        object ledger = LedgerProperty.GetValue(w)!;
        object stats = ledger.GetType().GetProperty("Stats")!.GetValue(ledger)!;
        string[] names = { "GatheredGold", "GatheredWood", "UnitsTrained", "UnitKills", "BuildingKills", "UnitLosses", "BuildingLosses", "BuildingsBuilt" };
        var saved = new Dictionary<string, int[]>();
        foreach (int value in new[] { 999_999, int.MaxValue })
        {
            foreach (string nm in names)
            {
                var arr = (int[])stats.GetType().GetProperty(nm)!.GetValue(stats)!;
                if (!saved.ContainsKey(nm)) saved[nm] = (int[])arr.Clone();
                Array.Fill(arr, value);
            }
            main.ShowStats();
            await Frames(3);
            await Shot($"qa-v11-digits-{value}");
            MatchStats st = main.Stats!;
            Rect2 screen = GetViewport().GetVisibleRect();
            var bad = StatsScreenCheck.Fits(st, screen, $"{value} in every cell at {screen.Size}");
            // A cell's text wider than its column would push the table (labels don't clip): the column stays 74 px.
            var grid = (GridContainer)st.Table;
            float widest = 0f;
            foreach (Node c in grid.GetChildren())
                if (c is Label l && l.HorizontalAlignment == HorizontalAlignment.Right) widest = Math.Max(widest, l.Size.X);
            if (value == 999_999)
            {
                foreach (string f in bad) Check(false, f);
                Check(widest <= MatchStats.ColumnWidth + 0.5f, $"6-digit cells widened a column to {widest} px (want {MatchStats.ColumnWidth})");
            }
            else _notes.Add($"int.MaxValue in every cell at {screen.Size}: widest column {widest} px, table {grid.GetGlobalRect()}, fits: {(bad.Count == 0 ? "yes" : string.Join("; ", bad))}");
        }
        foreach ((string nm, int[] arr) in saved)
            Array.Copy(arr, (int[])stats.GetType().GetProperty(nm)!.GetValue(stats)!, arr.Length);
    }

    // ---- helpers ----

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
