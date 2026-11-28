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
/// QA for M6-V32 (the last six box buildings and the Stone Cairn as models): the Producer's QA focus rows the developer's
/// <c>LastBuildingsTest</c> does not make.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V32Test.tscn</c>; prints
/// "QA M6-V32 TEST PASS" or FAIL lines. The seven types are named by id (QA's choice: the task's own list). Rows, per faction
/// (Malazan, Teblor) of the seven:
/// <list type="bullet">
/// <item>(B) Behind: three own workers 1 m behind (north of) each model, at zoom 20, 40 and 60: one the model hides wholly
/// selects the building, one seen whole (through a gap, over a fence, past the tower) selects the worker, a part-hidden one
/// either (M6-VH9's HiddenByBuilding rule).</item>
/// <item>(A) Air and gaps: every 8th pixel over each model's screen rectangle at zoom 40 and 20 whose ray enters the pick box
/// selects the building (between a fence rail and the building, through the open stable, the rocks' gaps), model pixels
/// too.</item>
/// <item>(I) Inside: a worker spawned on a paddock / yard spot inside the footprint; where the sim puts it, and a click on it
/// at zoom 40 selects what the (B) rule says.</item>
/// <item>(D) Drag round each type: six own workers round it; a drag over all of them at zoom 20, 40, 60 selects the six and
/// never the building; a drag inside its drawn rectangle clear of units selects nothing.</item>
/// <item>(R) Each at the closest zoom: a click on its model selects it; the outline's front bar in front of the model; the bar on
/// screen over the drawn top; a rally set south (where it trains): the line drawn and the model inside its margin; a worker in
/// front selected gets its ring.</item>
/// <item>(S) A slope: each type on the accepted anchor with the most uneven ground: the base against the ground at the plan's
/// corners (reported).</item>
/// <item>(V) Read-only: the sim state hash is the same with the views synced and clicked as without.</item>
/// </list>
/// </remarks>
public partial class QaM6V32Test : Node
{
    private const string Tag = "QA M6-V32 TEST";
    private static readonly string[] Ids =
    {
        "malazan_wickan_corral", "malazan_armory", "malazan_cadre_tower", "malazan_engineers_yard",
        "teblor_stables", "teblor_breakers_hall", "teblor_stone_cairn",
    };

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private int _faction;
    private string _key = "";

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    private string? _shots;

