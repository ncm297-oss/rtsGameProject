using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// M6-VH10, the view's hardening (the draw result's rows are in <c>QaM6V29Test</c>):
/// <list type="bullet">
/// <item>(N) BUG-1792: the draw notice on a live match's HUD at 1152 x 648 and 1920 x 1080: the shipped line drawn where it
/// was (800 x 30 px from 86 px down, centred), a 120-character line of words and one of 120 letters without a space each
/// centred within 2 px and wholly on screen (before the fix they grew right from the left edge: 3,000 px and more).</item>
/// <item>(C) BUG-1710: a Cadre Mage ringed in by eight holding Raiders loses its cast live (one cue, the Refused sound, the
/// disc and the <c>hud.cast_lost</c> words), and in the playback of that match's recording at speed 4 the cast is lost on
/// the same tick with no cue, no sound, no disc and no words.</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves the Stone Cairn in all eight setup colours (BUG-1830):
/// <c>m6vh10-cairn-colours-1-4-40m</c> / <c>-5-8-</c> at the default zoom and <c>-60m</c> zoomed out, the same four spots
/// empty (<c>m6vh10-cairn-empty-40m</c> / <c>-60m</c>) so a picture's team and wall pixels can be told apart by difference.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/ViewHardenVh10Test.tscn</c>; prints "VIEW HARDEN VH10 TEST PASS" and exits 0, or each failure and exits 1.</remarks>
public partial class ViewHardenVh10Test : Node
{
    private const int LongLength = 120;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string? _shots;

    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private AbilityViews _views = null!;
    private Sfx _sfx = null!;
    private int _drops;
    private int _firstDropTick = -1;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless") { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            Replay replay = await LiveCue();
            await NoticeRows();
            await EndMatch();
            await PlaybackCue(replay);
            if (_shots != null) await CairnPictures();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        GetTree().Root.Size = new Vector2I(1152, 648);
        foreach (string f in _failures) GD.Print($"VIEW HARDEN VH10 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("VIEW HARDEN VH10 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (C) live: the lost cast's cue, recorded ----

    private async Task<Replay> LiveCue()
    {
        StartMatch(record: true, "--seed", "1", "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute", "--speed", "4", "--zoom", "30");
        Check(_views.CuesEnabled, "(C) live: cues are off in a live match");
        EntityHandle mage = StageRingedMage(out NVector2 spot);
        if (mage == default) return _runner.Recorder!.ToReplay();
        _match.GetNode<RtsCamera>("RtsCamera").SetFocus(spot.X, spot.Y);
        long cues0 = _views.LostCasts.Added;
        int refused0 = _sfx.PlayCount(SfxEvent.Refused);
        _sim.Enqueue(Command.UseAbility(0, mage, 0, G.CellCenter(G.Width / 2, G.Height / 2)));
        (int maxShown, int maxLabels) = await RunUntilDropSeen();
        long cues = _views.LostCasts.Added - cues0;
        int refused = _sfx.PlayCount(SfxEvent.Refused) - refused0;
        GD.Print($"(C) live: {_drops} drops (first at tick {_firstDropTick}), {cues} cues, {refused} Refused sounds, at most {maxShown} discs and {maxLabels} words shown");
        Check(_drops == 1 && cues == 1 && refused == 1 && maxShown == 1 && maxLabels == 1,
            $"(C) live: {_drops} drops, {cues} cues, {refused} Refused, {maxShown} discs, {maxLabels} words (want 1 each)");
        _liveDropTick = _firstDropTick;
        return _runner.Recorder!.ToReplay();
    }

    private int _liveDropTick = -1;

    // ---- (C) playback: the same lost cast, no cue ----

    private async Task PlaybackCue(Replay replay)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        ReplayError refusedReplay = _match.StartReplay(_data, replay, LaunchOptions.Parse(new[] { "--speed", "4", "--mute", "--zoom", "30" }), "vh10-cue");
        if (!Check(refusedReplay == ReplayError.None, $"(C) playback refused: {refusedReplay}")) return;
        Open();
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        Check(!_views.CuesEnabled && _runner.GameSpeed == 4.0, $"(C) playback: cues enabled {_views.CuesEnabled}, speed {_runner.GameSpeed}");
        int refused0 = _sfx.PlayCount(SfxEvent.Refused);
        _drops = 0;
        _firstDropTick = -1;
        _runner.Ticked += CountDrops;
        int maxShown = 0, maxLabels = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (_runner.PlaybackState == PlaybackState.Playing && clock.Elapsed.TotalSeconds < 60)
        {
            await Frames(1);
            maxShown = Math.Max(maxShown, _views.ShownCues);
            maxLabels = Math.Max(maxLabels, VisibleCueLabels());
        }
        await Frames(20); // a cue would last 0.5 s of game time: nothing appears after the end either
        maxShown = Math.Max(maxShown, _views.ShownCues);
        maxLabels = Math.Max(maxLabels, VisibleCueLabels());
        _runner.Ticked -= CountDrops;
        int refused = _sfx.PlayCount(SfxEvent.Refused) - refused0;
        GD.Print($"(C) playback: {_runner.PlaybackState} at tick {W.TickNumber}; {_drops} drops (first at tick {_firstDropTick}, live {_liveDropTick}), {_views.LostCasts.Added} cues, {refused} Refused sounds, at most {maxShown} discs and {maxLabels} words");
        Check(_runner.PlaybackState == PlaybackState.Ended, $"(C) playback ended {_runner.PlaybackState}");
        Check(_drops == 1 && _firstDropTick == _liveDropTick, $"(C) playback: {_drops} drops at tick {_firstDropTick} (live: 1 at {_liveDropTick}); the row needs the lost cast replayed");
        Check(_views.LostCasts.Added == 0 && refused == 0 && maxShown == 0 && maxLabels == 0 && _views.CuePool.InUse == 0,
            $"(C) playback: {_views.LostCasts.Added} cues, {refused} Refused, {maxShown} discs, {maxLabels} words (want none: BUG-1710)");
        await EndMatch();
    }

