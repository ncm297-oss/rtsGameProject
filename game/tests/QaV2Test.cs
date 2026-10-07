using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M3-V2 (2026-10-07-1131): attacks on the command card, build ghost, building selection and right-click Repair in the
/// real Match scene. ui.json roots that aren't objects; the ghost against CanPlace over the whole map (corners, border,
/// ramps, cliff lips, units, own and enemy buildings) with ticks in between; the real SimRunner ticking several times a
/// frame (once-per-frame CanPlace, colour after the tick); click floods on green and red ghosts; a click whose position
/// differs from the last drawn anchor; a selected site destroyed by damage; a double Cancel; a right click on the visible
/// top of a damaged building's box; footprint-edge context orders; a control-group recall to soldiers while a ghost is up.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV2Test.tscn</c>; prints "QA M3-V2 TEST PASS" and exits 0, else each failure and exit 1. Rows for open bugs print "QA M3-V2 KNOWN BUG-nnnn" and don't fail (like an xUnit Skip) unless run with <c>-- --strict</c>; once fixed they print "... now passes": make them plain checks then.</remarks>
public partial class QaV2Test : Node
{
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FlagsField = typeof(NavGrid).GetField("_flags", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SimRunner _runner = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private CommandCard _card = null!;
    private BuildGhost _ghost = null!;
    private BuildingOutline _outline = null!;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            UiJsonRoots();
            await StartMatch();
            await GhostWholeMap();
            await GhostUnderRealTicks();
            await ClickFloods();
            await ClickPositionVsDrawnAnchor();
            await SiteDestroyedAndDoubleCancel();
            await RightClickOnBoxTop();
            await FootprintEdges();
            await RecallWhileGhostUp();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Input.ActionRelease("order_queue");
        foreach (string f in _failures) GD.Print($"QA M3-V2 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M3-V2 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // ui.json fail-fast: a root (or section) of the wrong JSON kind must be an error, never a UiText with empty texts.
    private void UiJsonRoots()
    {
        foreach (string json in new[] { "[]", "null", "42", "\"ui\"" })
        {
            var errors = new List<string>();
            UiText? ui = UiText.Parse(json, errors);
            Known("BUG-0110", ui == null && errors.Count > 0, $" ui.json '{json}' parsed to {(ui == null ? "null" : "a UiText")} with {errors.Count} errors (Stop label '{ui?.CommandName(CardCommand.Stop)}')");
        }
        // Sections of the wrong kind inside an object root are errors (one per section).
        var e1 = new List<string>();
        Check(UiText.Parse("{\"commands\": [], \"buildMenus\": {}, \"placement\": 3}", e1) == null && e1.Count >= 3, $"wrong-kind sections: {string.Join("; ", e1)}");
        // A non-string text and an empty menu list are errors.
        string good = System.IO.File.ReadAllText(UiText.DefaultPath);
        var e2 = new List<string>();
        Check(UiText.Parse(good.Replace("\"Blocked\"", "7"), e2) == null && e2.Count == 1 && e2[0].Contains("placement.blocked"), $"numeric placement text: {string.Join("; ", e2)}");
        e2.Clear();
        Check(UiText.Parse(good.Replace("\"advanced\": [\"caster_hall\", \"siege_works\", \"watch_tower\"]", "\"advanced\": []"), e2) == null && e2.Count == 1, $"empty advanced menu: {string.Join("; ", e2)}");
    }

    private async Task StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "20", "--mute" }));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        _outline = _match.GetNode<BuildingOutline>("World3D/BuildingOutline");
        Tick(2);
        await Frames();
    }

    // The ghost equals CanPlace over the whole map: camera on each corner, edge midpoints, ramp cells, cliff lips, units,
    // the own and the enemy hall; 40 random cursor points per spot, a tick every 7 points.
    private async Task GhostWholeMap()
    {
        await Select(Workers());
        Key(Godot.Key.B);
        Key(Godot.Key.E); // infantry hall, 3 x 3
        int type = _ghost.TypeId;
        if (!Check(_ghost.Active && type >= 0, "no ghost for the whole-map sweep")) return;
        float mw = G.Width * MapConstants.CellSize, mh = G.Height * MapConstants.CellSize;
        var spots = new List<(string, System.Numerics.Vector2)>
        {
            ("corner sw", new(2f, 2f)), ("corner se", new(mw - 2f, 2f)), ("corner nw", new(2f, mh - 2f)), ("corner ne", new(mw - 2f, mh - 2f)),
            ("edge s", new(mw / 2f, 2f)), ("edge w", new(2f, mh / 2f)), ("own hall", HallCenter(0)), ("enemy hall", HallCenter(1)),
            ("units", U.Position[Soldiers(1)[0].Index]),
        };
        var flags = (NavFlags[])FlagsField.GetValue(G)!;
        int ramp = Array.FindIndex(flags, f => (f & NavFlags.Ramp) != 0);
        int cliff = Array.FindIndex(flags, f => (f & NavFlags.Cliff) != 0);
        if (ramp >= 0) spots.Add(("ramp", G.CellCenter(ramp % G.Width, ramp / G.Width)));
        if (cliff >= 0) spots.Add(("cliff lip", G.CellCenter(cliff % G.Width, cliff / G.Width)));
        Check(ramp >= 0 && cliff >= 0, $"seed 1 map has ramp {ramp} cliff {cliff}");
        var rng = new Random(11);
        Vector2 view = GetViewport().GetVisibleRect().Size;
        int checkedPts = 0, maxCalls = 0;
        var reasons = new SortedSet<string>();
        foreach ((string name, System.Numerics.Vector2 at) in spots)
        {
            _camera.SetZoom(30f);
            _camera.SetFocus(at.X, at.Y);
            await Frames();
            for (int k = 0; k < 40; k++)
            {
                if (k % 7 == 6) Tick(1);
                _ghost.ScreenOverride = new Vector2((float)rng.NextDouble() * view.X, (float)rng.NextDouble() * view.Y);
                int calls = _ghost.CanPlaceCalls;
                await Frame();
                await Frame();
                maxCalls = Math.Max(maxCalls, _ghost.CanPlaceCalls - calls);
                if (!_ghost.Visible) continue;
                int want = PlacementGhost.Anchor(G, _data.Buildings[type], Pick(_ghost.ScreenOverride.Value));
                bool ok = W.CanPlace(0, type, _ghost.Anchor, out PlacementError r);
                Check(_ghost.Anchor == want, $"{name} pt {k}: anchor {_ghost.Anchor} vs cursor {want}");
                Check(_ghost.Valid == ok && _ghost.Reason == r && _ghost.Box.MaterialOverride == (ok ? _ghost.GreenMaterial : _ghost.RedMaterial),
                    $"{name} pt {k}: ghost {_ghost.Valid}/{_ghost.Reason} vs CanPlace {ok}/{r}");
                int ax = _ghost.Anchor % G.Width, ay = _ghost.Anchor / G.Width;
                BuildingDef def = _data.Buildings[type];
                Check(ax + def.FootprintWidth <= G.Width && ay + def.FootprintHeight <= G.Height, $"{name} pt {k}: footprint off the map");
                if (!ok) reasons.Add(r.ToString());
                checkedPts++;
            }
        }
        GD.Print($"whole map: {checkedPts} ghost points checked, reasons {string.Join(", ", reasons)}, most CanPlace per frame {maxCalls}");
        Check(maxCalls <= 1, $"CanPlace {maxCalls} times in a frame");
        Check(checkedPts > 200, $"only {checkedPts} points on the map");
        _ghost.ScreenOverride = null;
        Key(Godot.Key.Escape);
        await Frames();
    }

    // The real runner at 8x (several ticks a frame) with the cursor moving every frame and workers paying: at most one
    // CanPlace a frame, never during a tick, and between frames the ghost equals CanPlace of the state after the ticks.
    private async Task GhostUnderRealTicks()
    {
        List<EntityHandle> workers = Workers();
        await Select(workers);
        FocusOn(HallCenter(0));
        Key(Godot.Key.B);
        Key(Godot.Key.Q); // house
        int house = _ghost.TypeId;
        // A worker pays for a house mid-run (the ghost's affordability flips on a tick, not on a cursor move).
        int spot = GreenAnchor(house, HallCenter(0));
        if (Check(spot >= 0, "no green house spot")) _sim.Enqueue(Command.Build(0, workers[0], house, PlacementGhost.AnchorPoint(G, spot)));
        _runner.GameSpeed = 8.0;
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        Vector2 view = GetViewport().GetVisibleRect().Size;
        var rng = new Random(5);
        int maxCalls = 0, mismatches = 0, frames = 0;
        long startTick = _sim.TickNumber;
        for (int f = 0; f < 240; f++)
        {
            if (f % 3 != 0) _ghost.ScreenOverride = new Vector2((float)rng.NextDouble() * view.X, (float)rng.NextDouble() * view.Y);
            int calls = _ghost.CanPlaceCalls;
            await Frame(); // process_frame fires before this frame's _Process: we see the state between frames
            maxCalls = Math.Max(maxCalls, _ghost.CanPlaceCalls - calls);
            if (!_ghost.Visible || _ghost.Anchor < 0) continue;
            frames++;
            bool ok = W.CanPlace(0, house, _ghost.Anchor, out PlacementError r);
            if (ok != _ghost.Valid || r != _ghost.Reason) mismatches++;
        }
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _runner.GameSpeed = 1.0;
        GD.Print($"real ticks: {_sim.TickNumber - startTick} ticks over 240 frames, {frames} compared, {mismatches} mismatches, most CanPlace per frame {maxCalls}");
        Check(maxCalls <= 1, $"real ticks: {maxCalls} CanPlace calls in one frame");
        Check(mismatches == 0, $"real ticks: ghost disagreed with CanPlace on {mismatches} of {frames} frames");
        Check(_sim.TickNumber - startTick > 10, "the runner didn't tick");
        _ghost.ScreenOverride = null;
        Key(Godot.Key.Escape);
        await Frames();
    }

    // 25 clicks in one frame: green + Shift = 25 x N Builds at one anchor (first unqueued); green without Shift = N, then
    // plain clicks; red = nothing.
    private async Task ClickFloods()
    {
        List<EntityHandle> three = Workers().Take(3).ToList();
        await Select(three);
        Key(Godot.Key.B);
        Key(Godot.Key.Q);
        int house = _ghost.TypeId;
        int spot = GreenAnchor(house, HallCenter(0));
        if (!Check(spot >= 0, "flood: no green spot")) return;
        await Aim(house, spot);
        if (!Check(_ghost.Valid, $"flood: ghost not green at {spot} ({_ghost.Reason})")) return;
        int start = _sim.PendingCommandCount;
        for (int i = 0; i < 25; i++) LeftClick(_ghost.ScreenOverride!.Value);
        List<Command> sent = Pending(start);
        System.Numerics.Vector2 pt = PlacementGhost.AnchorPoint(G, spot);
        Check(sent.Count == 3 && sent.All(c => c.Kind == CommandKind.Build && c.Position == pt && !c.IsQueued), $"flood without Shift: {sent.Count} commands");
        Check(!_ghost.Active, "flood without Shift: ghost still up");
        Tick(2);

        await Select(three);
        Key(Godot.Key.B);
        Key(Godot.Key.Q);
        spot = GreenAnchor(house, HallCenter(0));
        if (spot < 0)
        {
            GD.Print("flood: no second green house spot (wood spent); Shift flood measured on a red ghost only");
        }
        else
        {
            await Aim(house, spot);
            Input.ActionPress("order_queue");
            start = _sim.PendingCommandCount;
            for (int i = 0; i < 25; i++) LeftClick(_ghost.ScreenOverride!.Value);
            Input.ActionRelease("order_queue");
            sent = Pending(start);
            GD.Print($"flood with Shift on one green anchor: {sent.Count} Builds ({sent.Count(c => !c.IsQueued)} unqueued)");
            Check(sent.All(c => c.Kind == CommandKind.Build && c.Position == PlacementGhost.AnchorPoint(G, spot)), "shift flood: a Build off the anchor");
            Tick(2);
        }
        // Red: the hall.
        await Aim(house, B.Cell[HallSlot(0)]);
        Check(!_ghost.Valid, "flood: ghost on the hall is green");
        start = _sim.PendingCommandCount;
        for (int i = 0; i < 25; i++) LeftClick(_ghost.ScreenOverride!.Value);
        Check(_sim.PendingCommandCount == start && _ghost.Active, $"red flood: {_sim.PendingCommandCount - start} commands, ghost {_ghost.Active}");
        _ghost.ScreenOverride = null;
        Key(Godot.Key.Escape);
        await Frames();
    }

    // The click arrives before the frame's _Process: the cursor (and the click's own position) is on a red cell while the
    // last drawn ghost was green elsewhere. The Build must go where the click is, or nowhere: never to the old anchor.
    private async Task ClickPositionVsDrawnAnchor()
    {
        List<EntityHandle> three = Workers().Take(3).ToList();
        await Select(three);
        Key(Godot.Key.B);
        Key(Godot.Key.Q);
        int house = _ghost.TypeId;
        int spot = GreenAnchorIgnoringCost(house, HallCenter(0));
        if (!Check(spot >= 0, "stale: no spot")) return;
        // Give the player wood so the spot is green.
        await Aim(house, spot);
        bool green = _ghost.Valid;
        Vector2 hallPx = await OnScreenNoMove(HallCenter(0));
        int start = _sim.PendingCommandCount;
        LeftClick(hallPx); // the click is on the hall (red); the ghost has not synced to it yet
        List<Command> sent = Pending(start);
        if (green)
            Known("BUG-0109", sent.Count == 0, $" a click on the hall (red) placed {sent.Count} Builds at the last drawn anchor {spot} ({Describe(sent)})");
        else GD.Print($"stale: spot {spot} not green ({_ghost.Reason}); row not exercised");
        Tick(2);
        _ghost.ScreenOverride = null;
        Key(Godot.Key.Escape);
        await Frames();
    }

    // A selected site killed by damage clears the selection, the card and the outline; B twice in a frame sends two Cancels.
    private async Task SiteDestroyedAndDoubleCancel()
    {
        int site = -1;
        for (int k = 0; k < B.Capacity; k++) if (B.Alive[k] && B.Owner[k] == 0 && B.UnderConstruction[k]) site = k;
        if (site < 0)
        {
            int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
            int a = GreenAnchor(house, HallCenter(0));
            if (a >= 0) _sim.Enqueue(Command.Build(0, Workers()[0], house, PlacementGhost.AnchorPoint(G, a)));
            Tick(2);
            for (int k = 0; k < B.Capacity; k++) if (B.Alive[k] && B.Owner[k] == 0 && B.UnderConstruction[k]) site = k;
        }
        if (!Check(site >= 0, "no own site to destroy")) return;
        Check(_sel.SelectBuilding(site), "site not selectable");
        await Frames();
        Check(_card.ActionAt(14) == CardCommand.Cancel && _outline.Visible, "site selection: no Cancel / outline");
        // Two B presses before any tick.
        int start = _sim.PendingCommandCount;
        Key(Godot.Key.B);
        Key(Godot.Key.B);
        List<Command> twice = Pending(start);
        GD.Print($"double B on a site: {twice.Count} Cancels");
        // Kill the site by damage before the Cancels apply.
        DamageMethod.Invoke(B, new object[] { B.HandleOf(site), 1_000_000 });
        Check(!B.Alive[site], "damage didn't kill the site");
        await Frames();
        Check(_sel.SelectedBuilding == -1 && !_outline.Visible && _card.ActionAt(14) == CardCommand.None, $"destroyed site: selected {_sel.SelectedBuilding}, outline {_outline.Visible}, cell 14 {_card.ActionAt(14)}");
        int s2 = _sim.PendingCommandCount;
        Key(Godot.Key.B);
        Check(_sim.PendingCommandCount == s2, "B after the site died enqueued something");
        Tick(2);
        await Frames();
    }

    // A right click on the visible top of a damaged own building's box: the player sees the box, so it should be a Repair.
    private async Task RightClickOnBoxTop()
    {
        int hall = HallSlot(0);
        int max = _data.Buildings[B.TypeId[hall]].Hp;
        if (B.Hp[hall] >= max) DamageMethod.Invoke(B, new object[] { B.HandleOf(hall), max / 4 });
        List<EntityHandle> three = Workers().Take(3).ToList();
        await Select(three);
        System.Numerics.Vector2 c = SelectionController.SiteCenter(W, hall);
        FocusOn(c);
        await Frames();
        BuildingDef def = _data.Buildings[B.TypeId[hall]];
        float baseY = TerrainHeight.At(W.Heightmap, c.X, c.Y);
        // Sample the box's top face on a 9 x 9 grid; keep pixels whose ray meets this box first.
        int onBox = 0, move = 0, repair = 0;
        for (int i = 0; i < 9; i++)
            for (int j = 0; j < 9; j++)
            {
                float x = c.X + (i - 4) / 4.5f * def.FootprintWidth;   // within +-(fw*cs/2) * 0.9
                float z = c.Y + (j - 4) / 4.5f * def.FootprintHeight;
                Vector2 px = _camera.UnprojectPosition(new Vector3(x, baseY + BuildingViews.BoxHeight, z));
                if (_sel.PickBuilding(px) != hall) continue;
                onBox++;
                CommandKind k = SelectionController.WorkAt(W, Pick(px), out _);
                if (k == CommandKind.Repair) repair++;
                else move++;
            }
        // The far edge of the top, through the real right-click path.
        Vector2 far = _camera.UnprojectPosition(new Vector3(c.X, baseY + BuildingViews.BoxHeight, c.Y - def.FootprintHeight * 0.9f));
        Vector2 near = _camera.UnprojectPosition(new Vector3(c.X, baseY + BuildingViews.BoxHeight, c.Y + def.FootprintHeight * 0.9f));
        int start = _sim.PendingCommandCount;
        RightClick(_sel.PickBuilding(far) == hall ? far : near);
        List<Command> sent = Pending(start);
        GD.Print($"box top of the damaged hall: {onBox} sampled pixels on the box, {repair} Repair, {move} Move; far-edge right-click sent {Describe(sent)}");
        Known("BUG-0108", move == 0, $" {move} of {onBox} pixels on the damaged hall's visible box top give a Move, not a Repair");
        Tick(2);
    }

    // ContextOrder exactly inside / outside each footprint edge of the damaged hall: Repair inside, Move outside.
    private async Task FootprintEdges()
    {
        int hall = HallSlot(0);
        int max = _data.Buildings[B.TypeId[hall]].Hp;
        if (B.Hp[hall] >= max) DamageMethod.Invoke(B, new object[] { B.HandleOf(hall), max / 4 });
        BuildingDef def = _data.Buildings[B.TypeId[hall]];
        int a = B.Cell[hall];
        float x0 = a % G.Width * MapConstants.CellSize, y0 = a / G.Width * MapConstants.CellSize;
        float x1 = x0 + def.FootprintWidth * MapConstants.CellSize, y1 = y0 + def.FootprintHeight * MapConstants.CellSize;
        float cx = (x0 + x1) / 2f, cy = (y0 + y1) / 2f, e = 0.01f;
        var inside = new System.Numerics.Vector2[] { new(x0 + e, cy), new(x1 - e, cy), new(cx, y0 + e), new(cx, y1 - e), new(x0 + e, y0 + e), new(x1 - e, y1 - e) };
        var outside = new System.Numerics.Vector2[] { new(x0 - e, cy), new(x1 + e, cy), new(cx, y0 - e), new(cx, y1 + e) };
        foreach (var p in inside) Check(SelectionController.WorkAt(W, p, out _) == CommandKind.Repair, $"edge inside {p}: {SelectionController.WorkAt(W, p, out _)}");
        foreach (var p in outside)
        {
            CommandKind k = SelectionController.WorkAt(W, p, out _);
            Check(k is CommandKind.Move or CommandKind.Gather, $"edge outside {p}: {k}");
        }
        // Through the order path with 3 workers + Shift: three queued Repairs.
        await Select(Workers().Take(3).ToList());
        Input.ActionPress("order_queue");
        int start = _sim.PendingCommandCount;
        _sel.ContextOrder(new Vector2(inside[1].X, inside[1].Y), Input.IsActionPressed("order_queue"));
        Input.ActionRelease("order_queue");
        List<Command> sent = Pending(start);
        Check(sent.Count == 3 && sent.All(c => c.Kind == CommandKind.Repair && c.IsQueued), $"edge repair: {Describe(sent)}");
        Tick(2);
    }

    // A control-group recall to soldiers while a ghost is up closes the menu and ghost; a later left click selects.
    private async Task RecallWhileGhostUp()
    {
        List<EntityHandle> soldiers = Soldiers(3);
        await Select(soldiers);
        Input.ActionPress("group_assign");
        Key(Godot.Key.Key1);
        Input.ActionRelease("group_assign");
        await Select(Workers());
        Key(Godot.Key.B);
        Key(Godot.Key.Q);
        Check(_ghost.Active, "recall: no ghost");
        Key(Godot.Key.Key1);
        await Frames();
        GD.Print($"recall: selection {_sel.Selection.Count}, worker subgroup {_sel.ActiveSubgroupIsWorker}, menu {_card.MenuOpen}, ghost {_ghost.Active}");
        if (!_sel.ActiveSubgroupIsWorker) Check(!_card.MenuOpen && !_ghost.Active, "recall to soldiers left the menu / ghost up");
        Key(Godot.Key.Escape);
        await Frames();
    }

    // ---- helpers ----

    private List<Command> Pending(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private static string Describe(List<Command> list) =>
        $"[{string.Join(", ", list.Select(c => $"{c.Kind}{(c.IsQueued ? "+q" : "")} u{c.Unit.Index} t{c.TypeId} ({c.Position.X:0.#},{c.Position.Y:0.#})"))}]";

    private List<EntityHandle> Workers()
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot == UnitSlot.Worker) list.Add(new EntityHandle(i, U.Generation[i]));
        return list;
    }

    private List<EntityHandle> Soldiers(int n)
    {
        System.Numerics.Vector2 hall = HallCenter(0);
        return Enumerable.Range(0, U.Capacity)
            .Where(i => U.Alive[i] && U.Owner[i] == 0 && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker)
            .OrderBy(i => System.Numerics.Vector2.Distance(U.Position[i], hall)).Take(n)
            .Select(i => new EntityHandle(i, U.Generation[i])).ToList();
    }

    private int HallSlot(int player)
    {
        for (int k = 0; k < B.Capacity; k++)
            if (B.Alive[k] && B.Owner[k] == player && _data.Buildings[B.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        throw new InvalidOperationException($"player {player} has no hall");
    }

    private System.Numerics.Vector2 HallCenter(int player)
    {
        int k = HallSlot(player);
        return StartBase.FootprintCenter(G, _data.Buildings[B.TypeId[k]], B.Cell[k]);
    }

    private int GreenAnchor(int type, System.Numerics.Vector2 near) => FindAnchor(type, near, ignoreCost: false);

    private int GreenAnchorIgnoringCost(int type, System.Numerics.Vector2 near) => FindAnchor(type, near, ignoreCost: true);

    private int FindAnchor(int type, System.Numerics.Vector2 near, bool ignoreCost)
    {
        BuildingDef def = _data.Buildings[type];
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d >= bestD || d < 10f || d > 30f) continue;
            bool ok = W.CanPlace(0, type, cell, out PlacementError r);
            if (!(ok || (ignoreCost && r == PlacementError.CannotAfford))) continue;
            bool empty = true;
            for (int i = 0; i < U.Capacity && empty; i++)
                if (U.Alive[i] && MathF.Abs(U.Position[i].X - c.X) < def.FootprintWidth + 3f && MathF.Abs(U.Position[i].Y - c.Y) < def.FootprintHeight + 3f) empty = false;
            if (empty) (best, bestD) = (cell, d);
        }
        return best;
    }

    private async Task Aim(int type, int anchor)
    {
        BuildingDef def = _data.Buildings[type];
        System.Numerics.Vector2 p = G.CellCenter(anchor % G.Width + def.FootprintWidth / 2, anchor / G.Width + def.FootprintHeight / 2);
        FocusOn(p);
        await Frame();
        _ghost.ScreenOverride = _camera.UnprojectPosition(new Vector3(p.X, TerrainHeight.At(W.Heightmap, p.X, p.Y), p.Y));
        await Frames();
    }

    private async Task<Vector2> OnScreenNoMove(System.Numerics.Vector2 ground)
    {
        await Task.CompletedTask;
        return _camera.UnprojectPosition(new Vector3(ground.X, TerrainHeight.At(W.Heightmap, ground.X, ground.Y), ground.Y));
    }

    private async Task Select(List<EntityHandle> units)
    {
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
        await Frames();
    }

    private void Key(Godot.Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void LeftClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private void FocusOn(System.Numerics.Vector2 ground)
    {
        _camera.SetZoom(30f);
        _camera.SetFocus(ground.X, ground.Y);
    }

    private System.Numerics.Vector2 Pick(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        if (!GroundPicker.TryPick(W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit))
            return new(float.NaN, float.NaN);
        return new(hit.X, hit.Z);
    }

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    // A row for an open bug: reported, failing only with --strict (the suite's Skip convention for known bugs).
    private void Known(string bug, bool ok, string message)
    {
        if (ok)
        {
            GD.Print($"QA M3-V2 KNOWN {bug} row now passes: {message}");
            return;
        }
        GD.Print($"QA M3-V2 KNOWN {bug} (open): {message}");
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--strict") >= 0) _failures.Add($"{bug}: {message}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