    public override async void _Ready()
    {
        string[] cl = OS.GetCmdlineUserArgs();
        int sa = Array.IndexOf(cl, "--shots");
        if (sa >= 0 && sa + 1 < cl.Length && DisplayServer.GetName() != "headless") { _shots = cl[sa + 1]; Directory.CreateDirectory(_shots); }
        bool onlyShots = Array.IndexOf(cl, "--shots-only") >= 0 && _shots != null;
        UnitViews.UseModels = false;
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = "res://assets/models/";
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            var all = new List<int>();
            foreach (string id in Ids)
            {
                int t = _data.FindBuilding(id);
                if (Check(t >= 0, $"{id} is not in the data")) all.Add(t);
            }
            if (_shots != null) await Pictures(all);
            if (!onlyShots)
            foreach (string fk in new[] { "malazan", "teblor" })
            {
                _faction = _data.FindFaction(fk);
                _key = fk;
                int[] types = all.FindAll(t => _data.Buildings[t].Faction == _faction).ToArray();
                GD.Print($"== {fk}: {types.Length} types");
                if (Row(cl, "B")) await BehindRows(types);
                if (Row(cl, "A")) await AirRows(types);
                if (Row(cl, "I")) await InsideRows(types);
                if (Row(cl, "D")) await DragRows(types);
                if (Row(cl, "R")) await ClosestZoomRows(types);
                if (Row(cl, "S")) await SlopeRows(types);
                if (Row(cl, "V")) await ReadOnlyRow(types);
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

    // `-- --rows BAR` runs only the named rows (all by default).
    private static bool Row(string[] args, string r)
    {
        int i = Array.IndexOf(args, "--rows");
        return i < 0 || i + 1 >= args.Length || args[i + 1].Contains(r);
    }

    private void Start() =>
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", _key, "--faction", "2", _key, "--resources", "high");

    private NVector2 Spot(NVector2 block, int i) => block + new NVector2(-44f + 28f * (i % 3), -24f + 28f * (i / 3));

    // A click on a unit: where every body point's ray meets the model first the building is wanted, where none does the
    // unit, else either. Returns the case (0 hidden, 1 seen, 2 partly) and checks it.
    private int ClickCase(string what, int slot, int g)
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
        if (!_sel.TryScreenPosition(g, out Vector2 at)) return -1;
        Vector3 ro = _camera.ProjectRayOrigin(at), rd = _camera.ProjectRayNormal(at);
        Vector3 bc = new(up.X, gy + uh / 2f, up.Y);
        bool modelFirst = _views.ModelRayDistance(slot, ro, rd) < (bc - ro).Length() - 0.5f;
        Click(at);
        bool unitPicked = _sel.Selection.Contains(new EntityHandle(g, W.Units.Generation[g]));
        string got = unitPicked ? "the worker" : _sel.SelectedBuilding == slot ? "the building" : $"building {_sel.SelectedBuilding}, {_sel.Selection.Count} units";
        GD.Print($"  {what}: {hidden} of {samples} body points hidden, model {(modelFirst ? "in front of" : "not in front of")} its centre; click selected {got}");
        if (hidden == samples || (modelFirst && topHidden))
        {
            Check(!unitPicked && _sel.SelectedBuilding == slot, $"{what}: a click where the model hides the worker ({hidden}/{samples}) selected {got}");
            return 0;
        }
        if (!modelFirst && hidden == 0)
        {
            Check(unitPicked, $"{what}: a click on a worker seen whole selected {got}");
            return 1;
        }
        Check(unitPicked || _sel.SelectedBuilding == slot, $"{what}: a click on a part-hidden worker selected {got}");
        return 2;
    }

    // ---- (B) ----

    private async Task BehindRows(int[] types)
    {
        Start();
        KeepAlive(0, new NVector2(-30f, 90f));
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int worker = UnitOf(UnitSlot.Worker);
        int[] cases = new int[3];
        for (int i = 0; i < types.Length; i++)
        {
            int type = types[i];
            BuildingDef def = _data.Buildings[type];
            if (_views.Models is not BuildingModels models || !models.Has(type)) { Check(false, $"(B) {def.Key}: no model in the match"); continue; }
            int slot = Spawn(0, type, Spot(block, i));
            if (!Check(slot >= 0, $"(B) {def.Key}: not spawned")) continue;
            NVector2 c = Centre(slot);
            var gs = new List<int>();
            for (int k = -1; k <= 1; k++)
            {
                int g = SpawnUnit(0, worker, c + new NVector2(1.4f * k * def.FootprintWidth / 2f, -(def.FootprintHeight * MapConstants.CellSize / 2f + 1.0f)));
                if (g >= 0) gs.Add(g);
            }
            if (!Check(gs.Count > 0, $"(B) {def.Key}: no worker behind")) continue;
            _views.Sync(W);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                foreach (int g in gs)
                {
                    int k = ClickCase($"(B) {def.Key} zoom {zoom}, worker at x {W.Units.Position[g].X - c.X:0.#}", slot, g);
                    if (k >= 0) cases[k]++;
                }
            }
            foreach (int g in gs) if (W.Units.Alive[g]) W.Units.Free(new EntityHandle(g, W.Units.Generation[g]));
            _sim.Tick();
            _views.Sync(W);
        }
        GD.Print($"(B) {_key} cases: {cases[0]} wholly hidden, {cases[1]} seen whole, {cases[2]} part hidden");
        await EndMatch();
    }

    // ---- (A) ----

