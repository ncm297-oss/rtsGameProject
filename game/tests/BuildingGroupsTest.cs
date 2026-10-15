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
/// M6-V7 (the owner's playtest note 7): control groups hold a building. Drives the real Match scene's
/// <see cref="SelectionController"/> with injected keys: Ctrl + 1 on a Billet, a click on empty ground, then 1 selects the
/// Billet again (the selection panel names it, the command card shows its production buttons, none for a Billet); Shift + 2
/// on a Legion Barracks, 2 shows its card (its Train button); Ctrl + 1 with a unit selected makes group 1 that unit, Ctrl + 1
/// with the Billet makes it the Billet again; a double-tap of 1 centres the camera on the Billet's footprint; a cancelled
/// site in group 4 is pruned and 4 then selects nothing.
/// </summary>
/// <remarks>
/// Run: <c>&amp; $env:GODOT --headless --path game res://tests/BuildingGroupsTest.tscn</c>; prints "BUILDING GROUPS TEST PASS" and
/// exits 0, or each failure and exits 1. The test ticks the sim itself (SimRunner disabled); combat off.
/// </remarks>
public partial class BuildingGroupsTest : Node
{
    private static readonly string[] Modifiers = { "group_assign", "group_add", "select_add" };

    private readonly List<string> _failures = new();
    private SelectionController _sel = null!;
    private CommandCard _card = null!;
    private SelectionPanel _panel = null!;
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
        foreach (string f in _failures) GD.Print($"BUILDING GROUPS TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("BUILDING GROUPS TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _data = loaded.Data!;
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--units", "4", "--no-bases", "--no-combat", "--mute" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = runner.Simulation!;
        _sel = match.GetNode<SelectionController>("SelectionController");
        _card = match.GetNode<CommandCard>("Hud/CommandCard");
        _panel = match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        Tick(3);

        // A Billet, a Legion Barracks and a worker for player 0, near its army.
        int faction = W.FactionOf(0);
        int billetType = StartBase.BuildingOfSlot(_data, faction, BuildingSlot.House);
        int barracksType = StartBase.BuildingOfSlot(_data, faction, BuildingSlot.InfantryHall);
        System.Numerics.Vector2 home = OwnMean();
        NavGrid g = W.NavGrid;
        g.WorldToCell(home, out int hx, out int hy);
        int billetAnchor = DefeatStaging.Anchor(W, 0, billetType, hx + 8, hy);
        DefeatStaging.SpawnAt(_sim, 0, billetType, billetAnchor);
        Tick(2);
        int barracksAnchor = DefeatStaging.Anchor(W, 0, barracksType, hx - 9, hy);
        DefeatStaging.SpawnAt(_sim, 0, barracksType, barracksAnchor);
        int worker = StartBase.UnitOfSlot(_data, faction, UnitSlot.Worker);
        int wc = Rts.Sim.Pathfinding.FlowField.NearestPassable(g, (hy + 6) * g.Width + hx);
        _sim.Enqueue(Command.SpawnUnit(0, worker, g.CellCenter(wc % g.Width, wc / g.Width)));
        Tick(2);
        EntityHandle billet = DefeatStaging.BuildingAt(W, 0, billetAnchor), barracks = DefeatStaging.BuildingAt(W, 0, barracksAnchor);
        Check(billet != default && barracks != default && _data.Buildings[billetType].Key == "malazan_billet",
            $"set-up: billet {billet} ({_data.Buildings[billetType].Key}), barracks {barracks}");
        if (billet == default || barracks == default) return;
        _camera.SetFocus(home.X, home.Y);
        await Frames(2);

        // Ctrl + 1 on the Billet, a click on empty ground, 1: the Billet again, with its panel and card.
        Check(_sel.SelectBuilding(billet.Index), "the Billet would not select");
        GroupKey(Key.Key1, "group_assign");
        Check(_sel.Groups.Building(0) == billet && _sel.Groups.Items(0).Length == 0, $"Ctrl + 1 on the Billet: group 1 holds {_sel.Groups.Building(0)} and {_sel.Groups.Items(0).Length} units");
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        await Frames(2);
        Check(_sel.SelectedBuilding < 0 && _sel.Selection.Count == 0, "the selection did not clear");
        Press(Key.Key1);
        await Frames(2);
        Check(_sel.SelectedBuilding == billet.Index && _sel.Selection.Count == 0, $"1: selected building {_sel.SelectedBuilding} (want the Billet {billet.Index}), {_sel.Selection.Count} units");
        CheckCard(billetType, "1 (the Billet)");
        Check(_panel.NameLabel.Text == _data.Buildings[billetType].DisplayName, $"1: panel '{_panel.NameLabel.Text}' (want '{_data.Buildings[billetType].DisplayName}')");

        // Shift + 2 on the Barracks: group 2 is the Barracks; 2 shows its Train button.
        _sel.SelectBuilding(barracks.Index);
        GroupKey(Key.Key2, "group_add");
        Press(Key.Key1);
        await Frames(2);
        Check(_sel.SelectedBuilding == billet.Index, "1 after Shift + 2 is not the Billet");
        Press(Key.Key2);
        await Frames(2);
        Check(_sel.SelectedBuilding == barracks.Index && _sel.Groups.Building(1) == barracks, $"2: selected building {_sel.SelectedBuilding} (want the Barracks {barracks.Index})");
        CheckCard(barracksType, "2 (the Barracks)");
        Check(Enumerable.Range(0, CommandCard.Cells).Any(i => _card.ActionAt(i) == CardCommand.Train), "the Barracks card has no Train button");

        // Ctrl + 1 with a unit: group 1 is that unit (the Billet gone); 1 selects it and no building.
        EntityHandle unit = Own()[0];
        Check(_sel.SelectOnly(unit), "the unit would not select");
        GroupKey(Key.Key1, "group_assign");
        _sel.SelectBuilding(barracks.Index);
        Press(Key.Key1);
        await Frames(2);
        Check(_sel.Groups.Building(0).Generation == 0 && _sel.Selection.Count == 1 && _sel.Selection.Contains(unit) && _sel.SelectedBuilding < 0,
            $"1 after Ctrl + 1 on a unit: group building {_sel.Groups.Building(0)}, selection {_sel.Selection.Count}, building {_sel.SelectedBuilding}");
        // And Ctrl + 1 with the Billet again: the unit goes.
        _sel.SelectBuilding(billet.Index);
        GroupKey(Key.Key1, "group_assign");
        Check(_sel.Groups.Building(0) == billet && _sel.Groups.Items(0).Length == 0, "Ctrl + 1 on the Billet after the unit");

        // Double-tap 1: the camera centres on the Billet's footprint.
        _camera.SetFocus(home.X - 40f, home.Y + 30f);
        ulong last = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - last < (ulong)ControlGroups.DoubleTapMs + 50) await Frame(); // a fresh pair (BUG-0088)
        System.Numerics.Vector2 away = _camera.Focus;
        Press(Key.Key1);
        Check(System.Numerics.Vector2.Distance(_camera.Focus, away) < 0.01f, "a single recall of the Billet moved the camera");
        Press(Key.Key1);
        System.Numerics.Vector2 centre = StartBase.FootprintCenter(g, _data.Buildings[billetType], billetAnchor);
        float off = System.Numerics.Vector2.Distance(_camera.Focus, centre);
        GD.Print($"double-tap on the Billet: focus {off:F3} m from its footprint centre");
        Check(off < 0.01f, $"double-tap focus {off:F2} m from the Billet's footprint centre");