    // A Cadre Mage with eight holding Raiders round it, a Laborer selected elsewhere; the mage's handle (default on failure).
    private EntityHandle StageRingedMage(out NVector2 spot)
    {
        int mageType = _data.FindUnit("malazan_cadre_mage"), raider = _data.FindUnit("whirlwind_raider"), laborer = _data.FindUnit("malazan_laborer");
        List<NVector2> spots = OpenSpots(2);
        spot = spots.Count > 0 ? spots[0] : default;
        if (!Check(spots.Count == 2 && mageType >= 0 && raider >= 0 && laborer >= 0, $"(C) staging: {spots.Count} spots, types {mageType} {raider} {laborer}")) return default;
        _sim.Enqueue(Command.SpawnUnit(0, mageType, spots[0]));
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8f;
            _sim.Enqueue(Command.SpawnUnit(1, raider, spots[0] + new NVector2(MathF.Cos(a), MathF.Sin(a))));
        }
        _sim.Enqueue(Command.SpawnUnit(0, laborer, spots[1]));
        _sim.Tick();
        _sim.Tick();
        EntityHandle mage = default, other = default;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            var h = new EntityHandle(i, U.Generation[i]);
            if (U.TypeId[i] == mageType) mage = h;
            else if (U.TypeId[i] == laborer) other = h;
            else _sim.Enqueue(Command.HoldPosition(1, h));
        }
        _sim.Tick();
        var sel = _match.GetNode<SelectionController>("SelectionController");
        sel.Selection.Clear();
        if (other != default) sel.Selection.Add(other);
        Check(mage != default, "(C) staging: no mage");
        return mage;
    }

    // Runs the live runner until the drop was seen and its cue has faded; the most discs and words drawn on one frame.
    private async Task<(int, int)> RunUntilDropSeen()
    {
        _drops = 0;
        _firstDropTick = -1;
        _runner.Ticked += CountDrops;
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        int maxShown = 0, maxLabels = 0, after = -1;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 30)
        {
            await Frames(1);
            maxShown = Math.Max(maxShown, _views.ShownCues);
            maxLabels = Math.Max(maxLabels, VisibleCueLabels());
            if (_drops > 0 && after < 0) after = 0;
            if (after >= 0 && ++after > 60) break;
        }
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _runner.Ticked -= CountDrops;
        return (maxShown, maxLabels);
    }

    private void CountDrops(Simulation sim)
    {
        foreach (AbilityEvent e in sim.World.AbilityEvents)
        {
            if (!e.Dropped) continue;
            _drops++;
            if (_firstDropTick < 0) _firstDropTick = sim.TickNumber;
        }
    }

    private int VisibleCueLabels()
    {
        int n = 0;
        for (int c = 0; c < _views.LostCasts.Capacity; c++) if (_views.CueLabel(c).Visible) n++;
        return n;
    }

    // ---- (N) the draw notice's layout ----

    private async Task NoticeRows()
    {
        // 120 characters each with the clock at 5:00 (checked below): a sentence of words, and letters with no space to break at.
        string words = "Nobody can train a single new unit any more, so this match now ends in a draw in {0} unless any unit falls before then.";
        string letters = Pad(new string('W', 40) + "{0}");
        UiText? wordsUi = UiWith(words), lettersUi = UiWith(letters);
        if (!Check(wordsUi != null && lettersUi != null, "(N) a long ui.json was refused")) return;
        string shipped = _ui.DrawCountdown(300);
        Check(wordsUi!.DrawCountdown(300).Length == LongLength && lettersUi!.DrawCountdown(300).Length == LongLength,
            $"(N) long texts {wordsUi.DrawCountdown(300).Length} / {lettersUi!.DrawCountdown(300).Length} characters (want {LongLength})");
        foreach (Vector2I size in new[] { new Vector2I(1152, 648), new Vector2I(1920, 1080) })
        {
            GetTree().Root.Size = size;
            await Frames(3);
            Rect2 screen = GetViewport().GetVisibleRect();
            // The shipped line, on the match's own notice: where M6-V29 drew it (800 x 30 from 86 px down, centred).
            Rect2 s = await Measure(_ui, shipped);
            var want = new Rect2(screen.Size.X / 2 - DrawNotice.Width / 2, DrawNotice.Top, DrawNotice.Width, DrawNotice.Height);
            GD.Print($"(N) {size.X} x {size.Y}: shipped '{shipped}' at {s} (want {want})");
            Check(Near(s, want, 0.5f), $"(N) {size.X} x {size.Y}: the shipped line at {s}, want {want}");
            foreach ((string what, UiText ui) in new[] { ("words", wordsUi), ("letters", lettersUi) })
            {
                Rect2 r = await Measure(ui, ui.DrawCountdown(300));
                float off = Math.Abs(r.GetCenter().X - screen.Size.X / 2);
                GD.Print($"(N) {size.X} x {size.Y}: {LongLength} characters of {what} at {r}: centre {off:0.#} px off, on screen {screen.Encloses(r)}");
                Check(off <= 2f && screen.Encloses(r) && r.Position.Y >= DrawNotice.Top - 0.5f,
                    $"(N) {size.X} x {size.Y}: {what}: label {r}, centre {off:0.#} px off, on screen {screen.Encloses(r)} (BUG-1792)");
            }
        }
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frames(2);
    }

    // A second notice on the match's HUD with `ui`, shown with `text` as the clock would show it; its label's screen rect.
    private async Task<Rect2> Measure(UiText ui, string text)
    {
        var extra = new DrawNotice { Name = "Vh10Notice" };
        _match.AddChild(extra);
        extra.Init(_runner, _match.GetNode<CanvasLayer>("Hud"), ui);
        Label label = extra.TextLabel!;
        label.Text = text; // the text Sync writes for a running clock (no draw clock runs in this match)
        label.Visible = true;
        await Frames(3);
        Rect2 r = label.GetGlobalRect();
        label.QueueFree();
        _match.RemoveChild(extra);
        extra.QueueFree();
        await Frames(1);
        return r;
    }

    // The template with W's in front until its text for 5:00 is LongLength characters.
    private string Pad(string template)
    {
        int missing = LongLength - template.Replace("{0}", _ui.Clock(300 * SimConstants.TicksPerSecond)).Length;
        return missing > 0 ? new string('W', missing) + template : template;
    }

    private static UiText? UiWith(string drawCountdown)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(UiText.DefaultPath))!;
        root["hud"]!["draw_countdown"] = drawCountdown;
        var errors = new List<string>();
        UiText? ui = UiText.Parse(root.ToJsonString(), errors);
        if (ui == null) GD.Print($"(N) ui.json refused: {string.Join("; ", errors)}");
        return ui;
    }

    private static bool Near(Rect2 a, Rect2 b, float tol) =>
        Math.Abs(a.Position.X - b.Position.X) <= tol && Math.Abs(a.Position.Y - b.Position.Y) <= tol
        && Math.Abs(a.Size.X - b.Size.X) <= tol && Math.Abs(a.Size.Y - b.Size.Y) <= tol;

    // ---- the Stone Cairn in eight colours (windowed, --shots) ----

    private async Task CairnPictures()
    {
        int cairn = _data.FindBuilding("teblor_stone_cairn"), hall = _data.FindBuilding("teblor_longhouse");
        if (!Check(cairn >= 0 && hall >= 0, "(P) no Cairn or Longhouse in the data")) return;
        foreach (int first in new[] { 1, 5 })
        {
            StartMatch(record: false, "--seed", "1", "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute", "--players", "4",
                "--faction", "1", "teblor", "--faction", "2", "teblor", "--faction", "3", "teblor", "--faction", "4", "teblor",
                "--colour", "1", $"{first}", "--colour", "2", $"{first + 1}", "--colour", "3", $"{first + 2}", "--colour", "4", $"{first + 3}");
            _sim.Tick();
            _sim.Tick();
            NVector2 row = StartLayout.Block(G, 1, west: true, 1f)[0] + new NVector2(-38f, -20f);
            // Every player keeps a Longhouse far off the picture, so a Cairn alone never counts it out.
            for (int p = 0; p < 4; p++) Spawn(p, hall, row + new NVector2(-14f * p, 70f));
            RtsCamera camera = _match.GetNode<RtsCamera>("RtsCamera");
            camera.EdgePanEnabled = false;
            NVector2 middle = row + new NVector2(1.5f * 12f, 0f);
            if (first == 1)
            {
                await Pair(camera, middle, "m6vh10-cairn-empty");
            }
            float x = 0f;
            for (int p = 0; p < 4; p++)
            {
                int slot = Spawn(p, cairn, row + new NVector2(x, 0f));
                Check(slot >= 0, $"(P) colours {first}-{first + 3}: no Cairn for player {p}");
                x += 12f;
            }
            for (int t = 0; t < 4; t++) _sim.Tick();
            await Pair(camera, middle, $"m6vh10-cairn-colours-{first}-{first + 3}");
            await EndMatch();
        }
    }

    private async Task Pair(RtsCamera camera, NVector2 at, string name)
    {
        camera.SetZoom(CameraLimits.DefaultZoom);
        camera.SetFocus(at.X, at.Y + 2f);
        await Shot($"{name}-40m");
        camera.SetZoom(CameraLimits.MaxZoom);
        camera.SetFocus(at.X, at.Y + 2f);
        await Shot($"{name}-60m");
    }

    // A building of `type` for `player` at the first free anchor near `near` (TeblorBuildingsTest's rule); its slot or -1.
    private int Spawn(int player, int type, NVector2 near)
    {
        BuildingDef def = _data.Buildings[type];
        for (int r = 0; r <= 12; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (!G.WorldToCell(near + new NVector2(dx, dy) * MapConstants.CellSize, out int cx, out int cy)) continue;
                    if (cx < 3 || cy < 3 || cx + def.FootprintWidth >= G.Width - 3 || cy + def.FootprintHeight >= G.Height - 3) continue;
                    int a = cy * G.Width + cx;
                    if (!B.Fits(type, a)) continue;
                    int level = G.LevelAt(cx, cy);
                    bool ok = true;
                    for (int y = cy - 1; y <= cy + def.FootprintHeight && ok; y++)
                        for (int x = cx - 1; x <= cx + def.FootprintWidth && ok; x++)
                            if (B.SlotAt(x, y) >= 0 || !G.IsPassable(x, y) || G.LevelAt(x, y) != level) ok = false;
                    if (!ok) continue;
                    _sim.Enqueue(Command.SpawnBuilding(player, type, PlacementGhost.AnchorPoint(G, a)));
                    _sim.Tick();
                    _sim.Tick();
                    int slot = B.SlotAt(cx, cy);
                    return slot >= 0 && B.Cell[slot] == a && B.TypeId[slot] == type ? slot : -1;
                }
        return -1;
    }

    // ---- helpers ----

    // Up to `count` cell centres whose cells within 3 are passable plain ground of one level, 10 m apart (QaM5V1Test's rule).
    private List<NVector2> OpenSpots(int count)
    {
        Heightmap hm = W.Heightmap;
        var spots = new List<NVector2>();
        for (int y = G.Height / 4; y < 3 * G.Height / 4 && spots.Count < count; y += 2)
            for (int x = G.Width / 4; x < 3 * G.Width / 4 && spots.Count < count; x += 2)
            {
                bool ok = true;
                for (int dy = -3; dy <= 3 && ok; dy++)
                    for (int dx = -3; dx <= 3 && ok; dx++)
                        ok = G.IsPassable(x + dx, y + dy) && !hm.IsRamp(x + dx, y + dy) && hm.LevelAt(x + dx, y + dy) == hm.LevelAt(x, y);
                if (!ok) continue;
                NVector2 p = G.CellCenter(x, y);
                bool clear = true;
                foreach (NVector2 s in spots) if (NVector2.Distance(s, p) < 10f) clear = false;
                if (clear) spots.Add(p);
            }
        return spots;
    }

    private void StartMatch(bool record, params string[] args)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        if (record) _match.GetNode<SimRunner>("SimRunner").RecordCheckpointInterval = 20;
        _match.Start(_data, LaunchOptions.Parse(args));
        Open();
        _runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here until a row runs it
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
    }

    private void Open()
    {
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _sim = _runner.Simulation!;
        _views = _match.GetNode<AbilityViews>("World3D/AbilityViews");
        _sfx = _match.GetNode<Sfx>("Sfx");
    }

    private async Task EndMatch()
    {
        if (_match == null || !IsInstanceValid(_match) || _match.GetParent() != this) return;
        RemoveChild(_match);
        _match.QueueFree();
        await Frames(1);
    }

    private async Task Shot(string name)
    {
        await Frames(6);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.Combine(_shots!, name + ".png");
        GD.Print($"screenshot {path} ({GetViewport().GetTexture().GetImage().SavePng(path)})");
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