    private async Task AirRows(int[] types)
    {
        Start();
        KeepAlive(0, new NVector2(-30f, 90f));
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        for (int i = 0; i < types.Length; i++)
        {
            int type = types[i];
            BuildingDef def = _data.Buildings[type];
            int slot = Spawn(0, type, Spot(block, i));
            if (!Check(slot >= 0, $"(A) {def.Key}: not spawned")) continue;
            NVector2 c = Centre(slot);
            _views.Sync(W);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                Rect2 r = ScreenRect(slot);
                float step = zoom == CameraLimits.MinZoom ? 14f : 8f;
                int air = 0, airPicked = 0, drawn = 0, drawnPicked = 0, outside = 0, outsideOther = 0;
                for (float y = r.Position.Y; y <= r.End.Y; y += step)
                    for (float x = r.Position.X; x <= r.End.X; x += step)
                    {
                        var p = new Vector2(x, y);
                        Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                        int on = _sel.BuildingOnRay(W, null, -1, o, d, out float t);
                        _sel.ClearBuilding();
                        _sel.Selection.Clear();
                        Click(p);
                        bool picked = _sel.SelectedBuilding == slot;
                        if (on != slot) { outside++; if (picked) outsideOther++; continue; }
                        if (t == SelectionController.AirDistance) { air++; if (picked) airPicked++; }
                        else { drawn++; if (picked) drawnPicked++; }
                    }
                GD.Print($"  (A) {def.Key} at zoom {zoom}: {airPicked} of {air} air pixels and {drawnPicked} of {drawn} model pixels select it; {outsideOther} of {outside} pixels off its pick box select it");
                Check(airPicked == air, $"(A) {def.Key} zoom {zoom}: {airPicked} of {air} air pixels (a gap in its pick box) select it");
                Check(drawn > 0 && drawnPicked == drawn, $"(A) {def.Key} zoom {zoom}: {drawnPicked} of {drawn} model pixels select it");
                Check(outsideOther == 0, $"(A) {def.Key} zoom {zoom}: {outsideOther} pixels whose ray misses its pick box select it");
            }
        }
        await EndMatch();
    }

    // ---- (I) ----

    private async Task InsideRows(int[] types)
    {
        Start();
        KeepAlive(0, new NVector2(-30f, 90f));
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int worker = UnitOf(UnitSlot.Worker);
        for (int i = 0; i < types.Length; i++)
        {
            int type = types[i];
            BuildingDef def = _data.Buildings[type];
            int slot = Spawn(0, type, Spot(block, i));
            if (!Check(slot >= 0, $"(I) {def.Key}: not spawned")) continue;
            NVector2 c = Centre(slot);
            float hw = def.FootprintWidth * MapConstants.CellSize / 2f, hd = def.FootprintHeight * MapConstants.CellSize / 2f;
            // Four spots inside the footprint: a paddock / yard corner each.
            foreach (NVector2 off in new NVector2[] { new(hw * 0.6f, hd * 0.6f), new(-hw * 0.6f, hd * 0.6f), new(hw * 0.6f, -hd * 0.3f), new(0f, 0f) })
            {
                int g = SpawnUnit(0, worker, c + off);
                if (g < 0) { GD.Print($"  (I) {def.Key}: the sim refused a worker at {off}"); continue; }
                NVector2 up = W.Units.Position[g];
                bool inside = Math.Abs(up.X - c.X) < hw && Math.Abs(up.Y - c.Y) < hd;
                GD.Print($"  (I) {def.Key}: a worker asked at {off} stands at {up - c} ({(inside ? "inside" : "outside")} the footprint)");
                Check(float.IsFinite(up.X) && float.IsFinite(up.Y), $"(I) {def.Key}: worker position not finite");
                _views.Sync(W);
                _camera.SetZoom(CameraLimits.DefaultZoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                ClickCase($"(I) {def.Key} zoom {CameraLimits.DefaultZoom}, worker {(inside ? "inside" : "outside")} at {up - c}", slot, g);
                W.Units.Free(new EntityHandle(g, W.Units.Generation[g]));
                _sim.Tick();
                _views.Sync(W);
            }
        }
        await EndMatch();
    }

    // ---- (D) ----

    private async Task DragRows(int[] types)
    {
        Start();
        KeepAlive(0, new NVector2(-30f, 90f));
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        for (int i = 0; i < types.Length; i++)
        {
            int type = types[i];
            BuildingDef def = _data.Buildings[type];
            int slot = Spawn(0, type, Spot(block, i));
            if (!Check(slot >= 0, $"(D) {def.Key}: not spawned")) continue;
            NVector2 c = Centre(slot);
            float hw = def.FootprintWidth * MapConstants.CellSize / 2f + 1.2f, hd = def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f;
            var gs = new List<int>();
            foreach (NVector2 off in new NVector2[] { new(-hw, -hd), new(hw, -hd), new(-hw, hd), new(hw, hd), new(0f, hd), new(0f, -hd) })
            {
                int g = SpawnUnit(0, UnitOf(UnitSlot.Worker), c + off);
                if (g >= 0) gs.Add(g);
            }
            Check(gs.Count == 6, $"(D) {def.Key}: {gs.Count} of 6 workers spawned");
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
                GD.Print($"  (D) {def.Key} zoom {zoom}: a drag round it selected {n} units ({mine} of the {gs.Count}), building {_sel.SelectedBuilding}");
                Check(mine == gs.Count && n == gs.Count && _sel.SelectedBuilding < 0, $"(D) {def.Key} zoom {zoom}: a drag round it selected {mine} of {gs.Count} (total {n}), building {_sel.SelectedBuilding}");
                Rect2 r = ScreenRect(slot);
                Vector2 a = r.Position + r.Size * 0.35f, b = r.Position + r.Size * 0.65f;
                bool clear = true;
                foreach (int g in gs)
                    if (_sel.TryScreenPosition(g, out Vector2 p) && new Rect2(a, b - a).Grow(12f).HasPoint(p)) clear = false;
                if (clear)
                {
                    int m = _sel.BoxSelect(a, b, add: false);
                    Check(m == 0 && _sel.SelectedBuilding < 0, $"(D) {def.Key} zoom {zoom}: a drag inside it selected {m} units, building {_sel.SelectedBuilding}");
                }
            }
            foreach (int g in gs) if (W.Units.Alive[g]) W.Units.Free(new EntityHandle(g, W.Units.Generation[g]));
            _sim.Tick();
            _views.Sync(W);
        }
        await EndMatch();
    }

    // ---- (R) ----

    private async Task ClosestZoomRows(int[] types)
    {
        foreach (int type in types)
        {
            Start();
            NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
            KeepAlive(0, new NVector2(-30f, 90f));
            KeepAlive(1, new NVector2(40f, 90f));
            BuildingDef def = _data.Buildings[type];
            int slot = -1;
            foreach (NVector2 off in new NVector2[] { new(-30f, 0f), new(-30f, 24f), new(-50f, -20f), new(0f, -30f), new(-60f, 30f) })
                if (slot < 0) slot = Spawn(0, type, block + off);
            GD.Print($"(R) {def.Key}: slot {slot}, player 0 defeated {W.IsDefeated(0)}, has model {_views.Models?.Has(type)}");
            if (!Check(slot >= 0 && _views.Models!.Has(type), $"(R) {def.Key}: not spawned as a model")) { await EndMatch(); continue; }
            NVector2 c = Centre(slot);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
            _views.Sync(W);
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            Rect2 screen = GetViewport().GetVisibleRect();
            Vector2 mid = _camera.UnprojectPosition(new Vector3(c.X, ground + _views.Models!.Top(type) * 0.4f, c.Y));
            _sel.ClearBuilding();
            _sel.Selection.Clear();
            Click(mid);
            Check(_sel.SelectedBuilding == slot, $"(R) {def.Key}: a click on its middle at zoom {CameraLimits.MinZoom} selected building {_sel.SelectedBuilding}");
            await Frames(2);
            BuildingOutline outline = _match.GetNode<BuildingOutline>("World3D/BuildingOutline");
            Check(outline.ShownSlot == slot && outline.Visible, $"(R) {def.Key}: the outline is on {outline.ShownSlot}, visible {outline.Visible}");
            float half = def.FootprintHeight * MapConstants.CellSize / 2f + BuildingOutline.Thickness / 2f;
            Vector3 front = new(c.X, ground + BuildingOutline.Lift, c.Y + half);
            Vector2 fp = _camera.UnprojectPosition(front);
            Vector3 fo = _camera.ProjectRayOrigin(fp), fd = _camera.ProjectRayNormal(fp);
            bool frontHidden = _views.ModelRayDistance(slot, fo, fd) < (front - fo).Length() - 0.05f;
            Check(!frontHidden && screen.HasPoint(fp), $"(R) {def.Key}: the outline's front bar hidden {frontHidden}, on screen {screen.HasPoint(fp)}");
            float barHalf = def.FootprintWidth * MapConstants.CellSize * BuildingViews.BarLengthShare / 2f;
            Vector3 bl = new(c.X - barHalf, ground + _views.BarY(slot), c.Y + _views.BarZ(slot)), br = new(c.X + barHalf, ground + _views.BarY(slot), c.Y + _views.BarZ(slot));
            bool barOn = screen.HasPoint(_camera.UnprojectPosition(bl)) && screen.HasPoint(_camera.UnprojectPosition(br));
            // The bar must not be hidden by the model: its middle's ray meets the model after the bar.
            Vector3 bm = new(c.X, ground + _views.BarY(slot), c.Y + _views.BarZ(slot));
            Vector2 bmp = _camera.UnprojectPosition(bm);
            Vector3 bo = _camera.ProjectRayOrigin(bmp), bd = _camera.ProjectRayNormal(bmp);
            bool barHidden = _views.ModelRayDistance(slot, bo, bd) < (bm - bo).Length() - 0.05f;
            GD.Print($"(R) {def.Key}: bar at {_views.BarY(slot):0.###} m (drawn top {_views.Models.Top(type):0.###} m), front offset {_views.BarZ(slot):0.##}, on screen {barOn}, hidden by the model {barHidden}; outline front hidden {frontHidden}");
            Check(barOn && !barHidden && (_views.BarZ(slot) != 0f || _views.BarY(slot) - BuildingViews.BarThickness / 2f > _views.Models.Top(type)), $"(R) {def.Key}: the bar is off screen, hidden or under the drawn top");
            NVector2 rally = c + new NVector2(3f, def.FootprintHeight * MapConstants.CellSize / 2f + 6f);
            bool rallied = _sel.RallyOrder(rally);
            _sim.Tick();
            _sim.Tick();
            RallyMarker marker = _match.GetNode<RallyMarker>("World3D/RallyMarker");
            marker.Sync(W);
            await Frames(2);
            GD.Print($"(R) {def.Key}: rally order {rallied}, has rally {B.HasRally[slot]}, marker on {marker.ShownSlot}, line {marker.LineShown}");
            if (B.HasRally[slot]) Check(marker.ShownSlot == slot && marker.LineShown, $"(R) {def.Key}: rally set but marker on {marker.ShownSlot}, line {marker.LineShown}");
            Aabb drawn = _views.ModelOf(slot)!.GlobalTransform * _views.ModelOf(slot)!.Mesh.GetAabb();
            Check(drawn.End.Z <= c.Y + def.FootprintHeight * MapConstants.CellSize / 2f - BuildingModels.FitMargin + 1e-3f, $"(R) {def.Key}: the drawn model reaches past its margin");
            int g = SpawnUnit(0, UnitOf(UnitSlot.Worker), c + new NVector2(-2f, def.FootprintHeight * MapConstants.CellSize / 2f + 1.5f));
            if (Check(g >= 0, $"(R) {def.Key}: no worker in front"))
            {
                _views.Sync(W);
                await Frames(2);
                _sel.TryScreenPosition(g, out Vector2 gp);
                Click(gp);
                await Frames(2);
                SelectionRings rings = _match.GetNode<SelectionRings>("World3D/SelectionRings");
                bool picked = _sel.Selection.Contains(new EntityHandle(g, W.Units.Generation[g]));
                Check(picked && rings.ShownCount == 1 && rings.ShownRadius(0) > 0f && outline.ShownSlot < 0, $"(R) {def.Key}: the worker in front: selected {picked}, rings {rings.ShownCount}, outline {outline.ShownSlot}");
            }
            await EndMatch();
        }
    }

    // ---- (S) ----

    private async Task SlopeRows(int[] types)
    {
        Start();
        KeepAlive(0, new NVector2(-30f, 90f));
        NavGrid g = W.NavGrid;
        foreach (int type in types)
        {
            BuildingDef def = _data.Buildings[type];
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
            if (best < 0 || bestSpread < 0.02f)
            {
                GD.Print($"  (S) {def.Key}: no accepted anchor has uneven ground under it (largest spread {bestSpread:0.###} m)");
                continue;
            }
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
        await EndMatch();
    }

    // ---- (V) ----

    private async Task ReadOnlyRow(int[] types)
    {
        ulong[] hash = new ulong[2];
        for (int run = 0; run < 2; run++)
        {
            Start();
            NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
            var slots = new List<int>();
            for (int i = 0; i < types.Length; i++)
            {
                int s = Spawn(0, types[i], Spot(block, i));
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
        GD.Print($"(V) {_key}: state hash without the view's reads {hash[0]:X16}, with {hash[1]:X16}");
        Check(hash[0] == hash[1], $"(V) {_key}: syncing and clicking the models changed the sim state hash");
    }

    // ---- pictures and luminance (windowed) ----

    private async Task Pictures(List<int> all)
    {
        int malazan = _data.FindFaction("malazan"), teblor = _data.FindFaction("teblor");
        // Each type in Ochre (2) and Jade (5), on level 0 (grass) and level 1 (terrace / sand), zoom 40 and 20; beside it the
        // look-alike to tell apart (Cadre Tower: the Malazan Watchtower; Stables: the Teblor Hut; Cairn: the Teblor Watchtower if any).
        foreach (string fk in new[] { "malazan", "teblor" })
        {
            _faction = _data.FindFaction(fk);
            _key = fk;
            int[] types = all.FindAll(t => _data.Buildings[t].Faction == _faction).ToArray();
            for (int level = 0; level <= 1; level++)
            {
                StartMatch("--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", fk, "--faction", "2", fk, "--colour", "1", "2", "--colour", "2", "5");
                KeepAlive(0, new NVector2(-30f, 90f));
                KeepAlive(1, new NVector2(30f, 90f));
                foreach (int type in types)
                {
                    int a0 = LevelAnchor(type, level, 26);
                    if (a0 < 0) { GD.Print($"picture: {_data.Buildings[type].Key}: no room on level {level}"); continue; }
                    NVector2 o = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[type], a0);
                    int s0 = Spawn(0, type, o);
                    int s1 = Spawn(1, type, o + new NVector2(_data.Buildings[type].FootprintWidth * MapConstants.CellSize + 3f, 0f));
                    int tower = BuildingOf(BuildingSlot.WatchTower);
                    if (tower >= 0 && tower != type) Spawn(0, tower, o + new NVector2(-(_data.Buildings[type].FootprintWidth * MapConstants.CellSize + 3f), 0f));
                    int worker = UnitOf(UnitSlot.Worker);
                    SpawnUnit(0, worker, o + new NVector2(0f, _data.Buildings[type].FootprintHeight * MapConstants.CellSize / 2f + 1.5f));
                    _views.Sync(W);
                    _camera.SetZoom(CameraLimits.DefaultZoom);
                    _camera.SetFocus(o.X + 3f, o.Y + 2f);
                    await Shot($"qa-m6v32-{_data.Buildings[type].Key}-level{level}-40m");
                    _camera.SetZoom(CameraLimits.MinZoom);
                    _camera.SetFocus(o.X, o.Y + 1f);
                    await Shot($"qa-m6v32-{_data.Buildings[type].Key}-level{level}-20m");
                    // Selected, a rally south, at the closest zoom: the bar, the outline, the rally line.
                    if (s0 >= 0)
                    {
                        _sel.SelectBuilding(s0);
                        _sel.RallyOrder(o + new NVector2(2f, _data.Buildings[type].FootprintHeight * MapConstants.CellSize / 2f + 6f));
                        _sim.Tick();
                        _sim.Tick();
                        _match.GetNode<RallyMarker>("World3D/RallyMarker").Sync(W);
                        _camera.SetFocus(o.X, o.Y + 2f);
                        await Shot($"qa-m6v32-{_data.Buildings[type].Key}-level{level}-selected-20m");
                        _sel.ClearBuilding();
                    }
                }
                await EndMatch();
            }
        }
        // The Cairn and the Armory in all eight colours, each player's beside its own Town Hall and House.
        foreach (int type in all)
        {
            BuildingDef def = _data.Buildings[type];
            bool cairn = def.Attack != null, armory = def.Slot == BuildingSlot.Forge && def.Faction == malazan;
            if (!cairn && !armory) continue;
            _faction = def.Faction;
            _key = _data.Factions[def.Faction].Key;
            foreach (int first in new[] { 1, 5 })
            {
                StartMatch("--players", "4", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", _key, "--faction", "2", _key,
                    "--faction", "3", _key, "--faction", "4", _key, "--colour", "1", $"{first}", "--colour", "2", $"{first + 1}",
                    "--colour", "3", $"{first + 2}", "--colour", "4", $"{first + 3}");
                NVector2 row = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-36f, -14f);
                for (int p = 0; p < 4; p++)
                {
                    Spawn(p, BuildingOf(BuildingSlot.TownHall), row + new NVector2(p * 14f - 10f, 70f));
                    Spawn(p, type, row + new NVector2(p * 12f, 0f));
                    Spawn(p, BuildingOf(BuildingSlot.House), row + new NVector2(p * 12f, 9f));
                }
                _views.Sync(W);
                _camera.SetZoom(CameraLimits.DefaultZoom);
                _camera.SetFocus(row.X + 18f, row.Y + 5f);
                await Shot($"qa-m6v32-{def.Key}-colours-{first}-{first + 3}-40m");
                await EndMatch();
            }
            for (int c = 1; c <= UiText.ColourCount; c++) await Luma(type, c);
        }
    }

    // One colour's luminance (Rec. 709 on the picture's sRGB values, 0-255), measured independently of LastBuildingsTest's tile
    // classifier: model pixels are those whose ray meets the model; team pixels are model pixels that change when the same
    // spot is drawn in two other colours; the rest are non-team. Prints QALUMA with modes and medians.
    private async Task Luma(int type, int colour)
    {
        BuildingDef def = _data.Buildings[type];
        int[] others = { colour % 8 + 1, (colour + 3) % 8 + 1 };
        Image[] imgs = new Image[3];
        bool[,]? model = null;
        Rect2 r = default;
        int[] cs = { colour, others[0], others[1] };
        for (int k = 0; k < 3; k++)
        {
            int other = cs[k] % 8 + 1 == colour ? (cs[k] + 1) % 8 + 1 : cs[k] % 8 + 1;
            StartMatch("--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", _key, "--faction", "2", _key,
                "--colour", "1", $"{cs[k]}", "--colour", "2", $"{other}");
            NVector2 spot = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, -10f);
            Spawn(0, BuildingOf(BuildingSlot.TownHall), spot + new NVector2(0f, 60f));
            Spawn(1, BuildingOf(BuildingSlot.TownHall), spot + new NVector2(-20f, 60f));
            int slot = Spawn(0, type, spot);
            if (!Check(slot >= 0, $"(luma) {def.Key}: not spawned")) { await EndMatch(); return; }
            NVector2 at = Centre(slot);
            _views.Sync(W);
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(at.X, at.Y + 2f);
            imgs[k] = await Shot(k == 0 ? $"qa-m6v32-{def.Key}-colour{colour}-40m" : null);
            if (k == 0)
            {
                r = ScreenRect(slot);
                model = new bool[(int)r.Size.X + 1, (int)r.Size.Y + 1];
                for (int y = 0; y <= (int)r.Size.Y; y++)
                    for (int x = 0; x <= (int)r.Size.X; x++)
                    {
                        var p = new Vector2(r.Position.X + x + 0.5f, r.Position.Y + y + 0.5f);
                        model[x, y] = float.IsFinite(_views.ModelRayDistance(slot, _camera.ProjectRayOrigin(p), _camera.ProjectRayNormal(p)));
                    }
            }
            await EndMatch();
        }
        int[] team = new int[256], rest = new int[256];
        for (int y = 0; y <= (int)r.Size.Y; y++)
            for (int x = 0; x <= (int)r.Size.X; x++)
            {
                if (!model![x, y]) continue;
                int px = (int)r.Position.X + x, py = (int)r.Position.Y + y;
                Color a = imgs[0].GetPixel(px, py), b = imgs[1].GetPixel(px, py), c = imgs[2].GetPixel(px, py);
                float diff = Math.Max(Dist(a, b), Dist(a, c));
                int l = Math.Clamp((int)Math.Round(255f * (0.2126f * a.R + 0.7152f * a.G + 0.0722f * a.B)), 0, 255);
                if (diff > 0.06f) team[l]++; else rest[l]++;
            }
        int tn = 0, rn = 0;
        foreach (int v in team) tn += v;
        foreach (int v in rest) rn += v;
        int tm = Mode(team), rm = Mode(rest), tmed = Median(team), rmed = Median(rest);
        string name = UiText.Shared!.ColourName(colour - 1);
        GD.Print($"QALUMA {def.Key} {name}: team mode {tm} median {tmed} ({tn} px); non-team mode {rm} median {rmed} ({rn} px); gap mode {Math.Abs(tm - rm)} median {Math.Abs(tmed - rmed)}");
    }

    private static float Dist(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static int Mode(int[] h)
    {
        int best = 0;
        for (int i = 1; i < h.Length; i++) if (h[i] > h[best]) best = i;
        return best;
    }

    private static int Median(int[] h)
    {
        int n = 0;
        foreach (int v in h) n += v;
        int seen = 0;
        for (int i = 0; i < h.Length; i++) { seen += h[i]; if (2 * seen >= n) return i; }
        return h.Length - 1;
    }

    private int LevelAnchor(int type, int level, int room)
    {
        BuildingDef def = _data.Buildings[type];
        NavGrid g = W.NavGrid;
        for (int cy = 4; cy + def.FootprintHeight + 8 < g.Height - 4; cy++)
            for (int cx = 12; cx + room < g.Width - 4; cx++)
            {
                bool ok = true;
                for (int y = cy - 1; y <= cy + def.FootprintHeight + 3 && ok; y++)
                    for (int x = cx - 10; x <= cx + room && ok; x++)
                        if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                if (ok && B.Fits(type, cy * g.Width + cx)) return cy * g.Width + cx;
            }
        return -1;
    }

    private async Task<Image> Shot(string? name)
    {
        await Frames(6);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        if (name != null)
        {
            string path = Path.Combine(_shots!, name + ".png");
            GD.Print($"screenshot {path} ({img.SavePng(path)})");
        }
        return img;
    }

    // ---- helpers ----

    private void KeepAlive(int player, NVector2 spot)
    {
        NVector2 at = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + spot;
        Check(Spawn(player, BuildingOf(BuildingSlot.TownHall), at) >= 0, $"no keep-alive hall for player {player}");
    }

    private int UnitOf(UnitSlot slot)
    {
        for (int u = 0; u < _data.Units.Length; u++) if (_data.Units[u].Faction == _faction && _data.Units[u].Slot == slot) return u;
        return -1;
    }

    private int BuildingOf(BuildingSlot slot)
    {
        for (int b = 0; b < _data.Buildings.Length; b++) if (_data.Buildings[b].Faction == _faction && _data.Buildings[b].Slot == slot) return b;
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
