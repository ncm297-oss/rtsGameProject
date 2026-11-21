using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-V26 (the first building models): attacks the developer's BuildingModelTest doesn't make.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V26Test.tscn</c>; prints
/// "QA M6-V26 TEST PASS" or "QA M6-V26 TEST FAIL: ..." lines. Rows for open bugs print "QA M6-V26 KNOWN BUG-nnnn" and fail
/// only with <c>-- --strict</c>. Rows:
/// <list type="bullet">
/// <item>Terraces: every anchor of each model type on the match map whose footprint borders another level or a ramp: the
/// ground under the fitted model's whole plan is the height its base stands at (no part sunk or floating).</item>
/// <item>Slot reuse: a model building destroyed and its slot taken by a box type before the view syncs draws the box (model
/// hidden, bar over the box); the reverse draws the model (box hidden, bar over the model).</item>
/// <item>A site of each model type razed while a site: its box until then, nothing after, no model.</item>
/// <item>Zero allocation with remembered models drawn (the developer's row ran with none drawn).</item>
/// <item>The bar on screen at the closest (BUG-1631) and farthest zoom, the camera focused on the building.</item>
/// <item>Picking (BUG-1630): a click on any pixel the fitted model covers, at the closest, default and farthest zoom, selects
/// the building; an own unit standing behind the Keep, hidden by it on screen, is not what a click there selects.</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves both types in Bone, Crimson, Iron and Jade at the default zoom,
/// and a selected Keep at the closest and farthest zoom.
/// </remarks>
public partial class QaM6V26Test : Node
{
    private const string Tag = "QA M6-V26";

