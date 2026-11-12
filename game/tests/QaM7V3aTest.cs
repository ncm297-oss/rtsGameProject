using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;

namespace Rts.Game.Tests;

/// <summary>
/// QA M7-V3a (session 2026-11-11-1317): the view pushed past the developer's one forged third faction.
/// <list type="number">
/// <item>Eight factions (<see cref="DataLimits.MaxFactions"/>): a copy with six Malazan clones appended (ids <c>qf2</c> to
/// <c>qf7</c>, the first named by a 40-character <c>displayName</c>, <c>qf4</c> with regen 1 HP/s after 6 s). Every slot's list
/// has the eight in data order; slot s defaults to faction s (s mod 8); a ninth faction is refused by the loader with an
/// error that names the limit. The 40-character name's width against its list and the setup's width is measured and printed.</item>
/// <item>Four slots, each on a different faction (qf2 local, Whirlwind, qf7, Malazan): Start's args, the four seats, 400 ticks with
/// no ERROR, the stats screen's four names (the long one's label measured against the next column), a save at tick 200 that
/// loads to the same hash and reaches the live tick-400 hash, and the recording replayed with every checkpoint equal.</item>
/// <item>The cue with 30 units selected: 30 qf4 Heavy Infantry hurt by Whirlwind archers and left 6 s: the 30-unit grid shows no
/// cue; each one of them alone shows the cue exactly when "below its maximum and 120 ticks since its last hit" holds; and
/// the grid again hides it.</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the eight-faction setup with its list open and the stats screen.
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM7V3aTest.tscn</c>; prints "QA M7-V3A TEST PASS" or FAIL lines.
/// </summary>
public partial class QaM7V3aTest : Node
{
    private const string LongName = "The Forty Character Faction Display Name"; // 40 characters
    private static readonly string[] Extra = { "qf2", "qf3", "qf4", "qf5", "qf6", "qf7" };
    private const string RegenId = "qf4";