        // A site in group 4, cancelled: pruned, and 4 selects nothing.
        int siteAnchor = BuildSite(billetType);
        if (siteAnchor >= 0)
        {
            EntityHandle site = DefeatStaging.BuildingAt(W, 0, siteAnchor);
            _sel.SelectBuilding(site.Index);
            GroupKey(Key.Key4, "group_assign");
            Check(_sel.Groups.Building(3) == site, "Ctrl + 4 on the site");
            _sel.SelectBuilding(billet.Index);
            _sim.Enqueue(Command.Cancel(0, StartBase.FootprintCenter(g, _data.Buildings[billetType], siteAnchor)));
            Tick(2);
            await Frames(2);
            Check(!W.Buildings.Alive[site.Index] || W.Buildings.Generation[site.Index] != site.Generation, "the site was not cancelled");
            Check(_sel.Groups.Building(3).Generation == 0, $"the cancelled site is still in group 4: {_sel.Groups.Building(3)}");
            Press(Key.Key4);
            await Frames(2);
            Check(_sel.SelectedBuilding == billet.Index && _sel.Selection.Count == 0, $"4 after the cancel: building {_sel.SelectedBuilding}, units {_sel.Selection.Count} (want the Billet kept)");
        }
    }

    // The card shows exactly `type`'s production entries (Train / Research cells, in any cell): what a selected building shows.
    private void CheckCard(int type, string what)
    {
        Span<ProductionEntry> entries = stackalloc ProductionEntry[CommandCard.Cells];
        int n = ProductionMenu.Entries(_data, type, entries);
        var want = new List<(bool, int)>();
        for (int k = 0; k < n; k++) want.Add((entries[k].IsTech, entries[k].TypeId));
        var shown = new List<(bool, int)>();
        for (int i = 0; i < CommandCard.Cells; i++)
            if (_card.ActionAt(i) is CardCommand.Train or CardCommand.Research) shown.Add((_card.ActionAt(i) == CardCommand.Research, _card.TypeAt(i)));
        Check(shown.OrderBy(x => x).SequenceEqual(want.OrderBy(x => x)) && !_card.MenuOpen,
            $"{what}: card [{string.Join(", ", shown)}], want [{string.Join(", ", want)}]");
    }

    // A House site for player 0 built by its worker on explored ground; its anchor, or -1.
    private int BuildSite(int type)
    {
        UnitStore u = W.Units;
        int worker = -1;
        for (int i = 0; i < u.Capacity && worker < 0; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && _data.Units[u.TypeId[i]].Slot == UnitSlot.Worker) worker = i;
        NavGrid g = W.NavGrid;
        g.WorldToCell(u.Position[Math.Max(0, worker)], out int wx, out int wy);
        int anchor = -1;
        for (int r = 2; r < 10 && anchor < 0 && worker >= 0; r++)
            for (int dy = -r; dy <= r && anchor < 0; dy++)
                for (int dx = -r; dx <= r && anchor < 0; dx++)
                    if (g.InBounds(wx + dx, wy + dy) && W.CanPlace(0, type, (wy + dy) * g.Width + wx + dx, out _)) anchor = (wy + dy) * g.Width + wx + dx;
        Check(worker >= 0 && anchor >= 0, $"site: worker {worker}, anchor {anchor}");
        if (worker < 0 || anchor < 0) return -1;
        _sim.Enqueue(Command.Build(0, new EntityHandle(worker, u.Generation[worker]), type, g.CellCenter(anchor % g.Width, anchor / g.Width)));
        for (int t = 0; t < 200 && DefeatStaging.BuildingAt(W, 0, anchor) == default; t++) Tick(1);
        EntityHandle site = DefeatStaging.BuildingAt(W, 0, anchor);
        Check(site != default && W.Buildings.UnderConstruction[site.Index], $"site at {anchor}: {site}");
        return site != default ? anchor : -1;
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

    private List<EntityHandle> Own() => DefeatStaging.UnitsOf(W, 0).Where(h => _data.Units[W.Units.TypeId[h.Index]].Slot != UnitSlot.Worker).ToList();

    private System.Numerics.Vector2 OwnMean()
    {
        List<EntityHandle> own = DefeatStaging.UnitsOf(W, 0);
        return own.Aggregate(System.Numerics.Vector2.Zero, (m, h) => m + W.Units.Position[h.Index]) / own.Count;
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
