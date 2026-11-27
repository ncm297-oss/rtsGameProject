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
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V30: the Teblor buildings drawn as models, their data <c>scale</c> (1.25) drawn on a model's height: the rows
/// <c>BuildingModelTest</c> (files, fit on flat ground, life cycle, fallbacks, team colour, for every manifest building) doesn't
/// make for them.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/TeblorBuildingsTest.tscn</c>; prints
/// "TEBLOR BUILDINGS TEST PASS" or FAIL lines. The Teblor model types are the manifest's building entries of the Teblor
/// faction (no count or id literal). Rows:
/// <list type="bullet">
/// <item>(H) The scale (criterion 4): for every manifest building the scene fits the file again by M6-V26's rule (the largest
/// uniform scale keeping the plan <see cref="BuildingModels.FitMargin"/> inside the footprint), which is what the base drew;
/// a scale-1 model (every Malazan one) is drawn exactly that size; a Teblor one has that width and depth and that height x its
/// data <c>scale</c> within 1 cm (and so is taller than the base drew it).</item>
/// <item>(T) Terrace edge: each Teblor model spawned at a terrace's foot and on its edge: inside its footprint by
/// <see cref="BuildingModels.FitMargin"/>, its base on the ground, the ground flat under its plan, its drawn height the fitted
/// height x <c>scale</c> within 1 cm, its bar over that top.</item>
/// <item>(P) Picking (criterion 5): an own Gatherer standing in front of each is what a click on it selects at zoom 20, 40 and
/// 60; every 12th pixel of the drawn model at zoom 40 selects the building; the bar's both ends on screen at the closest zoom
/// and over the drawn top when not lowered.</item>
/// <item>(S) The Stone Cairn's shot (criterion 6): its shot height is <see cref="BuildingViews.ModelShotShare"/> of its drawn
/// (scaled) top; a shot fired on flat ground and from a terrace leaves there and is drawn there on its first frame; a Cairn
/// site with an enemy held in range fires nothing in 200 ticks.</item>
/// <item>(F) Fallbacks (criterion 7): with models off (as <c>--placeholder-art</c> sets) and with the files missing, a finished
/// Teblor building is its box, <see cref="BuildingViews.DrawnHeight"/> (3 m x <c>scale</c>) tall with its bar over it; a Git
/// LFS pointer in place of one file gives that box and exactly one warning (the loop reads the log for <c>ERROR</c>).</item>
/// <item>(3) Three factions in one match (Malazan, Whirlwind, Teblor), every building type of each: drawn as its model exactly
/// when its file is there (the Whirlwind's are boxes), a model at its fitted height x its faction's <c>scale</c>.</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>m6v30-&lt;id&gt;-40m</c> / <c>-20m</c> for each Teblor model (a
/// Gatherer and a Warrior before it), <c>m6v30-base-40m</c> / <c>-40m-gray</c> / <c>-60m</c> (one Teblor base, Warriors beside
/// the buildings), <c>m6v30-colours-1-4-40m</c> and <c>m6v30-colours-5-8-40m</c> (every model in all eight setup colours),
/// <c>m6v30-teblor-beside-malazan-40m</c>, <c>m6v30-stretch-on-40m</c> / <c>m6v30-stretch-off-40m</c> (the same base with
/// the data's 1.25 and with 1, a scratch copy of the data) and <c>m6v30-cairn-shot-20m</c>.
/// </remarks>
public partial class TeblorBuildingsTest : Node
{
    private const string Tag = "TEBLOR BUILDINGS TEST";