    private readonly List<string> _failures = new();
    private bool _strict;
    private string? _shots;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _strict = Array.IndexOf(args, "--strict") >= 0;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
        if (_shots != null && DisplayServer.GetName() == "headless") _shots = null;
        UnitViews.UseModels = _shots != null;
        BuildingViews.UseModels = true;
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            GameData data = loaded.Data!;
            StartMatch(data, "--players", "3", "--faction", "1", "malazan", "--faction", "2", "malazan", "--faction", "3", "malazan", "--resources", "high");
            if (_views.Models is not BuildingModels models || models.ModelTypes == 0)
                Check(false, "no building models loaded");
            else
            {
                int[] types = ModelTypes(data);
                KeepAlive(data);
                TerraceRows(types);
                await ReuseRows(data, types);
                await SiteRazedRows(data, types);
                await GhostAllocationRow(data, types);
                await BarOnScreenRows(types);
                await PickRows(data, types);
            }
            await EndMatch();
            if (_shots != null) await Pictures(data);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        foreach (string f in _failures) GD.Print($"{Tag} TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private int[] ModelTypes(GameData data)
    {
        var list = new List<int>();
        for (int t = 0; t < data.Buildings.Length; t++) if (_views.Models!.Has(t)) list.Add(t);
        return list.ToArray();
    }

    // A Garrison Keep per player away from the rows (a box type such as the Armory can't be spawned before one), so razing a
    // row's building never eliminates its owner (whose commands then stop).
    private void KeepAlive(GameData data)
    {
        int keep = data.FindBuilding("malazan_garrison_keep");
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        for (int p = 0; p < 3; p++)
            Check(Spawn(p, keep, block + new NVector2(-30f + 14f * p, 44f)) >= 0, $"keep-alive Keep of player {p} not spawned");
    }

    // Every anchor whose footprint borders another level or a ramp: the ground under the fitted plan is flat at the base height.
    private void TerraceRows(int[] types)
    {
        NavGrid g = W.NavGrid;
        foreach (int type in types)
        {
            BuildingDef def = W.Data.Buildings[type];
            Aabb plan = _views.Models!.FittedBounds(type);
            int tested = 0;
            float worst = 0f;
            for (int cy = 1; cy + def.FootprintHeight < g.Height - 1; cy++)
                for (int cx = 1; cx + def.FootprintWidth < g.Width - 1; cx++)
                {
                    int a = cy * g.Width + cx;
                    if (!B.Fits(type, a) || !BordersStep(g, cx, cy, def)) continue;
                    tested++;
                    NVector2 c = StartBase.FootprintCenter(g, def, a);
                    float baseY = TerrainHeight.At(W.Heightmap, c.X, c.Y);
                    for (int sy = 0; sy <= 8; sy++)
                        for (int sx = 0; sx <= 8; sx++)
                        {
                            float x = c.X + plan.Position.X + plan.Size.X * sx / 8f, z = c.Y + plan.Position.Z + plan.Size.Z * sy / 8f;
                            worst = Math.Max(worst, Math.Abs(TerrainHeight.At(W.Heightmap, x, z) - baseY));
                        }
                }
            GD.Print($"  terraces {def.Key}: {tested} anchors beside a step or ramp, worst ground offset under the plan {worst:0.####} m");
            Check(tested > 0, $"{def.Key}: no anchor beside a step on this map");
            Check(worst < 0.01f, $"{def.Key}: the ground under the model's plan differs from its base by {worst} m (sunk or floating)");
        }
    }

    private static bool BordersStep(NavGrid g, int cx, int cy, BuildingDef def)
    {
        int level = g.LevelAt(cx, cy);
        for (int y = cy - 1; y <= cy + def.FootprintHeight; y++)
            for (int x = cx - 1; x <= cx + def.FootprintWidth; x++)
            {
                bool inside = x >= cx && x < cx + def.FootprintWidth && y >= cy && y < cy + def.FootprintHeight;
                if (!inside && g.InBounds(x, y) && g.LevelAt(x, y) != level) return true;
            }
        return false;
    }

    // A model slot freed and taken by a box type before the view syncs, and back.
    private async Task ReuseRows(GameData data, int[] types)
    {
        int malazan = data.FindFaction("malazan");
        int boxType = -1;
        for (int t = 0; t < data.Buildings.Length && boxType < 0; t++)
            if (data.Buildings[t].Faction == malazan && !_views.Models!.Has(t) && data.Buildings[t].FootprintWidth <= 3) boxType = t;
        if (!Check(boxType >= 0, "reuse: no box type")) return;
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int type = types[0];
        int slot = Spawn(0, type, near);
        if (!Check(slot >= 0, "reuse: model building not spawned")) return;
        _views.Sync(W);
        Check(_views.ShownAsModel(slot), "reuse: the model building not drawn as its model");
        Raze(slot);
        int reused = Spawn(0, boxType, near + new NVector2(0f, 20f));
        if (!Check(reused == slot, $"reuse: the box type took slot {reused}, not the freed {slot}")) return;
        _views.Sync(W);
        await Frames(1);
        MeshInstance3D? m = _views.ModelOf(slot);
        Check(_views.IsShown(slot) && !_views.ShownAsModel(slot) && _views.BoxOf(slot).Visible && (m == null || !m.Visible),
            $"reuse model -> box: model visible {m?.Visible}, box visible {_views.BoxOf(slot).Visible}");
        Check(Math.Abs(_views.BarY(slot) - BuildingViews.BarHeightOf(data.Buildings[boxType])) < 1e-4f,
            $"reuse model -> box: bar at {_views.BarY(slot)} m, want the box's {BuildingViews.BarHeightOf(data.Buildings[boxType])} m");
        Raze(slot);
        int back = Spawn(0, type, near);
        if (!Check(back == slot, $"reuse: the model type took slot {back}, not the freed {slot}")) return;
        _views.Sync(W);
        await Frames(1);
        m = _views.ModelOf(slot);
        Check(_views.ShownAsModel(slot) && m != null && m.Visible && !_views.BoxOf(slot).Visible,
            $"reuse box -> model: model visible {m?.Visible}, box visible {_views.BoxOf(slot).Visible}");
        Check(_views.BarY(slot) > _views.Models!.Top(type), $"reuse box -> model: bar at {_views.BarY(slot)} m under the model's top {_views.Models.Top(type)} m");
        GD.Print($"  reuse: slot {slot} model -> {data.Buildings[boxType].Key} box -> model, each drawn right");
        Raze(slot);
        _views.Sync(W);
    }

    // A site of each model type destroyed by attack while still a site: drawn as the rising box until then, nothing after,
    // and no model ever shown for it.
    private async Task SiteRazedRows(GameData data, int[] types)
    {
        int laborer = data.FindUnit("malazan_laborer");
        // Beside the reuse rows' spot, which player 0 has explored (a site needs explored ground).
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-14f, 18f);
        foreach (int type in types)
        {
            BuildingDef def = data.Buildings[type];
            int anchor = OpenAnchor(def, near);
            near += new NVector2(16f, 0f);
            if (!Check(anchor >= 0, $"{def.Key} razed site: no open anchor")) continue;
            NVector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor), at = c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 1f);
            _sim.Enqueue(Command.SpawnUnit(0, laborer, at));
            _sim.Tick();
            _sim.Tick();
            int builder = -1;
            for (int i = 0; i < W.Units.Capacity; i++)
                if (W.Units.Alive[i] && W.Units.Owner[i] == 0 && W.Units.TypeId[i] == laborer && NVector2.Distance(W.Units.Position[i], at) < 1.5f) builder = i;
            if (!Check(builder >= 0, $"{def.Key} razed site: no laborer")) continue;
            for (int t = 0; t < 10; t++) _sim.Tick(); // the Laborer's sight explores the footprint (a site needs explored ground)
            _sim.Enqueue(Command.Build(0, new EntityHandle(builder, W.Units.Generation[builder]), type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
            int site = -1;
            for (int t = 0; t < 400 && site < 0; t++)
            {
                _sim.Tick();
                int s = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
                if (s >= 0 && B.UnderConstruction[s]) site = s;
            }
            W.CanPlace(0, type, anchor, out PlacementError why);
            if (!Check(site >= 0, $"{def.Key} razed site: no site placed (placement {why}, builder state {W.Units.State[builder]}, gold {W.Gold[0]}, wood {W.Wood[0]})")) continue;
            for (int t = 0; t < 40; t++) _sim.Tick();
            _views.Sync(W);
            Check(_views.ShownAsSite(site) && _views.BoxOf(site).Visible && !_views.ShownAsModel(site), $"{def.Key} razed site: the site not drawn as its box");
            W.Units.Free(new EntityHandle(builder, W.Units.Generation[builder])); // staging: the builder goes so the site stays a site
            Raze(site);
            _views.Sync(W);
            await Frames(1);
            MeshInstance3D? m = _views.ModelOf(site);
            Check(!_views.IsShown(site) && !_views.ViewOf(site)!.Visible && (m == null || !m.Visible),
                $"{def.Key} razed site: still drawn (shown {_views.IsShown(site)}, model {m?.Visible})");
            GD.Print($"  {def.Key}: a site razed while a site leaves nothing drawn");
        }
    }

