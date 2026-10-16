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
/// QA M6-V8 (2026-10-15-2325): attacks the dev scenes don't make.
/// <list type="bullet">
/// <item>Keys file: a map the Keybinds tab can reach (an action takes a later action's default, two orders swapped) must
/// read back as written; every key the tab accepts round-trips by name; a modifier combo ("Shift+Q") and a lowercase
/// name in the file; after <see cref="KeyMap.Apply"/> the old key is no longer the action.</item>
/// <item>A control-group digit bound to a lower grid key (the clash rule allows it): with a building selected the digit must
/// still recall its group.</item>
/// <item>The resource click on the real Match scene (bases, AI off): 0 commands with units selected, Shift held, a
/// double click, a build ghost up; what A + click on a mine does (an order, not a resource click: logged).</item>
/// <item>The hash twin over 2,000 ticks with a click on the mine every frame (alternating plain, Shift, double, a unit),
/// the panel synced every frame.</item>
/// </list>
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V8Test.tscn</c>; prints "QA M6-V8 TEST PASS" or each failure.</remarks>
public partial class QaM6V8Test : Node
{
    private const string TestPath = "user://qa_m6v8_settings.cfg";
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private SelectionPanel _panel = null!;
    private CommandCard _card = null!;
    private RtsCamera _camera = null!;

    private World W => _sim.World;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            KeysFile();
            await Match();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        new KeyMap().Apply();
        foreach (string m in new[] { "group_assign", "group_add", "select_add" }) Input.ActionRelease(m);
        string full = ProjectSettings.GlobalizePath(TestPath);
        if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
        foreach (string f in _failures) GD.Print($"QA M6-V8 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V8 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private static int A(string action) => Array.IndexOf(KeyMap.Actions, action);

    private (GameSettings Back, List<string> Problems) RoundTrip(KeyMap keys)
    {
        var s = new GameSettings { Keys = keys };
        Check(s.Save(TestPath), "save failed");
        var problems = new List<string>();
        return (GameSettings.Read(TestPath, problems, out _), problems);
    }

    private (GameSettings Back, List<string> Problems) ReadText(string keysSection)
    {
        string text = new GameSettings().Encode();
        text = text.Substring(0, text.IndexOf("[keys]", StringComparison.Ordinal)) + keysSection;
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(TestPath), text);
        var problems = new List<string>();
        return (GameSettings.Read(TestPath, problems, out _), problems);
    }