    private readonly List<string> _failures = new();
    private string? _shots;
    private string _scratch = "";
    private GameData _data = null!;
    private JsonArray _entries = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private ProjectileViews _shotViews = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private int _teblor;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        _scratch = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://teblor-buildings-test-{OS.GetProcessId()}"));
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
            _teblor = _data.FindFaction("teblor");
            JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;
            _entries = manifest["buildings"]!.AsArray();
            int[] types = TeblorTypes();
            GD.Print($"{types.Length} Teblor building types are models: {string.Join(", ", Array.ConvertAll(types, t => _data.Buildings[t].Key))}");
            if (Check(types.Length > 0, "no Teblor building in the manifest"))
            {
                ScaleRows();
                await TerraceAndPickRows(types);
                int cairn = Array.Find(types, t => _data.Buildings[t].Attack != null);
                if (Check(Array.IndexOf(types, cairn) >= 0 && _data.Buildings[cairn].Attack != null, "(S) no Teblor tower model (the Stone Cairn)"))
                    await ShotRows(cairn);
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

    // The manifest's building entries of the Teblor faction, in its order.
    private int[] TeblorTypes()
    {
        var list = new List<int>();
        foreach (JsonNode? e in _entries)
        {
            int t = _data.FindBuilding((string)e!["id"]!);
            if (Check(t >= 0, $"manifest building {(string)e["id"]!} is not in the data") && _data.Buildings[t].Faction == _teblor) list.Add(t);
        }
        return list.ToArray();
    }

    // ---- (H) the scale against the base's fit ----

    private void ScaleRows()
    {
        var models = new BuildingModels(_data, new uint[] { 0xC03030 });
        foreach (JsonNode? e in _entries)
        {
            int type = _data.FindBuilding((string)e!["id"]!);
            if (type < 0 || !Check(models.Has(type), $"(H) {(string)e["id"]!}: no model")) continue;
            BuildingDef def = _data.Buildings[type];
            Aabb drawn = models.FittedBounds(type);
            Aabb base0 = BaseFit(def);
            float w = def.FootprintWidth * MapConstants.CellSize, d = def.FootprintHeight * MapConstants.CellSize;
            GD.Print($"(H) {def.Key} (scale {def.Scale}): the base's fit {base0.Size.X:0.###} x {base0.Size.Z:0.###} x {base0.Size.Y:0.###} m, "
                     + $"drawn {drawn.Size.X:0.###} x {drawn.Size.Z:0.###} x {drawn.Size.Y:0.###} m on {w} x {d} m, top {models.Top(type):0.###} m");
            bool plan = Math.Abs(drawn.Size.X - base0.Size.X) < 1e-4f && Math.Abs(drawn.Size.Z - base0.Size.Z) < 1e-4f
                        && Math.Abs(drawn.Position.X - base0.Position.X) < 1e-4f && Math.Abs(drawn.Position.Z - base0.Position.Z) < 1e-4f;
            Check(plan, $"(H) {def.Key}: drawn plan {drawn.Size.X} x {drawn.Size.Z} m, the base drew {base0.Size.X} x {base0.Size.Z} m");
            Check(Math.Abs(drawn.Position.Y) < 1e-4f, $"(H) {def.Key}: drawn base {drawn.Position.Y} m off the ground");
            Check(drawn.Size.X <= w - 2f * BuildingModels.FitMargin + 1e-3f && drawn.Size.Z <= d - 2f * BuildingModels.FitMargin + 1e-3f,
                $"(H) {def.Key}: {drawn.Size.X} x {drawn.Size.Z} m, not {BuildingModels.FitMargin} m inside its {w} x {d} m footprint");
            if (def.Scale == 1f)
                Check(Math.Abs(drawn.Size.Y - base0.Size.Y) < 1e-4f, $"(H) {def.Key}: scale 1 drawn {drawn.Size.Y} m tall, the base drew {base0.Size.Y} m");
            else
            {
                Check(Math.Abs(drawn.Size.Y - base0.Size.Y * def.Scale) < 0.01f, $"(H) {def.Key}: drawn {drawn.Size.Y} m tall, want {base0.Size.Y} x {def.Scale} = {base0.Size.Y * def.Scale} m");
                Check(Math.Abs(models.Top(type) - drawn.End.Y) < 1e-4f, $"(H) {def.Key}: top {models.Top(type)} m, the drawn bounds end at {drawn.End.Y} m");
            }
        }
    }

    // M6-V26's fit, from the file itself: the largest uniform scale keeping the plan FitMargin inside the footprint, the plan
    // centred, the lowest point on the ground (what the base drew for every type: it had no scale on a model).
    private Aabb BaseFit(BuildingDef def)
    {
        // Only called for a type the models loaded, so the file is there.
        string path = BuildingModels.PathFor(_data, def);
        Node probe = GD.Load<PackedScene>(path).Instantiate();
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

    // ---- (T) terrace edge and the drawn height, (P) picking and the bar ----

    private async Task TerraceAndPickRows(int[] types)
    {
        StartMatch(_data, "--players", "2", "--no-fog", "--no-combat", "--faction", "1", "teblor", "--faction", "2", "teblor", "--resources", "high");
        if (!Check(_views.Models is BuildingModels, "the match's building views have no models")) { await EndMatch(); return; }
        foreach (int type in types) Check(_views.Models!.Has(type), $"{_data.Buildings[type].Key}: not drawn as a model");
        KeepAlive();

        foreach (int type in types)
        foreach (bool high in new[] { false, true })
        {
            BuildingDef def = _data.Buildings[type];
            int anchor = StepAnchor(def, high);
            if (!Check(anchor >= 0, $"(T) {def.Key}: no anchor beside a step {(high ? "on a terrace" : "at a terrace's foot")} on this map")) continue;
            _sim.Enqueue(Command.SpawnBuilding(0, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
            _sim.Tick();
            _sim.Tick();
            int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (!Check(slot >= 0 && B.TypeId[slot] == type, $"(T) {def.Key}: not spawned at {anchor}")) continue;
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

        // (P) A Gatherer in front of each; the model's pixels; the bar at the closest zoom.
        int gatherer = UnitOf(UnitSlot.Worker);
        NVector2 near0 = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 0f);
        for (int i = 0; i < types.Length; i++)
        {
            int type = types[i];
            BuildingDef def = _data.Buildings[type];
            // Two columns 28 m apart, a row every 22 m (one column of eight ran off the open ground).
            int slot = Spawn(0, type, near0 + new NVector2(i % 2 * 28f, i / 2 * 22f));
            if (!Check(slot >= 0, $"(P) {def.Key} not spawned")) continue;
            NVector2 c = Centre(slot);
            int unit = SpawnUnit(0, gatherer, c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 1.5f));
            if (!Check(unit >= 0, $"(P) {def.Key}: no Gatherer in front")) continue;
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
                string msg = $"(P) {def.Key} at zoom {zoom}: a click on the Gatherer in front selected "
                             + (picked ? "the Gatherer" : _sel.SelectedBuilding == slot ? "the building" : $"{_sel.Selection.Count} units, building {_sel.SelectedBuilding}");
                GD.Print($"  {msg}");
                Check(picked, msg);
            }
            W.Units.Free(new EntityHandle(unit, W.Units.Generation[unit])); // staging between ticks
            _sim.Tick();
            _views.Sync(W);
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

    private int UnitOf(UnitSlot slot)
    {
        for (int u = 0; u < _data.Units.Length; u++) if (_data.Units[u].Faction == _teblor && _data.Units[u].Slot == slot) return u;
        return -1;
    }

    private int BuildingOf(BuildingSlot slot, int faction = -1)
    {
        if (faction < 0) faction = _teblor;
        for (int b = 0; b < _data.Buildings.Length; b++) if (_data.Buildings[b].Faction == faction && _data.Buildings[b].Slot == slot) return b;
        return -1;
    }

    // The first anchor (row order) where the type fits with a free cell round it and a cell of another level lies within two cells.
    private int StepAnchor(BuildingDef def, bool high)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int cy = 4; cy + def.FootprintHeight < g.Height - 4; cy++)
            for (int cx = 4; cx + def.FootprintWidth < g.Width - 4; cx++)
            {
                int a = cy * g.Width + cx;
                if (!B.Fits(type, a)) continue;
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
        int hits = 0, picked = 0;
        for (float y = y0; y <= y1; y += 12f)
            for (float x = x0; x <= x1; x += 12f)
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

    // ---- (S) the Stone Cairn's shot ----

    private async Task ShotRows(int cairn)
    {
        StartMatch(_data, "--players", "2", "--no-fog", "--faction", "1", "teblor", "--faction", "2", "teblor", "--resources", "high");
        if (!Check(_views.Models is BuildingModels models && models.Has(cairn), "(S) the Cairn has no model")) { await EndMatch(); return; }
        KeepAlive();
        BuildingDef def = _data.Buildings[cairn];
        float top = _views.Models!.Top(cairn), want = _views.ShotHeights[cairn], unscaled = BaseFit(def).Size.Y;
        GD.Print($"(S) {def.Key}: drawn top {top:0.###} m (the base's fit {unscaled:0.###} m x {def.Scale}), shot height {want:0.###} m");
        Check(Math.Abs(want - top * BuildingViews.ModelShotShare) < 1e-4f, $"(S) the Cairn's shot height {want} m is not {BuildingViews.ModelShotShare} of its drawn {top} m top");
        Check(Math.Abs(top - unscaled * def.Scale) < 0.01f, $"(S) the Cairn's top {top} m is not the fitted {unscaled} m x {def.Scale}");
        Check(want > unscaled * BuildingViews.ModelShotShare + 0.1f, $"(S) the shot height {want} m does not follow the scale (unscaled {unscaled * BuildingViews.ModelShotShare} m)");

        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 0f);
        int flat = Spawn(0, cairn, near);
        if (Check(flat >= 0, "(S) no Cairn on flat ground"))
            await ShotRow(flat, Centre(flat) + new NVector2(0f, 10f), "flat ground");

        (int anchor, NVector2 below) = HighAnchor(cairn);
        if (Check(anchor >= 0, "(S) no terrace anchor with lower ground in range on this map"))
        {
            _sim.Enqueue(Command.SpawnBuilding(0, cairn, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
            _sim.Tick();
            _sim.Tick();
            int high = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (Check(high >= 0 && B.TypeId[high] == cairn, "(S) the terrace Cairn not spawned"))
                await ShotRow(high, below, "a terrace over its target");
        }
        await SiteRow(cairn);
        await EndMatch();
    }

    // The tower in `slot` shoots an enemy Warrior held at `target`: the first shot's start height, as kept and as drawn.
    private async Task ShotRow(int slot, NVector2 target, string where)
    {
        int enemy = SpawnUnit(1, UnitOf(UnitSlot.Line), target);
        if (!Check(enemy >= 0, $"(S) {where}: no target")) return;
        _sim.Enqueue(Command.HoldPosition(1, new EntityHandle(enemy, W.Units.Generation[enemy])));
        var handle = new EntityHandle(slot, B.Generation[slot]);
        int shot = -1;
        for (int t = 0; t < 400 && shot < 0; t++)
        {
            _sim.Tick();
            _shotViews.CollectTick(W);
            for (int i = 0; i < W.Projectiles.Capacity && shot < 0; i++)
            {
                ShotEnds e = ShotEnds.Of(W.Projectiles, i);
                if (e.Live && e.AttackerIsBuilding && e.Attacker == handle) shot = i;
            }
        }
        if (!Check(shot >= 0, $"(S) {where}: the Cairn fired nothing in 400 ticks")) return;
        _shotViews.Sync(W, 0f);
        int k = -1;
        for (int j = 0; j < _shotViews.Shown; j++) if (_shotViews.DrawnSlot(j) == shot) k = j;
        float from = _shotViews.ShotFromHeight(shot), want = _views.ShotHeights[B.TypeId[slot]];
        NVector2 c = Centre(slot);
        float towerGround = TerrainHeight.At(W.Heightmap, c.X, c.Y), targetGround = TerrainHeight.At(W.Heightmap, target.X, target.Y);
        float drawnOver = k >= 0 ? _shotViews.DrawnAt(k).Y - TerrainHeight.At(W.Heightmap, _shotViews.DrawnAt(k).X, _shotViews.DrawnAt(k).Z) : float.NaN;
        float drawnWorld = k >= 0 ? _shotViews.DrawnAt(k).Y : float.NaN;
        GD.Print($"  (S) {where}: tower ground {towerGround:0.##} m, target ground {targetGround:0.##} m; the shot leaves {from:0.###} m up "
                 + $"(want {want:0.###}), drawn on its first frame at {drawnWorld:0.###} m ({drawnOver:0.###} m over the ground there)");
        Check(Math.Abs(from - want) < 1e-4f, $"(S) {where}: the shot leaves {from} m up, want the drawn Cairn's {want} m");
        Check(k >= 0 && Math.Abs(drawnOver - from) < 0.05f, $"(S) {where}: drawn {drawnOver} m over the ground on its first frame, want {from}");
        Check(k >= 0 && drawnWorld > towerGround + ProjectileViews.LaunchHeight, $"(S) {where}: drawn at {drawnWorld} m, under the old 1.2 m launch over the tower's {towerGround} m ground");
        if (W.Units.Alive[enemy]) W.Units.Free(new EntityHandle(enemy, W.Units.Generation[enemy])); // staging: the target goes
        _sim.Tick();
        await Frames(1);
    }

    // A tower anchor on level 1 or more, flat round it, with a passable cell of a lower level 8-14 m from its centre.
    private (int Anchor, NVector2 Below) HighAnchor(int tower)
    {
        BuildingDef def = _data.Buildings[tower];
        NavGrid g = W.NavGrid;
        for (int cy = 6; cy + def.FootprintHeight < g.Height - 6; cy++)
            for (int cx = 6; cx + def.FootprintWidth < g.Width - 6; cx++)
            {
                int level = g.LevelAt(cx, cy), a = cy * g.Width + cx;
                if (level < 1 || !B.Fits(tower, a)) continue;
                bool flat = true;
                for (int y = cy - 1; y <= cy + def.FootprintHeight && flat; y++)
                    for (int x = cx - 1; x <= cx + def.FootprintWidth; x++)
                        if (g.LevelAt(x, y) != level || B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y)) { flat = false; break; }
                if (!flat) continue;
                NVector2 c = StartBase.FootprintCenter(g, def, a);
                for (int y = cy - 7; y <= cy + 8; y++)
                    for (int x = cx - 7; x <= cx + 8; x++)
                    {
                        if (!g.IsPassable(x, y) || g.LevelAt(x, y) >= level) continue;
                        NVector2 p = g.CellCenter(x, y);
                        float d = NVector2.Distance(p, c);
                        if (d >= 8f && d <= 14f) return (a, p);
                    }
            }
        return (-1, default);
    }

    // Player 0 reaches Age II, a Gatherer starts a Cairn site with an enemy held 10 m away, the builder goes: 200 ticks, no shot.
    private async Task SiteRow(int tower)
    {
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int age = _data.FindTech("age_ii");
        int keep = _keepAlive;
        Check(Spawn(0, BuildingOf(BuildingSlot.InfantryHall), block + new NVector2(-16f, 60f)) >= 0, "(S) site: no infantry hall");
        Check(Spawn(0, BuildingOf(BuildingSlot.RangedHall), block + new NVector2(-4f, 60f)) >= 0, "(S) site: no ranged hall");
        if (!Check(keep >= 0, "(S) site: no Longhouse")) return;
        _sim.Enqueue(Command.Research(0, Centre(keep), age));
        for (int t = 0; t < 4000 && !W.HasTech(0, age); t++) _sim.Tick();
        if (!Check(W.HasTech(0, age), "(S) site: Age II not researched")) return;
        BuildingDef def = _data.Buildings[tower];
        int anchor = OpenAnchor(def, block + new NVector2(-30f, 84f));
        if (!Check(anchor >= 0, "(S) site: no open anchor")) return;
        NVector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor);
        int builder = SpawnUnit(0, UnitOf(UnitSlot.Worker), c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 1f));
        if (!Check(builder >= 0, "(S) site: no builder")) return;
        for (int t = 0; t < 10; t++) _sim.Tick();
        _sim.Enqueue(Command.Build(0, new EntityHandle(builder, W.Units.Generation[builder]), tower, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        int site = -1;
        for (int t = 0; t < 400 && site < 0; t++)
        {
            _sim.Tick();
            int s = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (s >= 0 && B.UnderConstruction[s]) site = s;
        }
        if (!Check(site >= 0, "(S) site: no Cairn site placed")) return;
        W.Units.Free(new EntityHandle(builder, W.Units.Generation[builder])); // staging: the builder goes, so the site stays a site
        int enemy = SpawnUnit(1, UnitOf(UnitSlot.Line), c + new NVector2(0f, -10f));
        if (enemy >= 0) _sim.Enqueue(Command.HoldPosition(1, new EntityHandle(enemy, W.Units.Generation[enemy])));
        var handle = new EntityHandle(site, B.Generation[site]);
        int fired = 0;
        for (int t = 0; t < 200; t++)
        {
            _sim.Tick();
            _shotViews.CollectTick(W);
            for (int i = 0; i < W.Projectiles.Capacity; i++)
            {
                ShotEnds e = ShotEnds.Of(W.Projectiles, i);
                if (e.Live && e.AttackerIsBuilding && e.Attacker == handle) fired++;
            }
        }
        _views.Sync(W);
        bool box = _views.ShownAsSite(site) && !_views.ShownAsModel(site);
        GD.Print($"  (S) a Cairn site (still a site: {B.UnderConstruction[site]}, drawn as its box: {box}, bar {_views.BarY(site):0.###} m) with an enemy 10 m away: {fired} shot-ticks in 200 ticks");
        Check(enemy >= 0 && B.UnderConstruction[site] && fired == 0, $"(S) site: {fired} shot-ticks from a Cairn site (target {enemy})");
        Check(box && Math.Abs(_views.BarY(site) - BuildingViews.BarHeightOf(def)) < 1e-4f, $"(S) site: not its box with the bar over the box's {BuildingViews.DrawnHeight(def)} m");
        await Frames(1);
    }

    // ---- (F) fallbacks: the box at the box's scale height ----

    private void FallbackRows(int[] types)
    {
        uint[] colours = { 0xC03030, 0x3060D0 };
        // Models off (what --placeholder-art sets), then every file missing.
        BuildingViews.UseModels = false;
        BoxRow(types, colours, "models off");
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = Path.Combine(_scratch, "missing").Replace('\\', '/') + "/";
        BoxRow(types, colours, "files missing");
        // One Git LFS pointer in place of the first type's file: its box, exactly one warning.
        BuildingDef first = _data.Buildings[types[0]];
        string dir = Path.Combine(_scratch, "pointer", _data.Factions[first.Faction].Key, first.Key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, first.Key + ".glb"), "version https://git-lfs.github.com/spec/v1\noid sha256:0000000000000000000000000000000000000000000000000000000000000000\nsize 1\n");
        BuildingModels.ModelRoot = Path.Combine(_scratch, "pointer").Replace('\\', '/') + "/";
        int warnings = BuildingModels.Warnings;
        BoxRow(new[] { types[0] }, colours, "an LFS pointer");
        GD.Print($"(F) an LFS pointer for {first.Key}: {BuildingModels.Warnings - warnings} warning(s)");
        Check(BuildingModels.Warnings - warnings == 1, $"(F) an LFS pointer: {BuildingModels.Warnings - warnings} warnings, want one");
        BuildingModels.ModelRoot = "res://assets/models/";
    }

    // A fresh view over a fresh sim with one finished building of each type: each its box, DrawnHeight tall, its bar over it.
    private void BoxRow(int[] types, uint[] colours, string what)
    {
        var sim = new Simulation(new SimConfig(1, 2, 8, 4096) { Data = _data, Combat = false });
        NVector2 p = new(40f, 40f);
        foreach (int type in types)
        {
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
                      && Math.Abs(h - BuildingViews.DrawnHeight(def)) < 1e-4f && Math.Abs(views.BarY(i) - BuildingViews.BarHeightOf(def)) < 1e-4f
                      && Math.Abs(box.Position.Y - h / 2f) < 1e-4f;
            Check(ok, $"(F) {what}: {def.Key} not its {BuildingViews.DrawnHeight(def)} m box (box {h} m, shown {views.IsShown(i)}, model {views.ShownAsModel(i)}, bar {views.BarY(i)})");
            if (drawn == 1) GD.Print($"(F) {what}: {def.Key} drawn as its box, {h:0.###} m tall (3 m x {def.Scale}), bar at {views.BarY(i):0.###} m");
        }
        Check(drawn == types.Length, $"(F) {what}: {drawn} buildings drawn of {types.Length}");
        RemoveChild(views);
        views.QueueFree();
    }

    // ---- (3) three factions in one match ----

    // A Malazan, a Whirlwind and a Teblor seat each with every building type of its faction: a type is drawn as its model
    // exactly when its file is there (the Malazan and Teblor models; the Whirlwind's boxes), each model at its faction's height.
    private async Task ThreeFactionRow()
    {
        string[] keys = { "malazan", "whirlwind", "teblor" };
        StartMatch(_data, "--players", "3", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", keys[0], "--faction", "2", keys[1], "--faction", "3", keys[2]);
        NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-46f, -24f);
        int models = 0, boxes = 0;
        for (int p = 0; p < keys.Length; p++)
        {
            int faction = _data.FindFaction(keys[p]);
            float x = 0f;
            // The town hall first (another type is refused before one), then the rest in data order.
            var order = new List<int> { BuildingOf(BuildingSlot.TownHall, faction) };
            for (int t = 0; t < _data.Buildings.Length; t++) if (_data.Buildings[t].Faction == faction && t != order[0]) order.Add(t);
            foreach (int t in order)
            {
                int slot = Spawn(p, t, o + new NVector2(x, p * 16f));
                x += _data.Buildings[t].FootprintWidth * MapConstants.CellSize + 2f;
                if (!Check(slot >= 0, $"(3) {_data.Buildings[t].Key} not spawned for player {p}")) continue;
            }
        }
        _views.Sync(W);
        for (int i = 0; i < B.Capacity; i++)
        {
            if (!B.Alive[i]) continue;
            BuildingDef def = _data.Buildings[B.TypeId[i]];
            bool file = ResourceLoader.Exists(BuildingModels.PathFor(_data, def));
            bool ok = _views.ShownAsModel(i) == file && _views.BoxOf(i).Visible == !file;
            if (file && ok) ok = Math.Abs(_views.Models!.Top(B.TypeId[i]) - BaseFit(def).Size.Y * def.Scale) < 0.01f;
            Check(ok, $"(3) {def.Key} (player {B.Owner[i]}): drawn as model {_views.ShownAsModel(i)}, its file there {file}");
            if (file) models++; else boxes++;
        }
        GD.Print($"(3) three factions in one match: {models} buildings drawn as models, {boxes} as boxes");
        Check(models > 0 && boxes > 0, $"(3) {models} models and {boxes} boxes");
        await EndMatch();
    }

    // ---- pictures (windowed) ----

    private async Task Pictures(int[] types)
    {
        int gatherer = UnitOf(UnitSlot.Worker), warrior = UnitOf(UnitSlot.Line);

        // Each model at zoom 40 and 20, a Gatherer and a Warrior before it for scale.
        StartMatch(_data, "--players", "4", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "teblor", "--faction", "2", "teblor",
            "--faction", "3", "teblor", "--faction", "4", "teblor", "--colour", "1", "8", "--colour", "2", "3", "--colour", "3", "1", "--colour", "4", "5");
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit; // figures animate
        KeepAlive(4, new NVector2(40f, -40f));
        NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, -16f);
        var centre = new Dictionary<int, NVector2>();
        float x = 0f;
        foreach (int type in types)
        {
            int slot = Spawn(0, type, o + new NVector2(x, 0f));
            x += _data.Buildings[type].FootprintWidth * MapConstants.CellSize + 12f;
            if (slot < 0) continue;
            NVector2 c = Centre(slot);
            centre[type] = c;
            float front = _data.Buildings[type].FootprintHeight * MapConstants.CellSize / 2f + 2f;
            _sim.Enqueue(Command.SpawnUnit(0, gatherer, c + new NVector2(-1.6f, front)));
            _sim.Enqueue(Command.SpawnUnit(0, warrior, c + new NVector2(1.6f, front)));
        }
        for (int t = 0; t < 4; t++) _sim.Tick();
        foreach (int type in types)
        {
            if (!centre.TryGetValue(type, out NVector2 c)) continue;
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(c.X, c.Y + 2f);
            await Shot($"m6v30-{_data.Buildings[type].Key}-40m");
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X, c.Y + 3f);
            await Shot($"m6v30-{_data.Buildings[type].Key}-20m");
        }
        await EndMatch();

        // Every model in all eight setup colours, four at a time.
        foreach (int first in new[] { 1, 5 })
        {
            StartMatch(_data, "--players", "4", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "teblor", "--faction", "2", "teblor",
                "--faction", "3", "teblor", "--faction", "4", "teblor", "--colour", "1", $"{first}", "--colour", "2", $"{first + 1}",
                "--colour", "3", $"{first + 2}", "--colour", "4", $"{first + 3}");
            KeepAlive(4, new NVector2(40f, -60f));
            NVector2 rows = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-50f, -22f);
            var placed = new List<NVector2>();
            float xx = 0f;
            // A column per type, a row per player (colour).
            foreach (int type in types)
            {
                BuildingDef def = _data.Buildings[type];
                for (int p = 0; p < 4; p++)
                {
                    int slot = Spawn(p, type, rows + new NVector2(xx, p * 11f));
                    if (slot >= 0) placed.Add(Centre(slot));
                }
                xx += def.FootprintWidth * MapConstants.CellSize + 3f;
            }
            if (placed.Count > 0)
            {
                float mx = 0f, mz = 0f;
                foreach (NVector2 q in placed) { mx += q.X; mz += q.Y; }
                _camera.SetZoom(CameraLimits.MaxZoom);
                _camera.SetFocus(mx / placed.Count, mz / placed.Count + 2f);
                await Shot($"m6v30-colours-{first}-{first + 3}-60m");
                _camera.SetZoom(CameraLimits.DefaultZoom);
                _camera.SetFocus(mx / placed.Count, mz / placed.Count + 2f);
                await Shot($"m6v30-colours-{first}-{first + 3}-40m");
            }
            await EndMatch();
        }

        // One Teblor base (player 1, Crimson), Warriors beside the buildings; then with the stretch off on a data copy.
        await BasePictures(_data, "m6v30-base", gray: true);
        await BasePictures(_data, "m6v30-stretch-on", gray: false, only40: true);
        GameData? flat = FlatCopy();
        if (Check(flat != null, "picture: the scale-1 data copy did not load")) await BasePictures(flat!, "m6v30-stretch-off", gray: false, only40: true);

        // A Teblor base beside a Malazan one (player 1 Teblor in Crimson, player 2 Malazan in Jade).
        await BesidePicture();

        // The Cairn shooting at zoom 20.
        await CairnPicture(types);
    }

    private async Task BasePictures(GameData data, string name, bool gray, bool only40 = false)
    {
        GameData keep = _data;
        _data = data;
        StartMatch(data, "--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "teblor", "--faction", "2", "teblor", "--colour", "1", "3");
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit;
        NVector2 hq = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-24f, 0f);
        PlaceBase(0, hq);
        int warrior = UnitOf(UnitSlot.Line), gatherer = UnitOf(UnitSlot.Worker);
        // Warriors beside the halls and the huts, Gatherers by the Longhouse.
        foreach (NVector2 off in new NVector2[] { new(-8f, 5f), new(-6.5f, 5f), new(6f, 5f), new(7.5f, 5f), new(-1f, 15.5f), new(5f, 15.5f), new(13f, 15f) })
            _sim.Enqueue(Command.SpawnUnit(0, warrior, hq + off));
        for (int k = 0; k < 4; k++) _sim.Enqueue(Command.SpawnUnit(0, gatherer, hq + new NVector2(-3f + 2f * k, 6f)));
        for (int t = 0; t < 4; t++) _sim.Tick();
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(hq.X, hq.Y + 2f);
        await Shot($"{name}-40m");
        if (gray) await Shot($"{name}-40m-gray", gray: true);
        if (!only40)
        {
            _camera.SetZoom(CameraLimits.MaxZoom);
            _camera.SetFocus(hq.X, hq.Y + 4f);
            await Shot($"{name}-60m");
        }
        await EndMatch();
        _data = keep;
    }

    // A base of faction `faction` (Teblor by default) round `hq` for player `player`: every model type and the box types behind.
    private void PlaceBase(int player, NVector2 hq, int faction = -1)
    {
        if (faction < 0) faction = _teblor;
        var layout = new List<(int Type, NVector2 At)>
        {
            (BuildingOf(BuildingSlot.TownHall, faction), new(0f, 0f)), (BuildingOf(BuildingSlot.InfantryHall, faction), new(-12f, 0f)),
            (BuildingOf(BuildingSlot.RangedHall, faction), new(12f, 0f)),
            (BuildingOf(BuildingSlot.House, faction), new(-9f, 10f)), (BuildingOf(BuildingSlot.Camp, faction), new(-3f, 10f)),
            (BuildingOf(BuildingSlot.House, faction), new(3f, 10f)), (BuildingOf(BuildingSlot.WatchTower, faction), new(10f, 10f)),
        };
        float bx = -16f;
        foreach (BuildingSlot s in new[] { BuildingSlot.Forge, BuildingSlot.CasterHall, BuildingSlot.ShockHall, BuildingSlot.SiegeWorks })
        {
            layout.Add((BuildingOf(s, faction), new(bx, -12f)));
            bx += 10f;
        }
        int models = 0, boxes = 0;
        foreach ((int type, NVector2 off) in layout)
        {
            if (type < 0) continue;
            int slot = Spawn(player, type, hq + off);
            if (slot < 0) continue;
            if (_views.Models!.Has(type)) models++; else boxes++;
        }
        GD.Print($"picture base ({_data.Factions[faction].Key}): {models} model buildings and {boxes} box buildings placed");
    }

    private async Task BesidePicture()
    {
        int malazan = _data.FindFaction("malazan");
        StartMatch(_data, "--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "teblor", "--faction", "2", "malazan",
            "--colour", "1", "3", "--colour", "2", "5");
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit;
        NVector2 hq = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-52f, 0f);
        PlaceBase(0, hq);
        PlaceBase(1, hq + new NVector2(34f, 0f), malazan);
        int warrior = UnitOf(UnitSlot.Line), heavy = -1;
        for (int u = 0; u < _data.Units.Length && heavy < 0; u++) if (_data.Units[u].Faction == malazan && _data.Units[u].Slot == UnitSlot.Line) heavy = u;
        for (int k = 0; k < 3; k++)
        {
            _sim.Enqueue(Command.SpawnUnit(0, warrior, hq + new NVector2(-2f + 2f * k, 6f)));
            _sim.Enqueue(Command.SpawnUnit(1, heavy, hq + new NVector2(32f + 2f * k, 6f)));
        }
        for (int t = 0; t < 4; t++) _sim.Tick();
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(hq.X + 17f, hq.Y + 2f);
        await Shot("m6v30-teblor-beside-malazan-40m");
        _camera.SetZoom(CameraLimits.MaxZoom);
        _camera.SetFocus(hq.X + 17f, hq.Y + 2f);
        await Shot("m6v30-teblor-beside-malazan-60m");
        await EndMatch();
    }

    private async Task CairnPicture(int[] types)
    {
        int cairn = Array.Find(types, t => _data.Buildings[t].Attack != null);
        if (Array.IndexOf(types, cairn) < 0 || _data.Buildings[cairn].Attack == null) return;
        StartMatch(_data, "--players", "2", "--no-fog", "--faction", "1", "teblor", "--faction", "2", "teblor", "--resources", "high");
        KeepAlive();
        NVector2 tp = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 0f);
        int ts = Spawn(0, cairn, tp);
        if (ts >= 0)
        {
            NVector2 c = Centre(ts);
            int enemy = SpawnUnit(1, UnitOf(UnitSlot.Line), c + new NVector2(4f, 11f));
            if (enemy >= 0) _sim.Enqueue(Command.HoldPosition(1, new EntityHandle(enemy, W.Units.Generation[enemy])));
            var handle = new EntityHandle(ts, B.Generation[ts]);
            int flying = -1;
            for (int t = 0; t < 400 && flying < 0; t++)
            {
                _sim.Tick();
                _shotViews.CollectTick(W);
                for (int i = 0; i < W.Projectiles.Capacity && flying < 0; i++)
                {
                    ShotEnds e = ShotEnds.Of(W.Projectiles, i);
                    if (e.Live && e.AttackerIsBuilding && e.Attacker == handle) flying = i;
                }
            }
            for (int t = 0; t < 2 && flying >= 0 && W.Projectiles.Alive[flying]; t++) { _sim.Tick(); _shotViews.CollectTick(W); }
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X + 2f, c.Y + 3f);
            await Frames(1);
            await Shot("m6v30-cairn-shot-20m");
        }
        await EndMatch();
    }

    // The shipped data with every building scale 1 (a scratch copy): the same base as the base drew it, for the stretch pair.
    private GameData? FlatCopy()
    {
        string src = ProjectSettings.GlobalizePath("res://data"), dst = Path.Combine(_scratch, "flat-data");
        foreach (string f in Directory.GetFiles(src, "*.json", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            string text = File.ReadAllText(f);
            if (f.Replace('\\', '/').EndsWith("teblor/buildings.json", StringComparison.Ordinal))
                text = System.Text.RegularExpressions.Regex.Replace(text, "\"scale\"\\s*:\\s*[0-9.]+", "\"scale\": 1.0");
            File.WriteAllText(to, text);
        }
        DataLoadResult r = DataLoader.LoadAll(dst);
        return r.Ok ? r.Data : null;
    }

    // ---- helpers (MalazanBuildingsTest's) ----

    private int _keepAlive = -1;

    // A Longhouse for each player before anything else: a player whose buildings train nothing and who has no unit is
    // defeated at once (docs/02 "Game flow" 4), and a defeated player's commands are refused.
    private void KeepAlive(int players = 1, NVector2? spot = null)
    {
        NVector2 at = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + (spot ?? new NVector2(-30f, 60f));
        for (int p = 0; p < players; p++)
        {
            int slot = Spawn(p, BuildingOf(BuildingSlot.TownHall), at + new NVector2(-14f * p, 0f));
            if (p == 0) _keepAlive = slot;
            Check(slot >= 0, $"no keep-alive Longhouse for player {p}");
        }
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
        _shotViews = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
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

    private async Task Shot(string name, bool gray = false)
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
