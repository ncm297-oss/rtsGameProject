using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V7 (2026-10-14-0946): attacks the dev scene doesn't make, on the real Match scene.
/// <list type="bullet">
/// <item>Edge pan wiring (the OS confinement itself needs a real cursor): a mouse motion parsed at the last pixel column / row
/// of each side, at three window sizes, pans the camera that way; one pixel outside the window, none; the bottom-left
/// corner pixel (beside the minimap, 8 px in) pans.</item>
/// <item>Groups: units, then a building, then units on one digit, each replacing the other; a building group recalled while
/// A is armed (targeting ends, the building is selected; the next left click is a plain selection); a building razed by
/// combat leaves its group (the digit then changes nothing).</item>
/// <item>Headless never confines: <c>Main.CursorConfined</c> stays false through a match, the pause menu and the menu.</item>
/// </list>
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V7Test.tscn</c>; prints "QA M6-V7 TEST PASS" or each failure.</remarks>
public partial class QaM6V7Test : Node
{
    private static readonly string[] Modifiers = { "group_assign", "group_add", "select_add" };

    private readonly List<string> _failures = new();
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private Simulation _sim = null!;
    private GameData _data = null!;

    private World W => _sim.World;

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string m in Modifiers) Input.ActionRelease(m);
        foreach (string f in _failures) GD.Print($"QA M6-V7 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V7 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _data = loaded.Data!;
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--units", "4", "--no-bases", "--mute" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _sel = match.GetNode<SelectionController>("SelectionController");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        Tick(3);

        await EdgePan();
        _camera.EdgePanEnabled = false;
        await Groups();
        RemoveChild(match);
        match.QueueFree();
        await Frames(2);
        await HeadlessNeverConfines();
    }

    private async Task EdgePan()
    {
        GD.Print($"edge pan: window focused {DisplayServer.WindowIsFocused()}");
        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(1152, 648) })
        {
            GetTree().Root.Size = size;
            await Frames(2);
            Vector2 vis = GetViewport().GetVisibleRect().Size;
            Check(vis == new Vector2(size.X, size.Y), $"{size}: visible rect {vis}");
            var cases = new (string Side, Vector2 At, int Dx, int Dy)[]
            {
                ("right last px", new(size.X - 1, size.Y / 2f), 1, 0),
                ("left first px", new(0, size.Y / 2f), -1, 0),
                ("top first px", new(size.X / 2f, 0), 0, -1),
                ("bottom last px", new(size.X / 2f, size.Y - 1), 0, 1),
                ("bottom-left corner px (beside the minimap)", new(0, size.Y - 1), -1, 1),
                ("outside right", new(size.X + 2, size.Y / 2f), 0, 0),
                ("outside bottom", new(size.X / 2f, size.Y + 2), 0, 0),
                ("middle", new(size.X / 2f, size.Y / 2f), 0, 0),
            };
            foreach ((string side, Vector2 at, int dx, int dy) in cases)
            {
                _camera.EdgePanEnabled = false;
                _camera.SetFocus(64f, 64f); // mid-map: no clamp in the way
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                await Frames(1);
                System.Numerics.Vector2 from = _camera.Focus;
                Vector2 seen = GetViewport().GetMousePosition();
                _camera.EdgePanEnabled = true;
                await Frames(6);
                _camera.EdgePanEnabled = false;
                System.Numerics.Vector2 moved = _camera.Focus - from;
                int sx = Math.Abs(moved.X) < 0.01f ? 0 : Math.Sign(moved.X), sy = Math.Abs(moved.Y) < 0.01f ? 0 : Math.Sign(moved.Y);
                Check(sx == dx && sy == dy, $"{size} {side}: cursor at {at} (viewport sees {seen}) moved the focus by {moved} (want sign {dx}, {dy})");
            }
        }
        GetTree().Root.Size = new Vector2I(1152, 648);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(576, 324), GlobalPosition = new Vector2(576, 324) });
        await Frames(2);
    }

    private async Task Groups()
    {
        int faction = W.FactionOf(0);
        int billetType = StartBase.BuildingOfSlot(_data, faction, BuildingSlot.House);
        int barracksType = StartBase.BuildingOfSlot(_data, faction, BuildingSlot.InfantryHall);
        List<EntityHandle> own = DefeatStaging.UnitsOf(W, 0);
        System.Numerics.Vector2 home = own.Aggregate(System.Numerics.Vector2.Zero, (m, h) => m + W.Units.Position[h.Index]) / own.Count;
        NavGrid g = W.NavGrid;
        g.WorldToCell(home, out int hx, out int hy);
        int billetAnchor = DefeatStaging.Anchor(W, 0, billetType, hx + 10, hy);
        DefeatStaging.SpawnAt(_sim, 0, billetType, billetAnchor);
        Tick(2);
        int barracksAnchor = DefeatStaging.Anchor(W, 0, barracksType, hx - 10, hy);
        DefeatStaging.SpawnAt(_sim, 0, barracksType, barracksAnchor);
        Tick(2);
        EntityHandle billet = DefeatStaging.BuildingAt(W, 0, billetAnchor), barracks = DefeatStaging.BuildingAt(W, 0, barracksAnchor);
        Check(billet != default && barracks != default, $"set-up: billet {billet}, barracks {barracks}");
        if (billet == default || barracks == default) return;
        _camera.SetFocus(home.X, home.Y);
        await Frames(2);

        // Units, then the Billet, then units again, all on digit 5.
        Check(_sel.SelectOnly(own[0]), "unit select");
        GroupKey(Key.Key5, "group_assign");
        Check(_sel.Groups.Items(4).Length == 1 && _sel.Groups.Building(4).Generation == 0, "Ctrl + 5 on a unit");
        _sel.SelectBuilding(billet.Index);
        GroupKey(Key.Key5, "group_assign");
        Check(_sel.Groups.Items(4).Length == 0 && _sel.Groups.Building(4) == billet, "Ctrl + 5 on the Billet after a unit");
        _sel.SelectOnly(own[1]);
        _sel.Selection.Add(own[2]);
        GroupKey(Key.Key5, "group_add"); // Shift + 5 with two units over a building group
        Check(_sel.Groups.Items(4).Length == 2 && _sel.Groups.Building(4).Generation == 0, $"Shift + 5 with units over the Billet: {_sel.Groups.Items(4).Length} units, building {_sel.Groups.Building(4)}");
        _sel.SelectBuilding(barracks.Index);
        Press(Key.Key5);
        await Frames(1);
        Check(_sel.SelectedBuilding < 0 && _sel.Selection.Count == 2, $"5 (units): building {_sel.SelectedBuilding}, units {_sel.Selection.Count}");

        // A building group recalled while A is armed: targeting ends, the building is selected alone.
        _sel.SelectBuilding(billet.Index);
        GroupKey(Key.Key6, "group_assign");
        _sel.SelectOnly(own[0]);
        _sel.BeginAttackMove();
        Check(_sel.Targeting, "A did not arm");
        int ordersBefore = _sim.PendingCommandCount;
        Press(Key.Key6);
        await Frames(1);
        Check(!_sel.Targeting && _sel.SelectedBuilding == billet.Index && _sel.Selection.Count == 0,
            $"6 with A armed: targeting {_sel.Targeting}, building {_sel.SelectedBuilding}, units {_sel.Selection.Count}");
        Check(_sim.PendingCommandCount == ordersBefore, $"6 with A armed enqueued {_sim.PendingCommandCount - ordersBefore} commands");
        // And the reverse: A armed on units, recall a unit group: QaH1 says A stays armed.
        _sel.SelectOnly(own[0]);
        _sel.BeginAttackMove();
        Press(Key.Key5);
        await Frames(1);
        Check(_sel.Targeting && _sel.Selection.Count == 2, $"5 (units) with A armed: targeting {_sel.Targeting}, units {_sel.Selection.Count}");
        _sel.CancelTargeting();

        // The Billet razed by combat: player 1's catapults shell it; group 6 lets it go and 6 changes nothing.
        int shooter = DefeatStaging.Shooter(_data);
        foreach (System.Numerics.Vector2 at in DefeatStaging.Battery(g, _data.Buildings[billetType], billetAnchor))
        {
            g.WorldToCell(at, out int ax, out int ay);
            int c = Rts.Sim.Pathfinding.FlowField.NearestPassable(g, ay * g.Width + ax);
            _sim.Enqueue(Command.SpawnUnit(1, shooter, g.CellCenter(c % g.Width, c / g.Width)));
        }
        Tick(2);
        foreach (EntityHandle gun in DefeatStaging.UnitsOf(W, 1).Where(h => W.Units.TypeId[h.Index] == shooter))
            _sim.Enqueue(Command.Attack(1, gun, billet, isBuilding: true));
        int t = 0;
        for (; t < 4000 && W.Buildings.Alive[billet.Index] && W.Buildings.Generation[billet.Index] == billet.Generation; t++) Tick(1);
        GD.Print($"billet razed after {t} ticks");
        Check(!(W.Buildings.Alive[billet.Index] && W.Buildings.Generation[billet.Index] == billet.Generation), "the Billet was never razed");
        await Frames(2);
        Check(_sel.Groups.Building(5).Generation == 0, $"group 6 still holds the razed Billet: {_sel.Groups.Building(5)}");
        _sel.SelectBuilding(barracks.Index);
        Press(Key.Key6);
        await Frames(1);
        Check(_sel.SelectedBuilding == barracks.Index && _sel.Selection.Count == 0, $"6 after the raze: building {_sel.SelectedBuilding} (want the Barracks kept), units {_sel.Selection.Count}");
    }

    private async Task HeadlessNeverConfines()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { "--mute" };
        main.SettingsPath = $"user://qa-m6v7-{OS.GetProcessId()}/settings.cfg"; // BUG-0770: never the player's file (missing: the defaults)
        AddChild(main);
        await Frames(3);
        bool ever = main.CursorConfined;
        main.StartSkirmish(new[] { "--units", "2", "--no-bases", "--mute" });
        for (int i = 0; i < 10; i++)
        {
            await Frames(1);
            ever |= main.CursorConfined;
        }
        Check(!ever && Input.MouseMode == Input.MouseModeEnum.Visible && main.ConfineAllowed == SettingsApplier.HasWindow,
            $"headless: confined {ever}, mode {Input.MouseMode}, allowed {main.ConfineAllowed}, window {SettingsApplier.HasWindow}");
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
        Check(Input.MouseMode == Input.MouseModeEnum.Visible, $"after the shell left: mode {Input.MouseMode}");
    }

    private void GroupKey(Key key, string modifier)
    {
        Input.ActionPress(modifier);
        Press(key);
        Input.ActionRelease(modifier);
    }

    private void Press(Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