    private void KeysFile()
    {
        // 1. An action takes a later action's default after that one moved (the tab allows it, in this order).
        var takeLater = new KeyMap();
        Check(takeLater.TrySet(A("order_hold"), Key.J) == KeyMap.Ok && takeLater.TrySet(A("order_attack_move"), Key.H) == KeyMap.Ok, "the tab refused hold->J, attack->H");
        (GameSettings back, List<string> problems) = RoundTrip(takeLater);
        Check(back.Keys.SameAs(takeLater) && problems.Count == 0,
            $"round trip of attack-move=H, hold=J (reachable on the tab): read attack {back.Keys.Get("order_attack_move")}, hold {back.Keys.Get("order_hold")}, problems [{string.Join("; ", problems)}]");

        // 2. Stop and Hold swapped through the tab (stop->X, hold->S, stop->H).
        var swap = new KeyMap();
        Check(swap.TrySet(A("order_stop"), Key.X) == KeyMap.Ok && swap.TrySet(A("order_hold"), Key.S) == KeyMap.Ok && swap.TrySet(A("order_stop"), Key.H) == KeyMap.Ok,
            "the tab refused the swap steps");
        (back, problems) = RoundTrip(swap);
        Check(back.Keys.SameAs(swap) && problems.Count == 0,
            $"round trip of stop=H, hold=S (reachable on the tab): read stop {back.Keys.Get("order_stop")}, hold {back.Keys.Get("order_hold")}, problems [{string.Join("; ", problems)}]");

        // 3. Every key the tab would take for group_9 round-trips by name.
        int g9 = A("group_9");
        var lost = new List<string>();
        int tried = 0;
        foreach (Key k in Enum.GetValues<Key>().Distinct())
        {
            var m = new KeyMap();
            if (m.TrySet(g9, k) != KeyMap.Ok) continue;
            tried++;
            var file = new ConfigFile();
            m.Write(file);
            var p = new List<string>();
            KeyMap r = KeyMap.Read(file, p);
            if (r.Get("group_9") != k || p.Count > 0) lost.Add($"{k} (0x{(long)k:x}) -> {r.Get("group_9")}");
        }
        Check(lost.Count == 0, $"{lost.Count} of {tried} accepted keys don't round-trip: {string.Join(", ", lost.Take(12))}");
        GD.Print($"keys: {tried} accepted keys tried for group_9, {lost.Count} lost");

        // 4. A modifier combo in the file: one problem and the default, never a masked key bound.
        (back, problems) = ReadText("[keys]\norder_stop=\"Shift+Q\"\n");
        Check(back.Keys.Get("order_stop") == Key.S && problems.Count == 1,
            $"'Shift+Q' for order_stop: bound {(long)back.Keys.Get("order_stop"):x} ('{OS.GetKeycodeString(back.Keys.Get("order_stop"))}'), problems [{string.Join("; ", problems)}]");
        // Applied, does Shift + Q (or Q) now stop? A masked physical key matches no key press: Stop is left with no key.
        back.Keys.Apply();
        bool shiftQ = InputMap.EventIsAction(new InputEventKey { Keycode = Key.Q, PhysicalKeycode = Key.Q, ShiftPressed = true, Pressed = true }, "order_stop");
        bool q = InputMap.EventIsAction(new InputEventKey { Keycode = Key.Q, PhysicalKeycode = Key.Q, Pressed = true }, "order_stop");
        bool s = InputMap.EventIsAction(new InputEventKey { Keycode = Key.S, PhysicalKeycode = Key.S, Pressed = true }, "order_stop");
        GD.Print($"'Shift+Q' applied: Shift+Q stops {shiftQ}, Q stops {q}, S stops {s}");
        Check(shiftQ || q || s, "'Shift+Q' read from the file leaves order_stop with no key that fires it");
        new KeyMap().Apply();
        (back, problems) = ReadText("[keys]\norder_stop=\"Ctrl+1\"\n");
        Check(back.Keys.Get("order_stop") == Key.S && problems.Count == 1,
            $"'Ctrl+1' for order_stop: bound {(long)back.Keys.Get("order_stop"):x}, problems [{string.Join("; ", problems)}]");
        // A lowercase name (a hand edit): logged only.
        (back, problems) = ReadText("[keys]\norder_stop=\"k\"\n");
        GD.Print($"note: 'k' for order_stop reads {back.Keys.Get("order_stop")} with {problems.Count} problems");

        // 5. After Apply the old key is dead and the new one is the action; Ctrl + the new digit still matches the group.
        var applied = new KeyMap();
        applied.TrySet(A("order_attack_move"), Key.K);
        applied.TrySet(A("group_1"), Key.Key0);
        applied.Apply();
        Check(InputMap.ActionGetEvents("order_attack_move").Count == 1
            && !InputMap.EventIsAction(new InputEventKey { Keycode = Key.A, PhysicalKeycode = Key.A, Pressed = true }, "order_attack_move")
            && InputMap.EventIsAction(new InputEventKey { Keycode = Key.K, PhysicalKeycode = Key.K, Pressed = true }, "order_attack_move")
            && !InputMap.EventIsAction(new InputEventKey { Keycode = Key.Key1, PhysicalKeycode = Key.Key1, Pressed = true }, "group_1")
            && InputMap.EventIsAction(new InputEventKey { Keycode = Key.Key0, PhysicalKeycode = Key.Key0, Pressed = true, CtrlPressed = true }, "group_1"),
            "after Apply: the old key still matches, or the new one (or Ctrl + new digit) doesn't");
        new KeyMap().Apply();
    }

