using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-V31 (the Whirlwind's buildings as kit-bashed tents): the Producer's QA focus rows the developer's
/// <c>WhirlwindBuildingsTest</c> does not make.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V31Test.tscn</c>; prints
/// "QA M6-V31 TEST PASS" or FAIL lines. Every Whirlwind model type is found by its file (no building id literal). Rows:
/// <list type="bullet">
/// <item>(B) Behind a tent: three own Camp Followers 1 m behind (north of) each model, at zoom 20, 40 and 60: a click on one the
/// model hides wholly (every body point's ray meets the model first) selects the building; a click on one seen whole through
/// a gap or over the roof selects the Camp Follower (M6-VH9's HiddenByBuilding rule); a part-hidden one selects either.</item>
/// <item>(A) The air: every 8th pixel over each model's screen rectangle at zoom 40 whose ray enters the pick box but meets no
/// triangle (the open side of an awning, between a tent and its prop) selects the building when nothing else is drawn there
/// (M6-VH9: the air picks the building).</item>
/// <item>(D) Drag round a camp: six own Camp Followers round the Holy Camp; a drag over all of them at zoom 20, 40, 60 selects
/// the six and never the building; a drag inside the camp's screen rectangle that holds no unit selects nothing.</item>
/// <item>(R) The Holy Camp at the closest zoom: selected by a click on its model; its outline round the footprint, its front
/// bar not hidden by the model; its bar on screen over the drawn top; a rally point set south: the cone and line drawn, the line
/// starting outside the drawn model; an own Camp Follower in front selected gets its ring, the Holy Camp's outline goes.</item>
/// <item>(S) A slope: whether any anchor the sim accepts has uneven ground under the footprint; each Whirlwind model placed
/// there (if any) measured: the base against the ground at the plan's corners (floating or sunken).</item>
/// <item>(V) Read-only: the sim state hash is the same with the views synced and clicked as without (the rows only read).</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves <c>qa-m6v31-*</c> pictures: Ochre and Bone camps on sand and on
/// grass at zoom 40 and 20, and the selected Holy Camp with its rally line at the closest zoom.
/// </remarks>
public partial class QaM6V31Test : Node
{
    private const string Tag = "QA M6-V31 TEST";

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private int _whirlwind;
    private string? _shots;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless") { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
        UnitViews.UseModels = _shots != null;
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = "res://assets/models/";
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _whirlwind = _data.FindFaction("whirlwind");
            int[] types = ModelTypes(_whirlwind);
            GD.Print($"{types.Length} Whirlwind model types");
            if (Check(types.Length >= 4, $"only {types.Length} Whirlwind building types have a model file"))
            {
                await BehindRows(types);
                await AirRows(types);
                await DragRows();
                await ClosestZoomRows();
                await SlopeRows(types);
                await ReadOnlyRow(types);
                if (_shots != null) await Pictures();
            }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private int[] ModelTypes(int faction)
    {
        var list = new List<int>();
        for (int t = 0; t < _data.Buildings.Length; t++)
            if (_data.Buildings[t].Faction == faction && ResourceLoader.Exists(BuildingModels.PathFor(_data, _data.Buildings[t]))) list.Add(t);
        return list.ToArray();
    }

    // ---- (B) ----

    private async Task BehindRows(int[] types)
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "whirlwind", "--faction", "2", "whirlwind", "--resources", "high");
        KeepAlive(0, new NVector2(-30f, 90f));
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int worker = UnitOf(UnitSlot.Worker);
        int wholly = 0, seen = 0, partly = 0;
        for (int i = 0; i < types.Length; i++)
        {
            int type = types[i];
            BuildingDef def = _data.Buildings[type];
            if (_views.Models is not BuildingModels models || !models.Has(type)) { Check(false, $"(B) {def.Key}: no model in the match"); continue; }
            int slot = Spawn(0, type, block + new NVector2(-40f + 26f * (i % 3), -20f + 26f * (i / 3)));
            if (!Check(slot >= 0, $"(B) {def.Key}: not spawned")) continue;
            NVector2 c = Centre(slot);
            var gs = new List<int>();
            for (int k = -1; k <= 1; k++)
            {
                int g = SpawnUnit(0, worker, c + new NVector2(1.4f * k * def.FootprintWidth / 2f, -(def.FootprintHeight * MapConstants.CellSize / 2f + 1.0f)));
                if (g >= 0) gs.Add(g);
            }
            if (!Check(gs.Count > 0, $"(B) {def.Key}: no Camp Follower behind")) continue;
            _views.Sync(W);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                foreach (int g in gs)
                {
                    NVector2 up = W.Units.Position[g];
                    float gy = TerrainHeight.At(W.Heightmap, up.X, up.Y);
                    float uh = UnitViews.DrawnHeight(_data.Units[W.Units.TypeId[g]]);
                    int hidden = 0, samples = 0;
                    bool topHidden = false;
                    for (int s = 0; s <= 8; s++)
                    {
                        Vector3 pt = new(up.X, gy + uh * s / 8f, up.Y);
                        Vector2 p = _camera.UnprojectPosition(pt);
                        Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p);
                        samples++;
                        bool h = _views.ModelRayDistance(slot, o, d) < (pt - o).Length() - 0.05f;
                        if (h) hidden++;
                        if (s == 8) topHidden = h;
                    }
                    _sel.ClearBuilding();
                    _sel.Selection.Clear();
                    if (!_sel.TryScreenPosition(g, out Vector2 at)) continue;
                    Vector3 ro = _camera.ProjectRayOrigin(at), rd = _camera.ProjectRayNormal(at);
                    Vector3 bc = new(up.X, gy + uh / 2f, up.Y);
                    bool modelFirst = _views.ModelRayDistance(slot, ro, rd) < (bc - ro).Length() - 0.5f;
                    Click(at);
                    bool unitPicked = _sel.Selection.Contains(new EntityHandle(g, W.Units.Generation[g]));
                    string what = unitPicked ? "the Camp Follower" : _sel.SelectedBuilding == slot ? "the building" : $"building {_sel.SelectedBuilding}, {_sel.Selection.Count} units";
                    GD.Print($"  (B) {def.Key} zoom {zoom}: Camp Follower at x {up.X - c.X:0.#}: {hidden} of {samples} body points hidden, model {(modelFirst ? "in front of" : "not in front of")} its centre; click selected {what}");
                    if (hidden == samples || (modelFirst && topHidden))
                    {
                        wholly++;
                        Check(!unitPicked && _sel.SelectedBuilding == slot, $"(B) {def.Key} zoom {zoom}: a click where the model hides a Camp Follower ({hidden}/{samples}) selected {what}");
                    }
                    else if (!modelFirst && hidden == 0)
                    {
                        seen++;
                        Check(unitPicked, $"(B) {def.Key} zoom {zoom}: a click on a Camp Follower seen whole behind the model selected {what}");
                    }
                    else
                    {
                        partly++;
                        Check(unitPicked || _sel.SelectedBuilding == slot, $"(B) {def.Key} zoom {zoom}: a click on a part-hidden Camp Follower selected {what}");
                    }
                }
            }
            foreach (int g in gs) if (W.Units.Alive[g]) W.Units.Free(new EntityHandle(g, W.Units.Generation[g]));
            _sim.Tick();
            _views.Sync(W);
        }
        GD.Print($"(B) cases: {wholly} wholly hidden (the building), {seen} seen whole (the unit), {partly} part hidden");
        Check(wholly > 0 && seen > 0, $"(B) the rows did not reach both cases (hidden {wholly}, seen {seen})");
        await EndMatch();
    }

    // ---- (A) ----

    private async Task AirRows(int[] types)
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "whirlwind", "--faction", "2", "whirlwind", "--resources", "high");
        KeepAlive(0, new NVector2(-30f, 90f));
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int totalAir = 0;
        for (int i = 0; i < types.Length; i++)
        {
            int type = types[i];
            BuildingDef def = _data.Buildings[type];
            int slot = Spawn(0, type, block + new NVector2(-40f + 26f * (i % 3), -20f + 26f * (i / 3)));
            if (!Check(slot >= 0, $"(A) {def.Key}: not spawned")) continue;
            NVector2 c = Centre(slot);
            _views.Sync(W);
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            Rect2 r = ScreenRect(slot);
            int air = 0, airPicked = 0, drawn = 0, drawnPicked = 0;
            for (float y = r.Position.Y; y <= r.End.Y; y += 8f)
                for (float x = r.Position.X; x <= r.End.X; x += 8f)
                {
                    var p = new Vector2(x, y);
                    Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                    int on = _sel.BuildingOnRay(W, null, -1, o, d, out float t);
                    if (on != slot) continue;
                    bool isAir = t == SelectionController.AirDistance;
                    _sel.ClearBuilding();
                    _sel.Selection.Clear();
                    Click(p);
                    bool picked = _sel.SelectedBuilding == slot;
                    if (isAir) { air++; if (picked) airPicked++; }
                    else { drawn++; if (picked) drawnPicked++; }
                }
            totalAir += air;
            GD.Print($"  (A) {def.Key} at zoom {CameraLimits.DefaultZoom}: {airPicked} of {air} air pixels and {drawnPicked} of {drawn} model pixels select it");
            Check(airPicked == air, $"(A) {def.Key}: {airPicked} of {air} air pixels (an awning's open side, between tent and prop) select it");
            Check(drawn > 0 && drawnPicked == drawn, $"(A) {def.Key}: {drawnPicked} of {drawn} model pixels select it");
        }
        Check(totalAir > 0, "(A) no air pixel met over any model");
        await EndMatch();
    }

    // ---- (D) ----

    private async Task DragRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "whirlwind", "--faction", "2", "whirlwind", "--resources", "high");
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int camp = BuildingOf(BuildingSlot.TownHall);
        int slot = Spawn(0, camp, block + new NVector2(-30f, 0f));
        if (!Check(slot >= 0 && _views.Models!.Has(camp), "(D) no Holy Camp model")) { await EndMatch(); return; }
        BuildingDef def = _data.Buildings[camp];
        NVector2 c = Centre(slot);
        float hw = def.FootprintWidth * MapConstants.CellSize / 2f + 1.2f, hd = def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f;
        var gs = new List<int>();
        foreach (NVector2 off in new NVector2[] { new(-hw, -hd), new(hw, -hd), new(-hw, hd), new(hw, hd), new(0f, hd), new(0f, -hd) })
        {
            int g = SpawnUnit(0, UnitOf(UnitSlot.Worker), c + off);
            if (g >= 0) gs.Add(g);
        }
        Check(gs.Count == 6, $"(D) {gs.Count} of 6 Camp Followers spawned");
        _views.Sync(W);
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (int g in gs)
            {
                if (!_sel.TryScreenPosition(g, out Vector2 p)) continue;
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
            }
            _sel.SelectBuilding(slot);
            int n = _sel.BoxSelect(new Vector2(x0 - 20f, y0 - 40f), new Vector2(x1 + 20f, y1 + 20f), add: false);
            int mine = 0;
            foreach (int g in gs) if (_sel.Selection.Contains(new EntityHandle(g, W.Units.Generation[g]))) mine++;
            GD.Print($"  (D) zoom {zoom}: a drag round the Holy Camp selected {n} units ({mine} of the {gs.Count}), building {_sel.SelectedBuilding}");
            Check(mine == gs.Count && n == gs.Count && _sel.SelectedBuilding < 0, $"(D) zoom {zoom}: a drag round the Holy Camp selected {mine} of {gs.Count} (total {n}), building {_sel.SelectedBuilding}");
            // A drag inside the camp's drawn rectangle, clear of every unit: nothing.
            Rect2 r = ScreenRect(slot);
            Vector2 a = r.Position + r.Size * 0.35f, b = r.Position + r.Size * 0.65f;
            bool clear = true;
            foreach (int g in gs)
                if (_sel.TryScreenPosition(g, out Vector2 p) && new Rect2(a, b - a).Grow(12f).HasPoint(p)) clear = false;
            if (clear)
            {
                int m = _sel.BoxSelect(a, b, add: false);
                Check(m == 0 && _sel.SelectedBuilding < 0, $"(D) zoom {zoom}: a drag inside the Holy Camp selected {m} units, building {_sel.SelectedBuilding}");
            }
        }
        await EndMatch();
    }

    // ---- (R) ----

    private async Task ClosestZoomRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "whirlwind", "--faction", "2", "whirlwind", "--resources", "high");
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int camp = BuildingOf(BuildingSlot.TownHall);
        KeepAlive(1, new NVector2(40f, 90f)); // a seat with nothing is out, and an ended match takes no rally order
        int slot = Spawn(0, camp, block + new NVector2(-30f, 0f));
        if (!Check(slot >= 0 && _views.Models!.Has(camp), "(R) no Holy Camp model")) { await EndMatch(); return; }
        BuildingDef def = _data.Buildings[camp];
        NVector2 c = Centre(slot);
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
        _views.Sync(W);
        _camera.SetZoom(CameraLimits.MinZoom);
        _camera.SetFocus(c.X, c.Y);
        await Frames(2);
        Rect2 screen = GetViewport().GetVisibleRect();
        // Selected by a click on its model.
        Vector2 mid = _camera.UnprojectPosition(new Vector3(c.X, ground + _views.Models!.Top(camp) * 0.4f, c.Y));
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        Click(mid);
        Check(_sel.SelectedBuilding == slot, $"(R) a click on the Holy Camp's middle at zoom {CameraLimits.MinZoom} selected building {_sel.SelectedBuilding}");
        await Frames(2);
        // Its outline: round the footprint, the front bar's middle in front of the model on its ray.
        BuildingOutline outline = _match.GetNode<BuildingOutline>("World3D/BuildingOutline");
        Check(outline.ShownSlot == slot && outline.Visible, $"(R) the outline is on {outline.ShownSlot}, visible {outline.Visible}");
        float half = def.FootprintHeight * MapConstants.CellSize / 2f + BuildingOutline.Thickness / 2f;
        Vector3 front = new(c.X, ground + BuildingOutline.Lift, c.Y + half);
        Vector2 fp = _camera.UnprojectPosition(front);
        Vector3 fo = _camera.ProjectRayOrigin(fp), fd = _camera.ProjectRayNormal(fp);
        bool frontHidden = _views.ModelRayDistance(slot, fo, fd) < (front - fo).Length() - 0.05f;
        GD.Print($"(R) the outline's front bar at screen {fp}: {(frontHidden ? "hidden by" : "in front of")} the model; on screen {screen.HasPoint(fp)}");
        Check(!frontHidden && screen.HasPoint(fp), $"(R) the outline's front bar hidden {frontHidden}, on screen {screen.HasPoint(fp)}");
        // The bar over the drawn top, on screen.
        float barHalf = def.FootprintWidth * MapConstants.CellSize * BuildingViews.BarLengthShare / 2f;
        Vector3 bl = new(c.X - barHalf, ground + _views.BarY(slot), c.Y + _views.BarZ(slot)), br = new(c.X + barHalf, ground + _views.BarY(slot), c.Y + _views.BarZ(slot));
        bool barOn = screen.HasPoint(_camera.UnprojectPosition(bl)) && screen.HasPoint(_camera.UnprojectPosition(br));
        GD.Print($"(R) bar at {_views.BarY(slot):0.###} m (drawn top {_views.Models.Top(camp):0.###} m), front offset {_views.BarZ(slot):0.##}, on screen {barOn}");
        Check(barOn && (_views.BarZ(slot) != 0f || _views.BarY(slot) - BuildingViews.BarThickness / 2f > _views.Models.Top(camp)), "(R) the bar is off screen or under the drawn top");
        // A rally point south: cone and line drawn, the line from outside the drawn model.
        NVector2 rally = c + new NVector2(3f, def.FootprintHeight * MapConstants.CellSize / 2f + 6f);
        Check(_sel.RallyOrder(rally), "(R) the rally order was not enqueued");
        _sim.Tick();
        _sim.Tick(); // as Spawn's two ticks
        RallyMarker marker = _match.GetNode<RallyMarker>("World3D/RallyMarker");
        marker.Sync(W);
        await Frames(2);
        GD.Print($"(R) rally: has rally {B.HasRally[slot]} at {B.RallyPosition[slot]}, selected finished {_sel.SelectedFinishedBuilding}, marker visible {marker.Visible} on {marker.ShownSlot}");
        Check(marker.ShownSlot == slot && marker.LineShown, $"(R) rally marker on {marker.ShownSlot}, line {marker.LineShown}");
        Aabb drawn = _views.ModelOf(slot)!.GlobalTransform * _views.ModelOf(slot)!.Mesh.GetAabb();
        Vector2 conePx = _camera.UnprojectPosition(marker.Cone.GlobalPosition);
        GD.Print($"(R) rally cone at screen {conePx} (on screen {screen.HasPoint(conePx)}); drawn model plan z to {drawn.End.Z - c.Y:0.###} m of the footprint's {def.FootprintHeight * MapConstants.CellSize / 2f} m");
        Check(drawn.End.Z <= c.Y + def.FootprintHeight * MapConstants.CellSize / 2f - BuildingModels.FitMargin + 1e-3f, "(R) the drawn model reaches past its margin, over the rally line's start");
        // An own Camp Follower in front, selected: its ring, and the camp's outline goes.
        int g = SpawnUnit(0, UnitOf(UnitSlot.Worker), c + new NVector2(-2f, def.FootprintHeight * MapConstants.CellSize / 2f + 1.5f));
        if (Check(g >= 0, "(R) no Camp Follower in front"))
        {
            _views.Sync(W);
            await Frames(2);
            _sel.TryScreenPosition(g, out Vector2 gp);
            Click(gp);
            await Frames(2);
            SelectionRings rings = _match.GetNode<SelectionRings>("World3D/SelectionRings");
            bool picked = _sel.Selection.Contains(new EntityHandle(g, W.Units.Generation[g]));
            GD.Print($"(R) a click on the Camp Follower in front at zoom {CameraLimits.MinZoom}: selected {picked}, rings {rings.ShownCount}, outline on {outline.ShownSlot}");
            Check(picked && rings.ShownCount == 1 && rings.ShownRadius(0) > 0f && outline.ShownSlot < 0, $"(R) the Camp Follower in front: selected {picked}, rings {rings.ShownCount}, outline {outline.ShownSlot}");
        }
        await EndMatch();
    }

    // ---- (S) ----

    private async Task SlopeRows(int[] types)
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "whirlwind", "--faction", "2", "whirlwind", "--resources", "high");
        KeepAlive(0, new NVector2(-30f, 90f));
        NavGrid g = W.NavGrid;
        int tested = 0, uneven = 0;
        foreach (int type in types)
        {
            BuildingDef def = _data.Buildings[type];
            // The accepted anchor with the most uneven ground under the footprint (sampled at the centres of its half cells, inside it).
            int best = -1;
            float bestSpread = 0f;
            for (int cy = 1; cy + def.FootprintHeight < g.Height - 1; cy++)
                for (int cx = 1; cx + def.FootprintWidth < g.Width - 1; cx++)
                {
                    int a = cy * g.Width + cx;
                    if (!B.Fits(type, a)) continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int sy = 0; sy < def.FootprintHeight * 2; sy++)
                        for (int sx = 0; sx < def.FootprintWidth * 2; sx++)
                        {
                            float h = TerrainHeight.At(W.Heightmap, (cx + (sx + 0.5f) / 2f) * MapConstants.CellSize, (cy + (sy + 0.5f) / 2f) * MapConstants.CellSize);
                            lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                        }
                    if (hi - lo > bestSpread) { bestSpread = hi - lo; best = a; }
                }
            tested++;
            if (best < 0 || bestSpread < 0.02f)
            {
                GD.Print($"  (S) {def.Key}: no accepted anchor has uneven ground under it (largest spread {bestSpread:0.###} m)");
                continue;
            }
            uneven++;
            int slot = SpawnAt(0, type, best);
            if (!Check(slot >= 0, $"(S) {def.Key}: the sim refused the uneven anchor it Fits")) continue;
            _views.Sync(W);
            MeshInstance3D m = _views.ModelOf(slot)!;
            Aabb box = m.GlobalTransform * m.Mesh.GetAabb();
            float worstFloat = 0f, worstSink = 0f;
            foreach ((float x, float z) in new[] { (box.Position.X, box.Position.Z), (box.End.X, box.Position.Z), (box.Position.X, box.End.Z), (box.End.X, box.End.Z) })
            {
                float gh = TerrainHeight.At(W.Heightmap, x, z);
                worstFloat = Math.Max(worstFloat, box.Position.Y - gh);
                worstSink = Math.Max(worstSink, gh - box.Position.Y);
            }
            GD.Print($"  (S) {def.Key} on an anchor with {bestSpread:0.###} m of spread: the base floats up to {worstFloat:0.###} m and sinks up to {worstSink:0.###} m at the plan's corners");
        }
        GD.Print($"(S) {uneven} of {tested} types had an accepted uneven anchor");
        await EndMatch();
    }

    // ---- (V) ----

    private async Task ReadOnlyRow(int[] types)
    {
        // Two matches, one seed and the same spawns; one is synced, framed and clicked over its models, the other is not.
        ulong[] hash = new ulong[2];
        for (int run = 0; run < 2; run++)
        {
            StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "whirlwind", "--faction", "2", "whirlwind", "--resources", "high");
            NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
            var slots = new List<int>();
            for (int i = 0; i < types.Length; i++)
            {
                int s = Spawn(0, types[i], block + new NVector2(-40f + 26f * (i % 3), -20f + 26f * (i / 3)));
                if (s >= 0) slots.Add(s);
            }
            if (run == 1)
            {
                _views.Sync(W);
                foreach (int s in slots)
                {
                    NVector2 c = Centre(s);
                    _camera.SetFocus(c.X, c.Y);
                    await Frames(1);
                    Vector2 p = _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(W.Heightmap, c.X, c.Y) + 1f, c.Y));
                    Click(p);
                    _sel.BoxSelect(p - new Vector2(80f, 80f), p + new Vector2(80f, 80f), add: false);
                }
            }
            for (int t = 0; t < 40; t++) _sim.Tick();
            hash[run] = _sim.StateHash();
            await EndMatch();
        }
        GD.Print($"(V) state hash without the view's reads {hash[0]:X16}, with {hash[1]:X16}");
        Check(hash[0] == hash[1], "(V) syncing and clicking the models changed the sim state hash");
    }

    // ---- pictures (windowed) ----

    private async Task Pictures()
    {
        // Ochre (2) for player 1 and Bone (8) for player 2: a Holy Camp, a Raider Camp and a Tent each, on the low ground and on a terrace.
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "whirlwind", "--faction", "2", "whirlwind",
            "--colour", "1", "2", "--colour", "2", "8");
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit;
        int[] kinds = { BuildingOf(BuildingSlot.TownHall), BuildingOf(BuildingSlot.InfantryHall), BuildingOf(BuildingSlot.House) };
        for (int level = 0; level <= 1; level++)
        {
            int a0 = LevelAnchor(kinds[0], level, 30);
            if (a0 < 0) { GD.Print($"picture: no room on level {level}"); continue; }
            NVector2 o = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[kinds[0]], a0);
            float x = 0f;
            for (int p = 0; p < 2; p++)
                foreach (int k in kinds)
                {
                    Spawn(p, k, o + new NVector2(x, 0f));
                    x += _data.Buildings[k].FootprintWidth * MapConstants.CellSize + 3f;
                }
            _views.Sync(W);
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(o.X + x / 2f - 4f, o.Y + 2f);
            await Shot($"qa-m6v31-ochre-bone-level{level}-40m");
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(o.X + x / 2f - 4f, o.Y + 2f);
            await Shot($"qa-m6v31-ochre-bone-level{level}-20m");
        }
        await EndMatch();

        // The Holy Camp selected with a rally line, and a Camp Follower beside it, at the closest zoom.
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "whirlwind", "--faction", "2", "whirlwind", "--colour", "1", "3");
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit;
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int camp = BuildingOf(BuildingSlot.TownHall);
        KeepAlive(1, new NVector2(40f, 90f));
        int slot = Spawn(0, camp, block + new NVector2(-30f, 0f));
        if (slot >= 0)
        {
            NVector2 c = Centre(slot);
            SpawnUnit(0, UnitOf(UnitSlot.Worker), c + new NVector2(-5.5f, 5.5f));
            _sel.SelectBuilding(slot);
            _sel.RallyOrder(c + new NVector2(3f, 10f));
            _sim.Tick();
            _sim.Tick();
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X, c.Y + 3f);
            await Shot("qa-m6v31-holy-camp-selected-rally-20m");
        }
        await EndMatch();
    }

    // ---- helpers ----

    private int LevelAnchor(int type, int level, int room)
    {
        BuildingDef def = _data.Buildings[type];
        NavGrid g = W.NavGrid;
        for (int cy = 4; cy + def.FootprintHeight < g.Height - 4; cy++)
            for (int cx = 4; cx + room < g.Width - 4; cx++)
            {
                bool ok = true;
                for (int y = cy - 1; y <= cy + def.FootprintHeight && ok; y++)
                    for (int x = cx - 1; x <= cx + room && ok; x++)
                        if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                if (ok && B.Fits(type, cy * g.Width + cx)) return cy * g.Width + cx;
            }
        return -1;
    }

    private void KeepAlive(int player, NVector2 spot)
    {
        NVector2 at = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + spot;
        Check(Spawn(player, BuildingOf(BuildingSlot.TownHall), at) >= 0, $"no keep-alive hall for player {player}");
    }

    private int UnitOf(UnitSlot slot)
    {
        for (int u = 0; u < _data.Units.Length; u++) if (_data.Units[u].Faction == _whirlwind && _data.Units[u].Slot == slot) return u;
        return -1;
    }

    private int BuildingOf(BuildingSlot slot)
    {
        for (int b = 0; b < _data.Buildings.Length; b++) if (_data.Buildings[b].Faction == _whirlwind && _data.Buildings[b].Slot == slot) return b;
        return -1;
    }

    private void StartMatch(params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _views = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _sim.Tick();
        _sim.Tick();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frames(1);
    }

    private int Spawn(int player, int type, NVector2 near)
    {
        if (type < 0) return -1;
        int anchor = OpenAnchor(_data.Buildings[type], near);
        return anchor < 0 ? -1 : SpawnAt(player, type, anchor);
    }

    private int SpawnAt(int player, int type, int anchor)
    {
        _sim.Enqueue(Command.SpawnBuilding(player, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        _sim.Tick();
        _sim.Tick();
        int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        return slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type ? slot : -1;
    }

    private int SpawnUnit(int player, int type, NVector2 at)
    {
        UnitStore u = W.Units;
        var alive = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) alive[i] = u.Alive[i];
        _sim.Enqueue(Command.SpawnUnit(player, type, at));
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && !alive[i]) return i;
        return -1;
    }

    private int OpenAnchor(BuildingDef def, NVector2 near)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int r = 0; r <= 14; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (!g.WorldToCell(near + new NVector2(dx, dy) * MapConstants.CellSize, out int cx, out int cy)) continue;
                    if (cx < 3 || cy < 3 || cx + def.FootprintWidth >= g.Width - 3 || cy + def.FootprintHeight >= g.Height - 3) continue;
                    int a = cy * g.Width + cx;
                    if (!B.Fits(type, a)) continue;
                    int level = g.LevelAt(cx, cy);
                    bool ok = true;
                    for (int y = cy - 1; y <= cy + def.FootprintHeight && ok; y++)
                        for (int x = cx - 1; x <= cx + def.FootprintWidth && ok; x++)
                            if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                    if (ok) return a;
                }
        return -1;
    }

    private NVector2 Centre(int slot) => StartBase.FootprintCenter(W.NavGrid, _data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    // The drawn model's screen rectangle, clamped to the screen.
    private Rect2 ScreenRect(int slot)
    {
        MeshInstance3D m = _views.ModelOf(slot)!;
        Aabb box = m.GlobalTransform * m.Mesh.GetAabb();
        Rect2 screen = GetViewport().GetVisibleRect();
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            Vector2 s = _camera.UnprojectPosition(box.GetEndpoint(k));
            x0 = Math.Min(x0, s.X); y0 = Math.Min(y0, s.Y); x1 = Math.Max(x1, s.X); y1 = Math.Max(y1, s.Y);
        }
        x0 = Math.Max(x0, 1f); y0 = Math.Max(y0, 1f); x1 = Math.Min(x1, screen.Size.X - 2f); y1 = Math.Min(y1, screen.Size.Y - 2f);
        return new Rect2(x0, y0, Math.Max(0f, x1 - x0), Math.Max(0f, y1 - y0));
    }

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Shot(string name)
    {
        await Frames(6);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        string path = Path.Combine(_shots!, name + ".png");
        GD.Print($"screenshot {path} ({img.SavePng(path)})");
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