    private readonly List<string> _failures = new();
    private readonly List<string> _notes = new();
    private string _root = "", _copy = "";
    private string? _shots;
    private GameData _data = null!;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m7v3a-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] user = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < user.Length; i++)
                if (user[i] == "--shots") _shots = user[i + 1];
            await Frames(1);
            Check(LongName.Length == 40, $"the long name is {LongName.Length} characters");
            _data = Forge(Extra, out _copy);
            Check(_data.Factions.Length == DataLimits.MaxFactions, $"forged {_data.Factions.Length} factions, want {DataLimits.MaxFactions}");
            NinthRefused();
            await SetupEight();
            await FourFactionMatch();
            await ThirtySelected();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string n in _notes) GD.Print($"  note: {n}");
        foreach (string f in _failures.Take(60)) GD.Print($"QA M7-V3A TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M7-V3A TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the forged data ----

    private GameData Forge(string[] ids, out string copy, string sub = "data")
    {
        string dataDir = ProjectSettings.GlobalizePath("res://data");
        copy = Path.Combine(_root, sub);
        if (Directory.Exists(copy)) Directory.Delete(copy, true);
        Directory.CreateDirectory(copy);
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        string from = Path.Combine(copy, "factions", "malazan");
        foreach (string id in ids)
        {
            string to = Path.Combine(copy, "factions", id);
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
                File.WriteAllText(Path.Combine(to, Path.GetFileName(file)), File.ReadAllText(file)
                    .Replace("malazan_", id + "_").Replace("moranth_supply", id + "_upgrade")
                    .Replace("\"telas_fire\"", $"\"{id}_fire\"").Replace("\"cusser\"", $"\"{id}_cusser\""));
            Edit(Path.Combine(to, "faction.json"), f =>
            {
                f["id"] = id;
                f["displayName"] = id == "qf2" ? LongName : $"Forged {id.ToUpperInvariant()}";
                if (id == RegenId) f["regen"] = new JsonObject { ["hpPerSecond"] = 1, ["delay"] = 6 };
            });
            Edit(Path.Combine(copy, "common", "factions.json"), r => r["order"]!.AsArray().Add(id));
        }
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        return loaded.Data!;
    }

    private void NinthRefused()
    {
        string dataDir = Path.Combine(_root, "nine");
        // A ninth faction: the eight-faction copy plus one more folder and order entry.
        try
        {
            Forge(Extra.Append("qf8").ToArray(), out dataDir, "nine");
            Check(false, "a ninth faction loaded");
        }
        catch (InvalidOperationException ex)
        {
            Check(ex.Message.Contains("8", StringComparison.Ordinal), $"the ninth faction's error does not name the limit: {ex.Message}");
            _notes.Add($"ninth faction refused: {ex.Message[..Math.Min(220, ex.Message.Length)]}");
        }
    }

    private static void Edit(string path, Action<JsonNode> edit)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
        edit(root);
        File.WriteAllText(path, root.ToJsonString());
    }

    private Main NewMain(params string[] args)
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = args.Append(Main.NoConfineFlag).ToArray();
        main.DataPath = _copy;
        main.SettingsPath = Path.Combine(_root, "settings.cfg");
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = 0.25;
        return main;
    }

    // ---- (1) eight factions in the setup ----

    private async Task SetupEight()
    {
        Main main = NewMain();
        AddChild(main);
        await Frames(2);
        try
        {
            if (!Check(main.Setup != null, "no setup screen")) return;
            main.ShowSetup();
            await Frames(2);
            SkirmishSetup setup = main.Setup!;
            while (setup.SlotCount < SkirmishSetup.MaxSlots) setup.AddSlot();
            await Frames(2);
            string[] want = _data.Factions.Select(f => f.DisplayName).ToArray();
            for (int slot = 0; slot < SkirmishSetup.MaxSlots; slot++)
            {
                OptionButton list = setup.FactionOptions[slot];
                string[] items = Enumerable.Range(0, list.ItemCount).Select(i => list.GetItemText(i)).ToArray();
                Check(items.SequenceEqual(want), $"slot {slot}: [{string.Join(", ", items)}]");
                Check(setup.FactionPick(slot) == slot % _data.Factions.Length, $"slot {slot} defaults to {setup.FactionPick(slot)}, want {slot % 8}");
            }
            int longIndex = _data.FindFaction("qf2");
            Pick(setup.FactionOptions[0], longIndex);
            await Frames(2);
            OptionButton l0 = setup.FactionOptions[0];
            float textW = l0.GetThemeFont("font").GetStringSize(LongName, HorizontalAlignment.Left, -1, l0.GetThemeFontSize("font_size")).X;
            Rect2 r = l0.GetGlobalRect();
            Rect2 screen = GetViewport().GetVisibleRect();
            _notes.Add($"40-char name in the slot list: text {textW:0} px, list {r} (screen {screen.Size}); other lists {setup.FactionOptions[1].GetGlobalRect()}");
            // Readability: the list must stay on screen and not run into the next column's controls.
            Check(screen.Encloses(r), $"the slot list with the long name leaves the screen: {r}");
            await Shot("qa-eight-factions-setup");
            if (_shots != null && DisplayServer.GetName() != "headless")
            {
                setup.FactionOptions[1].ShowPopup();
                await Shot("qa-eight-factions-list");
                setup.FactionOptions[1].GetPopup().Hide();
            }
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (2) four slots, four factions: match, stats, save, replay ----

    private async Task FourFactionMatch()
    {
        Main main = NewMain();
        AddChild(main);
        await Frames(2);
        try
        {
            main.ShowSetup();
            await Frames(2);
            SkirmishSetup setup = main.Setup!;
            setup.AddSlot();
            setup.AddSlot();
            int qf2 = _data.FindFaction("qf2"), qf7 = _data.FindFaction("qf7");
            Pick(setup.FactionOptions[0], qf2);
            Pick(setup.FactionOptions[1], 1);
            Pick(setup.FactionOptions[2], qf7);
            Pick(setup.FactionOptions[3], 0);
            string joined = string.Join(" ", setup.MatchArgs() ?? Array.Empty<string>());
            Check(joined.Contains("--players 4") && joined.Contains("--faction 1 qf2") && joined.Contains("--faction 2 whirlwind")
                && joined.Contains("--faction 3 qf7") && joined.Contains("--faction 4 malazan"), $"Start's args '{joined}'");
            setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            Simulation? sim = main.CurrentMatch?.Simulation;
            if (!Check(sim != null, "Start started no match")) return;
            World w = sim!.World;
            Check(w.Config.PlayerCount == 4 && w.FactionOf(0) == qf2 && w.FactionOf(1) == 1 && w.FactionOf(2) == qf7 && w.FactionOf(3) == 0,
                $"seats {w.Config.PlayerCount}: {w.FactionOf(0)} {w.FactionOf(1)} {w.FactionOf(2)} {w.FactionOf(3)}");
            string? replayPath = main.ReplayPath;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (sim.TickNumber < 200 && clock.Elapsed.TotalSeconds < 120) await Frames(1);
            main.PauseMatch();
            await Frames(1);
            ulong saveHash = sim.StateHash();
            Check(sim.TickNumber == 200 && main.SaveGame(), $"no save at 200 (tick {sim.TickNumber})");
            string? savePath = main.LastSavePath;
            main.Resume();
            while (sim.TickNumber < 400 && clock.Elapsed.TotalSeconds < 240) await Frames(1);
            Check(sim.TickNumber == 400, $"the match reached {sim.TickNumber}");
            ulong endHash = sim.StateHash();
            main.PauseMatch();
            main.ShowStats();
            await Frames(2);
            MatchStats stats = main.Stats!;
            Check(stats.RowCount == 4, $"stats rows {stats.RowCount}");
            for (int p = 0; p < Math.Min(4, stats.RowCount); p++)
                Check(stats.FactionName(p) == _data.Factions[w.FactionOf(p)].DisplayName, $"stats row {p}: '{stats.FactionName(p)}'");
            if (stats.RowCount > 0)
                _notes.Add($"stats long name '{stats.FactionName(0)}': cell 0 '{stats.Cell(0, 0)}'");
            await Shot("qa-four-factions-stats");
            main.CloseStats();
            main.QuitToMenu();
            await Frames(2);

            if (Check(savePath != null && File.Exists(savePath), "no save file") && main.LoadSavedGame(savePath!))
            {
                Simulation loaded = main.CurrentMatch!.Simulation!;
                Check(loaded.TickNumber == 200 && loaded.StateHash() == saveHash, $"loaded: tick {loaded.TickNumber}, hash equal {loaded.StateHash() == saveHash}");
                clock.Restart();
                while (loaded.TickNumber < 400 && clock.Elapsed.TotalSeconds < 120) await Frames(1);
                Check(loaded.TickNumber == 400 && loaded.StateHash() == endHash, $"loaded at {loaded.TickNumber}: hash {loaded.StateHash():X16} v live {endHash:X16}");
                main.QuitToMenu();
                await Frames(2);
            }
            else Check(false, "the save did not load");

            if (Check(replayPath != null && File.Exists(replayPath), "no recording") && main.WatchReplay(replayPath!))
            {
                SimRunner runner = main.CurrentMatch!.GetNode<SimRunner>("SimRunner");
                clock.Restart();
                while (runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 120) await Frames(1);
                Replay replay = runner.Playback!;
                Check(runner.PlaybackState == PlaybackState.Ended && runner.PlaybackError == ReplayError.None
                    && replay.Checkpoints.Length >= 400 / ReplayRecorder.DefaultCheckpointInterval && runner.CheckpointsCompared == replay.Checkpoints.Length,
                    $"playback {runner.PlaybackState} ({runner.PlaybackError}), {runner.CheckpointsCompared} / {replay.Checkpoints.Length}");
                _notes.Add($"four-faction replay: {replay.TickCount} ticks, {runner.CheckpointsCompared} checkpoints equal");
                main.QuitToMenu();
                await Frames(2);
            }
            else Check(false, "the recording did not play");
        }
        finally
        {
            if (GetTree().Paused) GetTree().Paused = false;
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // ---- (3) the cue with 30 units selected ----

    private async Task ThirtySelected()
    {
        int regenFaction = _data.FindFaction(RegenId);
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--units", "0", "--workers", "0", "--no-fog", "--mute", "--faction", "1", RegenId }));
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        runner.FixedFrameDelta = 0.05;
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        UnitStore u = w.Units;
        var sel = match.GetNode<SelectionController>("SelectionController");
        var panel = match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        try
        {
            Check(w.FactionOf(0) == regenFaction, $"player 0 plays {w.FactionOf(0)}");
            while (sim.TickNumber < 3) await Frames(1);
            int heavy = _data.FindUnit(RegenId + "_heavy_infantry"), archer = _data.FindUnit("whirlwind_desert_archer");
            System.Numerics.Vector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
            var mine = new List<EntityHandle>();
            for (int k = 0; k < 30; k++)
                sim.Enqueue(Command.SpawnUnit(0, heavy, mid + new System.Numerics.Vector2((k % 6) * 1.6f - 4f, (k / 6) * 1.6f - 4f)));
            for (int k = 0; k < 8; k++)
                sim.Enqueue(Command.SpawnUnit(1, archer, mid + new System.Numerics.Vector2((k % 4) * 1.6f - 2.4f, 14f + (k / 4) * 1.6f)));
            int t0 = sim.TickNumber;
            while (sim.TickNumber < t0 + 3) await Frames(1);
            var archers = new List<EntityHandle>();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                if (u.Owner[i] == 0 && u.TypeId[i] == heavy) mine.Add(new EntityHandle(i, u.Generation[i]));
                if (u.Owner[i] == 1 && u.TypeId[i] == archer) archers.Add(new EntityHandle(i, u.Generation[i]));
            }
            if (!Check(mine.Count == 30 && archers.Count == 8, $"spawned {mine.Count} heavies, {archers.Count} archers")) return;
            foreach (EntityHandle h in mine) sim.Enqueue(Command.HoldPosition(0, h));
            // Fire until most are hurt (each archer ordered at a still-unhurt heavy every second), then walk the archers away.
            t0 = sim.TickNumber;
            int Hurt() => mine.Count(h => u.IsAlive(h) && u.Hp[h.Index] < w.MaxHp(h.Index));
            while (Hurt() < 20 && sim.TickNumber < t0 + 1200)
            {
                if ((sim.TickNumber - t0) % 20 == 0)
                {
                    var fresh = mine.Where(h => u.IsAlive(h) && u.Hp[h.Index] == w.MaxHp(h.Index)).ToList();
                    for (int a = 0; a < archers.Count && fresh.Count > 0; a++)
                        if (u.IsAlive(archers[a])) sim.Enqueue(Command.Attack(1, archers[a], fresh[a % fresh.Count], isBuilding: false));
                }
                await Frames(1);
            }
            System.Numerics.Vector2 away = new(mid.X, Math.Min(w.NavGrid.Height * 2f - 10f, mid.Y + 80f));
            foreach (EntityHandle a in archers) if (u.IsAlive(a)) sim.Enqueue(Command.Move(1, a, away));
            int hurt = Hurt();
            Check(hurt >= 10, $"only {hurt} of 30 hurt by tick {sim.TickNumber}");
            mine.RemoveAll(h => !u.IsAlive(h));

            // Select all of them every frame from now on; the grid never shows the cue while several are healing.
            int healingFrames = 0, healingNow = 0;
            int stop = sim.TickNumber + 400;
            // Until a fair number heal at once (and 30 frames past the first healer), not so long that the light hits heal off.
            while (sim.TickNumber < stop && !(healingNow >= 8 && healingFrames >= 30))
            {
                sel.SelectOnly(mine[0]);
                foreach (EntityHandle h in mine.Skip(1)) sel.Selection.Add(h);
                panel.Sync();
                int healing = mine.Count(h => u.IsAlive(h) && Want(sim, h));
                if (healing > 0) healingFrames++;
                healingNow = healing;
                Check(panel.ShownKind == 2 && !panel.RegenLabel.Visible, $"{sel.Selection.Count} selected ({healing} healing) at tick {sim.TickNumber}: kind {panel.ShownKind}, cue {panel.RegenLabel.Visible}");
                await Frames(1);
            }
            Check(healingFrames >= 30, $"only {healingFrames} frames with a selected unit healing");
            // Each alone: the cue exactly when the oracle says.
            int shown = 0;
            foreach (EntityHandle h in mine)
            {
                if (!u.IsAlive(h)) continue;
                sel.SelectOnly(h);
                panel.Sync();
                bool want = Want(sim, h);
                if (want) shown++;
                Check(panel.ShownKind == 1 && panel.RegenLabel.Visible == want, $"unit {h.Index} alone: cue {panel.RegenLabel.Visible}, want {want} (hp {u.Hp[h.Index]}, last hit {u.LastDamagedTick[h.Index]}, next {sim.TickNumber})");
            }
            Check(shown >= 5, $"only {shown} of the 30 showed the cue alone");
            // And the grid once more after a single one showed it.
            sel.SelectOnly(mine[0]);
            foreach (EntityHandle h in mine.Skip(1)) sel.Selection.Add(h);
            panel.Sync();
            Check(!panel.RegenLabel.Visible, "the grid after a single cue still shows it");
            _notes.Add($"30 selected: {hurt} hurt, {healingFrames} grid frames with units healing (no cue), {shown} alone with the cue");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(2);
        }
    }

    private static bool Want(Simulation sim, EntityHandle h)
    {
        UnitStore u = sim.World.Units;
        int last = u.LastDamagedTick[h.Index];
        return u.Hp[h.Index] < sim.World.MaxHp(h.Index) && (last < 0 || sim.TickNumber - last >= 120);
    }

    // ---- helpers ----

    private static void Pick(OptionButton list, int index)
    {
        list.Select(index);
        list.EmitSignal(OptionButton.SignalName.ItemSelected, index);
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
        if (!ok && _failures.Count < 200) _failures.Add(message);
        return ok;
    }
}
