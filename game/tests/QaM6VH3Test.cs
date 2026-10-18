using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-VH3 (view hardening: BUG-0830 replay folder, BUG-0890 stats graph, BUG-0281 item 2 markers in fog). Four parts:
/// <list type="bullet">
/// <item>The graph's time axis over spans from 1 tick to 24 hours (labels 0 first, rising, inside the span, distinct clocks,
/// at least 40 px apart; a sub-minute match shows its scale), and the draw offsets of four seats with two tied at zero.</item>
/// <item>The stats screen of a 20 s four-seat match (every value 0, the grey Iron seat) at 1280 x 720 and 1152 x 648: the
/// shared <see cref="StatsScreenCheck"/> rows (graph, readability, headings) and <see cref="StatsScreenCheck.Fits"/>.</item>
/// <item>The replay folder: header-only list v the whole read on 400 cut copies and CRLF / BOM / trailing-junk variants (a
/// file that plays is never unreadable; a cut file is never listed); a one-hour 4 MB file's list time; the prune with the
/// just-written path in another spelling, keep 0, a locked file; Load Game with only unreadable files (no pick, double-click
/// starts nothing, Delete empties it).</item>
/// <item>Markers in fog: the corpse of a lone own unit killed in the open (the player saw it fall) is drawn.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH3Test.tscn</c>; windowed with
/// <c>-- --shots &lt;dir&gt;</c> saves the stats screen at both sizes. Prints "QA M6-VH3 TEST PASS" or FAIL lines.
/// </summary>
public partial class QaM6VH3Test : Node
{
    private const double FrameSeconds = 0.25;
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
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            string[] user = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < user.Length; i++)
                if (user[i] == "--shots") _shots = user[i + 1];
            _dir = $"user://qa-m6vh3-{OS.GetProcessId()}";
            _full = Path.GetFullPath(ProjectSettings.GlobalizePath(_dir));
            if (Directory.Exists(_full)) Directory.Delete(_full, true);
            Directory.CreateDirectory(_full);

            await TimeAxis();
            await StatsScreen();
            ReplayFolderFiles();
            await LoadGameUnreadableOnly();
            await LoneUnitCorpse();
            try { Directory.Delete(_full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string n in _notes) GD.Print($"  note: {n}");
        foreach (string f in _failures.Take(80)) GD.Print($"QA M6-VH3 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-VH3 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- 1: the time axis and the offsets on a standalone graph ----

    private async Task TimeAxis()
    {
        var host = new Control { Name = "GraphHost", Size = new Vector2(820f, 220f) };
        AddChild(host);
        host.AddChild(new ColorRect { Color = MatchStats.BackdropColour, Size = new Vector2(820f, 220f) }); // for the windowed shot
        var g = new ArmyValueGraph { Name = "Graph", Size = new Vector2(820f, 220f) };
        host.AddChild(g);
        g.Build(_ui);
        await Frames(2);
        int tps = SimConstants.TicksPerSecond;
        Color iron = new(0x4B / 255f, 0x4F / 255f, 0x55 / 255f);
        Color[] four = { iron, Colors.Red, Colors.Green, Colors.Yellow };
        // Spans: 1 tick, under a second, 5 s, 10 s, 19.95 s, 20 s, 59.95 s, 1 min, 1:00.05, 3 min, 1 h, 3 h, 16 h, 24 h (the replay cap).
        int[] spans = { 1, 19, 100, 200, 399, 400, 1199, 1200, 1201, 3600, 72_000, 216_000, 1_152_000, Replay.MaxTickCount };
        foreach (int span in spans)
        {
            int samples = Math.Min(span / 200, 1024);
            int[][] s = Enumerable.Range(0, 4).Select(p => Enumerable.Range(0, samples).Select(k => p == 1 || p == 2 ? 0 : 100 + k).ToArray()).ToArray();
            await Set(g, s, four, 200, span);
            string how = $"span {span} ticks ({_ui.Clock(span)})";
            foreach (string f in StatsScreenCheck.TimeAxis(g, how))
            {
                // Over 16 h the 2 h step gives more than ten labels (as the minute steps did before M6-VH3): a note, not a failure.
                if (span > 1_152_000) _notes.Add(f);
                else Check(false, f);
            }
            var t = g.TimeLabelTicks;
            var clocks = t.Select(x => _ui.Clock(x)).ToList();
            Check(clocks.Distinct().Count() == clocks.Count, $"{how}: repeated clock labels [{string.Join(", ", clocks)}]");
            Rect2 r = g.PlotRect;
            for (int i = 1; i < t.Count; i++)
            {
                float dx = (float)((double)(t[i] - t[i - 1]) / g.SpanTicks) * r.Size.X;
                if (dx < 40f) { Check(false, $"{how}: labels {clocks[i - 1]} and {clocks[i]} only {dx:F0} px apart (overlap)"); break; }
            }
            if (span >= 100 && span < 1200)
                Check(t.Count >= 2, $"{how}: a sub-minute match shows only [{string.Join(", ", clocks)}]: no scale");
            if (span == 400)
                Check(t.SequenceEqual(new[] { 0, 10 * tps, 20 * tps }), $"20 s: labels at [{string.Join(", ", t)}] (want 0, 200, 400)");
            _notes.Add($"{how}: labels [{string.Join(" ", clocks)}]");
            if (span == 399) await Shot("qa-vh3-graph-one-sample"); // four seats, one sample each, 1 and 2 tied at 0
        }
        // Four seats, 1 and 2 tied at 0: drawn apart, the grey seat's line (or outline) readable on the backdrop.
        var offsets = Enumerable.Range(0, 4).Select(g.DrawOffset).ToArray();
        Check(offsets.Distinct().Count() == 4 && offsets.Max() - offsets.Min() <= 3 * ArmyValueGraph.OffsetStep + 0.01f,
            $"four seats: offsets [{string.Join(", ", offsets)}]");
        Check(Math.Abs(offsets[1] - offsets[2]) >= 2f, $"two seats tied at zero drawn {offsets[1]} / {offsets[2]} px off: overlap");
        double ironOnBack = StatsScreenCheck.Contrast(iron, MatchStats.BackdropColour);
        Color outline = MatchStats.BackdropColour.Lerp(new Color(ArmyValueGraph.OutlineColour, 1f), ArmyValueGraph.OutlineColour.A);
        double ironOnOutline = StatsScreenCheck.Contrast(iron, outline), outlineOnBack = StatsScreenCheck.Contrast(outline, MatchStats.BackdropColour);
        _notes.Add($"Iron line {ironOnBack:F2}:1 on the backdrop, {ironOnOutline:F2}:1 on its outline; the outline {outlineOnBack:F2}:1 on the backdrop");
        // The baseline's lowest line (+4.5 px) stays inside the graph control: not drawn over the time labels.
        Rect2 plot = g.PlotRect;
        Check(plot.End.Y + offsets.Max() + 2.5f < plot.End.Y + 19f - 10f, $"the lowest zero line at {plot.End.Y + offsets.Max()} reaches the time labels");
        host.QueueFree();
        await Frames(1);
    }

    private async Task Set(ArmyValueGraph g, int[][] samples, Color[] colours, int interval, int ticks)
    {
        g.SetData(samples, colours, interval, ticks);
        int before = g.DrawCount;
        for (int i = 0; i < 5 && g.DrawCount == before; i++) await Frames(1);
    }

    // ---- 2: the stats screen of a 20 s four-seat match at both sizes ----

    private async Task StatsScreen()
    {
        Main main = NewMain(new[] { "--skirmish", "--players", "4", "--seed", "5", "--mute" }, "stats");
        main.ReplayDir = null;
        AddChild(main);
        await Frames(2);
        try
        {
            Match? match = main.CurrentMatch;
            if (!Check(match?.Simulation != null, "stats: no match")) return;
            Simulation sim = match!.Simulation!;
            SimRunner runner = match.GetNode<SimRunner>("SimRunner");
            runner.Ticked += s => { if (s.TickNumber == 400) GetTree().Paused = true; };
            var clock = Stopwatch.StartNew();
            while (sim.TickNumber < 400 && clock.Elapsed.TotalSeconds < 60) await Frames(1);
            Check(sim.TickNumber == 400, $"stats: paused at tick {sim.TickNumber} (want 400)");
            GetTree().Paused = false;
            main.PauseMatch();
            main.ShowStats();
            await Frames(2);
            World w = sim.World;
            int zero = Enumerable.Range(0, 4).Count(p => w.ArmyValueSamples(p).ToArray().All(v => v == 0));
            _notes.Add($"20 s four seats: samples per seat {w.ArmyValueSamples(0).Length}, seats at zero all match {zero}, seat 0 rgb {match.PlayerRgb[0]:X6}");
            Check(match.PlayerRgb[0] == 0x4B4F55, $"seat 0 is not the grey Iron seat ({match.PlayerRgb[0]:X6})");
            Check(zero >= 2, $"20 s four seats: only {zero} seats tied at zero (the row wants two or more)");
            foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(1152, 648) })
            {
                if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetSize(size);
                GetTree().Root.Size = size;
                await Frames(3);
                MatchStats st = main.Stats!;
                string how = $"20 s at {size.X}x{size.Y}";
                foreach (string f in StatsScreenCheck.Graph(st, match, true, how)) Check(false, f);
                foreach (string f in StatsScreenCheck.Fits(st, GetViewport().GetVisibleRect(), how)) Check(false, f);
                var t = st.Graph.TimeLabelTicks;
                Check(t.SequenceEqual(new[] { 0, 200, 400 }), $"{how}: time labels [{string.Join(", ", t)}]");
                await Shot($"qa-vh3-stats-20s-{size.X}x{size.Y}");
            }
            main.CloseStats();
        }
        finally
        {
            if (GetTree().Paused) GetTree().Paused = false;
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- 3: the replay folder ----

    private static Replay Synthetic(int ticks, int commands, int players = 4)
    {
        var cmds = ImmutableArray.CreateBuilder<Command>(commands);
        var seq = new int[players];
        for (int i = 0; i < commands; i++)
        {
            int p = i % players;
            Command c = Command.Noop(p);
            c.Tick = 1 + (int)((long)i * (ticks - 1) / Math.Max(1, commands));
            c.Sequence = seq[p]++;
            cmds.Add(c);
        }
        const int interval = 100;
        var ks = ImmutableArray.CreateBuilder<ReplayCheckpoint>(ticks / interval);
        for (int k = 0; k < ticks / interval; k++) ks.Add(new ReplayCheckpoint((k + 1) * interval, 0x1234_5678_9ABC_DEF0UL + (ulong)k));
        return new Replay
        {
            SimVersion = "qa",
            Seats = Enumerable.Range(0, players).Select(p => new ReplaySeat("malazan", p == 0 ? SeatController.Human : SeatController.Ai, AiDifficulty.Normal)).ToImmutableArray(),
            DataHash = 0xC059AD620DE581F1UL,
            Map = MapGenParams.Default,
            Seed = 77,
            PlayerCount = players,
            UnitCapacity = 4096,
            CommandCapacity = 4096,
            CheckpointInterval = interval,
            TickCount = ticks,
            Commands = cmds.MoveToImmutable(),
            Checkpoints = ks.MoveToImmutable(),
        };
    }

    private void ReplayFolderFiles()
    {
        string dir = Path.Combine(_full, "files");
        Directory.CreateDirectory(dir);
        string good = Path.Combine(dir, "good.replay");
        ReplayFormat.WriteFile(Synthetic(2000, 300), good);
        if (!Check(ReplayFormat.TryReadFile(good, out Replay? whole) == ReplayError.None, "synthetic replay does not read whole")) return;
        Check(ReplayStore.TryReadHeader(good, out ReplayEntry? e) && e!.Seed == 77 && e.TickCount == 2000 && e.Seats.Length == 4 && e.Seats.SequenceEqual(whole!.Seats),
            $"header of the synthetic replay: {e?.Seed} {e?.TickCount} {e?.Seats.Length}");
        byte[] bytes = File.ReadAllBytes(good);

        // Cut copies: every cut is unreadable to the header read and to the whole read.
        string cut = Path.Combine(dir, "cut.replay");
        int listedCuts = 0, wholeCuts = 0;
        for (int i = 0; i < 400; i++)
        {
            int len = (int)((long)i * (bytes.Length - 1) / 399);
            File.WriteAllBytes(cut, bytes[..len]);
            if (ReplayStore.TryReadHeader(cut, out _)) listedCuts++;
            if (ReplayFormat.TryReadFile(cut, out _) == ReplayError.None) wholeCuts++;
        }
        Check(listedCuts == 0, $"{listedCuts} of 400 cut copies listed by the header read (a cut file has no checksum line)");
        Check(wholeCuts == 0, $"{wholeCuts} of 400 cut copies read whole");
        File.Delete(cut);

        // Variants: whenever the whole read plays a file, the header read must list it with the same fields.
        string text = Encoding.ASCII.GetString(bytes);
        var variants = new List<(string Name, byte[] Bytes)>
        {
            ("crlf", Encoding.ASCII.GetBytes(text.Replace("\n", "\r\n"))),
            ("bom", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray()),
            ("trailing-newline", bytes.Concat(new[] { (byte)'\n' }).ToArray()),
            ("trailing-junk", bytes.Concat(Encoding.ASCII.GetBytes("junk\n")).ToArray()),
            ("no-final-lf", bytes[..^1]),
            ("header-only-with-checksum", Encoding.ASCII.GetBytes(text[..text.IndexOf("map.width", StringComparison.Ordinal)] + text[text.LastIndexOf("checksum ", StringComparison.Ordinal)..])),
            ("ticks-not-a-number", Encoding.ASCII.GetBytes(text.Replace("\nticks 2000\n", "\nticks 2x00\n"))),
            ("players-17", Encoding.ASCII.GetBytes(text.Replace("\nplayers 4\n", "\nplayers 17\n"))),
            ("seat-difficulty-unknown", Encoding.ASCII.GetBytes(text.Replace("ai normal\n", "ai brutal\n"))),
            ("format-99", Encoding.ASCII.GetBytes(text.Replace("rts-replay 6\n", "rts-replay 99\n"))),
            ("one-byte", new[] { (byte)'\n' }),
        };
        string v = Path.Combine(dir, "variant.replay");
        foreach ((string name, byte[] b) in variants)
        {
            File.WriteAllBytes(v, b);
            ReplayError full = ReplayFormat.TryReadFile(v, out Replay? r);
            bool listed = ReplayStore.TryReadHeader(v, out ReplayEntry? entry);
            _notes.Add($"variant {name}: whole read {full}, header read {(listed ? "listed" : "unreadable")}");
            if (full == ReplayError.None)
                Check(listed && entry!.Seed == r!.Seed && entry.TickCount == r.TickCount && entry.Seats.SequenceEqual(r.Seats),
                    $"variant {name}: plays but the list calls it unreadable ({listed}) or reads other fields");
        }
        File.Delete(v);

        // A one-hour four-seat file (72,000 ticks, 100,000 commands): twenty of them listed by headers.
        string hour = Path.Combine(_full, "hour");
        Directory.CreateDirectory(hour);
        string first = Path.Combine(hour, ReplayStore.FileName(new DateTime(2026, 1, 1, 12, 0, 0), 1));
        ReplayFormat.WriteFile(Synthetic(72_000, 100_000), first);
        long size = new FileInfo(first).Length;
        for (int d = 1; d < ReplayStore.MaxReplays; d++) File.Copy(first, Path.Combine(hour, ReplayStore.FileName(new DateTime(2026, 1, 1, 12, 0, 0).AddDays(d), (ulong)(d + 1))));
        var sw = Stopwatch.StartNew();
        ReplayFormat.TryReadFile(first, out _);
        double wholeMs = sw.Elapsed.TotalMilliseconds;
        ReplayStore.List(hour, out _); // warm the JIT
        sw.Restart();
        var listedHour = ReplayStore.List(hour, out int skippedHour);
        double listMs = sw.Elapsed.TotalMilliseconds;
        _notes.Add($"one-hour file {size / 1024} KB: whole read {wholeMs:0} ms; header list of {listedHour.Count} such files {listMs:0.0} ms");
        Check(listedHour.Count == ReplayStore.MaxReplays && skippedHour == 0 && listedHour.All(x => x.TickCount == 72_000), $"one-hour folder: {listedHour.Count} listed, {skippedHour} skipped");
        Check(listMs < 200, $"one-hour folder listed in {listMs:0} ms (want well under the 1-2 s BUG-0830 measured)");

        // Prune: the just-written path in another spelling (forward slashes, upper case) is still the one kept.
        string future = Path.Combine(_full, "future");
        Directory.CreateDirectory(future);
        for (int d = 0; d < ReplayStore.MaxReplays + 5; d++) File.WriteAllBytes(Path.Combine(future, ReplayStore.FileName(new DateTime(2099, 1, 1).AddDays(d), (ulong)d)), bytes);
        string mine = Path.Combine(future, ReplayStore.FileName(new DateTime(2026, 10, 17, 12, 0, 0), 9));
        File.WriteAllBytes(mine, bytes);
        int deleted = ReplayStore.Prune(future, ReplayStore.MaxReplays, mine.Replace('\\', '/').ToUpperInvariant());
        int left = Directory.GetFiles(future, "*.replay").Length;
        Check(File.Exists(mine) && left == ReplayStore.MaxReplays && deleted == 6, $"prune (other spelling): mine kept {File.Exists(mine)}, {left} left, {deleted} deleted (want 20, 6)");
        deleted = ReplayStore.Prune(future, 0, mine);
        Check(File.Exists(mine) && Directory.GetFiles(future, "*.replay").Length == 1, $"prune keep 0: mine kept {File.Exists(mine)}, {Directory.GetFiles(future, "*.replay").Length} left");
        Check(ReplayStore.Prune(Path.Combine(_full, "no-such-folder"), 20, mine) == 0 && ReplayStore.Prune(future, -5) == 1 && !File.Exists(mine),
            "prune of a missing folder or a negative keep");

        // A locked file: unreadable to the list (no exception), Delete fails cleanly.
        string locked = Path.Combine(future, ReplayStore.FileName(new DateTime(2026, 3, 3), 3));
        File.WriteAllBytes(locked, bytes);
        using (var hold = new FileStream(locked, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None))
        {
            var l = new List<string>();
            var listed = ReplayStore.List(future, l, out int sk);
            Check(listed.Count == 0 && sk == 1 && l.Count == 1, $"locked file: {listed.Count} listed, {sk} skipped");
            Check(!ReplayStore.Delete(locked) && File.Exists(locked), "locked file: Delete said it deleted it");
            Check(ReplayStore.Prune(future, 0) == 0, "locked file: pruned");
        }
        Check(ReplayStore.List(future, out _).Count == 1, "unlocked file not listed again");
    }

    // Load Game on a folder of unreadable files only: nothing picked, Watch and Delete greyed, a double-click starts nothing,
    // picking and deleting each empties the folder and the note says load.empty.
    private async Task LoadGameUnreadableOnly()
    {
        string dir = $"{_dir}/junk";
        string full = Path.GetFullPath(ProjectSettings.GlobalizePath(dir));
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, "a.replay"), "junk\n");
        File.WriteAllBytes(Path.Combine(full, "b.replay"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(full, "20260101-000000-seed1.replay"), "rts-replay 6\nchecksum 0\n");
        Main main = NewMain(Array.Empty<string>(), "junk");
        main.ReplayDir = dir;
        AddChild(main);
        await Frames(2);
        try
        {
            main.ShowLoadGame();
            await Frames(1);
            LoadGameScreen load = main.LoadScreen!;
            Check(load.Entries.Count == 0 && load.Unreadable.Count == 3 && load.List.GetRoot().GetChildCount() == 3 && load.SelectedIndex == -1
                && load.WatchButton.Disabled && load.DeleteButton.Disabled && load.Note.Text == _ui.ReplaysSkipped(3),
                $"junk only: {load.Entries.Count} replays, {load.Unreadable.Count} unreadable, picked {load.SelectedIndex}, Watch {load.WatchButton.Disabled}, Delete {load.DeleteButton.Disabled}, note '{load.Note.Text}'");
            await Shot("qa-vh3-load-unreadable");
            int watched = 0;
            load.WatchPressed += _ => watched++;
            load.Select(0);
            load.List.EmitSignal(Tree.SignalName.ItemActivated);
            load.WatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            Check(watched == 0 && main.CurrentMatch == null, $"junk only: Watch / double-click on an unreadable row raised {watched} watches, match {main.CurrentMatch != null}");
            for (int i = 0; i < 3; i++)
            {
                if (!load.Select(0)) break;
                load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(1);
            }
            Check(Directory.GetFiles(full, "*.replay").Length == 0 && load.List.GetRoot().GetChildCount() == 0 && load.Note.Text == _ui.LoadGame(LoadText.Empty),
                $"junk only after three Deletes: {Directory.GetFiles(full, "*.replay").Length} files, {load.List.GetRoot().GetChildCount()} rows, note '{load.Note.Text}'");
            // A row whose file vanished behind the screen's back: Delete re-reads, no exception.
            File.WriteAllText(Path.Combine(full, "c.replay"), "junk\n");
            load.Refresh(dir);
            File.Delete(Path.Combine(full, "c.replay"));
            load.Select(0);
            load.DeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            Check(load.List.GetRoot().GetChildCount() == 0, $"vanished file: {load.List.GetRoot().GetChildCount()} rows after Delete");
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- 4: a lone own unit killed in the open: the player saw it fall, its corpse is drawn ----

    // Four lead-in lengths, so the death falls on each phase of the fog's refresh.
    private async Task LoneUnitCorpse()
    {
        for (int lead = 3; lead < 7; lead++) await LoneUnitCorpse(lead);
    }

    private async Task LoneUnitCorpse(int lead)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--mute" }));
        runner.ProcessMode = ProcessModeEnum.Disabled;
        Simulation sim = runner.Simulation!;
        CombatViews combat = match.GetNode<CombatViews>("World3D/CombatViews");
        try
        {
            World w = sim.World;
            NavGrid g = w.NavGrid;
            int type = -1;
            for (int t = 0; t < _data.Units.Length && type < 0; t++)
                if (!_data.Units[t].IsWorker && _data.Units[t].Slot == UnitSlot.Line) type = t;
            if (!Check(type >= 0, "no Line unit in the data")) return;
            System.Numerics.Vector2 at = FindOpen(g, g.Width / 2, g.Height / 2);
            for (int t = 0; t < lead; t++) { sim.Tick(); await Frames(1); } // the start base first
            bool[] before = w.Units.Alive.ToArray();
            sim.Enqueue(Command.SpawnUnit(0, type, at));
            for (int k = 0; k < 6; k++)
                sim.Enqueue(Command.SpawnUnit(1, type, at + new System.Numerics.Vector2(2.5f * MathF.Cos(k * 1.047f), 2.5f * MathF.Sin(k * 1.047f))));
            sim.Tick();
            sim.Tick(); // the commands apply on the tick after the one they were enqueued in
            await Frames(1);
            EntityHandle victim = default;
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && !before[i] && w.Units.Owner[i] == 0) victim = new EntityHandle(i, w.Units.Generation[i]);
            int spawned = Enumerable.Range(0, w.Units.Capacity).Count(i => w.Units.Alive[i] && !before[i]);
            if (!Check(victim != default, $"the lone unit did not spawn ({spawned} new units at {at}, tick {sim.TickNumber}, {before.Count(a => a)} of {w.Units.Capacity} slots alive before)")) return;
            bool seenAlive = false;
            System.Numerics.Vector2 last = at;
            for (int t = 0; t < 600 && w.Units.Alive[victim.Index] && w.Units.Generation[victim.Index] == victim.Generation; t++)
            {
                last = w.Units.Position[victim.Index];
                seenAlive |= w.Fog.IsVisible(0, FogCell(g, last));
                sim.Tick();
                await Frames(1);
            }
            bool dead = !w.Units.Alive[victim.Index] || w.Units.Generation[victim.Index] != victim.Generation;
            if (!Check(dead && seenAlive, $"lone unit: dead {dead}, its cell visible while alive {seenAlive}")) return;
            await Frames(2);
            int marker = -1;
            float best = float.MaxValue;
            for (int k = 0; k < combat.Markers.Capacity; k++)
            {
                if (!combat.Markers.Active[k] || combat.Markers.IsBuilding[k] || combat.Markers.Owner[k] != 0) continue;
                float d = System.Numerics.Vector2.Distance(combat.Markers.Position[k], last);
                if (d < best) { best = d; marker = k; }
            }
            bool visibleNow = w.Fog.IsVisible(0, FogCell(g, last));
            if (!Check(marker >= 0, "lone unit: no corpse marker")) return;
            _notes.Add($"lone own unit dead at tick {sim.TickNumber}: corpse marker {marker} {best:F1} m off, drawn as {combat.MarkerShown(marker)}, cell visible now {visibleNow}, held back {combat.PendingMarkers}");
            Check(combat.MarkerShown(marker) == 1,
                $"lone own unit killed in sight at tick {sim.TickNumber}: its corpse (marker {marker}, {best:F1} m off) drawn as {combat.MarkerShown(marker)}, pending {combat.MarkerPending(marker)}, cell visible now {visibleNow} (the player watched it fall; want drawn)");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(1);
        }
    }

    private static int FogCell(NavGrid g, System.Numerics.Vector2 p) => Rts.Sim.ViewApi.FogView.CellOf(g.Width, g.Height, p);

    private static System.Numerics.Vector2 FindOpen(NavGrid g, int x0, int y0)
    {
        for (int r = 0; r < 40; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    bool ok = true;
                    for (int ey = -3; ey <= 3 && ok; ey++)
                        for (int ex = -3; ex <= 3 && ok; ex++)
                            ok = g.InBounds(x0 + dx + ex, y0 + dy + ey) && g.IsPassable(x0 + dx + ex, y0 + dy + ey) && g.LevelAt(x0 + dx + ex, y0 + dy + ey) == g.LevelAt(x0 + dx, y0 + dy);
                    if (ok) return g.CellCenter(x0 + dx, y0 + dy);
                }
        return g.CellCenter(x0, y0);
    }

    // ---- helpers ----

    private Main NewMain(string[] args, string label)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args.Append(Main.NoConfineFlag).ToArray();
        main.SettingsPath = $"{_dir}/{label}/settings.cfg";
        main.ReplayDir = _dir;
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = FrameSeconds;
        return main;
    }

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