    private async Task Match()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "3", "--mute", "--units", "4" }));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        await Ticks(5);

        await GroupDigitOnGridKey();
        await ResourceClicks();
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private int OwnUnit(int skip = 0)
    {
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == SelectionController.LocalPlayer && skip-- <= 0) return i;
        return -1;
    }

    private int OwnBuilding()
    {
        for (int i = 0; i < W.Buildings.Capacity; i++)
            if (W.Buildings.Alive[i] && W.Buildings.Owner[i] == SelectionController.LocalPlayer) return i;
        return -1;
    }

    private EntityHandle H(int unit) => new(unit, W.Units.Generation[unit]);

    // A control-group digit on a lower grid key (allowed by the clash rule): with a building selected the digit must still
    // recall its group, not press the production card's cell.
    private async Task GroupDigitOnGridKey()
    {
        int unit = OwnUnit(), hall = OwnBuilding();
        if (!Check(unit >= 0 && hall >= 0, $"no own unit ({unit}) or building ({hall})")) return;
        _sel.SelectOnly(H(unit));
        Input.ActionPress("group_assign");
        await Type(Key.Key1);
        Input.ActionRelease("group_assign");
        _sel.SelectBuilding(hall);
        _card.Sync();
        await Type(Key.Key1);
        Check(_sel.Selection.Count == 1 && _sel.SelectedBuilding < 0, $"default keys: 1 with the hall selected: units {_sel.Selection.Count}, building {_sel.SelectedBuilding}");

        foreach (int cell in new[] { 5, 10 })
        {
            var keys = new KeyMap();
            int result = keys.TrySet(KeyMap.FirstCard + cell, Key.Key1);
            GD.Print($"card_{cell} -> 1: TrySet {result}");
            if (result != KeyMap.Ok) continue;
            keys.Apply();
            _sel.SelectBuilding(hall);
            _card.Sync();
            int seq = Sequence(), pending = _sim.PendingCommandCount;
            await Type(Key.Key1);
            Check(_sel.Selection.Count == 1 && _sel.SelectedBuilding < 0,
                $"group_1 shares '1' with card_{cell} (accepted by the tab): 1 with the hall selected recalls nothing (units {_sel.Selection.Count}, building {_sel.SelectedBuilding}, commands {Sequence() - seq}, pending {_sim.PendingCommandCount - pending})");
            new KeyMap().Apply();
        }
        _sel.Selection.Clear();
        _sel.ClearBuilding();
    }

    // A gold mine in sight of player 0 and a screen point that picks it clear of every unit.
    private bool FindMine(out int mine, out Vector2 at)
    {
        mine = -1;
        at = default;
        ResourceStore r = W.Resources;
        Rts.Sim.Map.NavGrid g = W.NavGrid;
        for (int s = 0; s < r.Capacity; s++)
        {
            if (!r.Alive[s] || _data.Resources[r.TypeId[s]].Resource != ResourceKind.Gold) continue;
            ResourceDef def = _data.Resources[r.TypeId[s]];
            int ax = r.Cell[s] % g.Width, ay = r.Cell[s] / g.Width;
            if (!W.Fog.IsVisible(0, r.Cell[s])) continue;
            float cx = (ax + def.FootprintWidth / 2f) * Rts.Sim.Map.MapConstants.CellSize, cy = (ay + def.FootprintHeight / 2f) * Rts.Sim.Map.MapConstants.CellSize;
            _camera.SetFocus(cx, cy);
            float h = TerrainHeight.At(W.Heightmap, cx, cy) + PropsView.Shape.MineHeight * 0.5f;
            Vector2 middle = _camera.UnprojectPosition(new Vector3(cx, h, cy));
            for (int ring = 0; ring <= 5; ring++)
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                        Vector2 p = middle + new Vector2(dx, dy) * 8f;
                        if (_sel.PickResource(p) == s && !UnitNear(p, 30f))
                        {
                            (mine, at) = (s, p);
                            return true;
                        }
                    }
        }
        return false;
    }

    private bool UnitNear(Vector2 at, float px)
    {
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && _sel.TryScreenPosition(i, out Vector2 p) && p.DistanceTo(at) < px) return true;
        return false;
    }

    private async Task ResourceClicks()
    {
        if (!Check(FindMine(out int mine, out Vector2 at), "no visible gold mine with a clear click point")) return;
        await Frame();
        int unit = OwnUnit();
        if (!Check(unit >= 0, "no own unit")) return;

        // Units selected, then a click on the mine: the mine alone, no command.
        _sel.SelectOnly(H(unit));
        int c = await Commands(() => Click(at, false));
        Check(c == 0 && _sel.SelectedResource == mine && _sel.Selection.Count == 0, $"units selected + click on mine: {c} commands, resource {_sel.SelectedResource}, units {_sel.Selection.Count}");
        // Shift held with units selected.
        _sel.SelectOnly(H(unit));
        Input.ActionPress("select_add");
        c = await Commands(() => Click(at, false));
        Input.ActionRelease("select_add");
        Check(c == 0 && _sel.SelectedResource == mine, $"Shift + click on mine with units selected: {c} commands, resource {_sel.SelectedResource}, units {_sel.Selection.Count}");
        // Double click.
        _sel.SelectOnly(H(unit));
        c = await Commands(() => Click(at, true));
        Check(c == 0 && _sel.SelectedResource == mine && _sel.Selection.Count == 0, $"double click on mine: {c} commands, resource {_sel.SelectedResource}, units {_sel.Selection.Count}");

        // A build ghost up (a worker's basic menu): a click on the mine places nothing.
        int worker = -1;
        for (int i = 0; i < W.Units.Capacity && worker < 0; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 0 && W.Units.TypeId[i] == _data.FindUnit("malazan_laborer")) worker = i;
        if (Check(worker >= 0, "no own worker"))
        {
            _sel.SelectOnly(H(worker));
            _card.Sync();
            _card.OpenMenu(false);
            await Frame();
            int place = -1;
            for (int i = 0; i < CommandCard.Cells && place < 0; i++) if (_card.ActionAt(i) == CardCommand.Place) place = i;
            if (Check(place >= 0, "no Place cell"))
            {
                _card.Press(place);
                await Frame();
                Check(_card.GhostActive, "no ghost");
                c = await Commands(() => Click(at, false));
                Check(c == 0, $"ghost up + click on mine: {c} commands");
                GD.Print($"ghost click on mine: {c} commands, ghost still up {_card.GhostActive}, resource {_sel.SelectedResource}");
                _card.CloseMenu();
            }
        }

        // A armed + click on the mine: an attack-move order to that point (a targeting click is an order; logged, not a resource click).
        _sel.SelectOnly(H(unit));
        _sel.BeginAttackMove();
        c = await Commands(() => Click(at, false));
        GD.Print($"note: A + click on mine: {c} commands (attack-move {_sel.IssuedCount(CommandKind.AttackMove)} total), resource {_sel.SelectedResource}, targeting {_sel.Targeting}");

        // The twin: 2,000 ticks, a click every frame on the mine (plain, Shift, double) or on a unit, the panel synced.
        int bad = 0;
        for (int t = 0; t < 2000; t++)
        {
            _sim.Tick();
            switch (t % 4)
            {
                case 0: await Click(at, false); break;
                case 1:
                    Input.ActionPress("select_add");
                    await Click(at, false);
                    Input.ActionRelease("select_add");
                    break;
                case 2: await Click(at, true); break;
                default:
                    int u = OwnUnit(t % 3);
                    if (u >= 0 && _sel.TryScreenPosition(u, out Vector2 up)) await Click(up, false);
                    else await Frame();
                    break;
            }
            _panel.Sync();
            if (_sel.SelectedResource == mine && _panel.ShownKind != 4) bad++;
        }
        Check(bad == 0, $"{bad} frames with the mine selected but the panel not showing it");
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount && replay.TickCount >= 2000,
            $"twin: {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x}), {replay.Checkpoints.Length} checkpoints / {replay.TickCount} ticks");
        GD.Print($"twin: {replay.TickCount} ticks, {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints equal; mine {mine} left {W.Resources.Remaining[mine]}");
    }

    private async Task<int> Commands(Func<Task> act)
    {
        int seq = Sequence(), pending = _sim.PendingCommandCount;
        await act();
        return Math.Max(Sequence() - seq, _sim.PendingCommandCount - pending);
    }

    private int Sequence() =>
        ((int[])typeof(Simulation).GetField("_nextSequence", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_sim)!)[0];

    private async Task Click(Vector2 at, bool doubleClick)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = doubleClick, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frame();
    }

    private async Task Type(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frame();
        await Frame();
    }

    private async Task Ticks(int n)
    {
        for (int i = 0; i < n; i++)
        {
            _sim.Tick();
            await Frame();
        }
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
