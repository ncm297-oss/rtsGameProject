using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V32: the last box types as models (the Malazan Wickan Corral, Armory, Cadre Tower and Engineers' Yard, the Teblor
/// Stables &amp; Kennels and Breaker's Hall) and the Stone Cairn rebuilt as stacked rocks: the rows <c>BuildingModelTest</c>
/// (files, the triangles per tile, life cycle, team colour, for every manifest building) doesn't make for them. The pattern
/// of <c>WhirlwindBuildingsTest</c>, run once per faction the types belong to.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/LastBuildingsTest.tscn</c>; prints
/// "LAST BUILDINGS TEST PASS" or FAIL lines. The types are the manifest's building entries that carry a <c>kit</c> outside the
/// Whirlwind (whose ten are <c>WhirlwindBuildingsTest</c>'s): no count or building id literal. Rows:
/// <list type="bullet">
/// <item>(M) Each model (criterion 3): at most 5,000 triangles, one surface, the one <c>team</c> material, an atlas of 64 x 64 or
/// 128 x 128.</item>
/// <item>(H) The fit (M6-V26's): drawn exactly the plan of the fit recomputed from the file and its height x the data
/// <c>scale</c> (the Teblor 1.25), inside its footprint by <see cref="BuildingModels.FitMargin"/>, its base on the ground.</item>
/// <item>(T) Each model at a terrace's foot and on its edge: inside its footprint by the margin, its base on the ground, the
/// ground flat under its plan, the drawn height the fit's x <c>scale</c>, its bar over the drawn top.</item>
/// <item>(P) Its faction's worker in front of each is what a click on it selects at zoom 20, 40 and 60; every 12th pixel of
/// the drawn model at zoom 40 selects the building; the bar's both ends on screen at the closest zoom over the drawn top.</item>
/// <item>(S) A site is its box until finished: per faction, a worker builds the first new type that needs no tech (its
/// required buildings spawned first); while a site it is drawn as its rising box with its bar over the box, and on the first
/// sync after the tick it is finished it is the model. The tower among the types (the Stone Cairn) shoots from
/// <see cref="BuildingViews.ModelShotShare"/> of its drawn top.</item>
/// <item>(F) With models off and with the files missing a finished building is its box; a Git LFS pointer in place of one
/// file gives that box and exactly one warning (the loop reads the log for <c>ERROR</c>).</item>
/// <item>(3) A Malazan, a Whirlwind and a Teblor seat in one match with every building type of each: drawn as its model
/// exactly when its file is there, and every manifest building is drawn as a model.</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>m6v32-&lt;id&gt;-40m</c> / <c>-20m</c> for each type (its worker and line
/// unit before it), <c>m6v32-&lt;faction&gt;-base-40m</c> / <c>-40m-gray</c> (a whole Malazan and a whole Teblor base),
/// <c>m6v32-&lt;id&gt;-colours-1-4-40m</c> / <c>-5-8-40m</c> for the Stone Cairn and the Armory (the Malazan building measured), and
/// <c>m6v32-&lt;id&gt;-&lt;colour&gt;-40m</c> (each colour alone on one spot; criterion 7's luminance table printed as <c>LUMA</c> lines).
/// </remarks>
public partial class LastBuildingsTest : Node
{
    private const string Tag = "LAST BUILDINGS TEST";

    // docs/04's building budget and the two atlas sizes the builder makes (a 2 x 2 or 4 x 4 grid of 32-texel tiles).
    private const int TriangleBudget = 5000;
    private static readonly int[] AtlasSizes = { 64, 128 };

    private readonly List<string> _failures = new();
    private string? _shots;
    private string _scratch = "";
    private GameData _data = null!;
    private JsonArray _entries = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private int _faction; // the faction of the current match's rows

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        _scratch = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://last-buildings-test-{OS.GetProcessId()}"));
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless") { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
        UnitViews.UseModels = _shots != null; // figures only for the pictures
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = "res://assets/models/";
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;
            _entries = manifest["buildings"]!.AsArray();
            int[] types = NewTypes();
            GD.Print($"{types.Length} kit model types outside the Whirlwind: {string.Join(", ", Array.ConvertAll(types, t => _data.Buildings[t].Key))}");
            if (Check(types.Length > 0, "no kit building outside the Whirlwind in the manifest"))
            {
                ModelRows(types);
                foreach (int faction in FactionsOf(types))
                {
                    int[] mine = Array.FindAll(types, t => _data.Buildings[t].Faction == faction);
                    GD.Print($"-- {_data.Factions[faction].Key}: {mine.Length} types");
                    await TerraceAndPickRows(faction, mine);
                    await SiteAndShotRows(faction, mine);
                }
                FallbackRows(types);
                await ThreeFactionRow();
            }
            if (_shots != null) await Pictures(types);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = "res://assets/models/";
        try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The manifest's building entries that carry a kit, outside the Whirlwind, in manifest order.
    private int[] NewTypes()
    {
        int whirlwind = _data.FindFaction("whirlwind");
        var list = new List<int>();
        foreach (JsonNode? e in _entries)
        {
            int t = _data.FindBuilding((string)e!["id"]!);
            if (!Check(t >= 0, $"manifest building {(string)e["id"]!} is not in the data")) continue;
            if (e["kit"] is JsonArray && _data.Buildings[t].Faction != whirlwind) list.Add(t);
        }
        return list.ToArray();
    }

    private int[] FactionsOf(int[] types)
    {
        var list = new List<int>();
        foreach (int t in types) if (!list.Contains(_data.Buildings[t].Faction)) list.Add(_data.Buildings[t].Faction);
        return list.ToArray();
    }

    // ---- (M) the file, (H) the fit ----

    private void ModelRows(int[] types)
    {
        var models = new BuildingModels(_data, new uint[] { 0xC03030 });
        foreach (int type in types)
        {
            BuildingDef def = _data.Buildings[type];
            if (!Check(models.Has(type), $"(M) {def.Key}: no model")) continue;
            Mesh mesh = models.MeshOf(type)!;
            int tris = 0;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++) tris += mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index].AsInt32Array().Length / 3;
            var material = mesh.GetSurfaceCount() == 1 ? mesh.SurfaceGetMaterial(0) as BaseMaterial3D : null;
            Texture2D tex = models.TextureOf(type)!;
            GD.Print($"(M) {def.Key}: {tris} triangles, {mesh.GetSurfaceCount()} surface, material '{material?.ResourceName}', atlas {tex.GetWidth()} x {tex.GetHeight()}");
            Check(tris > 0 && tris <= TriangleBudget, $"(M) {def.Key}: {tris} triangles, over {TriangleBudget}");
            Check(mesh.GetSurfaceCount() == 1 && material?.ResourceName == UnitModels.TeamMaterialName, $"(M) {def.Key}: not one surface with the one '{UnitModels.TeamMaterialName}' material");
            Check(tex.GetWidth() == tex.GetHeight() && Array.IndexOf(AtlasSizes, tex.GetWidth()) >= 0, $"(M) {def.Key}: atlas {tex.GetWidth()} x {tex.GetHeight()}, want 64 x 64 or 128 x 128");

            Aabb drawn = models.FittedBounds(type);
            Aabb base0 = BaseFit(def);
            float w = def.FootprintWidth * MapConstants.CellSize, d = def.FootprintHeight * MapConstants.CellSize;
            GD.Print($"(H) {def.Key} (scale {def.Scale}): the fit {base0.Size.X:0.###} x {base0.Size.Z:0.###} x {base0.Size.Y:0.###} m (x {def.Scale}: {base0.Size.Y * def.Scale:0.###}), "
                     + $"drawn {drawn.Size.X:0.###} x {drawn.Size.Z:0.###} x {drawn.Size.Y:0.###} m on {w} x {d} m");
            bool plan = Math.Abs(drawn.Size.X - base0.Size.X) < 1e-4f && Math.Abs(drawn.Size.Z - base0.Size.Z) < 1e-4f
                        && Math.Abs(drawn.Position.X - base0.Position.X) < 1e-4f && Math.Abs(drawn.Position.Z - base0.Position.Z) < 1e-4f;
            Check(plan, $"(H) {def.Key}: drawn plan {drawn.Size.X} x {drawn.Size.Z} m, the fit is {base0.Size.X} x {base0.Size.Z} m");
            Check(Math.Abs(drawn.Size.Y - base0.Size.Y * def.Scale) < 0.01f, $"(H) {def.Key}: drawn {drawn.Size.Y} m tall, the fit x {def.Scale} is {base0.Size.Y * def.Scale} m");
            Check(Math.Abs(drawn.Position.Y) < 1e-4f, $"(H) {def.Key}: drawn base {drawn.Position.Y} m off the ground");
            Check(drawn.Size.X <= w - 2f * BuildingModels.FitMargin + 1e-3f && drawn.Size.Z <= d - 2f * BuildingModels.FitMargin + 1e-3f,
                $"(H) {def.Key}: {drawn.Size.X} x {drawn.Size.Z} m, not {BuildingModels.FitMargin} m inside its {w} x {d} m footprint");
        }
    }

    // M6-V26's fit, from the file itself: the largest uniform scale keeping the plan FitMargin inside the footprint, the plan
    // centred, the lowest point on the ground (scale 1 on the height).
    private Aabb BaseFit(BuildingDef def)
    {
        Node probe = GD.Load<PackedScene>(BuildingModels.PathFor(_data, def)).Instantiate();
        Aabb b = new();
        foreach (Node n in probe.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)n;
            if (mi.Mesh == null) continue;
            Transform3D toRoot = Transform3D.Identity;
            for (Node? x = mi; x != null && x != probe; x = x.GetParent())
                if (x is Node3D n3) toRoot = n3.Transform * toRoot;
            b = toRoot * mi.Mesh.GetAabb();
            break;
        }
        probe.Free();
        float w = def.FootprintWidth * MapConstants.CellSize - 2f * BuildingModels.FitMargin;
        float d = def.FootprintHeight * MapConstants.CellSize - 2f * BuildingModels.FitMargin;
        float s = Math.Min(w / Math.Max(b.Size.X, 1e-4f), d / Math.Max(b.Size.Z, 1e-4f));
        return new Aabb(new Vector3(-b.Size.X * s / 2f, 0f, -b.Size.Z * s / 2f), b.Size * s);
    }

    // ---- (T) terrace edge, (P) picking and the bar ----

    private async Task TerraceAndPickRows(int faction, int[] types)
    {
        _faction = faction;
        string key = _data.Factions[faction].Key;
        StartMatch(_data, "--players", "2", "--no-fog", "--no-combat", "--faction", "1", key, "--faction", "2", key, "--resources", "high");
        if (!Check(_views.Models is BuildingModels, "the match's building views have no models")) { await EndMatch(); return; }
        foreach (int type in types) Check(_views.Models!.Has(type), $"{_data.Buildings[type].Key}: not drawn as a model");
        KeepAlive();

        foreach (int type in types)
        foreach (bool high in new[] { false, true })
        {
            BuildingDef def = _data.Buildings[type];
            // The first anchor beside a step where the spawn is taken (one that would seal ground off, by the buildings
            // already standing, is refused: the M3-3 rule), at most 40 tried.
            int anchor = -1, slot = -1, from = 0;
            for (int tries = 0; tries < 40 && slot < 0; tries++)
            {
                anchor = StepAnchor(def, high, from);
                if (anchor < 0) break;
                _sim.Enqueue(Command.SpawnBuilding(0, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
                _sim.Tick();
                _sim.Tick();
                int s = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
                if (s >= 0 && B.TypeId[s] == type && B.Cell[s] == anchor) slot = s;
                from = anchor + 1;
            }
            if (!Check(anchor >= 0, $"(T) {def.Key}: no anchor beside a step {(high ? "on a terrace" : "at a terrace's foot")} on this map")) continue;
            if (!Check(slot >= 0, $"(T) {def.Key}: not spawned beside a step (last tried {anchor})")) continue;
            _views.Sync(W);
            MeshInstance3D? m = _views.ModelOf(slot);
            if (!Check(_views.ShownAsModel(slot) && m != null && m.Visible, $"(T) {def.Key}: not drawn as its model")) continue;
            Aabb box = m!.GlobalTransform * m.Mesh.GetAabb();
            float x0 = anchor % W.NavGrid.Width * MapConstants.CellSize, z0 = anchor / W.NavGrid.Width * MapConstants.CellSize;
            float x1 = x0 + def.FootprintWidth * MapConstants.CellSize, z1 = z0 + def.FootprintHeight * MapConstants.CellSize;
            float gap = Math.Min(Math.Min(box.Position.X - x0, x1 - box.End.X), Math.Min(box.Position.Z - z0, z1 - box.End.Z));
            NVector2 c = Centre(slot);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), worst = 0f;
            for (int sy = 0; sy <= 8; sy++)
                for (int sx = 0; sx <= 8; sx++)
                    worst = Math.Max(worst, Math.Abs(TerrainHeight.At(W.Heightmap, box.Position.X + box.Size.X * sx / 8f, box.Position.Z + box.Size.Z * sy / 8f) - ground));
            float want = BaseFit(def).Size.Y * def.Scale;
            GD.Print($"  (T) {def.Key} beside a step (level {W.NavGrid.LevelAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width)}): nearest edge gap {gap:0.###} m, "
                     + $"base {box.Position.Y - ground:0.####} m over the ground, ground under the plan within {worst:0.####} m, drawn {box.Size.Y:0.###} m tall (want {want:0.###}), bar at {_views.BarY(slot):0.###} m");
            Check(gap >= BuildingModels.FitMargin - 1e-3f, $"(T) {def.Key}: {gap} m from its footprint's nearest edge, want {BuildingModels.FitMargin}");
            Check(Math.Abs(box.Position.Y - ground) < 0.01f, $"(T) {def.Key}: base {box.Position.Y} m, ground {ground} m");
            Check(worst < 0.01f, $"(T) {def.Key}: the ground under the plan differs from the base by {worst} m");
            Check(Math.Abs(box.Size.Y - want) < 0.01f, $"(T) {def.Key}: drawn {box.Size.Y} m tall, want the fitted height x {def.Scale} = {want} m");
            Check(_views.BarY(slot) - BuildingViews.BarThickness / 2f > box.Size.Y, $"(T) {def.Key}: bar at {_views.BarY(slot)} m under the drawn top {box.Size.Y} m");
        }

        // (P) A worker in front of each; the model's pixels; the bar at the closest zoom.
        int worker = UnitOf(UnitSlot.Worker);
        NVector2 near0 = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 0f);
        for (int i = 0; i < types.Length; i++)
        {
            int type = types[i];
            BuildingDef def = _data.Buildings[type];
            // Two columns 28 m apart, a row every 22 m.
            int slot = Spawn(0, type, near0 + new NVector2(i % 2 * 28f, i / 2 * 22f));
            if (!Check(slot >= 0, $"(P) {def.Key} not spawned")) continue;
            NVector2 c = Centre(slot);
            int unit = SpawnUnit(0, worker, c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 1.5f));
            if (!Check(unit >= 0, $"(P) {def.Key}: no worker in front")) continue;
            _views.Sync(W);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                _sel.ClearBuilding();
                _sel.Selection.Clear();
                if (!Check(_sel.TryScreenPosition(unit, out Vector2 p), $"(P) {def.Key}: the unit is behind the camera")) continue;
                Click(p);
                bool picked = _sel.Selection.Contains(new EntityHandle(unit, W.Units.Generation[unit])) && _sel.SelectedBuilding < 0;
                string msg = $"(P) {def.Key} at zoom {zoom}: a click on the worker in front selected "
                             + (picked ? "the worker" : _sel.SelectedBuilding == slot ? "the building" : $"{_sel.Selection.Count} units, building {_sel.SelectedBuilding}");
                GD.Print($"  {msg}");
                Check(picked, msg);
            }
            W.Units.Free(new EntityHandle(unit, W.Units.Generation[unit])); // staging between ticks
            _sim.Tick();
            _views.Sync(W);
            // A type drawn as its box has no model pixels and no drawn top (BUG-1912's lesson): one FAIL line, then the next type.
            if (!Check(_views.ModelOf(slot) != null, $"(P) {def.Key}: not drawn as its model, so no model pixels or bar over a drawn top")) continue;
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            (int hits, int sel) = ModelPixels(slot);
            GD.Print($"  (P) {def.Key} at zoom {CameraLimits.DefaultZoom}: {sel} of {hits} sampled model pixels select it");
            Check(hits > 0 && sel == hits, $"(P) {def.Key}: {sel} of {hits} sampled model pixels select it");
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            _views.Sync(W);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), half = def.FootprintWidth * MapConstants.CellSize * BuildingViews.BarLengthShare / 2f;
            Vector3 l = new(c.X - half, ground + _views.BarY(slot), c.Y + _views.BarZ(slot)), r = new(c.X + half, ground + _views.BarY(slot), c.Y + _views.BarZ(slot));
            Rect2 screen = GetViewport().GetVisibleRect();
            bool on = screen.HasPoint(_camera.UnprojectPosition(l)) && screen.HasPoint(_camera.UnprojectPosition(r));
            float top = _views.Models!.Top(type);
            bool over = _views.BarZ(slot) != 0f || _views.BarY(slot) - BuildingViews.BarThickness / 2f > top;
            GD.Print($"  (P) {def.Key} bar at zoom {CameraLimits.MinZoom}: {_views.BarY(slot):0.###} m (drawn top {top:0.###} m, front offset {_views.BarZ(slot):0.##} m), {(on ? "on" : "off")} screen");
            Check(on && over, $"(P) {def.Key}: bar at {_views.BarY(slot)} m (top {top} m) on screen {on} at the closest zoom");
        }
        await EndMatch();
    }

    private int UnitOf(UnitSlot slot, int faction = -1)
    {
        if (faction < 0) faction = _faction;
        for (int u = 0; u < _data.Units.Length; u++) if (_data.Units[u].Faction == faction && _data.Units[u].Slot == slot) return u;
        return -1;
    }

    private int BuildingOf(BuildingSlot slot, int faction = -1)
    {
        if (faction < 0) faction = _faction;
        for (int b = 0; b < _data.Buildings.Length; b++) if (_data.Buildings[b].Faction == faction && _data.Buildings[b].Slot == slot) return b;
        return -1;
    }

    // The first anchor (row order) from `from` where the type fits with a free cell round it and a cell of another level lies within two cells.
    private int StepAnchor(BuildingDef def, bool high, int from = 0)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int cy = 4; cy + def.FootprintHeight < g.Height - 4; cy++)
            for (int cx = 4; cx + def.FootprintWidth < g.Width - 4; cx++)
            {
                int a = cy * g.Width + cx;
                if (a < from || !B.Fits(type, a)) continue;
                int level = g.LevelAt(cx, cy);
                if (high != level >= 1) continue;
                bool free = true, step = false;
                for (int y = cy - 2; y <= cy + def.FootprintHeight + 1 && free; y++)
                    for (int x = cx - 2; x <= cx + def.FootprintWidth + 1; x++)
                    {
                        bool inside = x >= cx && x < cx + def.FootprintWidth && y >= cy && y < cy + def.FootprintHeight;
                        if (B.SlotAt(x, y) >= 0) { free = false; break; }
                        if (!inside && g.LevelAt(x, y) != level) step = true;
                    }
                if (free && step) return a;
            }
        return -1;
    }

    // Every 12th pixel over the model's screen rectangle whose ray meets the drawn model: how many, and how many a click selects it at.
    private (int Hits, int Picked) ModelPixels(int slot)
    {
        Rect2 r = ScreenRect(slot);
        int hits = 0, picked = 0;
        for (float y = r.Position.Y; y <= r.End.Y; y += 12f)
            for (float x = r.Position.X; x <= r.End.X; x += 12f)
            {
                var p = new Vector2(x, y);
                if (!float.IsFinite(_views.ModelRayDistance(slot, _camera.ProjectRayOrigin(p), _camera.ProjectRayNormal(p)))) continue;
                hits++;
                _sel.ClearBuilding();
                _sel.Selection.Clear();
                Click(p);
                if (_sel.SelectedBuilding == slot) picked++;
            }
        _sel.ClearBuilding();
        return (hits, picked);
    }

    // The drawn model's screen rectangle, clamped to the screen (callers check ModelOf first).
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

    // ---- (S) a site is its box until finished; the tower's shot ----

    private async Task SiteAndShotRows(int faction, int[] types)
    {
        _faction = faction;
        string key = _data.Factions[faction].Key;
        StartMatch(_data, "--players", "2", "--no-fog", "--no-combat", "--faction", "1", key, "--faction", "2", key, "--resources", "high");
        KeepAlive();
        int tower = Array.Find(types, t => _data.Buildings[t].Attack != null);
        if (Array.IndexOf(types, tower) >= 0 && _data.Buildings[tower].Attack != null)
        {
            float top = _views.Models!.Top(tower), shot = _views.ShotHeights[tower];
            GD.Print($"(S) {_data.Buildings[tower].Key}: drawn top {top:0.###} m, shot height {shot:0.###} m");
            Check(top > 0f, $"(S) {_data.Buildings[tower].Key}: no drawn model top");
            Check(Math.Abs(shot - top * BuildingViews.ModelShotShare) < 1e-4f, $"(S) the tower's shot height {shot} m is not {BuildingViews.ModelShotShare} of its drawn {top} m top");
        }

        // The first type that needs no tech (Age II takes minutes of ticks); the buildings it needs are spawned first.
        int site0 = Array.Find(types, t => _data.Buildings[t].RequiresTechs.Length == 0);
        if (!Check(Array.IndexOf(types, site0) >= 0 && _data.Buildings[site0].RequiresTechs.Length == 0, $"(S) {key}: no new type a worker can build without a tech")) { await EndMatch(); return; }
        BuildingDef def = _data.Buildings[site0];
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        float bx = -50f;
        foreach (int need in def.RequiresBuildings)
        {
            Check(Spawn(0, need, block + new NVector2(bx, -10f)) >= 0, $"(S) {def.Key}: its required {_data.Buildings[need].Key} not spawned");
            bx += 16f;
        }
        int anchor = OpenAnchor(def, block + new NVector2(-30f, 20f));
        if (!Check(anchor >= 0, $"(S) no open anchor for a {def.Key}")) { await EndMatch(); return; }
        NVector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor);
        float clear = def.FootprintHeight * MapConstants.CellSize / 2f + 1f + _data.Units[UnitOf(UnitSlot.Worker)].Radius;
        int builder = SpawnUnit(0, UnitOf(UnitSlot.Worker), c + new NVector2(0f, clear));
        if (!Check(builder >= 0, "(S) no builder")) { await EndMatch(); return; }
        // The site must be explored ground (M4-3b): the fog takes the builder's sight at its next update (every 4 ticks).
        for (int t = 0; t < 4; t++) _sim.Tick();
        W.CanPlace(0, site0, anchor, out PlacementError place);
        if (!Check(place == PlacementError.None, $"(S) a {def.Key} can't be placed at {anchor}: {place}")) { await EndMatch(); return; }
        _sim.Enqueue(Command.Build(0, new EntityHandle(builder, W.Units.Generation[builder]), site0, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        int site = -1, boxFrames = 0, siteSyncs = 0, finishedAt = -1;
        bool modelOnFirst = false;
        string dropped = "";
        for (int t = 0; t < 4000; t++)
        {
            _sim.Tick();
            dropped += DroppedOf(builder, t);
            int s = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (s < 0) continue;
            site = s;
            _views.Sync(W);
            if (B.UnderConstruction[s])
            {
                siteSyncs++;
                if (_views.ShownAsSite(s) && !_views.ShownAsModel(s) && _views.BoxOf(s).Visible && Math.Abs(_views.BarY(s) - BuildingViews.BarHeightOf(def)) < 1e-4f) boxFrames++;
            }
            else
            {
                finishedAt = t;
                modelOnFirst = _views.ShownAsModel(s) && !_views.BoxOf(s).Visible;
                break;
            }
        }
        GD.Print($"  (S) a {def.Key} site: drawn as its box with the bar over it on {boxFrames} of {siteSyncs} syncs while a site; finished at tick {finishedAt}, its model on that sync: {modelOnFirst}{dropped}");
        Check(site >= 0 && siteSyncs > 0 && boxFrames == siteSyncs, $"(S) a {def.Key} site drawn as its box on {boxFrames} of {siteSyncs} syncs");
        Check(finishedAt >= 0 && modelOnFirst, $"(S) the finished {def.Key} (tick {finishedAt}) not drawn as its model on the first sync");
        await EndMatch();
    }

    // The orders of unit `unit` the last tick dropped, for the row's line (why a site never rose).
    private string DroppedOf(int unit, int tick)
    {
        string s = "";
        foreach (DroppedOrder e in W.DroppedOrders)
            if (e.Unit.Index == unit) s += $"; tick {tick}: its {e.Kind} dropped (placement {e.Placement}, queue full {e.QueueFull})";
        return s;
    }

    private bool TrainsAUnit(int type)
    {
        for (int u = 0; u < _data.Units.Length; u++) if (_data.Units[u].TrainedAt == _data.Buildings[type].Key) return true;
        return false;
    }

    // ---- (F) fallbacks ----

    private void FallbackRows(int[] types)
    {
        uint[] colours = { 0xC03030, 0x3060D0 };
        BuildingViews.UseModels = false;
        BoxRow(types, colours, "models off");
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = Path.Combine(_scratch, "missing").Replace('\\', '/') + "/";
        BoxRow(types, colours, "files missing");
        // The pointer stands in for the file of the first type that trains a unit (a player whose one building trains nothing
        // is out at once, its building gone before the view draws it: the Cairn, the first type, would be).
        int trainer = Array.FindIndex(types, TrainsAUnit);
        BuildingDef first = _data.Buildings[types[Math.Max(trainer, 0)]];
        string dir = Path.Combine(_scratch, "pointer", _data.Factions[first.Faction].Key, first.Key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, first.Key + ".glb"), "version https://git-lfs.github.com/spec/v1\noid sha256:0000000000000000000000000000000000000000000000000000000000000000\nsize 1\n");
        BuildingModels.ModelRoot = Path.Combine(_scratch, "pointer").Replace('\\', '/') + "/";
        int warnings = BuildingModels.Warnings;
        BoxRow(new[] { first.Id }, colours, "an LFS pointer");
        GD.Print($"(F) an LFS pointer for {first.Key}: {BuildingModels.Warnings - warnings} warning(s)");
        Check(BuildingModels.Warnings - warnings == 1, $"(F) an LFS pointer: {BuildingModels.Warnings - warnings} warnings, want one");
        BuildingModels.ModelRoot = "res://assets/models/";
    }

    // A fresh view over a fresh sim with one finished building of each type: each its box, DrawnHeight tall (x its scale), its bar over it.
    private void BoxRow(int[] types, uint[] colours, string what)
    {
        var sim = new Simulation(new SimConfig(1, 2, 8, 4096) { Data = _data, Combat = false });
        NVector2 p = new(40f, 40f);
        foreach (int type in types)
        {
            // Player 0 holds both factions' types (a staging spawn takes any type), as WhirlwindBuildingsTest's player holds its ten.
            sim.Enqueue(Command.SpawnBuilding(0, type, p));
            p += new NVector2(14f, 0f);
        }
        sim.Tick();
        sim.Tick();
        var views = new BuildingViews();
        AddChild(views);
        views.Bind(_data, sim.World.Buildings.Capacity, colours);
        views.Sync(sim.World);
        int drawn = 0;
        for (int i = 0; i < sim.World.Buildings.Capacity; i++)
        {
            if (!sim.World.Buildings.Alive[i]) continue;
            drawn++;
            BuildingDef def = _data.Buildings[sim.World.Buildings.TypeId[i]];
            MeshInstance3D box = views.BoxOf(i);
            float h = box.Mesh is BoxMesh bm ? bm.Size.Y * box.Scale.Y : -1f;
            bool ok = views.IsShown(i) && !views.ShownAsModel(i) && box.Visible && views.ModelOf(i) == null
                      && Math.Abs(h - BuildingViews.DrawnHeight(def)) < 1e-4f && Math.Abs(views.BarY(i) - BuildingViews.BarHeightOf(def)) < 1e-4f;
            Check(ok, $"(F) {what}: {def.Key} not its {BuildingViews.DrawnHeight(def)} m box (box {h} m, shown {views.IsShown(i)}, model {views.ShownAsModel(i)}, bar {views.BarY(i)})");
            GD.Print($"(F) {what}: {def.Key} drawn as its box, {h:0.###} m tall, bar at {views.BarY(i):0.###} m");
        }
        Check(drawn == types.Length, $"(F) {what}: {drawn} buildings drawn of {types.Length}");
        RemoveChild(views);
        views.QueueFree();
    }

    // ---- (3) three factions in one match ----

    private async Task ThreeFactionRow()
    {
        string[] keys = { "malazan", "whirlwind", "teblor" };
        StartMatch(_data, "--players", "3", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", keys[0], "--faction", "2", keys[1], "--faction", "3", keys[2]);
        NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-46f, -24f);
        for (int p = 0; p < keys.Length; p++)
        {
            int faction = _data.FindFaction(keys[p]);
            float x = 0f;
            var order = new List<int> { BuildingOf(BuildingSlot.TownHall, faction) };
            for (int t = 0; t < _data.Buildings.Length; t++) if (_data.Buildings[t].Faction == faction && t != order[0]) order.Add(t);
            foreach (int t in order)
            {
                int slot = Spawn(p, t, o + new NVector2(x, p * 16f));
                x += _data.Buildings[t].FootprintWidth * MapConstants.CellSize + 2f;
                Check(slot >= 0, $"(3) {_data.Buildings[t].Key} not spawned for player {p}");
            }
        }
        _views.Sync(W);
        int models = 0, boxes = 0;
        var drawnAsModel = new HashSet<int>();
        for (int i = 0; i < B.Capacity; i++)
        {
            if (!B.Alive[i]) continue;
            BuildingDef def = _data.Buildings[B.TypeId[i]];
            bool file = ResourceLoader.Exists(BuildingModels.PathFor(_data, def));
            bool ok = _views.ShownAsModel(i) == file && _views.BoxOf(i).Visible == !file;
            if (file && ok) ok = Math.Abs(_views.Models!.Top(B.TypeId[i]) - BaseFit(def).Size.Y * def.Scale) < 0.01f;
            Check(ok, $"(3) {def.Key} (player {B.Owner[i]}): drawn as model {_views.ShownAsModel(i)}, its file there {file}");
            if (file) models++; else boxes++;
            if (_views.ShownAsModel(i)) drawnAsModel.Add(B.TypeId[i]);
        }
        int missing = 0;
        foreach (JsonNode? e in _entries)
        {
            int t = _data.FindBuilding((string)e!["id"]!);
            if (t >= 0 && !drawnAsModel.Contains(t)) { missing++; Check(false, $"(3) manifest building {(string)e["id"]!} not drawn as a model"); }
        }
        GD.Print($"(3) three factions in one match: {models} buildings drawn as models, {boxes} as boxes; {_entries.Count - missing} of {_entries.Count} manifest types drawn as models");
        Check(models > 0 && missing == 0, $"(3) {models} models, {missing} manifest types not drawn as models");
        await EndMatch();
    }

    // ---- pictures (windowed) ----

    private async Task Pictures(int[] types)
    {
        foreach (int faction in FactionsOf(types))
        {
            _faction = faction;
            string key = _data.Factions[faction].Key;
            int worker = UnitOf(UnitSlot.Worker), line = UnitOf(UnitSlot.Line);
            // Each model at zoom 40 and 20, a worker and a line unit before it for scale (player 1 in Crimson).
            StartMatch(_data, "--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", key, "--faction", "2", key,
                "--colour", "1", "3", "--colour", "2", "5");
            _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit; // figures animate
            KeepAlive(2, new NVector2(40f, -40f));
            NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, -16f);
            var centre = new Dictionary<int, NVector2>();
            float x = 0f;
            foreach (int type in types)
            {
                if (_data.Buildings[type].Faction != faction) continue;
                int slot = Spawn(0, type, o + new NVector2(x, 0f));
                x += _data.Buildings[type].FootprintWidth * MapConstants.CellSize + 12f;
                if (slot < 0) continue;
                NVector2 c = Centre(slot);
                centre[type] = c;
                float front = _data.Buildings[type].FootprintHeight * MapConstants.CellSize / 2f + 2f;
                _sim.Enqueue(Command.SpawnUnit(0, worker, c + new NVector2(-1.8f, front)));
                _sim.Enqueue(Command.SpawnUnit(0, line, c + new NVector2(1.8f, front)));
            }
            for (int t = 0; t < 4; t++) _sim.Tick();
            foreach (int type in types)
            {
                if (!centre.TryGetValue(type, out NVector2 c)) continue;
                _camera.SetZoom(CameraLimits.DefaultZoom);
                _camera.SetFocus(c.X, c.Y + 2f);
                await Shot($"m6v32-{_data.Buildings[type].Key}-40m");
                _camera.SetZoom(CameraLimits.MinZoom);
                _camera.SetFocus(c.X, c.Y + 1f); // a little north of WhirlwindBuildingsTest's +3: the 7-8 m tops stay in the picture
                await Shot($"m6v32-{_data.Buildings[type].Key}-20m");
            }
            await EndMatch();
            await BasePicture(faction);
        }
        // Criterion 7: the Stone Cairn (the tower among the types) and the Armory (the Malazan forge), all eight colours.
        _faction = _data.FindFaction("teblor");
        int cairn = Array.Find(types, t => _data.Buildings[t].Attack != null);
        if (Array.IndexOf(types, cairn) >= 0) await Colours(cairn);
        _faction = _data.FindFaction("malazan");
        int armory = BuildingOf(BuildingSlot.Forge);
        if (Array.IndexOf(types, armory) >= 0) await Colours(armory);
    }

    // One whole base of `faction` (player 1, Crimson), line units and workers among the buildings.
    private async Task BasePicture(int faction)
    {
        _faction = faction;
        string key = _data.Factions[faction].Key;
        StartMatch(_data, "--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", key, "--faction", "2", key, "--colour", "1", "3");
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit;
        NVector2 hq = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-24f, 0f);
        PlaceBase(0, hq, faction);
        int line = UnitOf(UnitSlot.Line), worker = UnitOf(UnitSlot.Worker);
        foreach (NVector2 off in new NVector2[] { new(-8f, 6f), new(-6f, 6f), new(6f, 6f), new(8f, 6f), new(-1f, 16.5f), new(13f, 16f) })
            _sim.Enqueue(Command.SpawnUnit(0, line, hq + off));
        for (int k = 0; k < 3; k++) _sim.Enqueue(Command.SpawnUnit(0, worker, hq + new NVector2(-2f + 2f * k, 7f)));
        for (int t = 0; t < 4; t++) _sim.Tick();
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(hq.X, hq.Y + 1f);
        await Shot($"m6v32-{key}-base-40m");
        await Shot($"m6v32-{key}-base-40m-gray", gray: true);
        await EndMatch();
    }

    // A base of `faction` round `hq` for player `player`: every building slot once (the house twice).
    private void PlaceBase(int player, NVector2 hq, int faction)
    {
        var layout = new List<(int Type, NVector2 At)>
        {
            (BuildingOf(BuildingSlot.TownHall, faction), new(0f, 0f)), (BuildingOf(BuildingSlot.InfantryHall, faction), new(-13f, 0f)),
            (BuildingOf(BuildingSlot.RangedHall, faction), new(13f, 0f)),
            (BuildingOf(BuildingSlot.House, faction), new(-9f, 11f)), (BuildingOf(BuildingSlot.Camp, faction), new(-3f, 11f)),
            (BuildingOf(BuildingSlot.House, faction), new(3f, 11f)), (BuildingOf(BuildingSlot.WatchTower, faction), new(10f, 11f)),
        };
        float bx = -18f;
        foreach (BuildingSlot s in new[] { BuildingSlot.Forge, BuildingSlot.CasterHall, BuildingSlot.ShockHall, BuildingSlot.SiegeWorks })
        {
            layout.Add((BuildingOf(s, faction), new(bx, -13f)));
            bx += 12f;
        }
        int models = 0, boxes = 0;
        foreach ((int type, NVector2 off) in layout)
        {
            if (type < 0) continue;
            int slot = Spawn(player, type, hq + off);
            if (slot < 0) { GD.Print($"picture base: {_data.Buildings[type].Key} not placed"); continue; }
            if (_views.Models!.Has(type)) models++; else boxes++;
        }
        GD.Print($"picture base ({_data.Factions[faction].Key}): {models} model buildings and {boxes} box buildings placed");
    }

    // Type `type` in all eight setup colours, four a picture; then each colour alone on one spot under one camera at zoom 40
    // with the Rec. 709 luminance (0-255 levels) most common on the team pixels and on the largest non-team part's (the
    // non-team atlas tile with the most pixels), each pixel told by the atlas tile of the model triangle its ray meets first.
    private async Task Colours(int type)
    {
        string key = _data.Factions[_faction].Key, id = _data.Buildings[type].Key;
        int hall = BuildingOf(BuildingSlot.TownHall);
        foreach (int first in new[] { 1, 5 })
        {
            StartMatch(_data, "--players", "4", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", key, "--faction", "2", key,
                "--faction", "3", key, "--faction", "4", key, "--colour", "1", $"{first}", "--colour", "2", $"{first + 1}",
                "--colour", "3", $"{first + 2}", "--colour", "4", $"{first + 3}");
            NVector2 row = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, -10f);
            float step = _data.Buildings[type].FootprintWidth * MapConstants.CellSize + 3f;
            for (int p = 0; p < 4; p++)
            {
                Spawn(p, hall, row + new NVector2(p * 14f - 10f, 60f)); // each seat alive, out of the picture
                Spawn(p, type, row + new NVector2(p * step, 0f));
            }
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(row.X + 1.5f * step, row.Y + 2f);
            await Shot($"m6v32-{id}-colours-{first}-{first + 3}-40m");
            await EndMatch();
        }
        UiText ui = UiText.Shared!;
        for (int c = 1; c <= UiText.ColourCount; c++)
        {
            StartMatch(_data, "--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", key, "--faction", "2", key,
                "--colour", "1", $"{c}", "--colour", "2", $"{c % UiText.ColourCount + 1}");
            NVector2 spot = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, -10f);
            Spawn(0, hall, spot + new NVector2(0f, 60f));
            Spawn(1, hall, spot + new NVector2(-20f, 60f));
            int slot = Spawn(0, type, spot);
            if (!Check(slot >= 0, $"(luma) no {id} for colour {c}")) { await EndMatch(); continue; }
            NVector2 at = Centre(slot);
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(at.X, at.Y + 2f);
            Image img = await Shot($"m6v32-{id}-{ui.ColourName(c - 1).ToLowerInvariant()}-40m");
            _views.Sync(W);
            LumaRow(img, slot, $"{id} {ui.ColourName(c - 1)}");
            await EndMatch();
        }
    }

    private void LumaRow(Image img, int slot, string what)
    {
        if (!Check(_views.ModelOf(slot) != null, $"(luma) {what}: not drawn as its model")) return;
        MeshInstance3D m = _views.ModelOf(slot)!;
        UnitModels.Atlas atlas = _views.Models!.AtlasOf(B.TypeId[slot])!;
        Godot.Collections.Array arrays = m.Mesh.SurfaceGetArrays(0);
        Vector3[] verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        Transform3D g = m.GlobalTransform;
        var world = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++) world[i] = g * verts[i];
        var hist = new Dictionary<string, int[]>();
        Rect2 r = ScreenRect(slot);
        for (int y = (int)r.Position.Y; y <= (int)r.End.Y; y++)
            for (int x = (int)r.Position.X; x <= (int)r.End.X; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p);
                float best = float.PositiveInfinity;
                int hit = -1;
                for (int k = 0; k + 2 < index.Length; k += 3)
                {
                    float t = Ray(o, d, world[index[k]], world[index[k + 1]], world[index[k + 2]]);
                    if (t < best) { best = t; hit = k; }
                }
                if (hit < 0) continue;
                string tile = atlas.TileAt(uvs[index[hit]]);
                Color c = img.GetPixel(x, y);
                int l = Math.Clamp((int)Math.Round(255f * (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B)), 0, 255);
                if (!hist.TryGetValue(tile, out int[]? h)) hist[tile] = h = new int[256];
                h[l]++;
            }
        string largest = "";
        int largestCount = 0;
        foreach (KeyValuePair<string, int[]> kv in hist)
        {
            if (kv.Key == "team") continue;
            int n = 0;
            foreach (int v in kv.Value) n += v;
            if (n > largestCount) { largestCount = n; largest = kv.Key; }
        }
        int team = hist.TryGetValue("team", out int[]? th) ? ModeOf(th) : -1, part = largest.Length > 0 ? ModeOf(hist[largest]) : -1;
        int teamMid = th != null ? MedianOf(th) : -1, partMid = largest.Length > 0 ? MedianOf(hist[largest]) : -1;
        int teamCount = 0;
        if (th != null) foreach (int v in th) teamCount += v;
        // The mode (most common level, as M6-V31 / M6-VH10 measured) and the median: on a ridged tile roof the mode can sit on
        // the tiles' shaded undersides while most of the roof reads lit, so both are printed.
        GD.Print($"LUMA {what}: team mode {team} median {teamMid} ({teamCount} px), largest non-team part '{largest}' mode {part} median {partMid} ({largestCount} px), "
                 + $"gap mode {Math.Abs(team - part)} median {Math.Abs(teamMid - partMid)}");
    }

    private static int ModeOf(int[] h)
    {
        int best = 0;
        for (int i = 1; i < h.Length; i++) if (h[i] > h[best]) best = i;
        return best;
    }

    private static int MedianOf(int[] h)
    {
        int n = 0;
        foreach (int v in h) n += v;
        int seen = 0;
        for (int i = 0; i < h.Length; i++)
        {
            seen += h[i];
            if (2 * seen >= n) return i;
        }
        return h.Length - 1;
    }

    // Moller-Trumbore, both faces: the distance along the unit ray, or +infinity.
    private static float Ray(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a, p = d.Cross(e2);
        float det = e1.Dot(p);
        if (Math.Abs(det) < 1e-9f) return float.PositiveInfinity;
        float inv = 1f / det;
        Vector3 s = o - a;
        float u = s.Dot(p) * inv;
        if (u < 0f || u > 1f) return float.PositiveInfinity;
        Vector3 q = s.Cross(e1);
        float v = d.Dot(q) * inv;
        if (v < 0f || u + v > 1f) return float.PositiveInfinity;
        float t = e2.Dot(q) * inv;
        return t > 0f ? t : float.PositiveInfinity;
    }

    // ---- helpers (WhirlwindBuildingsTest's) ----

    // The faction's town hall for each player before anything else: a player whose buildings train nothing and who has no
    // unit is defeated at once (docs/02 "Game flow" 4), and a defeated player's commands are refused.
    private void KeepAlive(int players = 1, NVector2? spot = null)
    {
        NVector2 at = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + (spot ?? new NVector2(-30f, 60f));
        for (int p = 0; p < players; p++)
            Check(Spawn(p, BuildingOf(BuildingSlot.TownHall), at + new NVector2(-14f * p, 0f)) >= 0, $"no keep-alive town hall for player {p}");
    }

    private void StartMatch(GameData data, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
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
        BuildingDef def = _data.Buildings[type];
        int anchor = OpenAnchor(def, near);
        if (anchor < 0) return -1;
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

    // An anchor near `near` with a free, passable cell of one level round the footprint.
    private int OpenAnchor(BuildingDef def, NVector2 near)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int r = 0; r <= 12; r++)
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

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task<Image> Shot(string name, bool gray = false)
    {
        await Frames(6);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        if (gray)
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                {
                    Color px = img.GetPixel(x, y);
                    float l = 0.2126f * px.R + 0.7152f * px.G + 0.0722f * px.B;
                    img.SetPixel(x, y, new Color(l, l, l));
                }
        string path = Path.Combine(_shots!, name + ".png");
        GD.Print($"screenshot {path} ({img.SavePng(path)})");
        return img;
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