    // 20 syncs with remembered models drawn allocate nothing.
    private async Task GhostAllocationRow(GameData data, int[] types)
    {
        NVector2 far = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(50f, 0f);
        var enemy = new List<int>();
        float dx = 0f;
        foreach (int type in types)
        {
            int s = Spawn(1, type, far + new NVector2(dx, 0f));
            dx += data.Buildings[type].FootprintWidth * MapConstants.CellSize + 4f;
            if (s >= 0) enemy.Add(s);
        }
        if (!Check(enemy.Count == types.Length, "ghost allocation: enemy buildings not spawned")) return;
        NVector2 between = (Centre(enemy[0]) + Centre(enemy[^1])) / 2f + new NVector2(0f, 9f);
        int laborer = data.FindUnit("malazan_laborer");
        _sim.Enqueue(Command.SpawnUnit(0, laborer, between));
        _sim.Tick();
        _sim.Tick();
        int scout = -1;
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 0 && NVector2.Distance(W.Units.Position[i], between) < 1.5f) scout = i;
        if (!Check(scout >= 0, "ghost allocation: no scout")) return;
        for (int t = 0; t < 100 && !enemy.TrueForAll(s => W.Fog.CanSeeBuilding(0, s)); t++) _sim.Tick();
        _views.Sync(W);
        W.Units.Free(new EntityHandle(scout, W.Units.Generation[scout])); // staging between ticks: the scout goes, the spot falls out of sight
        for (int t = 0; t < 1500 && !enemy.TrueForAll(s => _views.GhostShownAsModel(s)); t++)
        {
            _sim.Tick();
            _views.Sync(W);
        }
        int ghosts = 0;
        foreach (int s in enemy) if (_views.GhostShownAsModel(s)) ghosts++;
        if (!Check(ghosts == enemy.Count, $"ghost allocation: {ghosts} of {enemy.Count} remembered as models")) return;
        _views.Sync(W);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 20; f++) _views.Sync(W);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GD.Print($"  ghost allocation: 20 syncs with {ghosts} remembered models drawn: {bytes} bytes");
        Check(bytes == 0, $"20 syncs with {ghosts} remembered models allocated {bytes} bytes");
        await Frames(1);
    }

    // The bar's both ends on screen at the closest and the farthest zoom, focus on the footprint's centre.
    private async Task BarOnScreenRows(int[] types)
    {
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(0f, 30f);
        foreach (int type in types)
        {
            int slot = Spawn(0, type, near);
            if (!Check(slot >= 0, $"bar: {W.Data.Buildings[type].Key} not spawned")) continue;
            near += new NVector2(16f, 0f);
            _views.Sync(W);
            NVector2 c = Centre(slot);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
            float half = W.Data.Buildings[type].FootprintWidth * MapConstants.CellSize * BuildingViews.BarLengthShare / 2f;
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(1);
                Rect2 screen = GetViewport().GetVisibleRect();
                Vector2 left = _camera.UnprojectPosition(new Vector3(c.X - half, ground + _views.BarY(slot), c.Y));
                Vector2 right = _camera.UnprojectPosition(new Vector3(c.X + half, ground + _views.BarY(slot), c.Y));
                GD.Print($"  bar {W.Data.Buildings[type].Key} at zoom {zoom}: {left} - {right} on a {screen.Size} screen");
                bool on = screen.HasPoint(left) && screen.HasPoint(right);
                string msg = $"{W.Data.Buildings[type].Key}: bar {(on ? "on" : "off")} screen at zoom {zoom}, focus on its centre ({left} - {right})";
                // BUG-1631: a model taller than the box it replaces (the 8.1 m Keep) has its bar over the top edge at the closest zoom.
                if (zoom == CameraLimits.MinZoom && _views.Models!.Top(type) > BuildingViews.DrawnHeight(W.Data.Buildings[type])) Known(on, "BUG-1631", msg);
                else Check(on, msg);
            }
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
    }

    // BUG-1630: every pixel of the drawn model picks the building; a unit hidden behind the Keep is not what a click there selects.
    private async Task PickRows(GameData data, int[] types)
    {
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, -20f);
        foreach (int type in types)
        {
            BuildingDef def = data.Buildings[type];
            int slot = Spawn(0, type, near);
            near += new NVector2(0f, 24f);
            if (!Check(slot >= 0, $"pick: {def.Key} not spawned")) continue;
            _views.Sync(W);
            MeshInstance3D m = _views.ModelOf(slot)!;
            Vector3[] faces = m.Mesh.GetFaces();
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                NVector2 c = Centre(slot);
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(1);
                (int hits, int picked) = Coverage(m, faces, slot);
                string msg = $"{def.Key} at zoom {zoom}: {picked} of {hits} sampled model pixels select it ({100f * picked / Math.Max(1, hits):0.#}%)";
                GD.Print($"  pick {msg}");
                Check(hits > 0, $"{def.Key} at zoom {zoom}: no model pixel sampled");
                // BUG-1630: picking still uses the box's height, so only a model taller than it (the Keep) misses.
                if (_views.Models!.Top(type) > BuildingViews.DrawnHeight(def)) Known(picked == hits, "BUG-1630", msg);
                else Check(picked == hits, msg);
            }
        }

        // An own Laborer just behind the Keep (north, away from the camera), hidden by its towers on screen.
        int keep = data.FindBuilding("malazan_garrison_keep");
        if (keep < 0 || !_views.Models!.Has(keep)) return;
        BuildingDef kd = data.Buildings[keep];
        int ks = Spawn(0, keep, near + new NVector2(0f, 6f));
        if (!Check(ks >= 0, "hidden unit: no Keep")) return;
        NVector2 kc = Centre(ks);
        NVector2 behind = kc + new NVector2(0f, -(kd.FootprintHeight * MapConstants.CellSize / 2f + 1.2f));
        _sim.Enqueue(Command.SpawnUnit(0, data.FindUnit("malazan_laborer"), behind));
        _sim.Tick();
        _sim.Tick();
        int unit = -1;
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 0 && NVector2.Distance(W.Units.Position[i], behind) < 1.5f) unit = i;
        if (!Check(unit >= 0, "hidden unit: no laborer")) return;
        _views.Sync(W);
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(kc.X, kc.Y);
        await Frames(2);
        MeshInstance3D km = _views.ModelOf(ks)!;
        NVector2 up = W.Units.Position[unit];
        float g = TerrainHeight.At(W.Heightmap, up.X, up.Y);
        Vector3 target = new(up.X, g + 0.9f, up.Y);
        Vector2 p = _camera.UnprojectPosition(target);
        Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p);
        float tModel = RayModel(km, km.Mesh.GetFaces(), o, d);
        float tUnit = (target - o).Length();
        bool hidden = tModel < tUnit;
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        Click(p);
        bool unitPicked = _sel.Selection.Contains(new EntityHandle(unit, W.Units.Generation[unit]));
        string what = $"a click on the Keep's tower where an own Laborer stands hidden behind it (model at {tModel:0.#} m, unit at {tUnit:0.#} m) selected "
                      + (unitPicked ? "the hidden Laborer" : _sel.SelectedBuilding == ks ? "the Keep" : $"building {_sel.SelectedBuilding}, {_sel.Selection.Count} units");
        GD.Print($"  hidden unit: {what}");
        Check(hidden, "hidden unit: the Laborer behind the Keep is not hidden by its model on screen (the row's setup)");
        if (hidden) Known(!unitPicked && _sel.SelectedBuilding == ks, "BUG-1630", what);
        _sel.ClearBuilding();
        _sel.Selection.Clear();
    }

    // Samples every 6th pixel over the model's screen rectangle: pixels whose ray meets the model, and of those the ones a click selects the building at.
    private (int Hits, int Picked) Coverage(MeshInstance3D m, Vector3[] faces, int slot)
    {
        Aabb box = m.GlobalTransform * m.Mesh.GetAabb();
        Rect2 screen = GetViewport().GetVisibleRect();
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            Vector2 s = _camera.UnprojectPosition(box.GetEndpoint(k));
            x0 = Math.Min(x0, s.X); y0 = Math.Min(y0, s.Y); x1 = Math.Max(x1, s.X); y1 = Math.Max(y1, s.Y);
        }
        x0 = Math.Max(x0, 1f); y0 = Math.Max(y0, 1f); x1 = Math.Min(x1, screen.Size.X - 2f); y1 = Math.Min(y1, screen.Size.Y - 2f);
        int hits = 0, picked = 0;
        for (float y = y0; y <= y1; y += 6f)
            for (float x = x0; x <= x1; x += 6f)
            {
                var p = new Vector2(x, y);
                if (RayModel(m, faces, _camera.ProjectRayOrigin(p), _camera.ProjectRayNormal(p)) == float.MaxValue) continue;
                hits++;
                _sel.ClearBuilding();
                _sel.Selection.Clear();
                Click(p);
                if (_sel.SelectedBuilding == slot) picked++;
            }
        _sel.ClearBuilding();
        return (hits, picked);
    }

    // The world distance along the ray to the model's nearest triangle, or float.MaxValue.
    private static float RayModel(MeshInstance3D m, Vector3[] faces, Vector3 o, Vector3 d)
    {
        Transform3D inv = m.GlobalTransform.AffineInverse();
        Vector3 lo = inv * o, ld = inv.Basis * d;
        float best = float.MaxValue;
        for (int i = 0; i + 2 < faces.Length; i += 3)
        {
            Vector3 a = faces[i], e1 = faces[i + 1] - a, e2 = faces[i + 2] - a;
            Vector3 pv = ld.Cross(e2);
            float det = e1.Dot(pv);
            if (Math.Abs(det) < 1e-9f) continue;
            float inv2 = 1f / det;
            Vector3 tv = lo - a;
            float u = tv.Dot(pv) * inv2;
            if (u < 0f || u > 1f) continue;
            Vector3 qv = tv.Cross(e1);
            float v = ld.Dot(qv) * inv2;
            if (v < 0f || u + v > 1f) continue;
            float t = e2.Dot(qv) * inv2;
            if (t > 0f && t < best) best = t;
        }
        if (best == float.MaxValue) return best;
        return (m.GlobalTransform * (lo + ld * best) - o).Length();
    }

    // Kills a building by attack (16 Heavy Infantry of player 2) without syncing the view; the attackers go after.
    private void Raze(int slot)
    {
        int gen = B.Generation[slot];
        BuildingDef def = W.Data.Buildings[B.TypeId[slot]];
        NVector2 c = Centre(slot);
        int heavy = W.Data.FindUnit("malazan_heavy_infantry");
        float r = def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f;
        for (int k = 0; k < 16; k++) _sim.Enqueue(Command.SpawnUnit(2, heavy, c + new NVector2(-6f + 0.8f * k, k % 2 == 0 ? r : -r)));
        _sim.Tick();
        _sim.Tick();
        var attackers = new List<int>();
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 2 && NVector2.Distance(W.Units.Position[i], c) < r + 6f) attackers.Add(i);
        foreach (int u in attackers) _sim.Enqueue(Command.Attack(2, new EntityHandle(u, W.Units.Generation[u]), new EntityHandle(slot, gen), isBuilding: true));
        for (int t = 0; t < 8000 && B.Alive[slot] && B.Generation[slot] == gen; t++) _sim.Tick();
        Check(!(B.Alive[slot] && B.Generation[slot] == gen), $"raze: slot {slot} not destroyed");
        foreach (int u in attackers) if (W.Units.Alive[u]) W.Units.Free(new EntityHandle(u, W.Units.Generation[u]));
        _sim.Tick();
    }

    // Windowed: both types in Bone, Crimson, Iron and Jade at the default zoom; a selected Keep at the closest and farthest zoom.
    private async Task Pictures(GameData data)
    {
        StartMatch(data, "--players", "4", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "malazan", "--faction", "2", "malazan",
            "--faction", "3", "malazan", "--faction", "4", "malazan", "--colour", "1", "8", "--colour", "2", "3", "--colour", "3", "1", "--colour", "4", "5");
        int[] types = ModelTypes(data);
        NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, -10f);
        var all = new List<NVector2>();
        float z = 0f;
        int firstKeep = -1;
        foreach (int type in types)
        {
            float x = 0f;
            for (int p = 0; p < 4; p++)
            {
                int slot = Spawn(p, type, o + new NVector2(x, z));
                if (slot >= 0)
                {
                    all.Add(Centre(slot));
                    if (firstKeep < 0 && data.Buildings[type].Key == "malazan_garrison_keep") firstKeep = slot;
                }
                x += data.Buildings[type].FootprintWidth * MapConstants.CellSize + 3f;
            }
            z += data.Buildings[type].FootprintHeight * MapConstants.CellSize + 5f;
        }
        int laborer = data.FindUnit("malazan_laborer");
        foreach (NVector2 c in all) _sim.Enqueue(Command.SpawnUnit(0, laborer, c + new NVector2(0f, -5.5f)));
        _sim.Tick();
        _sim.Tick();
        if (all.Count > 0)
        {
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus((all[0].X + all[^1].X) / 2f + 6f, (all[0].Y + all[^1].Y) / 2f);
            await Shot("qa-bone-crimson-iron-jade-40m");
        }
        if (firstKeep >= 0)
        {
            NVector2 c = Centre(firstKeep);
            float g = TerrainHeight.At(W.Heightmap, c.X, c.Y);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                _sel.ClearBuilding();
                Click(_camera.UnprojectPosition(new Vector3(c.X, g + 1f, c.Y)));
                await Shot($"qa-selected-keep-{zoom}m");
            }
        }
        await EndMatch();
    }

    private void StartMatch(GameData data, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(data, LaunchOptions.Parse(args.ToArray()));
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
        BuildingDef def = W.Data.Buildings[type];
        int anchor = OpenAnchor(def, near);
        if (anchor < 0) return -1;
        _sim.Enqueue(Command.SpawnBuilding(player, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        _sim.Tick();
        _sim.Tick();
        int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        return slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type ? slot : -1;
    }

    private int OpenAnchor(BuildingDef def, NVector2 near)
    {
        int type = W.Data.FindBuilding(def.Key);
        for (int r = 0; r <= 10; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    NVector2 p = near + new NVector2(dx, dy) * MapConstants.CellSize;
                    if (!W.NavGrid.WorldToCell(p, out int cx, out int cy)) continue;
                    int a = cy * W.NavGrid.Width + cx;
                    if (cx < 2 || cy < 2 || cx + def.FootprintWidth >= W.NavGrid.Width - 2 || cy + def.FootprintHeight >= W.NavGrid.Height - 2) continue;
                    if (!B.Fits(type, a) || !FitsWithMargin(def, cx, cy)) continue;
                    return a;
                }
        return -1;
    }

    // Two free cells round the footprint, all on one level, so neighbours and units never touch it.
    private bool FitsWithMargin(BuildingDef def, int cx, int cy)
    {
        int level = W.NavGrid.LevelAt(cx, cy);
        for (int y = cy - 2; y <= cy + def.FootprintHeight + 1; y++)
            for (int x = cx - 2; x <= cx + def.FootprintWidth + 1; x++)
                if (B.SlotAt(x, y) >= 0 || !W.NavGrid.IsPassable(x, y) || W.NavGrid.LevelAt(x, y) != level) return false;
        return true;
    }

    private NVector2 Centre(int slot) => StartBase.FootprintCenter(W.NavGrid, W.Data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Shot(string name)
    {
        Directory.CreateDirectory(_shots!);
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

    private void Known(bool ok, string bug, string message)
    {
        if (ok)
        {
            GD.Print($"{Tag} KNOWN {bug} row now passes: {message}");
            return;
        }
        GD.Print($"{Tag} KNOWN {bug} (open): {message}");
        if (_strict) _failures.Add($"{bug}: {message}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
