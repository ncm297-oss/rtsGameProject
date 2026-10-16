using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V8 (owner playtest note 8, Requests 41): a left click on a gold mine on the real Match scene, under the fog. A lone
/// worker spawned beside a mine (no bases): the click selects the mine alone (units, building and card empty), the panel's
/// name and amount equal the sim's; a right click and a Shift + click on it change nothing; a click on open ground drops it.
/// The worker gathers 60 ticks: the amount follows every tick. The worker walks away until no footprint cell is in sight:
/// "Last seen:" with the amount of the last tick it was seen; the mine emptied out of sight: still that amount, still
/// selected; the worker walks back: the mine is seen gone and deselected. Every click on the mine sends 0 commands (the
/// player's command sequence counter and the pending queue unchanged). The hash twin: a bare sim replaying the match's
/// commands hashes equal on every tick up to the staging (the emptying is a reflection call, not a command).
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/ResourceClickTest.tscn</c>; prints
/// "RESOURCE CLICK TEST PASS" and exits 0, or each failure and exits 1. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves
/// <c>resource-click.png</c> and <c>resource-last-seen.png</c>.</remarks>
public partial class ResourceClickTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private SelectionPanel _panel = null!;
    private CommandCard _card = null!;
    private RtsCamera _camera = null!;
    private string? _shots;

    private World W => _sim.World;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
            string[] args = OS.GetCmdlineUserArgs();
            int shotsAt = Array.IndexOf(args, "--shots");
            if (shotsAt >= 0 && shotsAt + 1 < args.Length) _shots = args[shotsAt + 1];
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            await MineClick(1);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"RESOURCE CLICK TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("RESOURCE CLICK TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task MineClick(ulong seed)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute", "--units", "0", "--no-bases" }));
        _runner.ProcessMode = ProcessModeEnum.Disabled; // ticked by the test, one frame a tick
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        Rts.Sim.Map.NavGrid g = W.NavGrid;
        ResourceStore r = W.Resources;

        // A 2 x 2 gold mine with an open cell west of it and open ground 16 cells from there on the same level.
        int mine = -1, standX = 0, standY = 0, farX = 0, farY = 0;
        for (int s = 0; s < r.Capacity && mine < 0; s++)
        {
            if (!r.Alive[s] || _data.Resources[r.TypeId[s]].Resource != ResourceKind.Gold) continue;
            int x = r.Cell[s] % g.Width, y = r.Cell[s] / g.Width;
            if (!g.IsPassable(x - 1, y) || !FarStand(g, x - 1, y, out farX, out farY)) continue;
            (mine, standX, standY) = (s, x - 1, y);
        }
        if (!Check(mine >= 0, "no gold mine with an open cell beside it and open ground 16 cells away")) return;
        ResourceDef def = _data.Resources[r.TypeId[mine]];
        string gold = _data.Factions[Math.Max(W.FactionOf(0), 0)].GoldName;
        int anchor = r.Cell[mine];
        var centre = new System.Numerics.Vector2((anchor % g.Width + def.FootprintWidth / 2f) * Rts.Sim.Map.MapConstants.CellSize,
            (anchor / g.Width + def.FootprintHeight / 2f) * Rts.Sim.Map.MapConstants.CellSize);
        _sim.Enqueue(Command.SpawnUnit(0, _data.FindUnit("malazan_laborer"), g.CellCenter(standX, standY)));
        await TickUntil(() => InSight(anchor, def), 40);
        int worker = -1;
        for (int i = 0; i < W.Units.Capacity; i++) if (W.Units.Alive[i] && W.Units.Owner[i] == 0) worker = i;
        if (!Check(worker >= 0 && InSight(anchor, def), $"worker {worker}, mine in sight {InSight(anchor, def)}")) return;
        var workerH = new EntityHandle(worker, W.Units.Generation[worker]);

        // The click: the mine alone, the panel equal to the sim, no command.
        _camera.SetFocus(centre.X, centre.Y);
        await Frame();
        await Frame();
        if (!Check(ClickPoint(mine, centre, out Vector2 at), "no screen point picks the mine clear of the worker")) return;
        await NoCommand(() => Click(at, MouseButton.Left), "the click on the mine");
        _panel.Sync();
        Check(_sel.SelectedResource == mine && _sel.Selection.Count == 0 && _sel.SelectedBuilding < 0 && !_sel.Targeting,
            $"click: resource {_sel.SelectedResource} (want {mine}), units {_sel.Selection.Count}, building {_sel.SelectedBuilding}");
        Check(Enumerable.Range(0, CommandCard.Cells).All(i => _card.ActionAt(i) == CardCommand.None), "the card is not empty with a mine selected");
        Check(_panel.ShownKind == 4 && _panel.NameLabel.Text == def.DisplayName && _panel.AmountLabel.Visible
            && _panel.AmountLabel.Text == _ui.ResourceLeft(r.Remaining[mine], gold),
            $"panel: kind {_panel.ShownKind}, name '{_panel.NameLabel.Text}', amount '{_panel.AmountLabel.Text}' (sim {r.Remaining[mine]} {gold})");
        await Shot("resource-click");
        await NoCommand(() => Click(at, MouseButton.Right), "a right click on the mine with the mine selected");
        Input.ActionPress("select_add");
        await NoCommand(() => Click(at, MouseButton.Left), "a Shift + click on the mine");
        Input.ActionRelease("select_add");
        Check(_sel.SelectedResource == mine, "the mine dropped after a right click and a Shift + click on it");
        // A click on open ground (the far stand, off the mine) drops it; a second click on the mine takes it again.
        Vector2 ground = _camera.UnprojectPosition(new Vector3(centre.X, TerrainHeight.At(W.Heightmap, centre.X, centre.Y), centre.Y + 6f * Rts.Sim.Map.MapConstants.CellSize));
        if (_sel.PickResource(ground) < 0)
        {
            await NoCommand(() => Click(ground, MouseButton.Left), "a click on open ground");
            Check(_sel.SelectedResource < 0, "a click on open ground kept the mine");
        }
        await NoCommand(() => Click(at, MouseButton.Left), "the second click on the mine");
        Check(_sel.SelectedResource == mine, "the second click did not select the mine");

        // The worker gathers 60 ticks: the panel follows the sim every tick.
        int start = r.Remaining[mine], wrong = 0;
        _sim.Enqueue(Command.Gather(0, workerH, centre));
        for (int t = 0; t < 60; t++)
        {
            _sim.Tick();
            await Frame();
            _panel.Sync();
            if (_panel.AmountLabel.Text != _ui.ResourceLeft(r.Remaining[mine], gold)) wrong++;
        }
        Check(wrong == 0 && r.Remaining[mine] < start, $"gathering: {wrong} frames off the sim; {start} -> {r.Remaining[mine]}");

        // BUG-0743 item 3: at 2x a frame runs two ticks (the runner's own loop, so its Ticked handlers run); the last-seen amount
        // follows every tick the mine is in sight, not only each frame's last tick. Checked from a Ticked handler added after
        // the match's own, so the fog has taken the tick first.
        SeenResources seenAmounts = _match.GetNode<FogOfWar>("World3D/FogOfWar").SeenResources!;
        int behind = 0, changes = 0, firstOfFrame = 0, prevAmount = r.Remaining[mine], tickInFrame = 0, ticked = 0;
        void OnTick(Simulation sim)
        {
            tickInFrame++;
            ticked++;
            if (r.Remaining[mine] != prevAmount)
            {
                changes++;
                if (tickInFrame == 1) firstOfFrame++;
                prevAmount = r.Remaining[mine];
            }
            if (InSight(anchor, def) && seenAmounts.Amount[mine] != r.Remaining[mine]) behind++;
        }
        _runner.Ticked += OnTick;
        _runner.GameSpeed = 2.0;
        _runner.FixedFrameDelta = 0.05; // a 20 Hz frame's time at 2x: exactly two ticks a frame, from the runner's own _Process
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        for (int f = 0; f < 120 && r.Alive[mine]; f++)
        {
            tickInFrame = 0;
            await Frame();
        }
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _runner.Ticked -= OnTick;
        _runner.GameSpeed = 1.0;
        GD.Print($"2x: {ticked} ticks in 120 frames, {changes} amount changes ({firstOfFrame} on a frame's first tick), last-seen amount behind the sim on {behind} ticks in sight");
        Check(ticked == 240 && changes > 0 && firstOfFrame > 0 && behind == 0, $"2x: {ticked} ticks (want 240), {changes} amount changes, {firstOfFrame} on a frame's first tick, the last-seen amount behind the sim on {behind} ticks in sight");

        // Out of sight: the amount last seen.
        _sim.Enqueue(Command.Move(0, workerH, g.CellCenter(farX, farY)));
        int last = r.Remaining[mine];
        await TickUntil(() =>
        {
            if (InSight(anchor, def)) last = r.Remaining[mine];
            return !InSight(anchor, def);
        }, 800);
        _panel.Sync();
        string lastSeen = $"{_ui.Hud(HudText.LastSeen)} {_ui.ResourceLeft(last, gold)}";
        Check(!InSight(anchor, def) && _sel.SelectedResource == mine && _panel.AmountLabel.Text == lastSeen,
            $"unseen: in sight {InSight(anchor, def)}, resource {_sel.SelectedResource}, amount '{_panel.AmountLabel.Text}', want '{lastSeen}'");
        await Shot("resource-last-seen");

        // The twin before the staging below: the reflection Take is no command, so a replay can't repeat it.
        Twin(seed);

        // Emptied out of sight: still the old amount, still selected.
        typeof(ResourceStore).GetMethod("Take", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(r, new object[] { r.HandleOf(mine), r.Remaining[mine] });
        Check(!r.Alive[mine], "the staging Take left the mine alive");
        for (int t = 0; t < 8; t++)
        {
            _sim.Tick();
            await Frame();
        }
        _panel.Sync();
        Check(_sel.SelectedResource == mine && _panel.ShownKind == 4 && _panel.AmountLabel.Text == lastSeen,
            $"emptied unseen: resource {_sel.SelectedResource}, kind {_panel.ShownKind}, amount '{_panel.AmountLabel.Text}'");

        // Seen again: gone, deselected.
        _sim.Enqueue(Command.Move(0, workerH, g.CellCenter(standX, standY)));
        await TickUntil(() => InSight(anchor, def), 800);
        _panel.Sync();
        Check(InSight(anchor, def) && _sel.SelectedResource < 0 && _panel.ShownKind == 0,
            $"seen gone: in sight {InSight(anchor, def)}, resource {_sel.SelectedResource}, kind {_panel.ShownKind}");
        GD.Print($"mine slot {mine}: {start} -> {last} gathered in sight, last seen {last} after the walk, emptied unseen, deselected once seen");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // A screen point whose ray picks the mine (its block's middle first, then a scan round it) with no own unit drawn within 30 px.
    private bool ClickPoint(int mine, System.Numerics.Vector2 centre, out Vector2 at)
    {
        float h = TerrainHeight.At(W.Heightmap, centre.X, centre.Y) + PropsView.Shape.MineHeight * 0.5f;
        Vector2 middle = _camera.UnprojectPosition(new Vector3(centre.X, h, centre.Y));
        for (int ring = 0; ring <= 5; ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    at = middle + new Vector2(dx, dy) * 8f;
                    if (_sel.PickResource(at) == mine && !UnitNear(at, 30f)) return true;
                }
        at = default;
        return false;
    }

    private bool UnitNear(Vector2 at, float px)
    {
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && _sel.TryScreenPosition(i, out Vector2 p) && p.DistanceTo(at) < px) return true;
        return false;
    }

    // Runs `act` and checks that it enqueued nothing: the local player's sequence counter and the pending queue unchanged.
    private async Task NoCommand(Func<Task> act, string what)
    {
        int seq = Sequence(), pending = _sim.PendingCommandCount;
        await act();
        Check(Sequence() == seq && _sim.PendingCommandCount == pending, $"{what} sent commands: sequence {seq} -> {Sequence()}, pending {pending} -> {_sim.PendingCommandCount}");
    }

    private int Sequence() =>
        ((int[])typeof(Simulation).GetField("_nextSequence", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_sim)!)[0];

    private bool InSight(int anchor, ResourceDef def)
    {
        int w = W.NavGrid.Width;
        for (int y = 0; y < def.FootprintHeight; y++)
            for (int x = 0; x < def.FootprintWidth; x++)
                if (W.Fog.IsVisible(0, anchor + y * w + x)) return true;
        return false;
    }

    private bool FarStand(Rts.Sim.Map.NavGrid g, int x, int y, out int fx, out int fy)
    {
        foreach ((int dx, int dy) in new[] { (-16, 0), (0, 16), (0, -16), (-12, 12), (-12, -12), (16, 0) })
        {
            (fx, fy) = (x + dx, y + dy);
            if (g.IsPassable(fx, fy) && W.Heightmap.LevelAt(fx, fy) == W.Heightmap.LevelAt(x, y)) return true;
        }
        (fx, fy) = (0, 0);
        return false;
    }

    private async Task TickUntil(Func<bool> done, int max)
    {
        for (int t = 0; t < max && !done(); t++)
        {
            _sim.Tick();
            await Frame();
        }
    }

    private async Task Click(Vector2 at, MouseButton button)
    {
        MouseButtonMask mask = button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = mask });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
        await Frame();
    }

    private void Twin(ulong seed)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints equal");
    }

    // Windowed only: the frame as drawn, saved as <name>.png in --shots.
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
